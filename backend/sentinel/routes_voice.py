import io
import os
import logging
import re
from typing import Literal
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from fastapi.responses import Response
from pydantic import BaseModel, Field
from emergentintegrations.llm.openai import OpenAISpeechToText, OpenAITextToSpeech

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


class SpeakIn(BaseModel):
    text: str = Field(min_length=1, max_length=20000)
    voice: Literal["alloy", "ash", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer"] = "sage"
    speed: float = Field(default=0.95, ge=0.5, le=1.5)
    model: Literal["tts-1", "tts-1-hd"] = "tts-1"


def _plain(text: str) -> str:
    text = re.sub(r"```.*?```", " ", text, flags=re.S)
    text = re.sub(r"[#*`>_]+", "", text)
    text = re.sub(r"^\s*[-•]\s+", "", text, flags=re.M)
    return re.sub(r"\s+", " ", text).strip()[:4000]


@router.post("/speak")
async def speak(body: SpeakIn):
    text = _plain(body.text)
    if not text:
        raise HTTPException(400, "nothing to speak")
    try:
        tts = OpenAITextToSpeech(api_key=os.environ["EMERGENT_LLM_KEY"])
        audio = await tts.generate_speech(text=text, model=body.model, voice=body.voice, speed=body.speed, response_format="mp3")
    except Exception as e:  # noqa: BLE001
        log.exception("tts failed")
        raise HTTPException(502, f"tts failed: {str(e)[:200]}")
    return Response(content=audio, media_type="audio/mpeg", headers={"Cache-Control": "no-store"})
