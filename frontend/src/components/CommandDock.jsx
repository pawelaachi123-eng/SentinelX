import { useCallback, useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { IconSend, IconStop, IconCopy, IconPlus } from "./icons";
import { api, streamChat } from "@/lib/api";
import { isHost, hostRequest, hostPost } from "@/lib/host";
import { play } from "@/lib/sound";
import { useSettings } from "@/context/SettingsContext";

const SID_KEY = "sentinel.session";
const newSid = () => { const s = crypto.randomUUID(); localStorage.setItem(SID_KEY, s); return s; };
const SUGGEST = ["Podsumuj stan Sentinel Base", "Jakie urządzenia są online?", "Co potrafisz zrobić na tym komputerze?"];
const hhmm = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "");

const inline = (t) => t.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).map((x, i) =>
  x.startsWith("**") ? <strong key={i}>{x.slice(2, -2)}</strong> : x.startsWith("`") ? <code key={i}>{x.slice(1, -1)}</code> : x);
const Rich = ({ text }) => text.split("\n").map((l, i) => {
  if (/^#{1,4}\s/.test(l)) return <span key={i} className="md-h">{inline(l.replace(/^#+\s/, ""))}</span>;
  if (/^\s*[-*]\s/.test(l)) return <span key={i} className="md-li">{inline(l.replace(/^\s*[-*]\s/, ""))}</span>;
  return <span key={i} className="md-p">{inline(l)}</span>;
});

function Message({ m }) {
  const [copied, setCopied] = useState(false);
  const copy = (e) => { e.stopPropagation(); navigator.clipboard?.writeText(m.content); setCopied(true); setTimeout(() => setCopied(false), 1200); };
  return (
    <motion.div className={`msg msg-${m.role}`} data-testid={`msg-${m.role}`} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ type: "spring", stiffness: 380, damping: 32 }}>
      {m.role === "assistant" && (
        <span className="msg-who">Sentinel{m.via === "host" && <em> · PC</em>}<time>{hhmm(m.created_at)}</time>
          {!m.streaming && m.content && <button className="msg-copy" data-testid="msg-copy" onClick={copy} aria-label="Kopiuj">{copied ? "skopiowano" : <IconCopy size={13} />}</button>}
        </span>
      )}
      <p>{m.role === "assistant" ? <Rich text={m.content} /> : m.content}{m.streaming && <span className="caret" />}</p>
      {m.fromVoice && <span className="msg-voice">głos</span>}
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
  const inputRef = useRef(null), logRef = useRef(null), abortRef = useRef(null);
  const sid = useRef(localStorage.getItem(SID_KEY) || newSid());
  const busyRef = useRef(false);

  useEffect(() => { api.get(`/chat/sessions/${sid.current}/messages`).then(setMsgs).catch(() => {}); }, []);
  useEffect(() => { logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: mt.reduced ? "auto" : "smooth" }); }, [msgs, open, mt.reduced]);
  useEffect(() => { const el = inputRef.current; if (!el) return; el.style.height = "0px"; el.style.height = `${Math.min(el.scrollHeight, 160)}px`; }, [text, open]);

  const patchLive = (fn) => setMsgs((m) => m.map((x) => (x.id === "live" ? fn(x) : x)));
  const finishLive = (extra) => patchLive((x) => ({ ...x, id: `a${Date.now()}`, streaming: false, created_at: new Date().toISOString(), ...extra }));

  const send = useCallback(async (raw, fromVoice = false) => {
    const t = (raw ?? "").trim();
    if (!t || busyRef.current) return;
    busyRef.current = true; setBusy(true);
    play("send"); onLocalState?.("thinking");
    if (fromVoice) setOpen(true);
    setMsgs((m) => [...m, { id: `u${Date.now()}`, role: "user", content: t, fromVoice }, { id: "live", role: "assistant", content: "", streaming: true }]);
    try {
      if (isHost()) {
        const res = await hostRequest("command.submit", { text: t, fromVoice });
        finishLive({ content: res?.text || "Wykonano.", via: "host" }); play("success");
      } else {
        const ac = new AbortController(); abortRef.current = ac;
        await streamChat(sid.current, t, (ev) => {
          if (ev.type === "delta") patchLive((x) => ({ ...x, content: x.content + ev.content }));
          if (ev.type === "done") { patchLive(() => ({ ...ev.message, streaming: false })); play("success"); }
          if (ev.type === "error") { finishLive({ error: "Nie udało się uzyskać odpowiedzi." }); play("error"); }
        }, ac.signal);
      }
    } catch (e) {
      finishLive({ error: e.name === "AbortError" ? "Przerwano." : isHost() ? e.message : "Brak połączenia z backendem." });
    } finally { busyRef.current = false; setBusy(false); onLocalState?.(null); }
  }, [onLocalState]);

  useEffect(() => {
    const k = (e) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); setOpen(true); inputRef.current?.focus(); } };
    const v = (e) => send(e.detail.text, e.detail.fromVoice);
    window.addEventListener("keydown", k); window.addEventListener("sentinel:command", v);
    return () => { window.removeEventListener("keydown", k); window.removeEventListener("sentinel:command", v); };
  }, [send]);

  const submit = () => { const t = text; setText(""); send(t); };
  const stop = (e) => { e.stopPropagation(); abortRef.current?.abort(); if (isHost()) hostPost("command.cancel"); };
  const newChat = async (e) => { e.stopPropagation(); if (busy) return; await api.del(`/chat/sessions/${sid.current}`).catch(() => {}); sid.current = newSid(); setMsgs([]); play("tap"); inputRef.current?.focus(); };
  const onKey = (e) => {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); submit(); }
    if (e.key === "Escape" && !busy) { setOpen(false); inputRef.current?.blur(); }
  };

  return (
    <motion.div layout className={`dock ${open ? "dock-open" : ""}`} data-testid="command-dock" transition={mt.spring}
      onFocus={() => setOpen(true)} onClick={() => inputRef.current?.focus()}>
      <AnimatePresence initial={false}>
        {open && (
          <motion.div key="log" className="dock-top" initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} transition={mt.spring}>
            {msgs.length > 0 ? (
              <>
                <div className="dock-bar">
                  <span>Rozmowa{isHost() ? " · Sentinel PC" : ""}</span>
                  <button className="dock-new" data-testid="chat-new" onClick={newChat} disabled={busy}><IconPlus size={13} />Nowa</button>
                </div>
                <div className="dock-log" ref={logRef} data-testid="conversation-log" aria-live="polite">
                  {msgs.map((m) => <Message key={m.id} m={m} />)}
                </div>
              </>
            ) : (
              <div className="dock-suggest" data-testid="dock-suggestions">
                {SUGGEST.map((s, i) => (
                  <motion.button key={s} data-testid={`suggestion-${i}`} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.05 + i * mt.stagger }}
                    onMouseDown={(e) => e.preventDefault()} onClick={(e) => { e.stopPropagation(); send(s); }}>{s}</motion.button>
                ))}
              </div>
            )}
          </motion.div>
        )}
      </AnimatePresence>
      <motion.div layout="position" className="dock-row">
        <textarea ref={inputRef} rows={1} value={text} data-testid="command-input" aria-label="Polecenie dla Sentinel"
          placeholder={open ? "Napisz polecenie…" : "Zapytaj Sentinel…"} onChange={(e) => setText(e.target.value)} onKeyDown={onKey} />
        {!open && <kbd className="dock-kbd" aria-hidden="true">Ctrl K</kbd>}
        {busy ? (
          <button className="send-btn" data-testid="command-stop" onClick={stop} aria-label="Przerwij"><IconStop size={17} /></button>
        ) : (
          <motion.button className={`send-btn ${text.trim() ? "ready" : ""}`} data-testid="command-send" whileTap={{ scale: 0.88 }} onClick={(e) => { e.stopPropagation(); submit(); }} aria-label="Wyślij"><IconSend size={18} /></motion.button>
        )}
      </motion.div>
      {open && <div className="dock-hint">Enter wysyła · Shift+Enter nowa linia · Esc zwija</div>}
    </motion.div>
  );
}
