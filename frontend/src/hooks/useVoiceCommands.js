import { useCallback, useState } from "react";
import { api } from "@/lib/api";
import { stopSpeaking } from "@/lib/speech";

const norm = (s) => s.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^\p{L}\p{N}\s]/gu, " ").replace(/\s+/g, " ").trim();

/** Speech recognition: utterance blob -> Whisper (backend) -> wake-word gate -> command bus. */
export function useVoiceCommands({ wakeWord, requireWake, language, onState }) {
  const [caption, setCaption] = useState(null); // {text, kind: heard|ignored|error}

  const handle = useCallback(async (blob) => {
    onState?.("recognizing");
    const fd = new FormData();
    fd.append("audio", blob, blob.type.includes("mp4") ? "speech.m4a" : "speech.webm");
    fd.append("language", language);
    fd.append("wake_word", wakeWord);
    try {
      const r = await fetch(`${api.base}/voice/transcribe`, { method: "POST", body: fd });
      if (!r.ok) throw new Error(await r.text());
      const { text } = await r.json();
      if (!text) return;
      let command = text;
      if (requireWake) {
        const w = norm(wakeWord), t = norm(text);
        const at = t.indexOf(w);
        if (!w || at < 0 || at > 12) { setCaption({ text, kind: "ignored" }); return; }
        command = t.slice(at + w.length).trim();
        if (!command) { setCaption({ text: `${wakeWord}…`, kind: "heard" }); return; }
      }
      setCaption({ text: command, kind: "heard" });
      window.dispatchEvent(new CustomEvent("sentinel:command", { detail: { text: command, fromVoice: true } }));
    } catch {
      setCaption({ text: "Nie udało się rozpoznać mowy", kind: "error" });
    } finally {
      onState?.(null);
    }
  }, [wakeWord, requireWake, language, onState]);

  // While Sentinel speaks: saying the wake word stops speech; anything after it becomes a new command.
  const handleInterrupt = useCallback(async (blob) => {
    const fd = new FormData();
    fd.append("audio", blob, blob.type.includes("mp4") ? "speech.m4a" : "speech.webm");
    fd.append("language", language);
    fd.append("wake_word", wakeWord);
    try {
      const r = await fetch(`${api.base}/voice/transcribe`, { method: "POST", body: fd });
      if (!r.ok) return;
      const { text } = await r.json();
      const w = norm(wakeWord), t = norm(text || "");
      const at = w ? t.indexOf(w) : -1;
      if (at < 0) return;
      stopSpeaking();
      const rest = t.slice(at + w.length).trim();
      setCaption({ text: rest || "Przerwano", kind: "heard" });
      if (rest.split(" ").length >= 2) window.dispatchEvent(new CustomEvent("sentinel:command", { detail: { text: rest, fromVoice: true } }));
    } catch { /* ignore: interruption is best-effort */ }
  }, [wakeWord, language]);

  return { handle, handleInterrupt, caption, clearCaption: () => setCaption(null) };
}
