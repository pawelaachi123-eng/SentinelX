import os
from typing import Any
from fastapi import APIRouter
from pydantic import BaseModel

from .db import db, NO_ID, now_iso
from .bus import bus

router = APIRouter()


class SettingsIn(BaseModel):
    data: dict[str, Any]


@router.get("/")
async def root():
    return {"service": "sentinel-x-nexus", "ok": True}


@router.get("/settings")
async def get_settings():
    doc = await db.settings.find_one({"id": "main"}, NO_ID)
    return doc or {"id": "main", "updated_at": None}


@router.put("/settings")
async def put_settings(body: SettingsIn):
    data = {k: v for k, v in body.data.items() if k not in ("id", "_id")}
    data["updated_at"] = now_iso()
    await db.settings.update_one({"id": "main"}, {"$set": data}, upsert=True)
    doc = await db.settings.find_one({"id": "main"}, NO_ID)
    await bus.emit("settings.changed", {"updated_at": doc["updated_at"]})
    return doc


@router.get("/tasks")
async def list_tasks(limit: int = 50):
    return await db.tasks.find({}, NO_ID).sort("created_at", -1).to_list(min(limit, 200))


@router.delete("/tasks")
async def clear_tasks():
    r = await db.tasks.delete_many({"status": {"$in": ["done", "error", "cancelled"]}})
    await bus.emit("tasks.cleared", {})
    return {"deleted": r.deleted_count}


@router.get("/diagnostics")
async def diagnostics():
    await db.command("ping")
    return {
        "backend": "ok",
        "database": "ok",
        "llm_configured": bool(os.environ.get("EMERGENT_LLM_KEY")),
        "llm_model": "claude-sonnet-4-5-20250929",
        "event_subscribers": bus.subscribers,
        "core_state": bus.core_state,
        "tasks_total": await db.tasks.count_documents({}),
        "tasks_failed": await db.tasks.count_documents({"status": "error"}),
        "base_reports": await db.base_reports.count_documents({}),
        "devices": await db.devices.count_documents({}),
        "ingest_token_hint": os.environ["ESP32_INGEST_TOKEN"][:6] + "…",
    }


@router.get("/base/ingest-config")
async def ingest_config():
    return {"header": "X-Sentinel-Token", "token": os.environ["ESP32_INGEST_TOKEN"], "path": "/api/base/ingest"}
