// Subtle synthesized UI sounds (no assets). Disabled unless enabled in settings.
let ctx = null;
let cfg = { enabled: true, volume: 0.25 };
export const configureSound = (c) => { cfg = { ...cfg, ...c }; };

const VOICES = {
  tap: [[880, 0.05, "sine"]],
  open: [[520, 0.09, "sine"], [780, 0.12, "sine", 0.04]],
  close: [[660, 0.08, "sine"], [440, 0.1, "sine", 0.03]],
  send: [[620, 0.06, "triangle"], [930, 0.1, "sine", 0.05]],
  success: [[660, 0.1, "sine"], [990, 0.16, "sine", 0.07]],
  error: [[300, 0.16, "triangle"], [240, 0.2, "sine", 0.06]],
};

export function play(name) {
  if (!cfg.enabled || cfg.volume <= 0) return;
  try {
    ctx = ctx || new (window.AudioContext || window.webkitAudioContext)();
    const now = ctx.currentTime;
    for (const [f, d, type, delay = 0] of VOICES[name] || []) {
      const o = ctx.createOscillator(), g = ctx.createGain();
      o.type = type; o.frequency.value = f;
      g.gain.setValueAtTime(0, now + delay);
      g.gain.linearRampToValueAtTime(0.08 * cfg.volume, now + delay + 0.012);
      g.gain.exponentialRampToValueAtTime(0.0001, now + delay + d);
      o.connect(g).connect(ctx.destination);
      o.start(now + delay); o.stop(now + delay + d + 0.02);
    }
  } catch { /* audio unavailable */ }
}
