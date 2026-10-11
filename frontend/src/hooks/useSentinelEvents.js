import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";

/**
 * Backend integration layer: SSE event stream from the bridge backend, plus the
 * WebView2 host channel (window.chrome.webview) so the native WinUI shell can push
 * the same event envelope ({type, data}) from CoreStateService.
 */
export function useSentinelEvents(onEvent) {
  const [connected, setConnected] = useState(false);
  const [coreState, setCoreState] = useState("idle");
  const handler = useRef(onEvent);
  handler.current = onEvent;

  useEffect(() => {
    let es, retry, alive = true;
    const dispatch = (msg) => {
      if (msg.type === "core.state") setCoreState(msg.data.state);
      handler.current?.(msg);
    };
    const open = () => {
      es = new EventSource(`${api.base}/events`);
      es.onopen = () => setConnected(true);
      es.onmessage = (e) => dispatch(JSON.parse(e.data));
      es.onerror = () => { setConnected(false); es.close(); if (alive) retry = setTimeout(open, 3000); };
    };
    open();
    const host = window.chrome?.webview;
    const onHost = (e) => dispatch(typeof e.data === "string" ? JSON.parse(e.data) : e.data);
    host?.addEventListener("message", onHost);
    return () => { alive = false; clearTimeout(retry); es?.close(); host?.removeEventListener("message", onHost); };
  }, []);

  return { connected, coreState, setCoreState };
}

export const postToHost = (type, data) => window.chrome?.webview?.postMessage({ type, data });
