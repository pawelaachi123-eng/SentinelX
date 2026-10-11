import { motion } from "framer-motion";
import { useEffect } from "react";
import { useSettings } from "@/context/SettingsContext";
import { IconClose } from "./icons";
import { play } from "@/lib/sound";

/** Contextual layer that grows out of its tile (shared layoutId) — never shifts the screen. */
export default function ContextPanel({ id, title, icon: Icon, onClose, children, className = "", actions }) {
  const { motion: m } = useSettings();
  useEffect(() => {
    const k = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", k);
    return () => window.removeEventListener("keydown", k);
  }, [onClose]);
  return (
    <motion.section layoutId={`tile-${id}`} transition={m.panel} className={`panel glass ${className}`} data-testid={`panel-${id}`} role="dialog" aria-label={title}>
      <motion.header className="panel-head" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: 0.08 }}>
        <span className="tile-icon"><Icon size={18} /></span>
        <h2>{title}</h2>
        <div className="panel-actions">{actions}</div>
        <button className="icon-btn" data-testid={`panel-${id}-close`} onClick={() => { play("close"); onClose(); }} aria-label="Zamknij"><IconClose size={17} /></button>
      </motion.header>
      <motion.div className="panel-body" initial="h" animate="s" variants={{ s: { transition: { staggerChildren: m.stagger, delayChildren: 0.1 } } }}>
        {children}
      </motion.div>
    </motion.section>
  );
}

export const Reveal = ({ children, className = "", ...p }) => (
  <motion.div className={className} variants={{ h: { opacity: 0, y: 8 }, s: { opacity: 1, y: 0 } }} transition={{ type: "spring", stiffness: 320, damping: 28 }} {...p}>{children}</motion.div>
);

export const Empty = ({ title, children, testid }) => (
  <Reveal className="empty" data-testid={testid}><strong>{title}</strong><p>{children}</p></Reveal>
);

export function ago(iso) {
  if (!iso) return "—";
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return `${Math.round(s)} s temu`;
  if (s < 3600) return `${Math.round(s / 60)} min temu`;
  if (s < 86400) return `${Math.round(s / 3600)} h temu`;
  return new Date(iso).toLocaleDateString();
}
