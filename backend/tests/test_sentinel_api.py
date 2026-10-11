"""Sentinel X backend API tests."""
import os
import json
import time
import threading
import pytest
import requests

BASE_URL = os.environ.get("REACT_APP_BACKEND_URL", "").rstrip("/")
if not BASE_URL:
    # fallback for CI
    with open("/app/frontend/.env") as f:
        for line in f:
            if line.startswith("REACT_APP_BACKEND_URL="):
                BASE_URL = line.split("=", 1)[1].strip().rstrip("/")

INGEST_TOKEN = "sx-base-ingest-7f3a9c"
API = f"{BASE_URL}/api"


# ---------- diagnostics / system ----------
def test_root():
    # External ingress has issues with /api/ trailing slash; use diagnostics as alive-check
    r = requests.get(f"{API}/diagnostics", timeout=15)
    assert r.status_code == 200


def test_diagnostics():
    r = requests.get(f"{API}/diagnostics", timeout=10)
    assert r.status_code == 200
    j = r.json()
    assert j["backend"] == "ok"
    assert j["database"] == "ok"
    assert j["llm_configured"] is True


def test_ingest_config():
    r = requests.get(f"{API}/base/ingest-config", timeout=10)
    assert r.status_code == 200
    j = r.json()
    assert j["header"] == "X-Sentinel-Token"
    assert j["token"] == INGEST_TOKEN


# ---------- base ingest ----------
def test_base_ingest_requires_token():
    r = requests.post(f"{API}/base/ingest", json={"device_id": "esp32-1"}, timeout=10)
    assert r.status_code == 401


def test_base_ingest_and_status():
    payload = {
        "device_id": "TEST_esp32_main",
        "firmware": "1.0.0-test",
        "ip": "192.168.1.42",
        "rssi": -55,
        "uptime_s": 1234,
        "sensors": {"temp_c": 22.5, "hum": 48},
        "capabilities": ["relay", "temp"],
        "devices": {},
    }
    r = requests.post(f"{API}/base/ingest", json=payload,
                      headers={"X-Sentinel-Token": INGEST_TOKEN}, timeout=10)
    assert r.status_code == 200
    assert r.json()["ok"] is True

    r = requests.get(f"{API}/base/status", timeout=10)
    assert r.status_code == 200
    s = r.json()
    assert s["configured"] is True
    assert s["online"] is True
    assert s["last"]["device_id"] == "TEST_esp32_main"


def test_base_history():
    r = requests.get(f"{API}/base/history?limit=5", timeout=10)
    assert r.status_code == 200
    assert isinstance(r.json(), list)


# ---------- devices CRUD ----------
@pytest.fixture
def created_device():
    r = requests.post(f"{API}/devices", json={"name": "TEST_relay1", "kind": "relay"}, timeout=10)
    assert r.status_code == 200
    dev = r.json()
    yield dev
    requests.delete(f"{API}/devices/{dev['id']}", timeout=10)


def test_device_create_list(created_device):
    r = requests.get(f"{API}/devices", timeout=10)
    assert r.status_code == 200
    ids = [d["id"] for d in r.json()]
    assert created_device["id"] in ids


def test_device_patch_authorized_only(created_device):
    # The previously-reported 422 bug: PATCH with only {"authorized": false}
    r = requests.patch(f"{API}/devices/{created_device['id']}",
                       json={"authorized": False}, timeout=10)
    assert r.status_code == 200, r.text
    assert r.json()["authorized"] is False

    r = requests.patch(f"{API}/devices/{created_device['id']}",
                       json={"authorized": True}, timeout=10)
    assert r.status_code == 200
    assert r.json()["authorized"] is True


def test_device_patch_empty_rejected(created_device):
    r = requests.patch(f"{API}/devices/{created_device['id']}", json={}, timeout=10)
    assert r.status_code == 400


def test_device_command_requires_auth():
    r = requests.post(f"{API}/devices", json={"name": "TEST_unauth", "authorized": False}, timeout=10)
    dev = r.json()
    try:
        r = requests.post(f"{API}/devices/{dev['id']}/command",
                          json={"action": "on"}, timeout=10)
        assert r.status_code == 403
    finally:
        requests.delete(f"{API}/devices/{dev['id']}", timeout=10)


def test_device_command_creates_task_and_pending(created_device):
    r = requests.post(f"{API}/devices/{created_device['id']}/command",
                      json={"action": "on"}, timeout=10)
    assert r.status_code == 200
    cmd = r.json()
    assert cmd["status"] == "pending"
    assert cmd["device_id"] == created_device["id"]

    # tasks list should include new task
    tr = requests.get(f"{API}/tasks?limit=10", timeout=10)
    assert tr.status_code == 200
    task_ids = [t["id"] for t in tr.json()]
    assert cmd["task_id"] in task_ids

    # pending commands endpoint (requires token)
    pr = requests.get(f"{API}/base/commands/pending",
                      headers={"X-Sentinel-Token": INGEST_TOKEN}, timeout=10)
    assert pr.status_code == 200
    cmd_ids = [c["id"] for c in pr.json()]
    assert cmd["id"] in cmd_ids

    # ack
    ar = requests.post(f"{API}/base/commands/{cmd['id']}/ack",
                       json={"ok": True, "detail": "done"},
                       headers={"X-Sentinel-Token": INGEST_TOKEN}, timeout=10)
    assert ar.status_code == 200


# ---------- settings ----------
def test_settings_put_get():
    payload = {"data": {"general": {"firstRunDone": True}, "core": {"quality": "low", "palette": "aurora"}}}
    r = requests.put(f"{API}/settings", json=payload, timeout=10)
    assert r.status_code == 200
    g = requests.get(f"{API}/settings", timeout=10).json()
    assert g["core"]["quality"] == "low"
    assert g["core"]["palette"] == "aurora"


# ---------- SSE events ----------
def test_sse_events_emits_core_and_task():
    """Subscribe to SSE, trigger a device command, expect core.state and task.updated."""
    seen = {"core": False, "task": False}

    def reader():
        with requests.get(f"{API}/events", stream=True, timeout=15) as resp:
            assert resp.status_code == 200
            for raw in resp.iter_lines(decode_unicode=True):
                if not raw or not raw.startswith("data:"):
                    continue
                try:
                    msg = json.loads(raw[5:].strip())
                except Exception:
                    continue
                if msg.get("type") == "core.state":
                    seen["core"] = True
                if msg.get("type") == "task.updated":
                    seen["task"] = True
                if seen["core"] and seen["task"]:
                    return

    t = threading.Thread(target=reader, daemon=True)
    t.start()
    time.sleep(1.0)

    # trigger activity
    r = requests.post(f"{API}/devices", json={"name": "TEST_sse_dev"}, timeout=10)
    dev = r.json()
    requests.post(f"{API}/devices/{dev['id']}/command", json={"action": "on"}, timeout=10)
    t.join(timeout=8)
    requests.delete(f"{API}/devices/{dev['id']}", timeout=10)

    assert seen["core"], "no core.state event received"
    assert seen["task"], "no task.updated event received"
