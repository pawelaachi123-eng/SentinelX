import { useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";

const WORDS = { listening: "Słucham", recognizing: "Rozpoznaję", speaking: "Mówię", thinking: "Analizuję", executing: "Wykonuję", success: "Gotowe", warning: "Uwaga", error: "Coś poszło nie tak", offline: "Offline" };

/** One discreet word under the core — only when the core is not idle. */
export function StateCaption({ state }) {
  const word = WORDS[state];
  return (
    <div className="state-caption" aria-live="polite" data-testid="core-state-caption">
      <AnimatePresence mode="wait">
        {word && (
          <motion.span key={state} className={`sc sc-${state}`} initial={{ opacity: 0, y: 6, letterSpacing: "0.5em" }} animate={{ opacity: 1, y: 0, letterSpacing: "0.32em" }} exit={{ opacity: 0, y: -4 }} transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}>
            {word}
          </motion.span>
        )}
      </AnimatePresence>
    </div>
  );
}

export function VoiceCaption({ caption, onDone }) {
  useEffect(() => { if (!caption) return; const t = setTimeout(onDone, caption.kind === "heard" ? 3200 : 2200); return () => clearTimeout(t); }, [caption, onDone]);
  return (
    <AnimatePresence>
      {caption && (
        <motion.div className={`voice-caption vc-${caption.kind}`} data-testid="voice-caption" initial={{ opacity: 0, y: 8, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 4 }} transition={{ type: "spring", stiffness: 300, damping: 28 }}>
          <i />{caption.kind === "ignored" ? `Pominięto (brak słowa aktywacyjnego): „${caption.text}”` : caption.text}
        </motion.div>
      )}
    </AnimatePresence>
  );
}

/** Real RSSI → 4 bars; null = no signal data. */
export function SignalBars({ rssi }) {
  const lvl = rssi == null ? 0 : rssi > -55 ? 4 : rssi > -65 ? 3 : rssi > -75 ? 2 : 1;
  return (
    <span className="bars" aria-label={rssi == null ? "brak sygnału" : `${rssi} dBm`}>
      {[1, 2, 3, 4].map((i) => <span key={i} className={i <= lvl ? "on" : ""} style={{ height: 4 + i * 3 }} />)}
    </span>
  );
}

export function DeviceDots({ devices }) {
  if (!devices.length) return null;
  return <span className="ddots">{devices.slice(0, 6).map((d) => <i key={d.id} className={d.online ? "on" : ""} />)}</span>;
}
