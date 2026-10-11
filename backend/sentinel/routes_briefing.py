import os
import logging
from datetime import datetime, timezone, timedelta
from fastapi import APIRouter
from pydantic import BaseModel, Field
from emergentintegrations.llm.chat import LlmChat, UserMessage

from .db import db, NO_ID, now_iso, new_id
from .bus import bus
from .tasks import create_task, update_task
from .routes_chat import MODEL

router = APIRouter()
log = logging.getLogger("sentinel.briefing")


class BriefingIn(BaseModel):
    session_id: str = Field(min_length=1, max_length=64)
    tz_offset_min: int = Field(default=0, ge=-840, le=840)


async def _facts(tz_offset_min: int) -> dict:
    s = await db.settings.find_one({"id": "main"}, NO_ID) or {}
    limit = int(s.get("base", {}).get("offline_after_s", 60))
    now = datetime.now(timezone.utc)
    last = await db.base_reports.find_one({}, NO_ID, sort=[("received_at", -1)])
    base_online = bool(last) and (now - datetime.fromisoformat(last["received_at"])).total_seconds() <= limit
    devices = await db.devices.find({}, NO_ID).to_list(100)
    for d in devices:
        d["online"] = bool(d.get("last_seen")) and (now - datetime.fromisoformat(d["last_seen"])).total_seconds() <= limit
    local_midnight = (now + timedelta(minutes=-tz_offset_min)).replace(hour=0, minute=0, second=0, microsecond=0) - timedelta(minutes=-tz_offset_min)
    today = await db.tasks.find({"created_at": {"$gte": local_midnight.isoformat()}, "kind": {"$ne": "briefing"}}, NO_ID).to_list(500)
    return {
        "base": {"configured": bool(last), "online": base_online, "last_report": last["received_at"] if last else None,
                 "sensors": (last or {}).get("sensors", {}), "rssi": (last or {}).get("rssi")},
        "devices": [{"name": d["name"], "online": d["online"], "state": d.get("state"), "authorized": d.get("authorized")} for d in devices],
        "tasks_today": {"total": len(today), "done": sum(t["status"] == "done" for t in today),
                        "failed": [t["title"] for t in today if t["status"] == "error"][:5],
                        "pending": [t["title"] for t in today if t["status"] in ("running", "queued")][:5]},
    }


def _fallback(f: dict) -> str:
    b = f["base"]
    base = ("Sentinel Base jest online." if b["online"] else "Sentinel Base jest offline.") if b["configured"] else "Sentinel Base jeszcze się nie połączył."
    on = sum(d["online"] for d in f["devices"])
    dev = f"Urządzenia online: {on} z {len(f['devices'])}." if f["devices"] else "Brak zarejestrowanych urządzeń."
    t = f["tasks_today"]
    tasks = f"Dzisiaj {t['total']} zadań, ukończono {t['done']}." if t["total"] else "Dzisiaj nie było jeszcze zadań."
    return f"Dzień dobry. {base} {dev} {tasks}"


@router.post("/briefing")
async def briefing(body: BriefingIn):
    task = await create_task("briefing", "Poranny raport")
    await bus.set_core("thinking", "Poranny raport")
    facts = await _facts(body.tz_offset_min)
    text = None
    try:
        chat = LlmChat(api_key=os.environ["EMERGENT_LLM_KEY"], session_id=f"briefing-{new_id()}",
                       system_message="Jesteś SENTINEL X. Tworzysz poranny raport do odczytania na głos: po polsku, 3–5 krótkich, spokojnych zdań, bez list, bez Markdown, bez emoji. Używasz WYŁĄCZNIE podanych faktów; jeżeli czegoś brakuje, mówisz to krótko.").with_model(*MODEL)
        text = (await chat.send_message(UserMessage(text=f"Fakty (JSON): {facts}"))).strip()
    except Exception:  # noqa: BLE001
        log.exception("briefing llm failed, using fallback")
    text = text or _fallback(facts)
    msg = {"id": new_id(), "session_id": body.session_id, "role": "assistant", "content": text, "created_at": now_iso(), "kind": "briefing"}
    await db.messages.insert_one(dict(msg))
    await update_task(task["id"], status="done", progress=1.0)
    await bus.set_core("success", "Poranny raport")
    await bus.set_core("idle")
    return {"message": msg, "facts": facts}
