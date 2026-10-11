# SENTINEL X — Project Nexus (PRD)
Original: next-gen AI interface for SENTINEL X (Windows 11 AI, repo pawelaachi123-eng/SentinelX, WPF/WinUI C#). Built as web UI (React+Three.js) hostable in WebView2 + FastAPI bridge backend.
Choices: Claude Sonnet 4.5 (Emergent key), mic-level-only voice reaction, ESP32 pushes to endpoint.

## Implemented (2026-10)
- Procedural 3D core (GLSL vertex deformation: macro/meso/micro/attractors/limbs/ripples/pulse/audio), state blending, adaptive quality, GPU context loss handling, particles, bloom
- Main screen: logo, Tasks tile + gear, Base & Device tiles, expanding command dock (SSE streaming chat)
- Backend: /api/events SSE, chat stream, tasks, base ingest (X-Sentinel-Token), command queue (pending/ack), devices CRUD, settings, diagnostics
- Settings: 10 categories persisted
- User feedback round: removed inner "brain" layer, toned down core & dock effects, static deep-ocean background (#04162A), smoother surface

## Backlog
- P1: WinUI 3 WebView2 host wiring to CoreStateService (bridge contract ready: window.chrome.webview messages {type,data})
- P2: speech recognition, Windows icon .ico export
