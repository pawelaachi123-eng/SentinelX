import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { LogoMark } from "./Logo";
import { useSettings } from "@/context/SettingsContext";

export function BootSequence() {
  return (
    <motion.div className="boot" data-testid="boot-sequence" initial={{ opacity: 1 }} exit={{ opacity: 0, transition: { duration: 0.9, ease: [0.65, 0, 0.35, 1] } }}>
      <motion.div initial={{ scale: 0.7, opacity: 0, filter: "blur(10px)" }} animate={{ scale: 1, opacity: 1, filter: "blur(0px)" }} transition={{ duration: 1.1, ease: [0.22, 1, 0.36, 1] }}>
        <LogoMark size={84} id="boot" />
      </motion.div>
      <motion.div className="boot-word" initial={{ opacity: 0, letterSpacing: "0.9em" }} animate={{ opacity: 1, letterSpacing: "0.42em" }} transition={{ delay: 0.4, duration: 1.2, ease: [0.22, 1, 0.36, 1] }}>SENTINEL X</motion.div>
      <motion.div className="boot-line" initial={{ scaleX: 0 }} animate={{ scaleX: 1 }} transition={{ delay: 0.6, duration: 1.4, ease: [0.65, 0, 0.35, 1] }} />
    </motion.div>
  );
}

const STEPS = [
  { t: "Witaj w SENTINEL X", d: "Centralny rdzeń to żywa reprezentacja systemu. Zmienia geometrię zależnie od tego, czy Sentinel słucha, myśli, czy wykonuje operacje." },
  { t: "Nasłuch i prywatność", d: "Mikrofon jest domyślnie wyłączony. Możesz pozwolić rdzeniowi reagować na rzeczywisty dźwięk — sterowanie znajdziesz w Ustawieniach → Voice.", voice: true },
  { t: "Sentinel Base i urządzenia", d: "Baza ESP32 wysyła raporty na endpoint push. Dopóki nie nadejdą dane, interfejs pokazuje uczciwy status ‚oczekiwanie’ — bez fikcyjnych odczytów." },
];

export function FirstRun({ onDone }) {
  const { update, settings } = useSettings();
  const [i, setI] = useState(0);
  const st = STEPS[i];
  const finish = () => { update("general", { firstRunDone: true }); onDone?.(); };
  return (
    <motion.div className="settings-scrim center" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
      <motion.div className="onboard glass" data-testid="first-run" initial={{ y: 30, opacity: 0, scale: 0.96 }} animate={{ y: 0, opacity: 1, scale: 1 }} exit={{ y: 20, opacity: 0 }} transition={{ type: "spring", stiffness: 220, damping: 24 }}>
        <div className="onboard-steps">{STEPS.map((_, k) => <span key={k} className={k <= i ? "on" : ""} />)}</div>
        <AnimatePresence mode="wait">
          <motion.div key={i} initial={{ opacity: 0, x: 16 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: -16 }} transition={{ duration: 0.3 }}>
            <h2>{st.t}</h2><p>{st.d}</p>
            {st.voice && (
              <label className="onboard-check">
                <button role="switch" aria-checked={settings.voice.listening} data-testid="first-run-voice-toggle" className={`switch ${settings.voice.listening ? "on" : ""}`} onClick={() => update("voice", { listening: !settings.voice.listening })}><span /></button>
                Pozwól rdzeniowi reagować na mikrofon
              </label>
            )}
          </motion.div>
        </AnimatePresence>
        <div className="onboard-actions">
          <button className="btn ghost" data-testid="first-run-skip" onClick={finish}>Pomiń</button>
          <button className="btn primary" data-testid="first-run-next" onClick={() => (i < STEPS.length - 1 ? setI(i + 1) : finish())}>{i < STEPS.length - 1 ? "Dalej" : "Rozpocznij"}</button>
        </div>
      </motion.div>
    </motion.div>
  );
}
