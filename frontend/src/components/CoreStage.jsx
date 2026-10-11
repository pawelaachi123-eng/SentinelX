import { forwardRef, useEffect, useImperativeHandle, useRef } from "react";
import { CoreRenderer } from "@/engine/core/CoreRenderer";
import { useSettings } from "@/context/SettingsContext";

/** React host for the independent core renderer. Exposes setState/setAudio/onStats. */
const CoreStage = forwardRef(function CoreStage({ state }, ref) {
  const canvasRef = useRef(null);
  const rendererRef = useRef(null);
  const { settings, motion, palette } = useSettings();

  useImperativeHandle(ref, () => ({
    setAudio: (a, b, c) => rendererRef.current?.setAudio(a, b, c),
    onStats: (fn) => rendererRef.current?.onStats(fn),
    stats: () => rendererRef.current?.stats,
  }));

  useEffect(() => {
    const r = new CoreRenderer(canvasRef.current);
    rendererRef.current = r;
    r.start();
    const onResize = () => r.resize();
    const onVis = () => (document.hidden ? r.pause() : r.start());
    const onMove = (e) => r.pointer(e.clientX, e.clientY);
    const onDown = (e) => { if (e.target === canvasRef.current) r.click(e.clientX, e.clientY); };
    window.addEventListener("resize", onResize);
    document.addEventListener("visibilitychange", onVis);
    window.addEventListener("pointermove", onMove, { passive: true });
    window.addEventListener("pointerdown", onDown);
    document.addEventListener("pointerleave", r.pointerLeave.bind(r));
    window.__sentinelCore = r;
    return () => {
      window.removeEventListener("resize", onResize);
      document.removeEventListener("visibilitychange", onVis);
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerdown", onDown);
      r.dispose();
      delete window.__sentinelCore;
    };
  }, []);

  useEffect(() => { rendererRef.current?.setState(state); }, [state]);

  useEffect(() => {
    const c = settings.core;
    rendererRef.current?.setOptions({
      palette, quality: c.quality, bloom: c.bloom, particles: c.particles, prCap: settings.advanced.prCap,
      custom: { deform: c.deform, macro: c.macro, meso: c.meso, micro: c.micro, light: c.light, energy: c.energy, speed: settings.motion.speed, reduced: motion.reduced },
    });
  }, [settings.core, settings.advanced.prCap, settings.motion.speed, motion.reduced, palette]);

  return <canvas ref={canvasRef} data-testid="core-canvas" className="core-canvas" aria-label="Sentinel X core" />;
});

export default CoreStage;
