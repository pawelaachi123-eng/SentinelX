const BASE = `${process.env.REACT_APP_BACKEND_URL}/api`;

async function req(path, opts = {}) {
  const r = await fetch(`${BASE}${path}`, { headers: { "Content-Type": "application/json" }, ...opts });
  if (!r.ok) throw new Error((await r.text()) || r.statusText);
  return r.json();
}

export const api = {
  base: BASE,
  get: (p) => req(p),
  put: (p, body) => req(p, { method: "PUT", body: JSON.stringify(body) }),
  post: (p, body) => req(p, { method: "POST", body: JSON.stringify(body) }),
  patch: (p, body) => req(p, { method: "PATCH", body: JSON.stringify(body) }),
  del: (p) => req(p, { method: "DELETE" }),
};

/** POST + SSE parsing for streaming chat. */
export async function streamChat(sessionId, text, onEvent, signal) {
  const r = await fetch(`${BASE}/chat/stream`, {
    method: "POST", headers: { "Content-Type": "application/json" }, signal,
    body: JSON.stringify({ session_id: sessionId, text }),
  });
  if (!r.ok || !r.body) throw new Error(`HTTP ${r.status}`);
  const reader = r.body.getReader();
  const dec = new TextDecoder();
  let buf = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buf += dec.decode(value, { stream: true });
    let i;
    while ((i = buf.indexOf("\n\n")) >= 0) {
      const chunk = buf.slice(0, i); buf = buf.slice(i + 2);
      const line = chunk.split("\n").find((l) => l.startsWith("data: "));
      if (line) onEvent(JSON.parse(line.slice(6)));
    }
  }
}
