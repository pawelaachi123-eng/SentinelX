import { motion } from "framer-motion";

export const MARK_PATHS = {
  a: "M8.6 7.4c8.4 1.4 15.6 7.6 20.6 15.2 4.2 6.4 8.2 12.4 10.6 17.8-6.8-1.6-13.4-6.6-18.2-13.2C17 21 11.4 14.2 8.6 7.4z",
  b: "M39.6 6.8c-1.8 7.2-7 13.8-13 18-6 4.4-12.8 9.6-17.8 15.4 1.2-6.4 5.8-12.6 11.6-16.8 6.2-4.6 12.6-10.8 19.2-16.6z",
};

/** SENTINEL X mark: two asymmetric fluid blades crossing around an offset nucleus. */
export function LogoMark({ size = 34, animated = true, id = "lm" }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true" className="logo-mark">
      <defs>
        <linearGradient id={`${id}-a`} x1="8" y1="6" x2="40" y2="42" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="var(--acc-a)" /><stop offset="1" stopColor="var(--acc-b)" />
        </linearGradient>
        <linearGradient id={`${id}-b`} x1="40" y1="6" x2="8" y2="42" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#EAF7FF" stopOpacity=".95" /><stop offset="1" stopColor="var(--acc-b)" stopOpacity=".55" />
        </linearGradient>
        <radialGradient id={`${id}-n`}><stop offset="0" stopColor="#fff" /><stop offset="1" stopColor="var(--acc-a)" /></radialGradient>
      </defs>
      <motion.path d={MARK_PATHS.a} fill={`url(#${id}-a)`} initial={animated ? { pathLength: 0, opacity: 0 } : false} animate={{ pathLength: 1, opacity: 1 }} transition={{ duration: 1.1, ease: [0.22, 1, 0.36, 1] }} />
      <motion.path d={MARK_PATHS.b} fill={`url(#${id}-b)`} initial={animated ? { opacity: 0, rotate: -14 } : false} animate={{ opacity: 1, rotate: 0 }} style={{ originX: "50%", originY: "50%" }} transition={{ duration: 1.2, delay: 0.15, ease: [0.22, 1, 0.36, 1] }} />
      <motion.circle cx="25.4" cy="23.2" r="2.6" fill={`url(#${id}-n)`} initial={animated ? { scale: 0 } : false} animate={{ scale: [1, 1.18, 1] }} transition={{ duration: 4.2, repeat: Infinity, ease: "easeInOut" }} />
    </svg>
  );
}

export function Logo() {
  return (
    <div className="logo" data-testid="app-logo">
      <LogoMark />
      <div className="logo-word">
        <span className="logo-name">SENTINEL</span>
        <span className="logo-x">X</span>
      </div>
    </div>
  );
}
