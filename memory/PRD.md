# SENTINEL X — Project Nexus (PRD)
Original: next-gen AI interface for SENTINEL X (Windows 11 AI, repo pawelaachi123-eng/SentinelX, WPF/WinUI C#). Built as web UI (React+Three.js) hostable in WebView2 + FastAPI bridge backend.
Choices: Claude Sonnet 4.5 (Emergent key), mic-level-only voice reaction, ESP32 pushes to endpoint.

## Implemented (2026-10)
- Procedural 3D core (GLSL vertex deformation: macro/meso/micro/attractors/limbs/ripples/pulse/audio), state blending, adaptive quality, GPU context loss handling, particles, bloom
- Main screen: logo, Tasks tile + gear, Base & Device tiles, expanding command dock (SSE streaming chat)
- Backend: /api/events SSE, chat stream, tasks, base ingest (X-Sentinel-Token), command queue (pending/ack), devices CRUD, settings, diagnostics
- Settings: 10 categories persisted
- User feedback round: removed inner "brain" layer, toned down core & dock effects, static deep-ocean background (#04162A), smoother surface

- Round 3: core vitality slider (calm↔lively), Deep Ocean palette (default) for core + glass + dock, speech recognition (VAD + Whisper whisper-1 via /api/voice/transcribe, wake word gate, language), WinUI 3 host files in /app/windows-host (NexusWindow + NexusBridge, not compiled), UI polish (state caption, voice caption, signal bars, device dots, dock suggestions, new chat, copy, timestamps)

- Round 4: Sentinel voice output (OpenAI TTS tts-1/tts-1-hd, voice sage default, /api/voice/speak), core reacts to speech audio, "speaking" state, replay button, VAD paused while speaking

- Round 5: Morning briefing (/api/briefing, real facts → Claude 3–5 spoken sentences, deterministic fallback, auto once/day in time window, manual triggers), interrupt speech by saying wake word (VAD during TTS, higher threshold, Whisper check, remainder executed)

## Backlog
- P1: compile & verify windows-host on Windows
- P2: speech recognition, Windows icon .ico export
