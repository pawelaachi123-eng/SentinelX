from .db import db, NO_ID, now_iso, new_id
from .bus import bus


async def create_task(kind: str, title: str, source: str = "ui") -> dict:
    task = {
        "id": new_id(), "kind": kind, "title": title[:120], "source": source,
        "status": "running", "progress": 0.0, "error": None,
        "created_at": now_iso(), "updated_at": now_iso(), "finished_at": None,
    }
    await db.tasks.insert_one(dict(task))
    await bus.emit("task.updated", task)
    return task


async def update_task(task_id: str, **fields) -> dict | None:
    fields["updated_at"] = now_iso()
    if fields.get("status") in ("done", "error", "cancelled"):
        fields["finished_at"] = now_iso()
    await db.tasks.update_one({"id": task_id}, {"$set": fields})
    task = await db.tasks.find_one({"id": task_id}, NO_ID)
    if task:
        await bus.emit("task.updated", task)
    return task
