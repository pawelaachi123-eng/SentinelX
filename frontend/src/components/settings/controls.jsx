import { motion } from "framer-motion";

export function Row({ label, hint, children, testid }) {
  return (
    <motion.div className="set-row" data-testid={testid} variants={{ h: { opacity: 0, x: 10 }, s: { opacity: 1, x: 0 } }}>
      <div className="set-label"><b>{label}</b>{hint && <span>{hint}</span>}</div>
      <div className="set-ctl">{children}</div>
    </motion.div>
  );
}

export const Toggle = ({ value, onChange, testid }) => (
  <button role="switch" aria-checked={value} data-testid={testid} className={`switch ${value ? "on" : ""}`} onClick={() => onChange(!value)}><span /></button>
);

export function Slider({ value, onChange, min = 0, max = 2, step = 0.05, testid, fmt = (v) => v.toFixed(2) }) {
  const pct = ((value - min) / (max - min)) * 100;
  return (
    <div className="slider">
      <input type="range" min={min} max={max} step={step} value={value} data-testid={testid} style={{ "--p": `${pct}%` }} onChange={(e) => onChange(parseFloat(e.target.value))} />
      <output>{fmt(value)}</output>
    </div>
  );
}

export const Segmented = ({ value, options, onChange, testid }) => (
  <div className="seg" data-testid={testid}>
    {options.map(([v, l]) => (
      <button key={v} className={value === v ? "on" : ""} data-testid={`${testid}-${v}`} onClick={() => onChange(v)}>
        {value === v && <motion.span layoutId={`seg-${testid}`} className="seg-ind" transition={{ type: "spring", stiffness: 500, damping: 36 }} />}
        <span className="seg-l">{l}</span>
      </button>
    ))}
  </div>
);
