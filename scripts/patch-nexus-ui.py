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
