import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { IconSend, IconStop } from "./icons";
import { api, streamChat } from "@/lib/api";
import { play } from "@/lib/sound";
import { useSettings } from "@/context/SettingsContext";

const SID_KEY = "sentinel.session";
const sessionId = () => localStorage.getItem(SID_KEY) || (localStorage.setItem(SID_KEY, crypto.randomUUID()), localStorage.getItem(SID_KEY));

const inline = (t) => t.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).map((x, i) =>
  x.startsWith("**") ? <strong key={i}>{x.slice(2, -2)}</strong> : x.startsWith("`") ? <code key={i}>{x.slice(1, -1)}</code> : x);
const Rich = ({ text }) => text.split("\n").map((l, i) => {
  if (/^#{1,4}\s/.test(l)) return <span key={i} className="md-h">{inline(l.replace(/^#+\s/, ""))}</span>;
  if (/^\s*[-*]\s/.test(l)) return <span key={i} className="md-li">{inline(l.replace(/^\s*[-*]\s/, ""))}</span>;
  return <span key={i} className="md-p">{inline(l)}</span>;
});

function Message({ m }) {
  return (
    <motion.div className={`msg msg-${m.role}`} data-testid={`msg-${m.role}`} initial={{ opacity: 0, y: 10, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} transition={{ type: "spring", stiffness: 380, damping: 30 }}>
      {m.role === "assistant" && <span className="msg-who">Sentinel</span>}
      <p>{m.role === "assistant" ? <Rich text={m.content} /> : m.content}{m.streaming && <span className="caret" />}</p>
      {m.error && <span className="msg-error" data-testid="msg-error">{m.error}</span>}
    </motion.div>
  );
}

export default function CommandDock({ onLocalState }) {
  const { motion: mt } = useSettings();
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const [msgs, setMsgs] = useState([]);
  const [busy, setBusy] = useState(false);
  const inputRef = useRef(null), logRef = useRef(null), abortRef = useRef(null), sid = useRef(sessionId());

  useEffect(() => { api.get(`/chat/sessions/${sid.current}/messages`).then(setMsgs).catch(() => {}); }, []);
  useEffect(() => { logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: mt.reduced ? "auto" : "smooth" }); }, [msgs, open, mt.reduced]);
  useEffect(() => {
    const el = inputRef.current; if (!el) return;
    el.style.height = "0px"; el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }, [text, open]);
  useEffect(() => {
    const k = (e) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); setOpen(true); inputRef.current?.focus(); } };
    window.addEventListener("keydown", k); return () => window.removeEventListener("keydown", k);
  }, []);

  const send = async () => {
    const t = text.trim();
    if (!t || busy) return;
    play("send"); setText(""); setBusy(true); onLocalState?.("thinking");
    setMsgs((m) => [...m, { id: `u${Date.now()}`, role: "user", content: t }, { id: "live", role: "assistant", content: "", streaming: true }]);
    const ac = new AbortController(); abortRef.current = ac;
    const patchLive = (fn) => setMsgs((m) => m.map((x) => (x.id === "live" ? fn(x) : x)));
    try {
      await streamChat(sid.current, t, (ev) => {
        if (ev.type === "delta") patchLive((x) => ({ ...x, content: x.content + ev.content }));
        if (ev.type === "done") { patchLive(() => ({ ...ev.message, streaming: false })); play("success"); }
        if (ev.type === "error") { patchLive((x) => ({ ...x, id: `e${Date.now()}`, streaming: false, error: "Nie udało się uzyskać odpowiedzi." })); play("error"); }
      }, ac.signal);
    } catch (e) {
      patchLive((x) => ({ ...x, id: `e${Date.now()}`, streaming: false, error: e.name === "AbortError" ? "Przerwano." : "Brak połączenia z backendem." }));
    } finally { setBusy(false); onLocalState?.(null); }
  };

  const onKey = (e) => {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); send(); }
    if (e.key === "Escape" && !busy) { setOpen(false); inputRef.current?.blur(); }
  };
  const expanded = open && msgs.length > 0;

  return (
    <motion.div layout className={`dock glass ${open ? "dock-open" : ""}`} data-testid="command-dock" transition={mt.spring}
      onFocus={() => setOpen(true)} onClick={() => inputRef.current?.focus()}>
      <AnimatePresence initial={false}>
        {expanded && (
          <motion.div key="log" className="dock-log" ref={logRef} data-testid="conversation-log" aria-live="polite"
            initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} transition={mt.spring}>
            {msgs.map((m) => <Message key={m.id} m={m} />)}
          </motion.div>
        )}
      </AnimatePresence>
      <motion.div layout="position" className="dock-row">
        <textarea ref={inputRef} rows={1} value={text} data-testid="command-input" aria-label="Polecenie dla Sentinel"
          placeholder={open ? "Napisz polecenie…  Enter wysyła · Shift+Enter nowa linia · Esc zwija" : "Zapytaj Sentinel…"}
          onChange={(e) => setText(e.target.value)} onKeyDown={onKey} />
        {busy ? (
          <button className="send-btn" data-testid="command-stop" onClick={(e) => { e.stopPropagation(); abortRef.current?.abort(); }} aria-label="Przerwij"><IconStop size={17} /></button>
        ) : (
          <motion.button className={`send-btn ${text.trim() ? "ready" : ""}`} data-testid="command-send" whileTap={{ scale: 0.88 }} onClick={(e) => { e.stopPropagation(); send(); }} aria-label="Wyślij"><IconSend size={18} /></motion.button>
        )}
      </motion.div>
    </motion.div>
  );
}
