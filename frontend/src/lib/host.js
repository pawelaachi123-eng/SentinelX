// WebView2 host bridge (WinUI 3 shell). Envelope: {type, id?, data}.
const host = () => window.chrome?.webview;
const pending = new Map();
let wired = false;

function wire() {
  if (wired || !host()) return;
  wired = true;
  host().addEventListener("message", (e) => {
    const msg = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
    if (msg?.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.type === "host.error" ? reject(new Error(msg.data?.error || "host error")) : resolve(msg.data);
    }
  });
}

export const isHost = () => !!host();

export function hostRequest(type, data, timeoutMs = 60000) {
  wire();
  if (!host()) return Promise.reject(new Error("no host"));
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    host().postMessage({ type, id, data });
    setTimeout(() => pending.has(id) && (pending.delete(id), reject(new Error("host timeout"))), timeoutMs);
  });
}

export const hostPost = (type, data) => host()?.postMessage({ type, data });
