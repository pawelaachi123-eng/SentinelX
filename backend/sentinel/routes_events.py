import asyncio
import json
from fastapi import APIRouter, Request
from fastapi.responses import StreamingResponse
from .bus import bus

router = APIRouter()


@router.get("/events")
async def events(request: Request):
    q = bus.subscribe()

    async def gen():
        try:
            hello = {"type": "core.state", "data": {"state": bus.core_state, "detail": bus.core_detail}}
            yield f"data: {json.dumps(hello)}\n\n"
            while True:
                if await request.is_disconnected():
                    break
                try:
                    msg = await asyncio.wait_for(q.get(), timeout=15)
                    yield f"data: {msg}\n\n"
                except asyncio.TimeoutError:
                    yield ": keepalive\n\n"
        finally:
            bus.unsubscribe(q)

    return StreamingResponse(gen(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})
