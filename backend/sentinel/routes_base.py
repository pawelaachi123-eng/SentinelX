import os
import hmac
from datetime import datetime, timezone
from typing import Any
from fastapi import APIRouter, Header, HTTPException
from pydantic import BaseModel, Field

from .db import db, NO_ID, now_iso, new_id
from .bus import bus
from .tasks import create_task, update_task

router = APIRouter()


def _check_token(token: str | None):
    expected = os.environ["ESP32_INGEST_TOKEN"]
    if not token or not hmac.compare_digest(token, expected):
        raise HTTPException(401, "invalid base token")


async def _offline_after() -> int:
    s = await db.settings.find_one({"id": "main"}, NO_ID) or {}
    return int(s.get("base", {}).get("offline_after_s", 60))


def _age_s(iso: str | None) -> float | None:
    if not iso:
        return None
    return (datetime.now(timezone.utc) - datetime.fromisoformat(iso)).total_seconds()


class BaseReport(BaseModel):
    device_id: str = Field(min_length=1, max_length=64)
    firmware: str | None = None
    ip: str | None = None
    rssi: int | None = None
    uptime_s: int | None = None
    sensors: dict[str, Any] = {}
    capabilities: list[str] = []
    devices: dict[str, str] = {}


class DeviceIn(BaseModel):
    name: str = Field(min_length=1, max_length=60)
    kind: str = Field(default="relay", max_length=30)
    address: str = Field(default="", max_length=120)
    authorized: bool = True


class CommandIn(BaseModel):
    action: str = Field(min_length=1, max_length=40)
    args: dict[str, Any] = {}


class AckIn(BaseModel):
    ok: bool
    detail: str = ""


# ---------- ESP32 → Sentinel (push) ----------
@router.post("/base/ingest")
async def ingest(report: BaseReport, x_sentinel_token: str | None = Header(default=None)):
    _check_token(x_sentinel_token)
    doc = report.model_dump() | {"id": new_id(), "received_at": now_iso()}
    await db.base_reports.insert_one(dict(doc))
    for dev_id, state in report.devices.items():
        await db.devices.update_one({"id": dev_id}, {"$set": {"state": state, "last_seen": doc["received_at"]}})
    doc.pop("_id", None)
    await bus.emit("base.report", doc)
    pending = await db.base_commands.count_documents({"status": "pending"})
    return {"ok": True, "pending_commands": pending}


@router.get("/base/commands/pending")
async def pending_commands(x_sentinel_token: str | None = Header(default=None)):
    _check_token(x_sentinel_token)
    cmds = await db.base_commands.find({"status": "pending"}, NO_ID).sort("created_at", 1).to_list(20)
    ids = [c["id"] for c in cmds]
    if ids:
        await db.base_commands.update_many({"id": {"$in": ids}}, {"$set": {"status": "delivered"}})
        for c in cmds:
            await update_task(c["task_id"], progress=0.5)
    return cmds


@router.post("/base/commands/{cmd_id}/ack")
async def ack_command(cmd_id: str, body: AckIn, x_sentinel_token: str | None = Header(default=None)):
    _check_token(x_sentinel_token)
    cmd = await db.base_commands.find_one({"id": cmd_id}, NO_ID)
    if not cmd:
        raise HTTPException(404, "unknown command")
    status = "done" if body.ok else "error"
    await db.base_commands.update_one({"id": cmd_id}, {"$set": {"status": status, "detail": body.detail}})
    await update_task(cmd["task_id"], status=status, progress=1.0, error=None if body.ok else body.detail)
    await bus.set_core("success" if body.ok else "error", cmd["action"])
    return {"ok": True}


# ---------- UI ----------
@router.get("/base/status")
async def base_status():
    last = await db.base_reports.find_one({}, NO_ID, sort=[("received_at", -1)])
    limit = await _offline_after()
    age = _age_s(last["received_at"]) if last else None
    return {
        "configured": last is not None,
        "online": age is not None and age <= limit,
        "age_s": age,
        "offline_after_s": limit,
        "last": last,
        "reports_total": await db.base_reports.count_documents({}),
    }


@router.get("/base/history")
async def base_history(limit: int = 30):
    rows = await db.base_reports.find({}, NO_ID).sort("received_at", -1).to_list(min(limit, 200))
    return list(reversed(rows))


@router.get("/devices")
async def list_devices():
    devices = await db.devices.find({}, NO_ID).sort("created_at", 1).to_list(200)
    limit = await _offline_after()
    for d in devices:
        age = _age_s(d.get("last_seen"))
        d["online"] = age is not None and age <= limit
    return devices


@router.post("/devices")
async def add_device(body: DeviceIn):
    doc = body.model_dump() | {"id": new_id(), "state": None, "last_seen": None, "created_at": now_iso()}
    await db.devices.insert_one(dict(doc))
    doc.pop("_id", None)
    await bus.emit("devices.changed", {})
    return doc


@router.patch("/devices/{dev_id}")
async def patch_device(dev_id: str, body: DeviceIn):
    r = await db.devices.update_one({"id": dev_id}, {"$set": body.model_dump()})
    if not r.matched_count:
        raise HTTPException(404, "unknown device")
    await bus.emit("devices.changed", {})
    return await db.devices.find_one({"id": dev_id}, NO_ID)


@router.delete("/devices/{dev_id}")
async def delete_device(dev_id: str):
    await db.devices.delete_one({"id": dev_id})
    await bus.emit("devices.changed", {})
    return {"ok": True}


@router.post("/devices/{dev_id}/command")
async def device_command(dev_id: str, body: CommandIn):
    dev = await db.devices.find_one({"id": dev_id}, NO_ID)
    if not dev:
        raise HTTPException(404, "unknown device")
    if not dev.get("authorized"):
        raise HTTPException(403, "device not authorized")
    task = await create_task("device.command", f"{dev['name']} · {body.action}")
    await update_task(task["id"], status="queued")
    cmd = {"id": new_id(), "task_id": task["id"], "device_id": dev_id, "action": body.action,
           "args": body.args, "status": "pending", "created_at": now_iso()}
    await db.base_commands.insert_one(dict(cmd))
    cmd.pop("_id", None)
    await bus.set_core("executing", task["title"])
    return cmd
