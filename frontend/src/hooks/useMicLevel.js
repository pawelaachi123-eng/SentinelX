import { useEffect, useRef, useState } from "react";

/** Real microphone analysis (no speech recognition). Feeds 3 bands to the core. */
export function useMicLevel(enabled, sensitivity, onBands) {
  const [status, setStatus] = useState("off"); // off | requesting | active | denied | unavailable
  const cb = useRef(onBands);
  cb.current = onBands;

  useEffect(() => {
    if (!enabled) { setStatus("off"); cb.current?.(0, 0, 0); return; }
    if (!navigator.mediaDevices?.getUserMedia) { setStatus("unavailable"); return; }
    let stream, ctx, raf, cancelled = false;
    setStatus("requesting");
    navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } }).then((s) => {
      if (cancelled) { s.getTracks().forEach((t) => t.stop()); return; }
      stream = s;
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      const an = ctx.createAnalyser(); an.fftSize = 512; an.smoothingTimeConstant = 0.7;
      ctx.createMediaStreamSource(s).connect(an);
      const bins = new Uint8Array(an.frequencyBinCount);
      const band = (a, b) => { let m = 0; for (let i = a; i < b; i++) m += bins[i]; return m / ((b - a) * 255); };
      setStatus("active");
      const tick = () => {
        an.getByteFrequencyData(bins);
        const g = 1.6 * sensitivity;
        cb.current?.(Math.min(band(1, 8) * g, 1), Math.min(band(8, 40) * g * 1.4, 1), Math.min(band(40, 120) * g * 2.2, 1));
        raf = requestAnimationFrame(tick);
      };
      tick();
    }).catch((e) => setStatus(e?.name === "NotAllowedError" ? "denied" : "unavailable"));
    return () => { cancelled = true; cancelAnimationFrame(raf); stream?.getTracks().forEach((t) => t.stop()); ctx?.close(); cb.current?.(0, 0, 0); };
  }, [enabled, sensitivity]);

  return status;
}
