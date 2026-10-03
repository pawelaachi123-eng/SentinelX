#!/usr/bin/env node
/* Atrapa serwera Sentinel X Link — do rozwijania i testowania interfejsu telefonu (Phone/web) bez komputera z Windows.
 * Odwzorowuje kontrakt z docs/PHONE-LINK.md: parowanie z kodem potwierdzenia, stan, czat (SSE), zadania, notatki, alerty.
 *
 *   node scripts/phone-mock/server.mjs [port]
 *   MOCK_APPROVE_MS=2500   po ilu ms atrapa „klika Zezwól” (0 = nigdy, -1 = odrzuć)
 *   MOCK_FP=<64 hex>       odcisk certyfikatu używany w kodzie potwierdzenia
 */
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(here, '../../Phone/web');
const API_VERSION = 1;
const FP = (process.env.MOCK_FP || crypto.createHash('sha256').update('sentinelx-mock-cert').digest('hex')).toLowerCase();
const APPROVE_MS = Number(process.env.MOCK_APPROVE_MS ?? 2500);
const PORT = Number(process.argv[2] || process.env.PORT || 8088);

export function sasFor(fpHex, nonceA, nonceB) {
  const b = s => Buffer.from(s.replace(/-/g, '+').replace(/_/g, '/'), 'base64');
  const h = crypto.createHash('sha256').update(Buffer.concat([Buffer.from(fpHex, 'hex'), b(nonceA), b(nonceB)])).digest();
  return String(h.readUInt32BE(0) % 1000000).padStart(6, '0');
}
const b64u = buf => buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const iso = d => new Date(d).toISOString();

/* ───────────── dane ───────────── */
const db = {
  tokens: new Map(), // token -> { id, name, addedAt, lastSeen }
  pairs: new Map(),
  nextId: 1,
  tasks: [
    { id: 't1', title: 'Zrobić kopię zapasową zdjęć', priority: 'wysoki', status: 'otwarte', due: iso(Date.now() + 26 * 3600e3), project: '' },
    { id: 't2', title: 'Zaktualizować sterowniki karty graficznej', priority: 'normalny', status: 'w toku', due: null, project: '' },
    { id: 't3', title: 'Odnowić domenę', priority: 'niski', status: 'otwarte', due: iso(Date.now() + 5 * 86400e3), project: '' }
  ],
  reminders: [{ id: 'r1', text: 'Zadzwonić do serwisu', at: iso(Date.now() + 3 * 3600e3), missed: false }],
  notes: [
    { id: 'n1', text: 'Hasło do Wi‑Fi gości jest na lodówce', category: 'notatka', pinned: true, at: iso(Date.now() - 86400e3 * 3) },
    { id: 'n2', text: 'Preferuję ciemny motyw i krótkie odpowiedzi', category: 'preferencja', pinned: false, at: iso(Date.now() - 86400e3) }
  ],
  alerts: [{ id: 1, at: iso(Date.now() - 600e3), level: 'info', title: 'Silnik AI gotowy', text: 'Model qwen3:4b-instruct działa lokalnie — bez Ollamy.' }],
  alertId: 1, stopped: false, busy: false
};
const waiters = new Set();
function pushAlert(level, title, text) {
  const a = { id: ++db.alertId, at: iso(Date.now()), level, title, text };
  db.alerts.push(a); if (db.alerts.length > 200) db.alerts.shift();
  for (const w of [...waiters]) w(); return a;
}
let cpu = 22;
function metrics() {
  cpu = Math.max(3, Math.min(97, cpu + (Math.random() - 0.5) * 12));
  return {
    cpu, ramUsed: 9.4 + Math.random() * 0.3, ramTotal: 15.9, gpu: Math.random() < 0.1 ? null : 14 + Math.random() * 8,
    game: process.env.MOCK_GAME || '', network: 'Wi‑Fi „Dom” · 866 Mb/s · ping 7 ms · internet OK',
    disks: [{ name: 'C:\\', usedGb: 221.4, totalGb: 476.3 }, { name: 'D:\\', usedGb: 1402.8, totalGb: 1863 }],
    processes: [['chrome', 812], ['Discord', 402.5], ['SentinelX', 288], ['llama-server', 2980], ['Code', 611], ['steamwebhelper', 240]]
      .sort((a, b) => b[1] - a[1]).map(([name, memoryMb], i) => ({ name, pid: 1200 + i * 37, memoryMb, cpu: Math.random() * 6 }))
  };
}
const engine = () => ({ state: 'ready', message: 'Działa lokalnie na tym komputerze, bez Ollamy i bez chmury.', progress: 1, model: 'qwen3:4b-instruct', installed: ['qwen3:1.7b', 'qwen3:4b-instruct'] });

