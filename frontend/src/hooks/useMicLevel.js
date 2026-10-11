import { useEffect, useRef, useState } from "react";

const pickMime = () => ["audio/webm;codecs=opus", "audio/webm", "audio/mp4"].find((m) => window.MediaRecorder?.isTypeSupported?.(m)) || "";

/**
 * Real microphone pipeline: 3-band analysis for the core + optional voice-activity
 * segmentation (MediaRecorder) that hands finished utterances to onSegment(blob).
 */
export function useMicLevel(enabled, sensitivity, onBands, { recognition = false, onSegment } = {}) {
  const [status, setStatus] = useState("off"); // off | requesting | active | denied | unavailable
  const [speaking, setSpeaking] = useState(false);
  const cb = useRef(onBands);
  const seg = useRef(onSegment);
  cb.current = onBands;
  seg.current = onSegment;

  useEffect(() => {
    if (!enabled) { setStatus("off"); cb.current?.(0, 0, 0); return; }
    if (!navigator.mediaDevices?.getUserMedia) { setStatus("unavailable"); return; }
    let stream, ctx, raf, rec, cancelled = false;
    let chunks = [], startedAt = 0, quietSince = 0;
    setStatus("requesting");
    navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } }).then((s) => {
      if (cancelled) { s.getTracks().forEach((t) => t.stop()); return; }
      stream = s;
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      const an = ctx.createAnalyser(); an.fftSize = 1024; an.smoothingTimeConstant = 0.7;
      ctx.createMediaStreamSource(s).connect(an);
      const bins = new Uint8Array(an.frequencyBinCount);
      const wave = new Float32Array(an.fftSize);
      const band = (a, b) => { let m = 0; for (let i = a; i < b; i++) m += bins[i]; return m / ((b - a) * 255); };
      const mime = pickMime();
      const canRecord = recognition && window.MediaRecorder;
      const thr = 0.03 / sensitivity;

      const stopRec = (keep) => {
        if (!rec) return;
        const r = rec; rec = null; setSpeaking(false);
        r.onstop = () => {
          const blob = new Blob(chunks, { type: mime || "audio/webm" }); chunks = [];
          if (keep && blob.size > 2000) seg.current?.(blob);
        };
        r.stop();
      };

      setStatus("active");
      const tick = () => {
        an.getByteFrequencyData(bins);
        const g = 1.6 * sensitivity;
        cb.current?.(Math.min(band(1, 16) * g, 1), Math.min(band(16, 80) * g * 1.4, 1), Math.min(band(80, 240) * g * 2.2, 1));
        if (canRecord && !document.hidden) {
          an.getFloatTimeDomainData(wave);
          let sum = 0; for (let i = 0; i < wave.length; i++) sum += wave[i] * wave[i];
          const rms = Math.sqrt(sum / wave.length);
          const now = performance.now();
          if (!rec && rms > thr) {
            rec = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined);
            rec.ondataavailable = (e) => e.data.size && chunks.push(e.data);
            rec.start(250); startedAt = now; quietSince = 0; setSpeaking(true);
          } else if (rec) {
            if (rms < thr * 0.7) quietSince = quietSince || now; else quietSince = 0;
            const dur = now - startedAt;
            if ((quietSince && now - quietSince > 900) || dur > 15000) stopRec(dur > 600);
          }
        }
        raf = requestAnimationFrame(tick);
      };
      tick();
    }).catch((e) => setStatus(e?.name === "NotAllowedError" ? "denied" : "unavailable"));
    return () => {
      cancelled = true; cancelAnimationFrame(raf);
      if (rec) { rec.onstop = null; try { rec.stop(); } catch { /* already stopped */ } }
      stream?.getTracks().forEach((t) => t.stop()); ctx?.close(); cb.current?.(0, 0, 0); setSpeaking(false);
    };
  }, [enabled, sensitivity, recognition]);

  return { status, speaking };
}
