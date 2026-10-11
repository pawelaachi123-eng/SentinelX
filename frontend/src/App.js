import { useCallback, useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, LayoutGroup } from "framer-motion";
import { Toaster, toast } from "sonner";
import "@/App.css";
import { SettingsProvider, useSettings } from "@/context/SettingsContext";
import { useSentinelEvents } from "@/hooks/useSentinelEvents";
import { useMicLevel } from "@/hooks/useMicLevel";
import { api } from "@/lib/api";
import { play } from "@/lib/sound";
import CoreStage from "@/components/CoreStage";
import { Logo } from "@/components/Logo";
import ModuleTile from "@/components/ModuleTile";
import CommandDock from "@/components/CommandDock";
import BasePanel from "@/components/panels/BasePanel";
import DevicesPanel from "@/components/panels/DevicesPanel";
import TasksPanel from "@/components/panels/TasksPanel";
import SettingsPanel from "@/components/settings/SettingsPanel";
import { BootSequence, FirstRun } from "@/components/Intro";
import { IconBase, IconDevices, IconTasks, IconGear } from "@/components/icons";

function usePolling(fn, ms) {
  useEffect(() => { fn(); const t = setInterval(fn, ms); return () => clearInterval(t); }, [fn, ms]);
}

function Nexus() {
  const { settings, loaded, motion: m } = useSettings();
  const core = useRef(null);
  const [panel, setPanel] = useState(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [boot, setBoot] = useState(settings.general.bootIntro);
  const [tasks, setTasks] = useState([]);
  const [devices, setDevices] = useState([]);
  const [base, setBase] = useState(null);
  const [localState, setLocalState] = useState(null);
  const [render, setRender] = useState({});

  const loadTasks = useCallback(() => api.get("/tasks").then(setTasks).catch(() => {}), []);
  const loadDevices = useCallback(() => api.get("/devices").then(setDevices).catch(() => {}), []);
  const loadBase = useCallback(() => api.get("/base/status").then(setBase).catch(() => {}), []);
  usePolling(loadTasks, 30000);
  usePolling(loadDevices, settings.devices.refreshS * 1000);
  usePolling(loadBase, 10000);

  const notify = useCallback((t) => {
    const level = settings.notifications.toasts;
    const isErr = t.status === "error";
    if (level === "none" || (level === "errors" && !isErr)) return;
    (isErr ? toast.error : toast.success)(t.title, { description: isErr ? t.error : "Zadanie zakończone" });
    if (settings.general.desktopNotifications && document.hidden && Notification?.permission === "granted") new Notification("SENTINEL X", { body: `${isErr ? "Błąd" : "Gotowe"}: ${t.title}`, icon: "/brand/sentinel-mark.svg" });
  }, [settings.notifications.toasts, settings.general.desktopNotifications]);

  const { connected, coreState } = useSentinelEvents((ev) => {
    if (ev.type === "task.updated") {
      setTasks((ts) => [ev.data, ...ts.filter((x) => x.id !== ev.data.id)]);
      if (ev.data.status === "done" || ev.data.status === "error") { notify(ev.data); if (ev.data.kind !== "ai.query") play(ev.data.status === "done" ? "success" : "error"); }
    }
    if (ev.type === "tasks.cleared") loadTasks();
    if (ev.type === "devices.changed") loadDevices();
    if (ev.type === "base.report") { loadBase(); loadDevices(); }
  });

  const micStatus = useMicLevel(settings.voice.listening, settings.voice.sensitivity, (a, b, c) => core.current?.setAudio(a, b, c));
  const state = !connected ? "offline" : coreState !== "idle" ? coreState : localState || (micStatus === "active" ? "listening" : "idle");

  useEffect(() => { const t = boot && setTimeout(() => setBoot(false), 2600); return () => clearTimeout(t); }, [boot]);
  useEffect(() => {
    let off; const t = setInterval(() => { if (core.current?.onStats) { off = core.current.onStats(setRender); clearInterval(t); } }, 200);
    return () => { clearInterval(t); off?.(); };
  }, []);

  const running = tasks.filter((t) => t.status === "running" || t.status === "queued");
  const online = devices.filter((d) => d.online).length;
  const toggle = (p) => setPanel((cur) => (cur === p ? null : p));
  const enter = (d) => ({ initial: { opacity: 0, y: 14 }, animate: { opacity: boot ? 0 : 1, y: boot ? 14 : 0 }, transition: { ...m.panel, delay: boot ? 0 : d } });

  return (
    <div className="nexus" data-testid="nexus-root" data-core-state={state}>
      <CoreStage ref={core} state={state} />
      <div className="ambient" aria-hidden="true" />
      <LayoutGroup>
        <motion.header className="top-left" {...enter(0.05)}><Logo /></motion.header>

        <motion.div className="top-right" {...enter(0.12)}>
          {settings.voice.privacyIndicator && micStatus === "active" && <span className="privacy" data-testid="privacy-indicator" title="Mikrofon aktywny"><i /></span>}
          {settings.advanced.showFps && <span className="fps mono" data-testid="fps-counter">{render.fps ?? "—"} fps · {render.tier}</span>}
          {panel !== "tasks" && <ModuleTile id="tasks" icon={IconTasks} title="Tasks" className="tile-sm" onOpen={() => toggle("tasks")}
            meta={running.length ? `${running.length} aktywne` : tasks.length ? "bezczynny" : "brak zadań"} pulse={running.length ? "run" : null} />}
          <motion.button className="gear" data-testid="settings-open" aria-label="Ustawienia" onClick={() => { play("open"); setSettingsOpen(true); }}
            whileHover={{ rotate: 40 }} whileTap={{ scale: 0.9 }} transition={m.spring}><IconGear size={21} /></motion.button>
        </motion.div>

        <motion.div className="bottom-left" {...enter(0.2)}>
          {panel !== "base" && <ModuleTile id="base" icon={IconBase} title="Sentinel Base" onOpen={() => toggle("base")}
            meta={!base ? "…" : base.online ? `online · ${Object.keys(base.last?.sensors || {}).length} czujn.` : base.configured ? "offline" : "oczekiwanie"} pulse={base?.online ? "ok" : base?.configured ? "warn" : "off"} />}
          {panel !== "devices" && <ModuleTile id="devices" icon={IconDevices} title="Device Control" className="tile-alt" onOpen={() => toggle("devices")}
            meta={devices.length ? `${online}/${devices.length} online` : "brak urządzeń"} pulse={devices.length ? (online ? "ok" : "off") : null} />}
        </motion.div>

        <AnimatePresence>
          {panel === "base" && <BasePanel key="base" status={base} onClose={() => setPanel(null)} />}
          {panel === "devices" && <DevicesPanel key="devices" devices={devices} onChanged={loadDevices} onClose={() => setPanel(null)} />}
          {panel === "tasks" && <TasksPanel key="tasks" tasks={tasks} onChanged={loadTasks} onClose={() => setPanel(null)} />}
        </AnimatePresence>
      </LayoutGroup>

      <motion.div className="bottom-center" {...enter(0.28)}><CommandDock onLocalState={setLocalState} /></motion.div>

      <AnimatePresence>{settingsOpen && <SettingsPanel onClose={() => setSettingsOpen(false)} diag={{ render, connected, mic: micStatus }} />}</AnimatePresence>
      <AnimatePresence>{!boot && loaded && !settings.general.firstRunDone && <FirstRun />}</AnimatePresence>
      <AnimatePresence>{boot && <BootSequence />}</AnimatePresence>
      <Toaster position="top-center" theme="dark" toastOptions={{ className: "sx-toast" }} />
    </div>
  );
}

export default function App() {
  return <SettingsProvider><Nexus /></SettingsProvider>;
}
