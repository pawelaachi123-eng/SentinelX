import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import { configureSound } from "@/lib/sound";

export const PALETTES = {
  ocean: { name: "Deep Ocean", a: "#4CC6D8", b: "#5E7FD8", deep: "#072642" },
  nexus: { name: "Nexus", a: "#59DCEB", b: "#8279E8", deep: "#0A1626" },
  aurora: { name: "Aurora", a: "#7CF0C9", b: "#6C8CF2", deep: "#08201F" },
  solstice: { name: "Solstice", a: "#F0C98C", b: "#9A7BE6", deep: "#1A1626" },
  graphite: { name: "Graphite", a: "#C6D4E6", b: "#7A88A8", deep: "#121A26" },
};

export const DEFAULTS = {
  general: { bootIntro: true, firstRunDone: false, desktopNotifications: false, confirmClear: true },
  appearance: { palette: "ocean", glass: 0.5, blur: 20, uiScale: 1 },
  core: { vitality: 0.5, deform: 1, macro: 1, meso: 1, micro: 1, light: 1, energy: 1, particles: 1, bloom: true, quality: "auto" },
  motion: { speed: 1, uiSpring: "balanced", reduced: "system" },
  voice: { listening: false, sensitivity: 1, privacyIndicator: true, recognition: false, wakeWord: "Sentinel", requireWake: true, language: "pl" },
  devices: { refreshS: 10 },
  base: { offline_after_s: 60 },
  notifications: { sounds: true, volume: 0.25, toasts: "all" },
  advanced: { prCap: 2, showFps: false },
};

const LS = "sentinel.settings.v1";
const merge = (src = {}) => Object.fromEntries(Object.entries(DEFAULTS).map(([k, v]) => [k, { ...v, ...(src[k] || {}) }]));
const Ctx = createContext(null);

export function SettingsProvider({ children }) {
  const [settings, setSettings] = useState(() => merge(JSON.parse(localStorage.getItem(LS) || "{}")));
  const [loaded, setLoaded] = useState(false);
  const [sysReduced, setSysReduced] = useState(() => window.matchMedia("(prefers-reduced-motion: reduce)").matches);
  const timer = useRef(null);

  useEffect(() => {
    api.get("/settings").then((s) => { if (s.updated_at) setSettings(merge(s)); }).catch(() => {}).finally(() => setLoaded(true));
    const mq = window.matchMedia("(prefers-reduced-motion: reduce)");
    const fn = (e) => setSysReduced(e.matches);
    mq.addEventListener("change", fn);
    return () => mq.removeEventListener("change", fn);
  }, []);

  const update = useCallback((section, patch) => {
    setSettings((prev) => {
      const next = section ? { ...prev, [section]: { ...prev[section], ...patch } } : merge(patch);
      localStorage.setItem(LS, JSON.stringify(next));
      clearTimeout(timer.current);
      timer.current = setTimeout(() => api.put("/settings", { data: next }).catch(() => {}), 350);
      return next;
    });
  }, []);

  useEffect(() => {
    const r = document.documentElement.style;
    const p = PALETTES[settings.appearance.palette] || PALETTES.ocean;
    r.setProperty("--acc-a", p.a); r.setProperty("--acc-b", p.b); r.setProperty("--deep", p.deep);
    r.setProperty("--glass", settings.appearance.glass); r.setProperty("--blur", `${settings.appearance.blur}px`);
    r.setProperty("font-size", `${16 * settings.appearance.uiScale}px`);
    configureSound({ enabled: settings.notifications.sounds, volume: settings.notifications.volume });
  }, [settings.appearance, settings.notifications]);

  const motion = useMemo(() => motionTokens(settings.motion, sysReduced), [settings.motion, sysReduced]);
  const value = useMemo(() => ({ settings, update, loaded, motion, palette: PALETTES[settings.appearance.palette] || PALETTES.ocean }), [settings, update, loaded, motion]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useSettings = () => useContext(Ctx);
