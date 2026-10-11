import asyncio
import json
from .db import now_iso


class EventBus:
    """In-process pub/sub fan-out for UI subscribers (SSE)."""

    def __init__(self):
        self._subs: set[asyncio.Queue] = set()
        self.core_state = "idle"
        self.core_detail = ""

    @property
    def subscribers(self) -> int:
        return len(self._subs)

    def subscribe(self) -> asyncio.Queue:
        q: asyncio.Queue = asyncio.Queue(maxsize=200)
        self._subs.add(q)
        return q

    def unsubscribe(self, q: asyncio.Queue):
        self._subs.discard(q)

    async def emit(self, kind: str, data: dict):
        payload = json.dumps({"type": kind, "data": data, "at": now_iso()})
        for q in list(self._subs):
            if q.full():
                q.get_nowait()
            q.put_nowait(payload)

    async def set_core(self, state: str, detail: str = ""):
        self.core_state, self.core_detail = state, detail
        await self.emit("core.state", {"state": state, "detail": detail})


bus = EventBus()
