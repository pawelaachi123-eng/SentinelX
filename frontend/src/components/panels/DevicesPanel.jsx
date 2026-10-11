import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import ContextPanel, { Reveal, Empty, ago } from "../ContextPanel";
import { IconDevices, IconPlus, IconTrash, IconPower } from "../icons";
import { api } from "@/lib/api";
import { play } from "@/lib/sound";

function AddForm({ onDone }) {
  const [f, setF] = useState({ name: "", kind: "relay", address: "" });
  const submit = async (e) => {
    e.preventDefault();
    if (!f.name.trim()) return;
    await api.post("/devices", { ...f, authorized: true });
    play("success"); onDone();
  };
  return (
    <motion.form className="add-form" onSubmit={submit} initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }}>
      <input data-testid="device-name-input" placeholder="Nazwa urządzenia" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} autoFocus />
      <select data-testid="device-kind-select" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>
        {["relay", "light", "sensor", "pc", "socket", "other"].map((k) => <option key={k}>{k}</option>)}
      </select>
      <input data-testid="device-address-input" placeholder="Adres / kanał w Base (opcjonalnie)" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} />
      <button className="btn primary" data-testid="device-add-submit" type="submit">Dodaj</button>
    </motion.form>
  );
}

export default function DevicesPanel({ devices, onClose, onChanged }) {
  const [adding, setAdding] = useState(false);
  const cmd = async (d, action) => { play("send"); await api.post(`/devices/${d.id}/command`, { action }).catch(() => play("error")); };
  const toggleAuth = async (d) => { await api.patch(`/devices/${d.id}`, { name: d.name, kind: d.kind, address: d.address, authorized: !d.authorized }); onChanged(); };
  const remove = async (d) => { await api.del(`/devices/${d.id}`); onChanged(); };
  return (
    <ContextPanel id="devices" title="Device Control" icon={IconDevices} onClose={onClose} className="panel-left"
      actions={<button className="icon-btn" data-testid="device-add-toggle" onClick={() => setAdding((a) => !a)} aria-label="Dodaj urządzenie"><IconPlus size={17} /></button>}>
      <AnimatePresence>{adding && <AddForm onDone={() => { setAdding(false); onChanged(); }} />}</AnimatePresence>
      {devices.length === 0 && !adding && <Empty title="Brak urządzeń" testid="devices-empty">Dodaj autoryzowane urządzenie. Komendy trafiają do kolejki, którą odbiera Sentinel Base.</Empty>}
      <div className="device-list">
        {devices.map((d) => (
          <Reveal key={d.id} className={`device ${d.authorized ? "" : "unauth"}`} data-testid={`device-${d.id}`} layout>
            <div className="device-main">
              <i className={`dot dot-${d.online ? "ok" : "off"}`} />
              <div><b>{d.name}</b><span>{d.kind}{d.address ? ` · ${d.address}` : ""} · {d.online ? `stan: ${d.state ?? "?"}` : d.last_seen ? `ostatnio ${ago(d.last_seen)}` : "nigdy nie raportowało"}</span></div>
            </div>
            <div className="device-actions">
              <button className="pill-btn" data-testid={`device-${d.id}-on`} disabled={!d.authorized} onClick={() => cmd(d, "on")}><IconPower size={14} />On</button>
              <button className="pill-btn" data-testid={`device-${d.id}-off`} disabled={!d.authorized} onClick={() => cmd(d, "off")}>Off</button>
              <button className={`switch ${d.authorized ? "on" : ""}`} data-testid={`device-${d.id}-auth`} onClick={() => toggleAuth(d)} aria-label="Autoryzacja" title="Autoryzacja"><span /></button>
              <button className="icon-btn sm" data-testid={`device-${d.id}-delete`} onClick={() => remove(d)} aria-label="Usuń"><IconTrash size={15} /></button>
            </div>
          </Reveal>
        ))}
      </div>
    </ContextPanel>
  );
}
