import { useEffect, useState } from "react";
import { Row, Toggle, Slider, Segmented } from "./controls";
import { PALETTES, DEFAULTS } from "@/context/SettingsContext";
import { api } from "@/lib/api";
import { speak } from "@/lib/speech";

const pct = (v) => `${Math.round(v * 100)}%`;

export function General({ s, up }) {
  const askNotify = async (v) => {
    if (v && "Notification" in window && Notification.permission !== "granted") {
      const p = await Notification.requestPermission();
      if (p !== "granted") return up({ desktopNotifications: false });
    }
    up({ desktopNotifications: v });
  };
  return (<>
    <Row label="Animacja startowa" hint="Sekwencja inicjalizacji rdzenia przy uruchomieniu"><Toggle testid="set-boot-intro" value={s.bootIntro} onChange={(v) => up({ bootIntro: v })} /></Row>
    <Row label="Powiadomienia systemowe" hint="Informuj o zakończonych zadaniach, gdy okno jest w tle"><Toggle testid="set-desktop-notify" value={s.desktopNotifications} onChange={askNotify} /></Row>
    <Row label="Potwierdzaj czyszczenie historii"><Toggle testid="set-confirm-clear" value={s.confirmClear} onChange={(v) => up({ confirmClear: v })} /></Row>
    <Row label="Ekran pierwszego uruchomienia" hint="Pokaż ponownie przewodnik startowy"><button className="btn" data-testid="set-rerun-onboarding" onClick={() => up({ firstRunDone: false })}>Pokaż</button></Row>
  </>);
}

export function Appearance({ s, up }) {
  return (<>
    <Row label="Paleta" hint="Maksymalnie dwa akcenty na głębokim tle">
      <div className="swatches">{Object.entries(PALETTES).map(([k, p]) => (
        <button key={k} data-testid={`set-palette-${k}`} className={`swatch ${s.palette === k ? "on" : ""}`} onClick={() => up({ palette: k })} title={p.name}
          style={{ background: `linear-gradient(135deg, ${p.a}, ${p.b})` }} />))}</div>
    </Row>
    <Row label="Krycie szkła"><Slider testid="set-glass" value={s.glass} min={0.2} max={0.9} onChange={(v) => up({ glass: v })} fmt={pct} /></Row>
    <Row label="Rozmycie tła"><Slider testid="set-blur" value={s.blur} min={0} max={40} step={1} onChange={(v) => up({ blur: v })} fmt={(v) => `${v}px`} /></Row>
    <Row label="Skala interfejsu"><Slider testid="set-ui-scale" value={s.uiScale} min={0.85} max={1.3} step={0.05} onChange={(v) => up({ uiScale: v })} fmt={pct} /></Row>
  </>);
}

export function Core({ s, up }) {
  const v = s.vitality ?? 0.5;
  return (<>
    <Row label="Charakter rdzenia" hint="Jeden suwak: od spokojnego, powolnego oddechu do żywej, energicznej ewolucji">
      <div className="vitality" data-testid="set-core-vitality-wrap">
        <span>Spokojny</span>
        <Slider testid="set-core-vitality" value={v} min={0} max={1} step={0.01} onChange={(x) => up({ vitality: x })} fmt={(x) => (x < 0.34 ? "spokój" : x < 0.67 ? "balans" : "żywy")} />
        <span>Żywy</span>
      </div>
    </Row>
    <Row label="Intensywność deformacji" hint="Globalny mnożnik wszystkich pól"><Slider testid="set-core-deform" value={s.deform} max={1.8} onChange={(v) => up({ deform: v })} fmt={pct} /></Row>
    <Row label="Macro morphing" hint="Wolna zmiana sylwetki"><Slider testid="set-core-macro" value={s.macro} onChange={(v) => up({ macro: v })} fmt={pct} /></Row>
    <Row label="Meso deformation" hint="Niezależne wypukłości i zapadnięcia"><Slider testid="set-core-meso" value={s.meso} onChange={(v) => up({ meso: v })} fmt={pct} /></Row>
    <Row label="Micro surface" hint="Drobne fale powierzchni"><Slider testid="set-core-micro" value={s.micro} onChange={(v) => up({ micro: v })} fmt={pct} /></Row>
    <Row label="Dynamika światła"><Slider testid="set-core-light" value={s.light} onChange={(v) => up({ light: v })} fmt={pct} /></Row>
    <Row label="Przepływ energii"><Slider testid="set-core-energy" value={s.energy} onChange={(v) => up({ energy: v })} fmt={pct} /></Row>
    <Row label="Gęstość cząsteczek"><Slider testid="set-core-particles" value={s.particles} min={0} max={1.5} step={0.1} onChange={(v) => up({ particles: v })} fmt={pct} /></Row>
    <Row label="Bloom"><Toggle testid="set-core-bloom" value={s.bloom} onChange={(v) => up({ bloom: v })} /></Row>
    <Row label="Jakość renderowania" hint="Auto adaptuje szczegółowość do wydajności GPU">
      <Segmented testid="set-core-quality" value={s.quality} onChange={(v) => up({ quality: v })} options={[["auto", "Auto"], ["high", "Wysoka"], ["medium", "Średnia"], ["low", "Niska"]]} />
    </Row>
  </>);
}

