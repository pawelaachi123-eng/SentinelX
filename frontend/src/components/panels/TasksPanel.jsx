import ContextPanel, { Reveal, Empty, ago } from "../ContextPanel";
import { IconTasks, IconTrash } from "../icons";
import { api } from "@/lib/api";
import { useSettings } from "@/context/SettingsContext";

const LABEL = { running: "W toku", queued: "W kolejce", done: "Zakończone", error: "Błąd", cancelled: "Anulowane" };

export default function TasksPanel({ tasks, onClose, onChanged }) {
  const { settings } = useSettings();
  const active = tasks.filter((t) => t.status === "running" || t.status === "queued");
  const done = tasks.filter((t) => !active.includes(t));
  const clear = async () => {
    if (settings.general.confirmClear && !window.confirm("Wyczyścić historię zakończonych zadań?")) return;
    await api.del("/tasks"); onChanged();
  };
  const Row = (t) => (
    <Reveal key={t.id} className={`task task-${t.status}`} data-testid={`task-${t.id}`} layout>
      <div className="task-top"><b>{t.title}</b><span className={`badge b-${t.status}`}>{LABEL[t.status] || t.status}</span></div>
      <div className="task-meta"><span>{t.kind}</span><span>{ago(t.created_at)}</span></div>
      {(t.status === "running" || t.status === "queued") && <div className="progress"><span style={{ width: `${Math.max(6, t.progress * 100)}%` }} /></div>}
      {t.error && <p className="task-err">{t.error}</p>}
    </Reveal>
  );
  return (
    <ContextPanel id="tasks" title="Tasks" icon={IconTasks} onClose={onClose} className="panel-right"
      actions={done.length > 0 && <button className="icon-btn" data-testid="tasks-clear" onClick={clear} aria-label="Wyczyść historię"><IconTrash size={16} /></button>}>
      {tasks.length === 0 && <Empty title="Brak zadań" testid="tasks-empty">Operacje AI i komendy urządzeń pojawią się tutaj w czasie rzeczywistym.</Empty>}
      {active.length > 0 && <Reveal><h3 className="sub">Aktywne · kolejka</h3></Reveal>}
      {active.map(Row)}
      {done.length > 0 && <Reveal><h3 className="sub">Historia</h3></Reveal>}
      {done.map(Row)}
    </ContextPanel>
  );
}