/* ───────────── pomocnicze ───────────── */
const json = (res, code, obj, extra = {}) => { const body = JSON.stringify(obj); res.writeHead(code, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', ...extra }); res.end(body); };
const readBody = req => new Promise((ok, bad) => { const c = []; let n = 0; req.on('data', d => { n += d.length; if (n > 65536) { bad(new Error('too big')); req.destroy(); } else c.push(d); }); req.on('end', () => { try { ok(c.length ? JSON.parse(Buffer.concat(c).toString('utf8')) : {}); } catch (e) { bad(e); } }); });
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.webmanifest': 'application/manifest+json' };
const sleep = ms => new Promise(r => setTimeout(r, ms));

function auth(req) {
  const m = /^Bearer (.+)$/.exec(req.headers.authorization || '');
  const d = m && db.tokens.get(m[1]);
  if (d) d.lastSeen = Date.now();
  return d || null;
}

async function chat(req, res, text) {
  res.writeHead(200, { 'content-type': 'text/event-stream; charset=utf-8', 'cache-control': 'no-store', connection: 'close' });
  const send = (ev, data) => res.write(`event: ${ev}\ndata: ${JSON.stringify(data)}\n\n`);
  send('start', { id: String(++db.nextId) });
  const t = text.toLowerCase();
  let answer, status = 'unverified', stream = false;
  if (db.busy) { answer = 'Trwa zadanie. Poczekaj lub je anuluj.'; }
  else if (/potwierd/.test(t)) { answer = 'Potwierdzenie jest możliwe wyłącznie przyciskiem lub klawiaturą.'; }
  else if (/ram/.test(t)) { answer = '9,6 GB z 15,9 GB (60%).'; status = 'verified'; }
  else if (/cpu/.test(t)) { answer = `${Math.round(cpu)}%`; status = 'verified'; }
  else if (/proces/.test(t)) { answer = 'TOP PROCESY (RAM)\n1. llama-server — 2,9 GB\n2. chrome — 812 MB\n3. Code — 611 MB'; status = 'verified'; }
  else if (/godzin/.test(t)) { answer = new Date().toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' }); status = 'verified'; }
  else if (/zamknij/.test(t)) { answer = 'Czy zamknąć Notatnik? Potwierdź w oknie Sentinel na komputerze.'; status = 'waitingpermission'; }
  else if (/blad|error|fail/.test(t)) { send('error', { error: 'Symulowany błąd narzędzia.' }); return res.end(); }
  else { stream = true; answer = `Jasne! Działam lokalnie na Twoim komputerze, więc Twoje pytanie „${text.slice(0, 80)}” nie wychodzi poza domową sieć.\n\nMogę m.in.:\n• sprawdzić **RAM**, CPU i dyski,\n• dodać zadanie albo \`przypomnienie\`,\n• zapamiętać notatkę.\n\n\`\`\`\nile mam RAM\n\`\`\`\nTaką komendę możesz wpisać w każdej chwili.`; }
  let aborted = false; req.on('close', () => { aborted = true; });
  db.busy = true;
  try {
    if (stream) {
      await sleep(350);
      for (const w of answer.match(/\S+\s*/g)) { if (aborted) return; send('delta', { text: w }); await sleep(35); }
    } else await sleep(180);
    if (!aborted) send('done', { text: answer, status, elapsedMs: stream ? 900 : 180 });
  } finally { db.busy = false; res.end(); }
}

async function api(req, res, url) {
  const p = url.pathname;
  if (p === '/api/hello') return json(res, 200, { app: 'SentinelX', api: API_VERSION, version: '0.94', name: 'PAWEL-PC', fingerprint: FP,
    capabilities: ['systemMetrics', 'processList', 'assistantChat', 'assistantControl', 'tasks', 'reminders', 'notes', 'alerts', 'deviceManagement'] });
  if (p === '/api/pair/request' && req.method === 'POST') {
    const b = await readBody(req); if (!b.nonce || !b.device) return json(res, 400, { error: 'Brak danych parowania.' });
    const id = 'p' + (++db.nextId); const nonce = b64u(crypto.randomBytes(16));
    const allowedCapabilities = new Set(['notifications', 'voiceInput', 'wakeOnLan']);
    const capabilities = Array.isArray(b.capabilities) ? [...new Set(b.capabilities.filter(x => typeof x === 'string' && allowedCapabilities.has(x)))].slice(0, 3) : [];
    const pair = { id, device: String(b.device).slice(0, 32), capabilities, nonce, state: 'pending', sas: sasFor(FP, b.nonce, nonce), at: Date.now() };
    db.pairs.set(id, pair);
    console.log(`[pair] prośba od „${pair.device}” · kod ${pair.sas.slice(0, 3)} ${pair.sas.slice(3)}`);
    if (APPROVE_MS >= 0 && APPROVE_MS !== 0) setTimeout(() => { pair.state = 'approved'; pair.token = b64u(crypto.randomBytes(32)); pair.deviceId = 'd' + (++db.nextId); db.tokens.set(pair.token, { id: pair.deviceId, name: pair.device, capabilities: pair.capabilities, addedAt: Date.now(), lastSeen: Date.now() }); console.log('[pair] zatwierdzono'); }, APPROVE_MS);
    else if (APPROVE_MS < 0) setTimeout(() => { pair.state = 'denied'; }, 1200);
    return json(res, 200, { id, nonce, expiresIn: 120, sas: pair.sas });
  }
  if (p === '/api/pair/status') {
    const pair = db.pairs.get(url.searchParams.get('id') || ''); if (!pair) return json(res, 404, { error: 'Nieznana prośba.' });
    if (pair.state === 'approved') { const t = pair.token; if (pair.delivered) return json(res, 200, { state: 'expired' }); pair.delivered = true; return json(res, 200, { state: 'approved', token: t, deviceId: pair.deviceId }); }
    return json(res, 200, { state: pair.state });
  }

  const dev = auth(req);
  if (!dev) return json(res, 401, { error: 'unauthorized' });

  if (p === '/api/state') {
    return json(res, 200, {
      pc: { name: 'PAWEL-PC', version: '0.94', uptime: '3 d 4 h', time: iso(Date.now()), mac: ['AA:BB:CC:DD:EE:FF'], addresses: ['https://192.168.1.23:43180/'] },
      metrics: metrics(), engine: engine(),
      assistant: { busy: db.busy, stopped: db.stopped, pending: false, pendingSummary: '' },
      care: { ok: true, text: 'Wszystko działa samo · ostatnia kontrola ' + new Date().toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' }) },
      counts: { tasks: db.tasks.length, reminders: db.reminders.length, notes: db.notes.length, alerts: db.alerts.length }, alertsLast: db.alertId,
      capabilities: ['systemMetrics', 'processList', 'assistantChat', 'assistantControl', 'tasks', 'reminders', 'notes', 'alerts', 'deviceManagement']
    });
  }
  if (p === '/api/chat' && req.method === 'POST') { const b = await readBody(req); return chat(req, res, String(b.text || '')); }
  if (p === '/api/control' && req.method === 'POST') { const b = await readBody(req); if (b.action === 'stop') db.stopped = true; if (b.action === 'resume') db.stopped = false; return json(res, 200, { ok: true, stopped: db.stopped }); }
  if (p === '/api/tasks' && req.method === 'GET') return json(res, 200, { tasks: db.tasks.filter(t => t.status !== 'zrobione'), reminders: db.reminders });
  if (p === '/api/tasks' && req.method === 'POST') { const b = await readBody(req); const t = { id: 't' + (++db.nextId), title: String(b.title || '').slice(0, 200), priority: b.priority || 'normalny', status: 'otwarte', due: b.due || null, project: '' }; db.tasks.push(t); return json(res, 200, { ok: true, id: t.id }); }
  if (p === '/api/tasks/status') { const b = await readBody(req); const t = db.tasks.find(x => x.id === b.id); if (t) t.status = b.status; return json(res, 200, { ok: !!t }); }
  if (p === '/api/tasks/delete') { const b = await readBody(req); db.tasks = db.tasks.filter(x => x.id !== b.id); return json(res, 200, { ok: true }); }
  if (p === '/api/reminders' && req.method === 'POST') { const b = await readBody(req); if (new Date(b.at) < new Date()) return json(res, 400, { error: 'Termin przypomnienia musi być w przyszłości.' }); db.reminders.push({ id: 'r' + (++db.nextId), text: String(b.text || ''), at: b.at, missed: false }); return json(res, 200, { ok: true }); }
  if (p === '/api/reminders/delete') { const b = await readBody(req); db.reminders = db.reminders.filter(x => x.id !== b.id); return json(res, 200, { ok: true }); }
  if (p === '/api/notes' && req.method === 'GET') { const q = (url.searchParams.get('q') || '').toLowerCase(); return json(res, 200, { notes: db.notes.filter(n => !q || n.text.toLowerCase().includes(q)).sort((a, b) => Number(b.pinned) - Number(a.pinned)) }); }
  if (p === '/api/notes' && req.method === 'POST') { const b = await readBody(req); if (db.notes.some(n => n.text === b.text)) return json(res, 200, { ok: true, result: 'Duplicate' }); db.notes.unshift({ id: 'n' + (++db.nextId), text: String(b.text || ''), category: 'notatka', pinned: false, at: iso(Date.now()) }); return json(res, 200, { ok: true, result: 'Added' }); }
  if (p === '/api/alerts') {
    const after = Number(url.searchParams.get('after') || 0), wait = Math.min(25, Number(url.searchParams.get('wait') || 0));
    const pick = () => db.alerts.filter(a => a.id > after).slice(-50);
    let list = pick();
    if (!list.length && wait > 0) { await new Promise(ok => { const t = setTimeout(() => { waiters.delete(f); ok(); }, wait * 1000); const f = () => { clearTimeout(t); waiters.delete(f); ok(); }; waiters.add(f); req.on('close', f); }); list = pick(); }
    return json(res, 200, { alerts: list, last: db.alertId });
  }
  if (p === '/api/devices') return json(res, 200, { devices: [...db.tokens.values()].map(d => ({ id: d.id, name: d.name, addedAt: iso(d.addedAt), lastSeen: iso(d.lastSeen), current: d === dev, capabilities: d.capabilities || [] })) });
  if (p === '/api/unpair' && req.method === 'POST') { for (const [t, d] of db.tokens) if (d === dev) db.tokens.delete(t); return json(res, 200, { ok: true }); }
  if (p === '/api/mock/alert' && req.method === 'POST') { const b = await readBody(req); pushAlert(b.level || 'warn', b.title || 'Test', b.text || ''); return json(res, 200, { ok: true }); }
  return json(res, 404, { error: 'Nie znaleziono.' });
}

export function start(port = PORT) {
  const server = http.createServer(async (req, res) => {
    try {
      const url = new URL(req.url, 'http://x');
      if (url.pathname.startsWith('/api/')) return await api(req, res, url);
      let rel = decodeURIComponent(url.pathname); if (rel === '/') rel = '/index.html';
      const file = path.join(WEB, rel);
      if (!file.startsWith(WEB) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); return res.end('nie znaleziono'); }
      res.writeHead(200, { 'content-type': TYPES[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-cache' });
      fs.createReadStream(file).pipe(res);
    } catch (e) { try { json(res, 500, { error: String(e.message || e) }); } catch { /* połączenie zamknięte */ } }
  });
  return new Promise(ok => server.listen(port, '0.0.0.0', () => ok({ server, port: server.address().port, db, pushAlert })));
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const { port } = await start();
  console.log(`Sentinel X Link (atrapa) → http://127.0.0.1:${port}/   odcisk: ${FP.slice(0, 16)}…   zatwierdzenie po ${APPROVE_MS} ms`);
}
