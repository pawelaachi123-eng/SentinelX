// Central motion parameter system. Every UI animation reads from here.
export const SPRINGS = {
  snappy: { type: "spring", stiffness: 520, damping: 38, mass: 0.8 },
  balanced: { type: "spring", stiffness: 340, damping: 30, mass: 0.9 },
  soft: { type: "spring", stiffness: 190, damping: 24, mass: 1 },
};
export const DURATION = { micro: 0.16, short: 0.28, medium: 0.45, long: 0.8 };
export const EASE = { out: [0.22, 1, 0.36, 1], inOut: [0.65, 0, 0.35, 1] };
export const STAGGER = 0.045;

export function motionTokens(motion, systemReduced) {
  const reduced = motion.reduced === "on" || (motion.reduced === "system" && systemReduced);
  const k = Math.max(0.4, motion.speed || 1);
  const base = SPRINGS[motion.uiSpring] || SPRINGS.balanced;
  const spring = reduced ? { type: "tween", duration: 0.12 } : { ...base, stiffness: base.stiffness * k * k };
  return {
    reduced,
    spring,
    panel: reduced ? { type: "tween", duration: 0.12 } : { ...SPRINGS.soft, stiffness: SPRINGS.soft.stiffness * k * k },
    fade: { duration: (reduced ? 0.1 : DURATION.short) / k, ease: EASE.out },
    stagger: reduced ? 0 : STAGGER / k,
    hoverLift: reduced ? 0 : -2,
  };
}
