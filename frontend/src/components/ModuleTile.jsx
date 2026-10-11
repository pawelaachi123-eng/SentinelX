import { motion } from "framer-motion";
import { useSettings } from "@/context/SettingsContext";
import { play } from "@/lib/sound";

/** Glass module tile that morphs (shared layoutId) into its contextual panel. */
export default function ModuleTile({ id, icon: Icon, title, meta, pulse, onOpen, open, className = "", visual }) {
  const { motion: m } = useSettings();
  return (
    <motion.button
      layoutId={`tile-${id}`}
      data-testid={`tile-${id}`}
      className={`tile glass ${open ? "tile-open" : ""} ${className}`}
      onClick={() => { play("open"); onOpen(); }}
      onPointerMove={(e) => {
        const r = e.currentTarget.getBoundingClientRect();
        e.currentTarget.style.setProperty("--mx", `${e.clientX - r.left}px`);
        e.currentTarget.style.setProperty("--my", `${e.clientY - r.top}px`);
      }}
      whileHover={{ y: m.hoverLift }}
      whileTap={{ scale: 0.97 }}
      transition={m.spring}
      aria-expanded={open}
    >
      <span className="tile-sheen" />
      <span className="tile-icon"><Icon size={19} /></span>
      <span className="tile-text">
        <span className="tile-title">{title}</span>
        <span className="tile-meta" data-testid={`tile-${id}-meta`}>
          {pulse && <i className={`dot dot-${pulse}`} />}{meta}
        </span>
      </span>
      {visual && <span className="tile-visual">{visual}</span>}
    </motion.button>
  );
}
