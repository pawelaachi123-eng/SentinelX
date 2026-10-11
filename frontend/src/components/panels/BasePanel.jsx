import { useEffect, useState } from "react";
import ContextPanel, { Reveal, Empty, ago } from "../ContextPanel";
import { IconBase, IconCopy } from "../icons";
import { api } from "@/lib/api";

const fmtUptime = (s) => (s == null ? "—" : `${Math.floor(s / 3600)} h ${Math.floor((s % 3600) / 60)} min`);

function Spark({ values }) {
  if (values.length < 2) return null;
  const min = Math.min(...values), max = Math.max(...values), span = max - min || 1;
  const pts = values.map((v, i) => `${(i / (values.length - 1)) * 100},${28 - ((v - min) / span) * 24}`).join(" ");
  return <svg className="spark" viewBox="0 0 100 30" preserveAspectRatio="none"><polyline points={pts} /></svg>;
}

export default function BasePanel({ status, onClose }) {
  const [history, setHistory] = useState([]);
  const [cfg, setCfg] = useState(null);
  const last = status?.last;
  useEffect(() => { api.get("/base/history?limit=40").then(setHistory).catch(() => {}); }, [status?.reports_total]);
  useEffect(() => { api.get("/base/ingest-config").then(setCfg).catch(() => {}); }, []);

  const sensors = Object.entries(last?.sensors || {});
  const curl = cfg && `curl -X POST ${api.base}/base/ingest \\\n  -H "Content-Type: application/json" -H "${cfg.header}: ${cfg.token}" \\\n  -d '{"device_id":"base-01","firmware":"4.2.1","rssi":-58,"uptime_s":3600,"sensors":{"temp_c":22.4,"humidity":41},"capabilities":["relay","wol"]}'`;

  return (
    <ContextPanel id="base" title="Sentinel Base" icon={IconBase} onClose={onClose} className="panel-left">
      <Reveal className="stat-row">
        <div className="stat" data-testid="base-connection-status">
          <span className="k">Połączenie</span>
          <span className={`v ${status?.online ? "ok" : "muted"}`}><i className={`dot dot-${status?.online ? "ok" : status?.configured ? "warn" : "off"}`} />{status?.online ? "Online" : status?.configured ? "Offline" : "Nigdy nie połączono"}</span>
        </div>
        <div className="stat"><span className="k">Ostatni raport</span><span className="v" data-testid="base-last-seen">{ago(last?.received_at)}</span></div>
      </Reveal>
      {last ? (
        <>
          <Reveal className="kv-grid" data-testid="base-meta">
            <div><span>Urządzenie</span><b>{last.device_id}</b></div>
            <div><span>Firmware</span><b>{last.firmware || "—"}</b></div>
            <div><span>IP</span><b>{last.ip || "—"}</b></div>
            <div><span>RSSI</span><b>{last.rssi != null ? `${last.rssi} dBm` : "—"}</b></div>
            <div><span>Uptime</span><b>{fmtUptime(last.uptime_s)}</b></div>
            <div><span>Raporty</span><b>{status.reports_total}</b></div>
          </Reveal>
          <Reveal><h3 className="sub">Czujniki</h3></Reveal>
          {sensors.length ? (
            <Reveal className="sensor-grid" data-testid="base-sensors">
              {sensors.map(([k, v]) => (
                <div className="sensor" key={k} data-testid={`sensor-${k}`}>
                  <span className="k">{k}</span><b>{typeof v === "number" ? +v.toFixed(2) : String(v)}</b>
                  {typeof v === "number" && <Spark values={history.map((h) => h.sensors?.[k]).filter((x) => typeof x === "number")} />}
                </div>
              ))}
            </Reveal>
          ) : <Empty title="Brak odczytów">Base nie raportuje czujników.</Empty>}
          {!!last.capabilities?.length && (
            <Reveal className="chips" data-testid="base-capabilities">{last.capabilities.map((c) => <span className="chip" key={c}>{c}</span>)}</Reveal>
          )}
        </>
      ) : (
        <Empty title="Oczekiwanie na Base" testid="base-empty">Sentinel Base jeszcze nie wysłał raportu. Skonfiguruj firmware ESP32, aby wysyłał dane na poniższy endpoint.</Empty>
      )}
      {cfg && (
        <Reveal className="code-block">
          <div className="code-head"><span>Endpoint ESP32 · push</span>
            <button className="icon-btn sm" data-testid="base-copy-curl" onClick={() => navigator.clipboard?.writeText(curl)} aria-label="Kopiuj"><IconCopy size={15} /></button>
          </div>
          <pre>{curl}</pre>
        </Reveal>
      )}
    </ContextPanel>
  );
}
