import io
import os
import logging
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from emergentintegrations.llm.openai import OpenAISpeechToText

from .bus import bus

router = APIRouter(prefix="/voice")
log = logging.getLogger("sentinel.voice")
MAX_BYTES = 8 * 1024 * 1024


@router.post("/transcribe")
async def transcribe(audio: UploadFile = File(...), language: str = Form("pl"), wake_word: str = Form("")):
    data = await audio.read()
    if not data:
        raise HTTPException(400, "empty audio")
    if len(data) > MAX_BYTES:
        raise HTTPException(413, "audio too large")
    buf = io.BytesIO(data)
    buf.name = audio.filename or "speech.webm"
    await bus.set_core("recognizing", "")
    try:
        stt = OpenAISpeechToText(api_key=os.environ["EMERGENT_LLM_KEY"])
        prompt = f"Polecenie dla asystenta {wake_word}." if wake_word else "Polecenie dla asystenta Sentinel."
        res = await stt.transcribe(file=buf, model="whisper-1", response_format="json", language=language[:2] or None, prompt=prompt, temperature=0.0)
        return {"text": (res.text or "").strip()}
    except Exception as e:  # noqa: BLE001
        log.exception("transcription failed")
        raise HTTPException(502, f"transcription failed: {str(e)[:200]}")
    finally:
        if bus.core_state == "recognizing":
            await bus.set_core("idle")
