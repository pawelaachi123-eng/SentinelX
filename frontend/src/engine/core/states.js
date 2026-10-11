// Parameter sets per AI state. The renderer blends toward the active target every frame
// (exponential damping), so transitions never restart motion.
export const STATE_PARAMS = {
  idle:      { speed: 1.0,  macro: 1.0,  meso: 1.0,  micro: 1.0, energy: 0.45, glow: 0.55, tension: 0, sat: 1.0, pulse: 0, err: 0 },
  listening: { speed: 1.15, macro: 1.0,  meso: 1.25, micro: 1.4, energy: 0.7,  glow: 0.75, tension: 0, sat: 1.0, pulse: 0, err: 0 },
  thinking:  { speed: 1.8,  macro: 1.3,  meso: 1.7,  micro: 1.3, energy: 1.25, glow: 0.9,  tension: 0.1, sat: 1.0, pulse: 0.25, err: 0 },
  executing: { speed: 1.45, macro: 1.1,  meso: 1.3,  micro: 1.2, energy: 1.0,  glow: 0.85, tension: 0, sat: 1.0, pulse: 1.0, err: 0 },
  success:   { speed: 1.1,  macro: 1.05, meso: 1.1,  micro: 1.0, energy: 0.9,  glow: 1.0,  tension: 0, sat: 1.0, pulse: 0, err: 0 },
  error:     { speed: 0.85, macro: 0.9,  meso: 1.2,  micro: 1.9, energy: 0.55, glow: 0.6,  tension: 1, sat: 0.9, pulse: 0, err: 1 },
  offline:   { speed: 0.45, macro: 0.8,  meso: 0.7,  micro: 0.6, energy: 0.15, glow: 0.3,  tension: 0, sat: 0.35, pulse: 0, err: 0 },
};
STATE_PARAMS.recognizing = STATE_PARAMS.listening;
STATE_PARAMS.warning = { ...STATE_PARAMS.error, tension: 0.5, err: 0.5 };

export const BLEND_RATE = { default: 2.2, success: 4.0, error: 1.6 };

export function damp(current, target, rate, dt) {
  return target + (current - target) * Math.exp(-rate * dt);
}
