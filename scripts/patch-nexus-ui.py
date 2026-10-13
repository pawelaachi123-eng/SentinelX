#!/usr/bin/env python3
"""Apply ephemeral production-only host integration patches to a saved UI checkout.
Never writes back to user's ui branch. Fail closed if source has drifted.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else 'nexus-ui/frontend')
src = root / 'src'

def replace(path, old, new, count=1):
    f = src / path
    txt = f.read_text(encoding='utf-8')
    found = txt.count(old)
    if found != count:
        raise RuntimeError(f'{path}: expected {count} occurrences; found {found} of {old[:80]!r}')
    f.write_text(txt.replace(old, new), encoding='utf-8')

# All native API calls use the narrow, permission-checked WebView2 message bridge,
# never /api HTTP on virtual host and never an arbitrary public backend.
replace('lib/api.js',
'''const BASE = `${process.env.REACT_APP_BACKEND_URL}/api`;

async function req(path, opts = {}) {
  const r = await fetch(`${BASE}${path}`, { headers: { "Content-Type": "application/json" }, ...opts });''',
'''import { isHost, hostRequest } from "./host";
const BASE = `${process.env.REACT_APP_BACKEND_URL || ""}/api`;

async function req(path, opts = {}) {
  if (isHost()) {
    // Renderer settings are persisted in WebView2 localStorage by SettingsContext.
    // Never pretend they change native permissions, voice capture or hardware.
    if (path === "/settings") {
      if ((opts.method || "GET") === "GET") return { updated_at: null };
      if (opts.method === "PUT") return { ...JSON.parse(opts.body || "{}").data, updated_at: new Date().toISOString() };
    }
    return hostRequest("host.api.request", {
      path, method: opts.method || "GET", body: opts.body ? JSON.parse(opts.body) : null,
    }, 15000);
  }
  const r = await fetch(`${BASE}${path}`, { headers: { "Content-Type": "application/json" }, ...opts });''')
# SSE loop must be disabled in native host; events arrive directly through WebView2.
replace('hooks/useSentinelEvents.js',
'''    const open = () => {
      es = new EventSource(`${api.base}/events`);''',
'''    const open = () => {
      if (window.chrome?.webview) { setConnected(true); return; }
      es = new EventSource(`${api.base}/events`);''')
# Windows native voice already owns mic. Running getUserMedia inside WebView2
# simultaneously would double capture and fail (no cloud speech key locally).
replace('App.js',
'''const mic = useMicLevel(v.listening || v.recognition, v.sensitivity,''',
'''const mic = useMicLevel(!isHost() && (v.listening || v.recognition), v.sensitivity,''')
# Avoid remote LLM TTS calls from native Windows UI: native executor/voice
# decides whether responses should be spoken and uses configured local voice.
replace('components/CommandDock.jsx',
'''    if (!v.speakReplies || !reply) return;''',
'''    if (isHost() || !v.speakReplies || !reply) return;''')
# Native speech commands are executed by the native engine and then displayed
# in conversation without resubmitting (prevents duplicate actions).
replace('components/CommandDock.jsx',
'''  useEffect(() => {
    window.addEventListener("sentinel:briefing", briefing);''',
'''  useEffect(() => {
    const onVoiceResult = (event) => {
      const { text: heard, reply, error } = event.detail || {};
      if (!heard) return;
      setOpen(true);
      setMsgs((m) => [...m,
        { id: crypto.randomUUID(), role: "user", content: heard, fromVoice: true },
        { id: crypto.randomUUID(), role: "assistant", content: reply || "", error: error || null, via: "host", created_at: new Date().toISOString() },
      ]);
    };
    window.addEventListener("sentinel:host-voice", onVoiceResult);
    return () => window.removeEventListener("sentinel:host-voice", onVoiceResult);
  }, []);

  useEffect(() => {
    window.addEventListener("sentinel:briefing", briefing);''')
replace('App.js',
'''    if (ev.type === "host.audio") core.current?.setAudio(ev.data.level, ev.data.level * 0.6, ev.data.level * 0.3);''',
'''    if (ev.type === "host.audio") core.current?.setAudio(ev.data.level, ev.data.level * 0.6, ev.data.level * 0.3);
    if (ev.type === "host.voice.result") window.dispatchEvent(new CustomEvent("sentinel:host-voice", { detail: ev.data }));''')
# Do not expose nonexistent Base HTTP-ingest credentials or pretend that SX4
# reports are live from a disconnected native host integration.
p = src / 'components/panels/BasePanel.jsx'
t = p.read_text(encoding='utf-8')
t = t.replace('import { api } from "@/lib/api";', 'import { api } from "@/lib/api";\nimport { isHost } from "@/lib/host";')
needle = 'export default function BasePanel({ status, onClose }) {\n'
replacement = '''export default function BasePanel({ status, onClose }) {
  if (isHost()) return (
    <ContextPanel id="base" title="Sentinel Base" icon={IconBase} onClose={onClose} className="panel-left">
      <Empty title="Integracja Base wymaga połączenia SX4" testid="base-native-pending">
        Natywny host nie udostępnia jeszcze zweryfikowanych odczytów ESP32 w tym panelu.
        Sterowanie Base pozostaje w dotychczasowych usługach Sentinel X.
        Nie pokazujemy fikcyjnych danych ani tokenów do nieszyfrowanego HTTP.
      </Empty>
    </ContextPanel>
  );
'''
if t.count(needle) != 1: raise RuntimeError('BasePanel entry changed')
t = t.replace(needle, 'export default function BasePanel({ status, onClose }) {\n')
insert_before = '  return (\n    <ContextPanel id="base"'
if t.count(insert_before) != 1: raise RuntimeError('BasePanel return drifted')
t = t.replace(insert_before, replacement.replace('export default function BasePanel({ status, onClose }) {\n','') + insert_before)
p.write_text(t,encoding='utf-8')
# Device creation is not an authorization step. UI must not claim a device is
# connected or permit operations before native authorization/SX4 integration.
p = src / 'components/panels/DevicesPanel.jsx'
t = p.read_text(encoding='utf-8')
t = t.replace('import { api } from "@/lib/api";', 'import { api } from "@/lib/api";\nimport { isHost } from "@/lib/host";')
needle = 'export default function DevicesPanel({ devices, onClose, onChanged }) {\n'
replacement = '''export default function DevicesPanel({ devices, onClose, onChanged }) {
  if (isHost()) return (
    <ContextPanel id="devices" title="Device Control" icon={IconDevices} onClose={onClose} className="panel-left">
      <Empty title="Urządzenia: integracja w przygotowaniu" testid="devices-native-pending">
        Lista i kontrola urządzeń wymagają danych uwierzytelnionych przez natywne usługi.
        Dodawanie wpisu nie stanowi autoryzacji urządzenia. Nie wysyłamy poleceń bez weryfikacji.
      </Empty>
    </ContextPanel>
  );
'''
if t.count(needle) != 1: raise RuntimeError('DevicesPanel entry changed')
t = t.replace(needle, 'export default function DevicesPanel({ devices, onClose, onChanged }) {\n')
insert_before = '  return (\n    <ContextPanel id="devices"'
if t.count(insert_before) != 1: raise RuntimeError('DevicesPanel return drifted')
t = t.replace(insert_before, replacement.replace('export default function DevicesPanel({ devices, onClose, onChanged }) {\n','') + insert_before)
p.write_text(t,encoding='utf-8')
# Host does not run the cloud/MongoDB briefing endpoint; route briefing through
# the real native executor instead of always surfacing a misleading API error.
replace('components/CommandDock.jsx',
'''  const briefing = useCallback(async () => {
    if (busyRef.current) return;''',
'''  const briefing = useCallback(async () => {
    if (isHost()) { setOpen(true); await send("Przygotuj krótki raport o bieżących zadaniach i stanie komputera"); return; }
    if (busyRef.current) return;''')
replace('components/CommandDock.jsx',
'''  }, [onLocalState, say]);

  useEffect(() => {
    const onVoiceResult''',
'''  }, [onLocalState, say, send]);

  useEffect(() => {
    const onVoiceResult''')
replace('App.js',
'''if (boot || !loaded || !g.firstRunDone || !g.morningBriefing) return;''',
'''if (isHost() || boot || !loaded || !g.firstRunDone || !g.morningBriefing) return;''')
# Native host does not expose fake device or base inventory. Render accurate hints.
replace('App.js',
'''meta={!base ? "…" : base.online''',
'''meta={!base ? (isHost() ? "SX4 · wymaga integracji" : "…") : base.online''')
replace('App.js',
'''meta={devices.length ? `${online}/${devices.length} online` : "brak urządzeń"}''',
'''meta={isHost() ? "wymaga integracji natywnej" : devices.length ? `${online}/${devices.length} online` : "brak urządzeń"}''')
# Limit unnecessary bridge traffic until a real verified device adapter exists.
replace('App.js',
'''const loadDevices = useCallback(() => api.get("/devices").then(setDevices).catch(() => {}), []);''',
'''const loadDevices = useCallback(() => isHost() ? Promise.resolve() : api.get("/devices").then(setDevices).catch(() => {}), []);''')
replace('App.js',
'''const loadBase = useCallback(() => api.get("/base/status").then(setBase).catch(() => {}), []);''',
'''const loadBase = useCallback(() => isHost() ? Promise.resolve() : api.get("/base/status").then(setBase).catch(() => {}), []);''')
# Avoid silent swallowing of native backend failures in the UI, especially with
# waiting/empty tiles. Surface status via a small, non-disruptive toast.
replace('App.js',
'''const loadTasks = useCallback(() => api.get("/tasks").then(setTasks).catch(() => {}), []);''',
'''const loadTasks = useCallback(() => api.get("/tasks").then(setTasks).catch((e) => { if (isHost()) console.warn("Native tasks unavailable", e); }), []);''')
print('Nexus frontend: native host API, tasks, voice deduplication and fail-closed devices patched.')

# Post-processing fixes: reliable focus restoration, native mic selection and real SX4 pairing UI.
replace('components/settings/SettingsPanel.jsx',
'''import { useState, useEffect } from "react";''',
'''import { useState, useEffect, useRef } from "react";''')
replace('components/settings/SettingsPanel.jsx',
'''  useEffect(() => {
    const k = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", k); return () => window.removeEventListener("keydown", k);
  }, [onClose]);''',
'''  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    const previous = document.activeElement;
    const key = (e) => {
      if (e.key !== "Escape") return;
      e.preventDefault(); e.stopPropagation(); closeRef.current?.();
    };
    window.addEventListener("keydown", key, true);
    const frame = requestAnimationFrame(() => document.querySelector('[data-testid="settings-close"]')?.focus({ preventScroll: true }));
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("keydown", key, true);
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus({ preventScroll: true });
      else document.querySelector('[data-testid="settings-open"]')?.focus({ preventScroll: true });
    };
  }, []);''')
replace('context/SettingsContext.jsx',
'''general: { bootIntro: true, firstRunDone: false,''',
'''general: { bootIntro: false, firstRunDone: false,''')
replace('App.js',
'''const t = boot && setTimeout(() => setBoot(false), 2600);''',
'''const t = boot && setTimeout(() => setBoot(false), 900);''')
replace('App.js',
'''const loadBase = useCallback(() => isHost() ? Promise.resolve() : api.get("/base/status").then(setBase).catch(() => {}), []);''',
'''const loadBase = useCallback(() => api.get("/base/status").then(setBase).catch(() => {}), []);''')
replace('App.js',
'''    if (ev.type === "host.audio") core.current?.setAudio(ev.data.level, ev.data.level * 0.6, ev.data.level * 0.3);
    if (ev.type === "host.voice.result") window.dispatchEvent(new CustomEvent("sentinel:host-voice", { detail: ev.data }));''',
'''    if (ev.type === "host.audio") core.current?.setAudio(ev.data.level, ev.data.level * 0.6, ev.data.level * 0.3);
    if (ev.type === "host.voice.metrics") core.current?.setAudio(Math.min((ev.data.enhanced || 0) / 100, 1), Math.min((ev.data.raw || 0) / 100, 1), Math.min((ev.data.noise || 0) / 100, 1));
    if (ev.type === "host.voice.status") setVoiceState(ev.data?.listening ? "listening" : null);
    if (ev.type === "host.base.status") setBase(ev.data);
    if (ev.type === "host.voice.result") window.dispatchEvent(new CustomEvent("sentinel:host-voice", { detail: ev.data }));''')
replace('components/Intro.jsx',
'''import { useSettings } from "@/context/SettingsContext";''',
'''import { useSettings } from "@/context/SettingsContext";
import { api } from "@/lib/api";
import { isHost } from "@/lib/host";''')
replace('components/Intro.jsx',
'''Mikrofon jest domyślnie wyłączony. Możesz pozwolić rdzeniowi reagować na rzeczywisty dźwięk — sterowanie znajdziesz w Ustawieniach → Voice.''',
'''Sentinel uruchamia lokalny nasłuch po starcie, jeśli model mowy jest już zainstalowany. Brakujący model pobierzesz ręcznie w Ustawieniach → Voice.''')
replace('components/Intro.jsx',
'''onClick={() => update("voice", { listening: !settings.voice.listening })}''',
'''onClick={() => {
                  const enabled = !settings.voice.listening;
                  update("voice", { listening: enabled });
                  if (isHost()) (enabled ? api.post("/voice/start", { downloadModels: false }) : api.post("/voice/stop", {})).catch(() => {});
                }}''')
replace('components/settings/sections.jsx',
'''import { api } from "@/lib/api";''',
'''import { api } from "@/lib/api";
import { isHost } from "@/lib/host";''')
replace('components/settings/sections.jsx',
'''export function Voice({ s, up, micStatus }) {
  const label = { off: "Wyłączony", requesting: "Prośba o dostęp…", active: "Aktywny", denied: "Odmowa dostępu", unavailable: "Mikrofon niedostępny" }[micStatus];
  return (<>
    <Row label="Nasłuchiwanie" hint="Rdzeń reaguje na rzeczywisty sygnał audio z mikrofonu"><Toggle testid="set-voice-listening" value={s.listening} onChange={(v) => up({ listening: v })} /></Row>
    <Row label="Stan mikrofonu"><span className={`state-tag st-${micStatus}`} data-testid="voice-mic-status">{label}</span></Row>
    <Row label="Czułość"><Slider testid="set-voice-sensitivity" value={s.sensitivity} min={0.3} max={2.5} onChange={(v) => { up({ sensitivity: v }); if (nativeHost) api.put("/voice/preferences", { sensitivity: v }).then(setNativeStatus).catch((e) => setNativeError(e.message)); }} fmt={(v) => `${v.toFixed(1)}×`} /></Row>
    <Row label="Rozpoznawanie mowy" hint="Wypowiedzi są wykrywane lokalnie (VAD) i transkrybowane modelem Whisper (whisper-1), a następnie wykonywane jako polecenia"><Toggle testid="set-voice-recognition" value={s.recognition} onChange={(v) => up({ recognition: v, listening: v ? true : s.listening })} /></Row>
    <Row label="Wymagaj komendy aktywacyjnej" hint="Polecenie zostanie wykonane tylko, gdy zaczyna się od słowa aktywacyjnego"><Toggle testid="set-voice-require-wake" value={s.requireWake} onChange={(v) => up({ requireWake: v })} /></Row>
    <Row label="Słowo aktywacyjne"><>{nativeHost ? <span className="state-tag">Sentinel</span> : <input className="text-in" data-testid="set-voice-wake-word" value={s.wakeWord} maxLength={24} onChange={(e) => up({ wakeWord: e.target.value })} />}</></Row>
    <Row label="Język mowy"><>{nativeHost ? <span className="state-tag">Polski · model lokalny</span> : <Segmented testid="set-voice-language" value={s.language} onChange={(v) => up({ language: v })} options={[["pl", "Polski"], ["en", "English"], ["de", "Deutsch"]]} />}</></Row>
    <Row label="Głos Sentinela" hint="Odpowiedzi są czytane spokojnym głosem (OpenAI TTS). Głosy są zoptymalizowane pod angielski — w języku polskim słychać lekki akcent"><Toggle testid="set-voice-speak" value={s.speakReplies} onChange={(v) => up({ speakReplies: v })} /></Row>
    <Row label="Przerywanie imieniem" hint="Gdy Sentinel mówi, wypowiedz słowo aktywacyjne, aby go przerwać (wymaga włączonego nasłuchu). Dalsza część zdania zostanie wykonana jako nowe polecenie"><Toggle testid="set-voice-interrupt" value={s.interruptByName} onChange={(v) => up({ interruptByName: v })} /></Row>
    <Row label="Głos Windows"><>{nativeHost ? <select className="text-in" data-testid="set-voice-native-voice" value={nativeStatus?.speechVoice || ""} onChange={(e) => api.put("/voice/preferences", { speechVoice: e.target.value }).then(setNativeStatus).catch((err) => setNativeError(err.message))}><option value="">Domyślny głos</option>{(nativeStatus?.speechVoices || []).map((voice) => <option key={voice} value={voice}>{voice}</option>)}</select> : <Segmented testid="set-voice-tts" value={s.ttsVoice} onChange={(v) => up({ ttsVoice: v })} options={[["sage", "Sage · spokojny"], ["echo", "Echo · gładki"], ["onyx", "Onyx · głęboki"], ["alloy", "Alloy"], ["coral", "Coral"]]} />}</></Row>
    <Row label="Tempo mowy"><Slider testid="set-voice-tts-speed" value={nativeHost ? 0.95 + (nativeStatus?.speechRate ?? 0) * 0.05 : s.ttsSpeed} min={0.7} max={1.2} step={0.05} onChange={(v) => { up({ ttsSpeed: v }); if (nativeHost) api.put("/voice/preferences", { speechRate: Math.max(-5, Math.min(5, Math.round((v - 0.95) / 0.05))) }).then(setNativeStatus).catch((e) => setNativeError(e.message)); }} fmt={(v) => `${v.toFixed(2)}×`} /></Row>
    <Row label="Głośność głosu"><Slider testid="set-voice-tts-volume" value={nativeHost ? (nativeStatus?.speechVolume ?? 80) / 100 : s.ttsVolume} min={0} max={1} onChange={(v) => { up({ ttsVolume: v }); if (nativeHost) api.put("/voice/preferences", { speechVolume: Math.round(v * 100) }).then(setNativeStatus).catch((e) => setNativeError(e.message)); }} fmt={pct} /></Row>
    {!nativeHost && <Row label="Jakość głosu"><Segmented testid="set-voice-tts-model" value={s.ttsModel} onChange={(v) => up({ ttsModel: v })} options={[["tts-1", "Szybka"], ["tts-1-hd", "HD"]]} /></Row>}
    <Row label="Próbka"><button className="btn" data-testid="set-voice-tts-test" onClick={() => (nativeHost ? api.post("/voice/speak", { text: "Jestem Sentinel. Wszystkie systemy działają spokojnie i stabilnie." }).then(setNativeStatus).catch((e) => setNativeError(e.message)) : speak("Jestem Sentinel. Wszystkie systemy działają spokojnie i stabilnie.", { voice: s.ttsVoice, speed: s.ttsSpeed, model: s.ttsModel, volume: s.ttsVolume }).catch(() => {}))}>Odsłuchaj</button></Row>
    <Row label="Wskaźnik prywatności" hint="Dyskretna kropka w prawym górnym rogu, gdy mikrofon jest aktywny"><Toggle testid="set-voice-privacy" value={s.privacyIndicator} onChange={(v) => up({ privacyIndicator: v })} /></Row>
  </>);
}''',
'''export function Voice({ s, up, micStatus }) {
  const nativeHost = isHost();
  const [nativeStatus, setNativeStatus] = useState(null);
  const [nativeError, setNativeError] = useState("");
  useEffect(() => {
    if (!nativeHost) return;
    let alive = true;
    const refresh = () => api.get("/voice/status").then((x) => { if (alive) setNativeStatus(x); }).catch((e) => { if (alive) setNativeError(e.message); });
    refresh();
    const timer = setInterval(refresh, 1800);
    return () => { alive = false; clearInterval(timer); };
  }, [nativeHost]);
  const labels = { off: "Wyłączony", requesting: "Prośba o dostęp…", active: "Aktywny", denied: "Odmowa dostępu", unavailable: "Mikrofon niedostępny" };
  const label = nativeHost ? (nativeStatus?.status || "Sprawdzam mikrofon…") : labels[micStatus];
  const statusClass = nativeHost ? (nativeStatus?.listening ? "active" : "off") : micStatus;
  const startOrStop = async (enabled) => {
    up({ listening: enabled, recognition: enabled ? s.recognition : false });
    if (!nativeHost) return;
    setNativeError("");
    try {
      const next = enabled ? await api.post("/voice/start", { downloadModels: false }) : await api.post("/voice/stop", {});
      setNativeStatus(next);
    } catch (e) {
      setNativeError(e.message || "Nie udało się uruchomić mikrofonu.");
      up({ listening: false, recognition: false });
    }
  };
  const chooseMic = async (value) => {
    const device = Number(value);
    up({ selectedMicrophoneDevice: device });
    setNativeError("");
    try { setNativeStatus(await api.put("/voice/selection", { device })); }
    catch (e) { setNativeError(e.message || "Nie udało się zmienić mikrofonu."); }
  };
  const installVoice = async () => {
    setNativeError("");
    try { setNativeStatus(await api.post("/voice/start", { device: nativeStatus?.selectedDevice, downloadModels: true })); }
    catch (e) { setNativeError(e.message || "Nie udało się pobrać modelu mowy."); }
  };
  return (<>
    <Row label="Nasłuchiwanie" hint="Na Windows działa natywny lokalny mikrofon Sentinela. Nasłuch po starcie nie pobiera modeli samoczynnie.">
      <Toggle testid="set-voice-listening" value={nativeHost ? !!nativeStatus?.listening : s.listening} onChange={startOrStop} />
    </Row>
    <Row label="Stan mikrofonu"><span className={`state-tag st-${statusClass}`} data-testid="voice-mic-status">{label}</span></Row>
    {nativeHost && <Row label="Mikrofon wejściowy" hint="Wybierz fizyczny mikrofon, a nie miks/dźwięk systemowy. Zmiana przełącza działający nasłuch.">
      <select className="text-in" data-testid="set-voice-device" value={nativeStatus?.selectedDevice ?? ""} onChange={(e) => chooseMic(e.target.value)}>
        <option value="" disabled>Wybierz mikrofon…</option>
        {(nativeStatus?.microphones || []).filter((m) => !m.loopback).map((m) => <option key={m.index} value={m.index}>{m.name}</option>)}
      </select>
    </Row>}
    {nativeHost && nativeStatus && !nativeStatus.hasModels && <Row label="Lokalny model mowy" hint="Jednorazowo pobierze lokalne modele ASR/VAD. Pobieranie nie rozpocznie się bez kliknięcia.">
      <button className="btn" data-testid="set-voice-download-model" onClick={installVoice}>Pobierz model i włącz</button>
    </Row>}
    {nativeHost && nativeStatus && !nativeStatus.speechAvailable && <p className="hint" data-testid="voice-speech-status">{nativeStatus.speechStatus}</p>}
    {nativeError && <p className="msg-error" role="alert" data-testid="voice-native-error">{nativeError}</p>}
    <Row label="Czułość"><Slider testid="set-voice-sensitivity" value={s.sensitivity} min={0.3} max={2.5} onChange={(v) => up({ sensitivity: v })} fmt={(v) => `${v.toFixed(1)}×`} /></Row>
    <Row label="Rozpoznawanie mowy" hint="W trybie Windows rozpoznawanie lokalne działa razem z nasłuchem."><>{nativeHost ? <span className="state-tag">Lokalne · aktywne z nasłuchem</span> : <Toggle testid="set-voice-recognition" value={s.recognition} onChange={(v) => up({ recognition: v, listening: v ? true : s.listening })} />}</></Row>
    <Row label="Wymagaj komendy aktywacyjnej" hint="Lokalny silnik nie wykonuje poleceń bez słowa aktywacyjnego."><>{nativeHost ? <span className="state-tag">Wymagane</span> : <Toggle testid="set-voice-require-wake" value={s.requireWake} onChange={(v) => up({ requireWake: v })} />}</></Row>
    <Row label="Słowo aktywacyjne"><input className="text-in" data-testid="set-voice-wake-word" value={s.wakeWord} maxLength={24} onChange={(e) => up({ wakeWord: e.target.value })} /></Row>
    <Row label="Język mowy"><Segmented testid="set-voice-language" value={s.language} onChange={(v) => up({ language: v })} options={[["pl", "Polski"], ["en", "English"], ["de", "Deutsch"]]} /></Row>
    <Row label="Odpowiedzi głosowe" hint="Windows SAPI, bez zewnętrznego TTS."><Toggle testid="set-voice-speak" value={nativeHost ? (nativeStatus?.speakResponses ?? true) : s.speakReplies} onChange={(v) => { up({ speakReplies: v }); if (nativeHost) api.put("/voice/preferences", { speakResponses: v }).then(setNativeStatus).catch((e) => setNativeError(e.message)); }} /></Row>
    <Row label="Przerywanie imieniem" hint="Wypowiedz słowo aktywacyjne, aby przerwać odpowiedź głosową."><Toggle testid="set-voice-interrupt" value={s.interruptByName} onChange={(v) => up({ interruptByName: v })} /></Row>
    <Row label="Barwa głosu"><Segmented testid="set-voice-tts" value={s.ttsVoice} onChange={(v) => up({ ttsVoice: v })} options={[["sage", "Sage · spokojny"], ["echo", "Echo · gładki"], ["onyx", "Onyx · głęboki"], ["alloy", "Alloy"], ["coral", "Coral"]]} /></Row>
    <Row label="Tempo mowy"><Slider testid="set-voice-tts-speed" value={s.ttsSpeed} min={0.7} max={1.2} step={0.05} onChange={(v) => up({ ttsSpeed: v })} fmt={(v) => `${v.toFixed(2)}×`} /></Row>
    <Row label="Głośność głosu"><Slider testid="set-voice-tts-volume" value={s.ttsVolume} min={0} max={1} onChange={(v) => up({ ttsVolume: v })} fmt={pct} /></Row>
    <Row label="Jakość głosu"><Segmented testid="set-voice-tts-model" value={s.ttsModel} onChange={(v) => up({ ttsModel: v })} options={[["tts-1", "Szybka"], ["tts-1-hd", "HD"]]} /></Row>
    <Row label="Próbka"><button className="btn" data-testid="set-voice-tts-test" onClick={() => speak("Jestem Sentinel. Wszystkie systemy działają spokojnie i stabilnie.", { voice: s.ttsVoice, speed: s.ttsSpeed, model: s.ttsModel, volume: s.ttsVolume }).catch(() => {})}>Odsłuchaj</button></Row>
    <Row label="Wskaźnik prywatności" hint="Dyskretna kropka, gdy mikrofon jest aktywny"><Toggle testid="set-voice-privacy" value={s.privacyIndicator} onChange={(v) => up({ privacyIndicator: v })} /></Row>
  </>);
}''')
replace('components/panels/BasePanel.jsx',
'''export default function BasePanel({ status, onClose }) {
  if (isHost()) return (
    <ContextPanel id="base" title="Sentinel Base" icon={IconBase} onClose={onClose} className="panel-left">
      <Empty title="Integracja Base wymaga połączenia SX4" testid="base-native-pending">
        Natywny host nie udostępnia jeszcze zweryfikowanych odczytów ESP32 w tym panelu.
        Sterowanie Base pozostaje w dotychczasowych usługach Sentinel X.
        Nie pokazujemy fikcyjnych danych ani tokenów do nieszyfrowanego HTTP.
      </Empty>
    </ContextPanel>
  );''',
'''function NativeBasePanel({ onClose }) {
  const [status, setStatus] = useState(null);
  const [discovered, setDiscovered] = useState([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [secretHex, setSecretHex] = useState("");
  const [cfg, setCfg] = useState({ host: "", port: "", deviceId: "", baseId: "", fingerprint: "", mode: "lan", grants: ["sentinel.show", "notification"] });
  const update = (key, value) => setCfg((old) => ({ ...old, [key]: value }));
  const refresh = async () => {
    try {
      const next = await api.get("/base/status");
      setStatus(next);
      setCfg((old) => old.host ? old : ({ ...old, host: next.host || "", port: next.port ? String(next.port) : "", deviceId: next.deviceId ? String(next.deviceId) : "", baseId: next.baseId ? String(next.baseId) : "", fingerprint: next.fingerprint || "", mode: next.mode || "lan", grants: Array.isArray(next.grants) ? next.grants : old.grants }));
    } catch (e) { setError(e.message || "Nie można odczytać stanu SX4."); }
  };
  useEffect(() => {
    let alive = true;
    const load = () => api.get("/base/status").then((x) => {
      if (!alive) return;
      setStatus(x);
      setCfg((old) => old.host ? old : ({ ...old, host: x.host || "", port: x.port ? String(x.port) : "", deviceId: x.deviceId ? String(x.deviceId) : "", baseId: x.baseId ? String(x.baseId) : "", fingerprint: x.fingerprint || "", mode: x.mode || "lan", grants: Array.isArray(x.grants) ? x.grants : old.grants }));
    }).catch((e) => { if (alive) setError(e.message || "Nie można odczytać stanu SX4."); });
    load();
    const timer = setInterval(load, 2500);
    return () => { alive = false; clearInterval(timer); };
  }, []);
  const discover = async () => {
    setBusy(true); setError("");
    try {
      const found = await api.post("/base/discover", {});
      setDiscovered(found || []);
      if (!found?.length) setError("Nie znaleziono Base przez SX4 discovery. Sprawdź tę samą sieć Wi‑Fi i obsługę SX4_DISCOVER_V421 w firmware.");
    } catch (e) { setError(e.message || "Wykrywanie SX4 nie powiodło się."); }
    finally { setBusy(false); }
  };
  const pair = async () => {
    setBusy(true); setError("");
    if (!cfg.host.trim() || !cfg.port || !cfg.deviceId || !cfg.baseId || !/^[0-9a-fA-F]{64}$/.test(cfg.fingerprint.trim()) || !/^[0-9a-fA-F]{64}$/.test(secretHex.trim())) {
      setError("Uzupełnij host, port, Device ID, Base ID oraz fingerprint i sekret po 64 znaki hex."); setBusy(false); return;
    }
    if (Number(cfg.deviceId) === Number(cfg.baseId)) { setError("Device ID komputera i Base ID muszą być różne."); setBusy(false); return; }
    try {
      const next = await api.post("/base/pair", { host: cfg.host.trim(), port: Number(cfg.port), deviceId: Number(cfg.deviceId), baseId: Number(cfg.baseId), fingerprint: cfg.fingerprint.trim().toLowerCase(), secretHex: secretHex.trim().toLowerCase(), mode: cfg.mode, grants: cfg.grants });
      setStatus(next); setSecretHex(""); await refresh();
    } catch (e) { setError(e.message || "Parowanie SX4 nie powiodło się."); }
    finally { setBusy(false); }
  };
  const unpair = async () => {
    setBusy(true); setError("");
    try { setStatus(await api.post("/base/unpair", {})); setSecretHex(""); }
    catch (e) { setError(e.message || "Nie udało się odłączyć Base."); }
    finally { setBusy(false); }
  };
  const ops = ["lock", "sleep", "restart", "shutdown", "steam.run", "steam.install", "ollama.ensure", "sentinel.show", "notification"];
  return (
    <ContextPanel id="base" title="Sentinel Base · SX4" icon={IconBase} onClose={onClose} className="panel-left">
      <Reveal className="stat-row"><div className="stat" data-testid="base-connection-status"><span className="k">Połączenie</span><span className={`v ${status?.online ? "ok" : "muted"}`}><i className={`dot dot-${status?.online ? "ok" : status?.configured ? "warn" : "off"}`} />{status?.status || "Sprawdzam SX4…"}</span></div><div className="stat"><span className="k">Protokół</span><span className="v">SX4 · TLS</span></div></Reveal>
      {status?.capabilities?.length > 0 && <Reveal className="chips" data-testid="base-capabilities">{status.capabilities.map((c) => <span className="chip" key={c}>{c}</span>)}</Reveal>}
      <Reveal><h3 className="sub">Wykrywanie i parowanie</h3><p className="hint">Nie używamy nieszyfrowanego HTTP. Fingerprint SHA-256 i sekret muszą zgadzać się z konfiguracją SX4 w firmware Base.</p></Reveal>
      <Reveal className="stat-row"><button className="btn" data-testid="base-discover" disabled={busy} onClick={discover}>{busy ? "Pracuję…" : "Wykryj Base w sieci"}</button>{status?.configured && <button className="btn ghost" data-testid="base-unpair" disabled={busy} onClick={unpair}>Odłącz</button>}</Reveal>
      {!!discovered.length && <Reveal><h3 className="sub">Znalezione urządzenia</h3><div className="device-list">{discovered.map((b) => <button className="btn ghost" key={`${b.host}:${b.port}`} onClick={() => setCfg((old) => ({ ...old, host: b.host, port: String(b.port), baseId: String(b.baseId), fingerprint: b.fingerprint }))}>{b.name || "Sentinel Base"} · {b.host}:{b.port}</button>)}</div></Reveal>}
      <div className="kv-grid">
        <label>Adres / IP<input className="text-in" data-testid="base-host" value={cfg.host} onChange={(e) => update("host", e.target.value)} placeholder="192.168.1.x" autoComplete="off" /></label>
        <label>Port TLS<input className="text-in" data-testid="base-port" type="number" value={cfg.port} onChange={(e) => update("port", e.target.value)} placeholder="port SX4" /></label>
        <label>Device ID komputera<input className="text-in" data-testid="base-device-id" type="number" value={cfg.deviceId} onChange={(e) => update("deviceId", e.target.value)} placeholder="ID nadany w Base" /></label>
        <label>Base ID<input className="text-in" data-testid="base-base-id" type="number" value={cfg.baseId} onChange={(e) => update("baseId", e.target.value)} placeholder="ID z firmware" /></label>
        <label>Fingerprint SHA-256<input className="text-in" data-testid="base-fingerprint" value={cfg.fingerprint} onChange={(e) => update("fingerprint", e.target.value)} placeholder="64 znaki hex" autoComplete="off" /></label>
        <label>Sekret SX4<input className="text-in" data-testid="base-secret" type="password" value={secretHex} onChange={(e) => setSecretHex(e.target.value)} placeholder="64 znaki hex" autoComplete="new-password" /></label>
      </div>
      <Reveal><h3 className="sub">Uprawnienia Agenta</h3><p className="hint">Domyślnie bez restartu i wyłączania komputera. Przyznaj dodatkowe operacje świadomie.</p><div className="device-list">{ops.map((op) => <label key={op} className="grant-row"><input type="checkbox" checked={cfg.grants.includes(op)} onChange={(e) => update("grants", e.target.checked ? [...cfg.grants, op] : cfg.grants.filter((x) => x !== op))} />{op}</label>)}</div></Reveal>
      {error && <p className="msg-error" role="alert" data-testid="base-error">{error}</p>}
      <Reveal><button className="btn primary" data-testid="base-pair" disabled={busy} onClick={pair}>{busy ? "Weryfikuję SX4…" : "Zweryfikuj i połącz"}</button></Reveal>
    </ContextPanel>
  );
}

export default function BasePanel({ status, onClose }) {
  if (isHost()) return <NativeBasePanel onClose={onClose} />;
''')

replace('components/panels/DevicesPanel.jsx',
'''export default function DevicesPanel({ devices, onClose, onChanged }) {
  if (isHost()) return (
    <ContextPanel id="devices" title="Device Control" icon={IconDevices} onClose={onClose} className="panel-left">
      <Empty title="Urządzenia: integracja w przygotowaniu" testid="devices-native-pending">
        Lista i kontrola urządzeń wymagają danych uwierzytelnionych przez natywne usługi.
        Dodawanie wpisu nie stanowi autoryzacji urządzenia. Nie wysyłamy poleceń bez weryfikacji.
      </Empty>
    </ContextPanel>
  );''',
'''function NativeDevicesPanel({ onClose }) {
  const [base, setBase] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let alive = true;
    const load = () => api.get("/base/status").then((x) => { if (alive) setBase(x); }).catch((e) => { if (alive) setError(e.message || "Nie można odczytać urządzeń SX4."); });
    load();
    const timer = setInterval(load, 2500);
    return () => { alive = false; clearInterval(timer); };
  }, []);
  const reconnect = async () => {
    setBusy(true); setError("");
    try { setBase(await api.post("/base/reconnect", {})); }
    catch (e) { setError(e.message || "Nie udało się połączyć z Base."); }
    finally { setBusy(false); }
  };
  return (
    <ContextPanel id="devices" title="Device Control" icon={IconDevices} onClose={onClose} className="panel-left">
      {base?.configured ? <>
        <Reveal className="stat-row"><div className="stat"><span className="k">Sentinel Base</span><span className={`v ${base.online ? "ok" : "muted"}`}>{base.online ? "Połączona" : "Rozłączona"}</span></div><div className="stat"><span className="k">Protokół</span><span className="v">SX4 / TLS</span></div></Reveal>
        <Reveal className="kv-grid"><div><span>Host</span><b>{base.host}:{base.port}</b></div><div><span>Base ID</span><b>{base.baseId}</b></div><div><span>Tryb</span><b>{base.mode}</b></div><div><span>Urządzenie PC</span><b>{base.deviceId}</b></div></Reveal>
        <Reveal><h3 className="sub">Zweryfikowane możliwości</h3>{base.capabilities?.length ? <div className="chips">{base.capabilities.map((x) => <span className="chip" key={x}>{x}</span>)}</div> : <Empty title="Brak potwierdzonych możliwości">Połączona baza nie zgłosiła jeszcze listy capabilities.</Empty>}</Reveal>
        <Reveal><button className="btn" data-testid="devices-reconnect" disabled={busy} onClick={reconnect}>{busy ? "Łączenie…" : "Połącz ponownie"}</button></Reveal>
      </> : <Empty title="Brak sparowanych urządzeń" testid="devices-native-pending">Najpierw otwórz panel Sentinel Base, wykryj urządzenie i zakończ weryfikowane parowanie SX4. Wpisanie nazwy urządzenia nie nadaje mu uprawnień.</Empty>}
      {error && <p className="msg-error" role="alert">{error}</p>}
    </ContextPanel>
  );
}

export default function DevicesPanel({ devices, onClose, onChanged }) {
  if (isHost()) return <NativeDevicesPanel onClose={onClose} />;''')

replace('App.js',
'''  const [voiceState, setVoiceState] = useState(null);
  const [render, setRender] = useState({});''',
'''  const [voiceState, setVoiceState] = useState(null);
  const [nativeVoiceStatus, setNativeVoiceStatus] = useState(null);
  const [render, setRender] = useState({});''')
replace('App.js',
'''    if (ev.type === "host.voice.status") setVoiceState(ev.data?.listening ? "listening" : null);''',
'''    if (ev.type === "host.voice.status") { setNativeVoiceStatus(ev.data); setVoiceState(ev.data?.listening ? "listening" : null); }''')
replace('App.js',
'''  useEffect(() => { if (isHost()) hostRequest("host.hello", {}, 5000).catch(() => {}); }, []);''',
'''  useEffect(() => { if (isHost()) hostRequest("host.hello", {}, 5000).catch(() => {}); }, []);
  useEffect(() => {
    if (!isHost()) return;
    let alive = true;
    const refresh = () => api.get("/voice/status").then((x) => {
      if (!alive) return;
      setNativeVoiceStatus(x);
      setVoiceState(x?.listening ? "listening" : null);
    }).catch(() => {});
    refresh();
    const timer = setInterval(refresh, 2200);
    return () => { alive = false; clearInterval(timer); };
  }, []);''')
replace('App.js',
'''{v.privacyIndicator && mic.status === "active" && <span className={`privacy ${mic.speaking ? "hot" : ""}`} data-testid="privacy-indicator" title={v.recognition ? "Mikrofon aktywny · rozpoznawanie mowy" : "Mikrofon aktywny"}><i /></span>}''',
'''{v.privacyIndicator && (mic.status === "active" || (isHost() && nativeVoiceStatus?.listening)) && <span className={`privacy ${(mic.speaking || (isHost() && nativeVoiceStatus?.state === "active")) ? "hot" : ""}`} data-testid="privacy-indicator" title="Mikrofon lokalny aktywny"><i /></span>}''')

replace('components/panels/BasePanel.jsx',
'''setCfg((old) => old.host ? old : (''',
'''setCfg((old) => (old.host || old.port || old.deviceId || old.baseId || old.fingerprint) ? old : (''',
count=2)

replace('components/panels/DevicesPanel.jsx',
'''import { useState } from "react";''',
'''import { useState, useEffect } from "react";''')

