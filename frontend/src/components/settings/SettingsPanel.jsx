import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useSettings } from "@/context/SettingsContext";
import { IconClose } from "../icons";
import * as S from "./sections";
import { play } from "@/lib/sound";

const CATS = [
  ["general", "General", S.General], ["appearance", "Appearance", S.Appearance], ["core", "Core Customization", S.Core],
  ["motion", "Motion", S.Motion], ["voice", "Voice", S.Voice], ["devices", "Devices", S.Devices], ["base", "Sentinel Base", S.Base],
  ["notifications", "Notifications", S.Notifications], ["diagnostics", "Diagnostics", S.Diagnostics], ["advanced", "Advanced", S.Advanced],
];

export default function SettingsPanel({ onClose, diag }) {
  const { settings, update, motion: m } = useSettings();
  const [cat, setCat] = useState("general");
  useEffect(() => {
    const k = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", k); return () => window.removeEventListener("keydown", k);
  }, [onClose]);
  const [, , Section] = CATS.find((c) => c[0] === cat);
  return (
    <motion.div className="settings-scrim" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={onClose}>
      <motion.div className="settings glass" data-testid="settings-panel" role="dialog" aria-label="Ustawienia" onClick={(e) => e.stopPropagation()}
        initial={{ opacity: 0, x: 40, scale: 0.98 }} animate={{ opacity: 1, x: 0, scale: 1 }} exit={{ opacity: 0, x: 30, scale: 0.98 }} transition={m.panel}>
        <nav className="settings-nav">
          <h2>Ustawienia</h2>
          {CATS.map(([k, label], i) => (
            <motion.button key={k} data-testid={`settings-nav-${k}`} className={cat === k ? "on" : ""} onClick={() => { play("tap"); setCat(k); }}
              initial={{ opacity: 0, x: -8 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: 0.05 + i * m.stagger }}>
              {cat === k && <motion.span layoutId="nav-ind" className="nav-ind" transition={m.spring} />}
              <span>{label}</span>
            </motion.button>
          ))}
        </nav>
        <div className="settings-body">
          <button className="icon-btn settings-close" data-testid="settings-close" onClick={onClose} aria-label="Zamknij"><IconClose size={18} /></button>
          <AnimatePresence mode="wait">
            <motion.div key={cat} className="settings-section" initial="h" animate="s" exit={{ opacity: 0, y: -6, transition: { duration: 0.12 } }}
              variants={{ s: { transition: { staggerChildren: m.stagger } } }}>
              <motion.h3 variants={{ h: { opacity: 0, y: 6 }, s: { opacity: 1, y: 0 } }}>{CATS.find((c) => c[0] === cat)[1]}</motion.h3>
              <Section s={settings[cat]} up={(p) => update(cat, p)} diag={diag} micStatus={diag.mic} resetAll={(all) => update(null, all)} />
            </motion.div>
          </AnimatePresence>
        </div>
      </motion.div>
    </motion.div>
  );
}
