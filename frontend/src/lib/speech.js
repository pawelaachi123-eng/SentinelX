import { api } from "@/lib/api";

// Sentinel voice output: backend TTS -> <audio> -> analyser driving the core while speaking.
let current = null;

export function stopSpeaking() {
  if (!current) return;
  current.audio.pause();
  current.cleanup();
  current = null;
}

export async function speak(text, { voice, speed, model, volume = 1 }, { onStart, onEnd } = {}) {
  stopSpeaking();
  const r = await fetch(`${api.base}/voice/speak`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text, voice, speed, model }),
  });
  if (!r.ok) throw new Error(await r.text());
  const url = URL.createObjectURL(await r.blob());
  const audio = new Audio(url);
  audio.volume = volume;
  const ctx = new (window.AudioContext || window.webkitAudioContext)();
  const an = ctx.createAnalyser(); an.fftSize = 512; an.smoothingTimeConstant = 0.75;
  ctx.createMediaElementSource(audio).connect(an);
  an.connect(ctx.destination);
  const bins = new Uint8Array(an.frequencyBinCount);
  let raf;
  const band = (a, b) => { let m = 0; for (let i = a; i < b; i++) m += bins[i]; return m / ((b - a) * 255); };
  const tick = () => {
    an.getByteFrequencyData(bins);
    // softer than mic input: speaking should feel calm, not jittery
    window.__sentinelCore?.setAudio(band(1, 10) * 0.9, band(10, 50) * 0.8, band(50, 140) * 0.6);
    raf = requestAnimationFrame(tick);
  };
  const entry = {
    audio,
    cleanup: () => { window.__sentinelSpeaking = false; cancelAnimationFrame(raf); window.__sentinelCore?.setAudio(0, 0, 0); URL.revokeObjectURL(url); ctx.close(); onEnd?.(); },
  };
  current = entry;
  audio.onended = () => { if (current === entry) { entry.cleanup(); current = null; } };
  await ctx.resume();
  await audio.play();
  window.__sentinelSpeaking = true;
  onStart?.();
  tick();
}
