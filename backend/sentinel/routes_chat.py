import os
import json
import asyncio
import logging
from fastapi import APIRouter, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field
from emergentintegrations.llm.chat import LlmChat, UserMessage, TextDelta, StreamDone

from .db import db, NO_ID, now_iso, new_id
from .bus import bus
from .tasks import create_task, update_task

router = APIRouter(prefix="/chat")
log = logging.getLogger("sentinel.chat")
MODEL = ("anthropic", "claude-sonnet-4-5-20250929")
SYSTEM = (
    "Jesteś SENTINEL X — osobisty system AI działający na Windows 11, połączony z bazą ESP32 "
    "(Sentinel Base) i autoryzowanymi urządzeniami. Odpowiadasz zwięźle, rzeczowo i elegancko, "
    "w języku użytkownika. Nigdy nie wymyślasz odczytów czujników ani stanów urządzeń — jeżeli "
    "dane nie zostały dostarczone w kontekście, mówisz to wprost."
)


class ChatIn(BaseModel):
    session_id: str = Field(min_length=1, max_length=64)
    text: str = Field(min_length=1, max_length=8000)


async def _context_block() -> str:
    base = await db.base_reports.find_one({}, NO_ID, sort=[("received_at", -1)])
    devices = await db.devices.find({}, NO_ID).to_list(50)
    lines = ["[Kontekst systemu — dane rzeczywiste]"]
    if base:
        lines.append(f"Ostatni raport Sentinel Base ({base['received_at']}): " + json.dumps(base.get("sensors", {}), ensure_ascii=False))
    else:
        lines.append("Sentinel Base: brak raportów (niepołączona).")
    lines.append(f"Zarejestrowane urządzenia: {len(devices)} — " + ", ".join(d["name"] for d in devices) if devices else "Brak zarejestrowanych urządzeń.")
    return "\n".join(lines)


@router.get("/sessions/{session_id}/messages")
async def history(session_id: str):
    return await db.messages.find({"session_id": session_id}, NO_ID).sort("created_at", 1).to_list(500)


@router.delete("/sessions/{session_id}")
async def clear(session_id: str):
    r = await db.messages.delete_many({"session_id": session_id})
    return {"deleted": r.deleted_count}


@router.post("/stream")
async def stream(body: ChatIn):
    key = os.environ.get("EMERGENT_LLM_KEY")
    if not key:
        raise HTTPException(503, "LLM key not configured")
    prior = await db.messages.find({"session_id": body.session_id}, NO_ID).sort("created_at", 1).to_list(40)
    user_msg = {"id": new_id(), "session_id": body.session_id, "role": "user", "content": body.text, "created_at": now_iso()}
    await db.messages.insert_one(dict(user_msg))
    task = await create_task("ai.query", body.text)

    system = SYSTEM + "\n\n" + await _context_block()
    initial = [{"role": "system", "content": system}] + [{"role": m["role"], "content": m["content"]} for m in prior if m.get("content")]
    chat = LlmChat(api_key=key, session_id=body.session_id, system_message=system, initial_messages=initial).with_model(*MODEL)

    async def gen():
        await bus.set_core("thinking", task["title"])
        yield f"data: {json.dumps({'type': 'task', 'task_id': task['id']})}\n\n"
        parts: list[str] = []
        try:
            first = True
            async for ev in chat.stream_message(UserMessage(text=body.text)):
                if isinstance(ev, TextDelta):
                    if first:
                        first = False
                        await bus.set_core("executing", task["title"])
                    parts.append(ev.content)
                    yield f"data: {json.dumps({'type': 'delta', 'content': ev.content})}\n\n"
                elif isinstance(ev, StreamDone):
                    break
            text = "".join(parts)
            msg = {"id": new_id(), "session_id": body.session_id, "role": "assistant", "content": text, "created_at": now_iso()}
            await db.messages.insert_one(dict(msg))
            await update_task(task["id"], status="done", progress=1.0)
            await bus.set_core("success", task["title"])
            yield f"data: {json.dumps({'type': 'done', 'message': msg})}\n\n"
        except Exception as e:  # noqa: BLE001
            log.exception("chat failed")
            await update_task(task["id"], status="error", error=str(e)[:300])
            await bus.set_core("error", str(e)[:120])
            yield f"data: {json.dumps({'type': 'error', 'error': str(e)[:300]})}\n\n"
        finally:
            asyncio.get_event_loop().call_later(2.2, lambda: asyncio.ensure_future(bus.set_core("idle")))

    return StreamingResponse(gen(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})
