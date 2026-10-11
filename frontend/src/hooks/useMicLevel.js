import { useEffect, useRef, useState } from "react";

const pickMime = () => ["audio/webm;codecs=opus", "audio/webm", "audio/mp4"].find((m) => window.MediaRecorder?.isTypeSupported?.(m)) || "";

/**
 * Real microphone pipeline: 3-band analysis for the core + optional voice-activity
 * segmentation (MediaRecorder) that hands finished utterances to onSegment(blob).
 */
export function useMicLevel(enabled, sensitivity, onBands, { recognition = false, onSegment, interrupt = false, onInterrupt } = {}) {
  const [status, setStatus] = useState("off"); // off | requesting | active | denied | unavailable
  const [speaking, setSpeaking] = useState(false);
  const cb = useRef(onBands);
  const seg = useRef(onSegment);
  const segInt = useRef(onInterrupt);
  segInt.current = onInterrupt;
  cb.current = onBands;
  seg.current = onSegment;

  useEffect(() => {
    if (!enabled) { setStatus("off"); cb.current?.(0, 0, 0); return; }
    if (!navigator.mediaDevices?.getUserMedia) { setStatus("unavailable"); return; }
    let stream, ctx, raf, rec, cancelled = false;
    let chunks = [], startedAt = 0, quietSince = 0, duringSpeech = false;
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
      const canRecord = (recognition || interrupt) && window.MediaRecorder;
      const thr = 0.03 / sensitivity;

      const stopRec = (keep) => {
        if (!rec) return;
        const r = rec; rec = null; setSpeaking(false);
        const interruptSeg = duringSpeech;
        r.onstop = () => {
          const blob = new Blob(chunks, { type: mime || "audio/webm" }); chunks = [];
          if (!keep || blob.size < 2000) return;
          if (interruptSeg) segInt.current?.(blob); else if (recognition) seg.current?.(blob);
        };
        r.stop();
      };

      setStatus("active");
      const tick = () => {
        an.getByteFrequencyData(bins);
        const g = 1.6 * sensitivity;
        cb.current?.(Math.min(band(1, 16) * g, 1), Math.min(band(16, 80) * g * 1.4, 1), Math.min(band(80, 240) * g * 2.2, 1));
        const speakingNow = !!window.__sentinelSpeaking;
        if (canRecord && !document.hidden && (!speakingNow || interrupt || rec)) {
          an.getFloatTimeDomainData(wave);
          let sum = 0; for (let i = 0; i < wave.length; i++) sum += wave[i] * wave[i];
          const rms = Math.sqrt(sum / wave.length);
          const now = performance.now();
          const t2 = speakingNow ? thr * 2.2 : thr;
          if (!rec && rms > t2 && (speakingNow ? interrupt : recognition)) {
            duringSpeech = speakingNow;
            rec = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined);
            rec.ondataavailable = (e) => e.data.size && chunks.push(e.data);
            rec.start(250); startedAt = now; quietSince = 0; setSpeaking(true);
          } else if (rec) {
            if (rms < t2 * 0.7) quietSince = quietSince || now; else quietSince = 0;
            const dur = now - startedAt;
            const maxDur = duringSpeech ? 4000 : 15000;
            if ((quietSince && now - quietSince > (duringSpeech ? 500 : 900)) || dur > maxDur) stopRec(dur > (duringSpeech ? 350 : 600));
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
  }, [enabled, sensitivity, recognition, interrupt]);

  return { status, speaking };
}
