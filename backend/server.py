import os
import logging
from pathlib import Path
from dotenv import load_dotenv

load_dotenv(Path(__file__).parent / ".env")

from fastapi import FastAPI, APIRouter
from starlette.middleware.cors import CORSMiddleware

from sentinel.db import client
from sentinel.routes_events import router as events_router
from sentinel.routes_chat import router as chat_router
from sentinel.routes_base import router as base_router
from sentinel.routes_system import router as system_router

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s")

app = FastAPI(title="Sentinel X Nexus Bridge")
api = APIRouter(prefix="/api")
for r in (events_router, chat_router, base_router, system_router):
    api.include_router(r)
app.include_router(api)

app.add_middleware(
    CORSMiddleware,
    allow_credentials=True,
    allow_origins=os.environ.get("CORS_ORIGINS", "*").split(","),
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.on_event("shutdown")
async def shutdown_db_client():
    client.close()