export function Motion({ s, up }) {
  return (<>
    <Row label="Szybkość animacji"><Slider testid="set-motion-speed" value={s.speed} min={0.4} max={1.8} onChange={(v) => up({ speed: v })} fmt={(v) => `${v.toFixed(2)}×`} /></Row>
    <Row label="Charakter sprężyn UI"><Segmented testid="set-motion-spring" value={s.uiSpring} onChange={(v) => up({ uiSpring: v })} options={[["snappy", "Sprężysty"], ["balanced", "Zbalansowany"], ["soft", "Miękki"]]} /></Row>
    <Row label="Ograniczony ruch" hint="System = zgodnie z ustawieniem Windows"><Segmented testid="set-motion-reduced" value={s.reduced} onChange={(v) => up({ reduced: v })} options={[["system", "System"], ["on", "Włączony"], ["off", "Wyłączony"]]} /></Row>
  </>);
}

export function Voice({ s, up, micStatus }) {
  const label = { off: "Wyłączony", requesting: "Prośba o dostęp…", active: "Aktywny", denied: "Odmowa dostępu", unavailable: "Mikrofon niedostępny" }[micStatus];
  return (<>
    <Row label="Nasłuchiwanie" hint="Rdzeń reaguje na rzeczywisty sygnał audio z mikrofonu"><Toggle testid="set-voice-listening" value={s.listening} onChange={(v) => up({ listening: v })} /></Row>
    <Row label="Stan mikrofonu"><span className={`state-tag st-${micStatus}`} data-testid="voice-mic-status">{label}</span></Row>
    <Row label="Czułość"><Slider testid="set-voice-sensitivity" value={s.sensitivity} min={0.3} max={2.5} onChange={(v) => up({ sensitivity: v })} fmt={(v) => `${v.toFixed(1)}×`} /></Row>
    <Row label="Rozpoznawanie mowy" hint="Wypowiedzi są wykrywane lokalnie (VAD) i transkrybowane modelem Whisper (whisper-1), a następnie wykonywane jako polecenia"><Toggle testid="set-voice-recognition" value={s.recognition} onChange={(v) => up({ recognition: v, listening: v ? true : s.listening })} /></Row>
    <Row label="Wymagaj komendy aktywacyjnej" hint="Polecenie zostanie wykonane tylko, gdy zaczyna się od słowa aktywacyjnego"><Toggle testid="set-voice-require-wake" value={s.requireWake} onChange={(v) => up({ requireWake: v })} /></Row>
    <Row label="Słowo aktywacyjne"><input className="text-in" data-testid="set-voice-wake-word" value={s.wakeWord} maxLength={24} onChange={(e) => up({ wakeWord: e.target.value })} /></Row>
    <Row label="Język mowy"><Segmented testid="set-voice-language" value={s.language} onChange={(v) => up({ language: v })} options={[["pl", "Polski"], ["en", "English"], ["de", "Deutsch"]]} /></Row>
    <Row label="Głos Sentinela" hint="Odpowiedzi są czytane spokojnym głosem (OpenAI TTS). Głosy są zoptymalizowane pod angielski — w języku polskim słychać lekki akcent"><Toggle testid="set-voice-speak" value={s.speakReplies} onChange={(v) => up({ speakReplies: v })} /></Row>
    <Row label="Barwa głosu"><Segmented testid="set-voice-tts" value={s.ttsVoice} onChange={(v) => up({ ttsVoice: v })} options={[["sage", "Sage · spokojny"], ["echo", "Echo · gładki"], ["onyx", "Onyx · głęboki"], ["alloy", "Alloy"], ["coral", "Coral"]]} /></Row>
    <Row label="Tempo mowy"><Slider testid="set-voice-tts-speed" value={s.ttsSpeed} min={0.7} max={1.2} step={0.05} onChange={(v) => up({ ttsSpeed: v })} fmt={(v) => `${v.toFixed(2)}×`} /></Row>
    <Row label="Głośność głosu"><Slider testid="set-voice-tts-volume" value={s.ttsVolume} min={0} max={1} onChange={(v) => up({ ttsVolume: v })} fmt={pct} /></Row>
    <Row label="Jakość głosu"><Segmented testid="set-voice-tts-model" value={s.ttsModel} onChange={(v) => up({ ttsModel: v })} options={[["tts-1", "Szybka"], ["tts-1-hd", "HD"]]} /></Row>
    <Row label="Próbka"><button className="btn" data-testid="set-voice-tts-test" onClick={() => speak("Jestem Sentinel. Wszystkie systemy działają spokojnie i stabilnie.", { voice: s.ttsVoice, speed: s.ttsSpeed, model: s.ttsModel, volume: s.ttsVolume }).catch(() => {})}>Odsłuchaj</button></Row>
    <Row label="Wskaźnik prywatności" hint="Dyskretna kropka w prawym górnym rogu, gdy mikrofon jest aktywny"><Toggle testid="set-voice-privacy" value={s.privacyIndicator} onChange={(v) => up({ privacyIndicator: v })} /></Row>
  </>);
}

export function Devices({ s, up }) {
  return (<Row label="Odświeżanie listy" hint="Jak często pobierać stany urządzeń"><Slider testid="set-devices-refresh" value={s.refreshS} min={3} max={60} step={1} onChange={(v) => up({ refreshS: v })} fmt={(v) => `${v} s`} /></Row>);
}

export function Base({ s, up }) {
  const [cfg, setCfg] = useState(null);
  useEffect(() => { api.get("/base/ingest-config").then(setCfg).catch(() => {}); }, []);
  return (<>
    <Row label="Próg offline" hint="Po tylu sekundach bez raportu Base i urządzenia są offline"><Slider testid="set-base-offline" value={s.offline_after_s} min={15} max={600} step={5} onChange={(v) => up({ offline_after_s: v })} fmt={(v) => `${v} s`} /></Row>
    <Row label="Endpoint push"><code className="mono" data-testid="set-base-endpoint">{api.base}{cfg?.path}</code></Row>
    <Row label="Nagłówek autoryzacji"><code className="mono">{cfg ? `${cfg.header}: ${cfg.token}` : "—"}</code></Row>
    <Row label="Kolejka komend" hint="Base pobiera GET /api/base/commands/pending i potwierdza POST …/{id}/ack"><code className="mono">polling</code></Row>
  </>);
}

export function Notifications({ s, up }) {
  return (<>
    <Row label="Dźwięki interfejsu"><Toggle testid="set-sounds" value={s.sounds} onChange={(v) => up({ sounds: v })} /></Row>
    <Row label="Głośność"><Slider testid="set-volume" value={s.volume} min={0} max={1} onChange={(v) => up({ volume: v })} fmt={pct} /></Row>
    <Row label="Szczegółowość powiadomień"><Segmented testid="set-toasts" value={s.toasts} onChange={(v) => up({ toasts: v })} options={[["all", "Wszystkie"], ["errors", "Tylko błędy"], ["none", "Brak"]]} /></Row>
  </>);
}

export function Diagnostics({ diag }) {
  const [d, setD] = useState(null);
  const [err, setErr] = useState(null);
  useEffect(() => {
    const load = () => api.get("/diagnostics").then((x) => { setD(x); setErr(null); }).catch((e) => setErr(String(e.message || e)));
    load(); const t = setInterval(load, 4000); return () => clearInterval(t);
  }, []);
  const r = diag.render || {};
  const items = [
    ["FPS", r.fps], ["Czas klatki", r.frameMs != null ? `${r.frameMs} ms` : "—"], ["Poziom jakości", r.tier], ["Wierzchołki rdzenia", r.vertices?.toLocaleString()],
    ["GPU", r.gpu], ["Utracone konteksty GPU", r.contextLosses], ["Strumień zdarzeń", diag.connected ? "połączony" : "rozłączony"],
    ["Backend", d?.backend || (err ? "błąd" : "…")], ["Baza danych", d?.database], ["Model AI", d?.llm_model], ["Klucz LLM", d ? (d.llm_configured ? "skonfigurowany" : "brak") : "—"],
    ["Subskrybenci zdarzeń", d?.event_subscribers], ["Host natywny", window.chrome?.webview ? "WinUI 3 · WebView2" : "przeglądarka"], ["Zadania / błędy", d ? `${d.tasks_total} / ${d.tasks_failed}` : "—"], ["Raporty Base", d?.base_reports], ["Mikrofon", diag.mic],
  ];
  return (<div className="diag-grid" data-testid="diagnostics-grid">{items.map(([k, v]) => <div key={k}><span>{k}</span><b>{v ?? "—"}</b></div>)}{err && <p className="task-err">{err}</p>}</div>);
}

export function Advanced({ s, up, resetAll }) {
  const exp = () => {
    const blob = new Blob([localStorage.getItem("sentinel.settings.v1") || "{}"], { type: "application/json" });
    const a = Object.assign(document.createElement("a"), { href: URL.createObjectURL(blob), download: "sentinel-settings.json" }); a.click();
  };
  return (<>
    <Row label="Limit gęstości pikseli" hint="Niższa wartość odciąża GPU na ekranach 4K"><Slider testid="set-pr-cap" value={s.prCap} min={0.75} max={2} step={0.25} onChange={(v) => up({ prCap: v })} fmt={(v) => `${v}×`} /></Row>
    <Row label="Licznik FPS na ekranie"><Toggle testid="set-show-fps" value={s.showFps} onChange={(v) => up({ showFps: v })} /></Row>
    <Row label="Eksport ustawień"><button className="btn" data-testid="set-export" onClick={exp}>Eksportuj JSON</button></Row>
    <Row label="Przywróć domyślne"><button className="btn danger" data-testid="set-reset" onClick={() => window.confirm("Przywrócić wszystkie ustawienia?") && resetAll({ ...DEFAULTS, general: { ...DEFAULTS.general, firstRunDone: true } })}>Resetuj</button></Row>
  </>);
}
