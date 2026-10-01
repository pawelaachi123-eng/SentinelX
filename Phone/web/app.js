/* Sentinel X — aplikacja telefonu (PWA). Czysty JS, bez zależności i bez kroku budowania.
 * Serwowana przez komputer (Services/Link) i ładowana w aplikacji na Androida (phone-android).
 * Cały tekst od komputera trafia do DOM wyłącznie przez textContent — żadnego innerHTML. */
(() => {
'use strict';

/* ───────────── narzędzia ───────────── */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const SVGNS = 'http://www.w3.org/2000/svg';

function h(tag, attrs, ...kids) {
  const n = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === 'class') n.className = v;
    else if (k === 'text') n.textContent = v;
    else if (k.startsWith('on') && typeof v === 'function') n.addEventListener(k.slice(2), v);
    else n.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of kids.flat()) if (c != null && c !== false) n.append(c.nodeType ? c : document.createTextNode(String(c)));
  return n;
}
function icon(id) {
  const s = document.createElementNS(SVGNS, 'svg');
  s.setAttribute('viewBox', '0 0 24 24');
  const u = document.createElementNS(SVGNS, 'use');
  u.setAttribute('href', '#' + id);
  s.append(u);
  return s;
}
function logo(cls) {
  const s = document.createElementNS(SVGNS, 'svg');
  s.setAttribute('viewBox', '0 0 48 48'); s.setAttribute('class', cls);
  const u = document.createElementNS(SVGNS, 'use'); u.setAttribute('href', '#i-logo'); s.append(u);
  return s;
}
/* Własne okno potwierdzenia: window.confirm bywa tłumione w WebView, a to wygląda spójnie. */
function ask(message, okText = 'Tak') {
  return new Promise(resolve => {
    const done = v => { modal.remove(); resolve(v); };
    const modal = h('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' },
      h('div', { class: 'sheet' }, h('p', { text: message }),
        h('div', { class: 'row' },
          h('button', { class: 'btn ghost', type: 'button', text: 'Anuluj', onclick: () => done(false) }),
          h('button', { class: 'btn danger', type: 'button', text: okText, onclick: () => done(true) }))));
    modal.addEventListener('click', e => { if (e.target === modal) done(false); });
    document.body.append(modal);
    const b = modal.querySelector('button.danger'); if (b) b.focus();
  });
}
const store = {
  get(k, d = '') { try { const v = localStorage.getItem('sx.' + k); return v == null ? d : v; } catch { return d; } },
  set(k, v) { try { localStorage.setItem('sx.' + k, String(v)); } catch { /* prywatny tryb */ } },
  del(k) { try { localStorage.removeItem('sx.' + k); } catch { /* ignoruj */ } }
};
const nf = (v, d = 0) => Number.isFinite(v) ? v.toLocaleString('pl-PL', { maximumFractionDigits: d, minimumFractionDigits: 0 }) : '–';
const pad2 = n => String(n).padStart(2, '0');
function fmtTime(iso) { const d = new Date(iso); return isNaN(d) ? '' : `${pad2(d.getHours())}:${pad2(d.getMinutes())}`; }
function fmtWhen(iso) {
  if (!iso) return '';
  const d = new Date(iso); if (isNaN(d)) return '';
  const now = new Date(); const day = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(d) - day(now)) / 86400000);
  const t = fmtTime(iso);
  if (diff === 0) return 'dziś ' + t;
  if (diff === 1) return 'jutro ' + t;
  if (diff === -1) return 'wczoraj ' + t;
  return d.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' }) + ' ' + t;
}
const fmtMem = mb => !Number.isFinite(mb) ? '–' : mb >= 1024 ? nf(mb / 1024, 1) + ' GB' : nf(mb, 0) + ' MB';

/* SHA-256 bez crypto.subtle (ta jest niedostępna na stronie http/LAN) — potrzebny do kodu potwierdzenia. */
const SHA = (() => {
  const K = new Uint32Array(64), H0 = new Uint32Array(8);
  const primes = []; for (let n = 2; primes.length < 64; n++) if (primes.every(p => n % p)) primes.push(n);
  const frac = x => x - Math.floor(x);
  primes.forEach((p, i) => { K[i] = Math.floor(frac(Math.cbrt(p)) * 4294967296); if (i < 8) H0[i] = Math.floor(frac(Math.sqrt(p)) * 4294967296); });
  const rotr = (x, n) => (x >>> n) | (x << (32 - n));
  return function sha256(bytes) {
    const len = bytes.length, total = ((len + 9 + 63) >> 6) << 6;
    const buf = new Uint8Array(total); buf.set(bytes); buf[len] = 0x80;
    const dv = new DataView(buf.buffer);
    dv.setUint32(total - 8, Math.floor(len / 536870912), false);
    dv.setUint32(total - 4, (len << 3) >>> 0, false);
    const H = Uint32Array.from(H0), w = new Uint32Array(64);
    for (let o = 0; o < total; o += 64) {
      for (let i = 0; i < 16; i++) w[i] = dv.getUint32(o + i * 4, false);
      for (let i = 16; i < 64; i++) {
        const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3);
        const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10);
        w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
      }
      let [a, b, c, d, e, f, g, hh] = H;
      for (let i = 0; i < 64; i++) {
        const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25), ch = (e & f) ^ (~e & g);
        const t1 = (hh + S1 + ch + K[i] + w[i]) >>> 0;
        const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22), mj = (a & b) ^ (a & c) ^ (b & c);
        const t2 = (S0 + mj) >>> 0;
        hh = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
      }
      H[0] += a; H[1] += b; H[2] += c; H[3] += d; H[4] += e; H[5] += f; H[6] += g; H[7] += hh;
    }
    const out = new Uint8Array(32); const odv = new DataView(out.buffer);
    H.forEach((x, i) => odv.setUint32(i * 4, x >>> 0, false));
    return out;
  };
})();
const b64uEnc = b => btoa(String.fromCharCode(...b)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const b64uDec = s => { s = s.replace(/-/g, '+').replace(/_/g, '/'); while (s.length % 4) s += '='; return Uint8Array.from(atob(s), c => c.charCodeAt(0)); };
const hexDec = s => Uint8Array.from((s.match(/../g) || []).map(x => parseInt(x, 16)));
function computeSas(fpHex, nonceA, nonceB) {
  const fp = hexDec(fpHex), a = b64uDec(nonceA), b = b64uDec(nonceB);
  const all = new Uint8Array(fp.length + a.length + b.length); all.set(fp); all.set(a, fp.length); all.set(b, fp.length + a.length);
  const d = SHA(all);
  const n = ((d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3]) >>> 0;
  const code = String(n % 1000000).padStart(6, '0');
  return code.slice(0, 3) + ' ' + code.slice(3);
}

/* ───────────── most do aplikacji Android (opcjonalny) ───────────── */
const Native = (() => {
  const n = window.SXNative;
  if (!n) return null;
  const call = (name, ...args) => { try { return typeof n[name] === 'function' ? n[name](...args) : undefined; } catch { return undefined; } };
  return {
    isApp: true,
    tlsFingerprint: () => String(call('tlsFingerprint') || ''),
    deviceName: () => String(call('deviceName') || ''),
    savedToken: () => String(call('savedToken') || ''),
    saveSession: (token, name, pc) => call('saveSession', token, name, pc),
    saveMac: mac => call('saveMac', mac),
    clearSession: () => call('clearSession'),
    hasVoice: () => !!call('hasVoice'),
    startVoice: () => call('startVoice'),
    canWake: () => !!call('canWake'),
    wakePc: () => call('wakePc'),
    rediscover: () => call('rediscover')
  };
})();

/* ───────────── stan ───────────── */
const S = {
  token: store.get('token'), device: store.get('device'), pcName: store.get('pc'),
  tab: 'chat', online: true, busy: false, ctrl: null,
  speak: store.get('speak') === '1',
  state: null, tasks: null, notes: null, alerts: [], alertLast: 0, alertSeen: Number(store.get('alertSeen', '0')) || 0,
  chat: [], pair: null, offlineSince: 0, booted: false
};
const ui = {}; // referencje do elementów

class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }

async function api(path, { method = 'GET', body, signal, raw = false } = {}) {
  const headers = { Accept: 'application/json' };
  if (S.token) headers.Authorization = 'Bearer ' + S.token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let res;
  try {
    res = await fetch(path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined, signal, cache: 'no-store' });
  } catch (e) {
    if (e.name !== 'AbortError') setOnline(false);
    throw e;
  }
  setOnline(true);
  if (res.status === 401 && S.token) { unauthorized(); throw new ApiError(401, 'Brak autoryzacji'); }
  if (raw) return res;
  let data = {};
  try { data = await res.json(); } catch { /* pusta odpowiedź */ }
  if (!res.ok) throw new ApiError(res.status, data.error || res.statusText || 'Błąd');
  return data;
}

function friendly(e) {
  if (e instanceof ApiError) return e.message;
  if (e && e.name === 'AbortError') return 'Przerwano.';
  return 'Brak połączenia z komputerem. Sprawdź, czy jest włączony i w tej samej sieci Wi‑Fi.';
}

/* ───────────── łączność ───────────── */
function setOnline(on) {
  if (S.online === on) return;
  S.online = on;
  S.offlineSince = on ? 0 : Date.now();
  renderNet();
  if (!on) retryLoop();
}
function renderNet() {
  const pill = ui.netPill;
  pill.className = 'pill ' + (S.online ? 'ok' : 'off');
  ui.netText.textContent = S.online ? 'połączono' : 'offline';
  if (S.online) { ui.banner.hidden = true; }
  else {
    ui.banner.hidden = false;
    ui.banner.className = 'banner err';
    ui.banner.textContent = 'Brak połączenia z komputerem. Sprawdź, czy jest włączony i w tej samej sieci Wi‑Fi — łączę ponownie…';
  }
}
let retrying = false;
async function retryLoop() {
  if (retrying) return; retrying = true;
  try {
    while (!S.online) {
      await sleep(3000);
      try { await fetch('api/hello', { cache: 'no-store' }).then(r => { if (r.ok) setOnline(true); }); } catch { /* dalej offline */ }
      if (!S.online && Native && Date.now() - S.offlineSince > 15000) { Native.rediscover(); S.offlineSince = Date.now(); }
    }
    if (S.token) { refreshState(); if (S.tab !== 'chat') loadTab(S.tab); }
  } finally { retrying = false; }
}
function unauthorized() {
  S.token = ''; store.del('token');
  if (Native) Native.clearSession();
  showPair('Sesja wygasła albo telefon został odłączony na komputerze. Połącz go ponownie.');
}

/* ───────────── toasty ───────────── */
function toast(title, text = '', level = 'info', ms = 5200) {
  const t = h('div', { class: 'toast ' + level, role: 'status' }, h('b', { text: title }), text ? h('span', { text }) : null);
  ui.toasts.append(t);
  setTimeout(() => t.remove(), ms);
  while (ui.toasts.children.length > 3) ui.toasts.firstChild.remove();
}

/* ───────────── nawigacja ───────────── */
const VIEWS = ['pair', 'chat', 'pc', 'tasks', 'notes', 'alerts', 'settings'];
function showView(name) {
  $('#app').dataset.state = name === 'pair' ? 'pair' : 'main';
  for (const v of VIEWS) $('#view-' + v).hidden = v !== name;
  const inMain = name !== 'pair';
  ui.tabs.hidden = !inMain;
  ui.composer.hidden = name !== 'chat';
  ui.btnSettings.classList.toggle('on', name === 'settings');
  $$('#tabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === name));
}
function go(tab) {
  S.tab = tab;
  showView(tab);
  loadTab(tab);
  if (tab === 'chat') scrollChat(true);
}
function loadTab(tab) {
  if (tab === 'pc') refreshState();
  else if (tab === 'tasks') loadTasks();
  else if (tab === 'notes') loadNotes();
  else if (tab === 'alerts') { const seen = S.alertSeen; renderAlerts(seen); markAlertsRead(); }
  else if (tab === 'settings') renderSettings();
}

/* ───────────── parowanie ───────────── */
function guessDeviceName() {
  const fromApp = Native && Native.deviceName();
  if (fromApp) return fromApp.slice(0, 32);
  const ua = navigator.userAgent || '';
  let m = /Android [\d.]+; ([^;)]+?)(?: Build|\))/.exec(ua);
  if (m) return m[1].trim().slice(0, 32);
  if (/iPhone/.test(ua)) return 'iPhone';
  if (/iPad/.test(ua)) return 'iPad';
  return 'Telefon';
}
function showPair(message = '') {
  stopPair();
  S.tab = 'pair';
  showView('pair');
  $('#pairIdle').hidden = false; $('#pairWait').hidden = true; $('#pairWarn').hidden = true;
  ui.pairName.value = S.device || guessDeviceName();
  ui.pairMsg.textContent = message;
  ui.pcName.textContent = 'Sentinel X'; ui.pcSub.textContent = 'nie połączono';
}
function stopPair() { if (S.pair) { S.pair.cancelled = true; S.pair = null; } }
async function startPair() {
  const name = ui.pairName.value.replace(/[^\p{L}\p{N} _-]/gu, '').trim().slice(0, 32) || 'Telefon';
  ui.pairName.value = name; S.device = name; store.set('device', name);
  ui.btnPair.disabled = true; ui.pairMsg.textContent = 'Łączę z komputerem…';
  const ctx = { cancelled: false };
  S.pair = ctx;
  try {
    const nonce = b64uEnc(crypto.getRandomValues(new Uint8Array(16)));
    const r = await api('api/pair/request', { method: 'POST', body: { device: name, nonce } });
    if (ctx.cancelled) return;
    const fp = Native ? Native.tlsFingerprint() : '';
    let sas = r.sas;
    if (fp) {
      const mine = computeSas(fp, nonce, r.nonce);
      if (mine.replace(' ', '') !== String(r.sas)) { $('#pairWarn').hidden = false; sas = mine; ctx.mismatch = true; }
      else sas = mine;
    }
    const digits = String(sas).replace(/\D/g, '').padStart(6, '0');
    $('#sas').textContent = digits.slice(0, 3) + ' ' + digits.slice(3);
    $('#pairIdle').hidden = true; $('#pairWait').hidden = false;
    ui.pairMsg.textContent = '';
    const until = Date.now() + (Number(r.expiresIn) || 120) * 1000 + 5000;
    while (!ctx.cancelled && Date.now() < until) {
      await sleep(1500);
      if (ctx.cancelled) return;
      const st = await api('api/pair/status?id=' + encodeURIComponent(r.id));
      if (st.state === 'approved') {
        if (ctx.mismatch) { ui.pairMsg.textContent = 'Kody się różniały — połączenie odrzucone dla bezpieczeństwa.'; break; }
        S.token = st.token; store.set('token', st.token);
        if (Native) Native.saveSession(st.token, name, S.pcName || '');
        S.pair = null;
        await afterPair();
        return;
      }
      if (st.state === 'denied') { ui.pairMsg.textContent = 'Komputer odrzucił prośbę o połączenie.'; break; }
      if (st.state === 'expired') { ui.pairMsg.textContent = 'Prośba wygasła. Spróbuj jeszcze raz.'; break; }
    }
    if (!ui.pairMsg.textContent && !ctx.cancelled) ui.pairMsg.textContent = 'Nie doczekałem się zgody na komputerze. Spróbuj jeszcze raz.';
  } catch (e) {
    if (!ctx.cancelled) ui.pairMsg.textContent = friendly(e);
  } finally {
    if (!ctx.cancelled) { $('#pairIdle').hidden = false; $('#pairWait').hidden = true; }
    ui.btnPair.disabled = false;
    if (S.pair === ctx) S.pair = null;
  }
}
async function afterPair() {
  await refreshState(true).catch(() => {});
  toast('Połączono', S.pcName ? 'z komputerem ' + S.pcName : '', 'ok');
  enterMain();
}

/* ───────────── stan komputera ───────────── */
async function refreshState(force = false) {
  if (!S.token) return;
  if (!force && S.stateBusy) return;
  S.stateBusy = true;
  try {
    const s = await api('api/state');
    S.state = s;
    if (s.pc && s.pc.name) { S.pcName = s.pc.name; store.set('pc', s.pc.name); }
    if (s.pc && s.pc.mac && s.pc.mac.length && Native) Native.saveMac(s.pc.mac[0]);
    renderHeader(); if (S.tab === 'pc') renderPc(); if (S.tab === 'settings') renderSettings();
    return s;
  } finally { S.stateBusy = false; }
}
function renderHeader() {
  const s = S.state; ui.pcName.textContent = S.pcName || 'Sentinel X';
  if (!s || !s.metrics) { ui.pcSub.textContent = S.online ? 'łączenie…' : 'offline'; return; }
  const m = s.metrics; const parts = [];
  if (m.game) parts.push('gra: ' + m.game);
  if (Number.isFinite(m.cpu)) parts.push('CPU ' + nf(m.cpu) + '%');
  if (Number.isFinite(m.ramUsed) && Number.isFinite(m.ramTotal) && m.ramTotal > 0) parts.push('RAM ' + nf(m.ramUsed / m.ramTotal * 100) + '%');
  ui.pcSub.textContent = parts.join(' · ') || 'połączono';
}
function setBar(id, v) {
  const b = $('#' + id); const val = Number.isFinite(v) ? Math.max(0, Math.min(100, v)) : 0;
  b.style.width = val + '%'; b.parentElement.classList.toggle('hot', val >= 85);
}
function renderPc() {
  const s = S.state; if (!s || !s.metrics) return;
  const m = s.metrics;
  $('#mCpu').textContent = Number.isFinite(m.cpu) ? nf(m.cpu) + '%' : '–'; setBar('bCpu', m.cpu);
  const rp = m.ramTotal > 0 ? m.ramUsed / m.ramTotal * 100 : NaN;
  $('#mRam').textContent = Number.isFinite(rp) ? nf(rp) + '%' : '–'; setBar('bRam', rp);
  $('#mRamSub').textContent = Number.isFinite(m.ramUsed) ? `${nf(m.ramUsed, 1)} / ${nf(m.ramTotal, 1)} GB` : '\u00a0';
  $('#mGpu').textContent = Number.isFinite(m.gpu) ? nf(m.gpu) + '%' : 'brak odczytu'; setBar('bGpu', m.gpu);
  $('#mGame').textContent = m.game || 'brak gry';
  $('#mUptime').textContent = s.pc && s.pc.uptime ? 'działa: ' + s.pc.uptime : '\u00a0';

  const disks = $('#disks'); disks.replaceChildren();
  (m.disks || []).forEach(d => {
    const pct = d.totalGb > 0 ? d.usedGb / d.totalGb * 100 : 0;
    const bar = h('div', { class: 'bar' + (pct >= 90 ? ' hot' : '') }, h('i', { style: `width:${Math.min(100, pct)}%` }));
    disks.append(h('div', { class: 'item' }, h('div', { class: 'grow' },
      h('div', { class: 't', text: d.name }), h('div', { class: 'm', text: `zajęte ${nf(d.usedGb, 0)} z ${nf(d.totalGb, 0)} GB · wolne ${nf(d.totalGb - d.usedGb, 0)} GB` }), bar)));
  });
  if (!disks.children.length) disks.append(h('div', { class: 'empty', text: 'Brak danych o dyskach.' }));

  const procs = $('#procs'); procs.replaceChildren();
  (m.processes || []).slice(0, 8).forEach(p => procs.append(h('div', { class: 'item' },
    h('div', { class: 'grow' }, h('div', { class: 't', text: p.name }), h('div', { class: 'm', text: 'PID ' + p.pid + (Number.isFinite(p.cpu) ? ' · CPU ' + nf(p.cpu, 1) + '%' : '') })),
    h('div', { class: 'r', text: fmtMem(p.memoryMb) }))));
  if (!procs.children.length) procs.append(h('div', { class: 'empty', text: 'Brak danych o procesach.' }));

  const eng = s.engine || {}; const box = $('#engine'); box.replaceChildren();
  const level = eng.state === 'ready' ? 'ok' : eng.state === 'error' ? 'error' : 'warn';
  box.append(h('div', { class: 'status-line' }, h('span', { class: 'dot ' + level }), h('div', null,
    h('div', { text: eng.state === 'ready' ? 'Gotowy' + (eng.model ? ' · ' + eng.model : '') : eng.state === 'installing' ? 'Przygotowuje się sam' : eng.state === 'error' ? 'Wymaga uwagi' : 'Czeka' }),
    h('div', { class: 'm', style: 'color:var(--text2);font-size:13px;margin-top:2px', text: eng.message || '' }))));
  if (eng.state === 'installing' && Number.isFinite(eng.progress)) {
    box.append(h('div', { class: 'prog' }, h('i', { style: `width:${Math.round(eng.progress * 100)}%` })));
  }

  const care = s.care || {}; const cb = $('#care'); cb.replaceChildren();
  cb.append(h('div', { class: 'status-line' }, h('span', { class: 'dot ' + (care.ok === false ? 'warn' : 'ok') }), h('div', { text: care.text || 'Sentinel pilnuje komputera sam.' })));

  const net = $('#net'); net.replaceChildren();
  net.append(h('div', { text: m.network || 'Brak danych' }));
  const a = s.assistant || {};
  const stop = $('#btnStop');
  stop.textContent = a.stopped ? 'Wznów asystenta' : 'Awaryjny STOP asystenta';
  stop.className = a.stopped ? 'btn primary' : 'btn danger';
}

/* ───────────── czat ───────────── */
const CHIPS = ['Ile mam RAM?', 'Top procesy', 'Użycie CPU', 'Wolne miejsce na dyskach', 'Test internetu', 'Diagnostyka komputera', 'Która godzina?', 'Co pamiętasz?'];
function loadChat() {
  try { S.chat = JSON.parse(store.get('chat', '[]')).slice(-40); } catch { S.chat = []; }
}
function saveChat() {
  const keep = S.chat.filter(m => !m.pending).slice(-40).map(m => ({ role: m.role, text: String(m.text || '').slice(0, 4000), status: m.status, error: m.error, at: m.at }));
  store.set('chat', JSON.stringify(keep));
}
function richInto(node, text) {
  node.replaceChildren();
  const parts = String(text).split('```');
  parts.forEach((part, i) => {
    if (i % 2 === 1) { node.append(h('pre', null, h('code', { text: part.replace(/^\w*\n/, '').replace(/\n$/, '') }))); return; }
    if (!part.trim()) return;
    const p = h('p');
    part.split(/(`[^`\n]+`|\*\*[^*\n]+\*\*)/g).forEach(seg => {
      if (seg.length > 2 && seg[0] === '`' && seg.endsWith('`')) p.append(h('code', { text: seg.slice(1, -1) }));
      else if (seg.length > 4 && seg.startsWith('**') && seg.endsWith('**')) p.append(h('strong', { text: seg.slice(2, -2) }));
      else if (seg) p.append(seg);
    });
    node.append(p);
  });
}
const STATUS_META = {
  verified: ['ok', '✔ zweryfikowano dowodem'], unverified: ['', ''], failed: ['bad', '✖ nie udało się'],
  cancelled: ['warn', '⏹ przerwano'], waitingpermission: ['warn', '⏳ czeka na Twoją decyzję na komputerze'], rolledback: ['warn', '↩ wycofano']
};
function bubble(m) {
  const node = h('div', { class: 'msg ' + (m.role === 'user' ? 'user' : 'bot') + (m.error ? ' err' : '') });
  fillBubble(node, m);
  return node;
}
function fillBubble(node, m) {
  const entering = node.classList.contains('enter');
  node.className = 'msg ' + (m.role === 'user' ? 'user' : 'bot') + (m.error ? ' err' : '') + (entering ? ' enter' : '');
  if (m.role === 'user') { node.textContent = m.text; return; }
  node.replaceChildren();
  if (m.pending && !m.text) { node.append(h('span', { class: 'typing', 'aria-label': 'Sentinel pisze' }, h('i'), h('i'), h('i'))); return; }
  const body = h('div'); richInto(body, m.error ? m.error : m.text); node.append(body);
  const key = String(m.status || '').toLowerCase();
  const meta = STATUS_META[key];
  if (!m.pending && meta && meta[1]) node.append(h('div', { class: 'meta ' + meta[0], text: meta[1] }));
  else if (m.pending && m.text) node.append(h('div', { class: 'meta', text: 'pisze…' }));
}
function renderChat() {
  const list = ui.chatList; list.replaceChildren();
  if (!S.chat.length) {
    list.append(h('div', { class: 'welcome' },
      logo('logo big'),
      h('div', null, h('b', { text: 'Cześć! To ten sam Sentinel, co na komputerze.' })),
      h('div', { text: 'Pytaj o stan komputera, dodawaj zadania, zapisuj notatki albo po prostu rozmawiaj. Wszystko liczy się na Twoim PC — nic nie idzie do chmury.' })));
    return;
  }
  S.chat.forEach(m => { m.node = bubble(m); list.append(m.node); });
}
function scrollChat(force) {
  const l = ui.chatList; const near = l.scrollHeight - l.scrollTop - l.clientHeight < 140;
  if (force || near) l.scrollTop = l.scrollHeight;
}
function setBusy(on) {
  S.busy = on;
  ui.btnSend.classList.toggle('stop', on);
  ui.btnSend.replaceChildren(icon(on ? 'i-stop' : 'i-up'));
  ui.btnSend.setAttribute('aria-label', on ? 'Zatrzymaj' : 'Wyślij');
  ui.input.disabled = false;
}
function autosize() { const t = ui.input; t.style.height = 'auto'; t.style.height = Math.min(132, t.scrollHeight) + 'px'; }

async function readSse(res, onEvent) {
  const reader = res.body.getReader(); const dec = new TextDecoder(); let buf = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buf += dec.decode(value, { stream: true }).replace(/\r\n/g, '\n');
    let i;
    while ((i = buf.indexOf('\n\n')) >= 0) {
      const block = buf.slice(0, i); buf = buf.slice(i + 2);
      let ev = 'message'; const data = [];
      for (const line of block.split('\n')) {
        if (line.startsWith('event:')) ev = line.slice(6).trim();
        else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));
      }
      if (!data.length) continue;
      let payload; try { payload = JSON.parse(data.join('\n')); } catch { payload = { text: data.join('\n') }; }
      onEvent(ev, payload);
    }
  }
}
async function sendChat(text) {
  text = String(text || '').trim();
  if (!text || S.busy) return;
  if (!S.chat.length) ui.chatList.replaceChildren();
  const user = { role: 'user', text, at: Date.now() };
  const bot = { role: 'bot', text: '', pending: true, at: Date.now() };
  S.chat.push(user, bot);
  user.node = bubble(user); bot.node = bubble(bot);
  user.node.classList.add('enter'); bot.node.classList.add('enter');
  ui.chatList.append(user.node, bot.node); scrollChat(true);
  ui.input.value = ''; autosize();
  setBusy(true);
  const ctrl = new AbortController(); S.ctrl = ctrl;
  let finished = false;
  try {
    const res = await api('api/chat', { method: 'POST', body: { text }, signal: ctrl.signal, raw: true });
    if (!res.ok) { let msg = 'Błąd ' + res.status; try { msg = (await res.json()).error || msg; } catch { /* brak treści */ } throw new ApiError(res.status, msg); }
    await readSse(res, (ev, d) => {
      if (ev === 'delta') { bot.text += d.text || ''; fillBubble(bot.node, bot); scrollChat(false); }
      else if (ev === 'done') {
        finished = true; bot.pending = false; bot.text = d.text != null ? d.text : bot.text; bot.status = d.status || '';
        fillBubble(bot.node, bot); scrollChat(false); speak(bot.text);
      } else if (ev === 'error') { finished = true; bot.pending = false; bot.error = d.error || 'Błąd'; fillBubble(bot.node, bot); }
    });
    if (!finished) { bot.pending = false; if (!bot.text) bot.error = 'Połączenie z komputerem zostało przerwane.'; fillBubble(bot.node, bot); }
  } catch (e) {
    bot.pending = false;
    if (e && e.name === 'AbortError') { bot.status = 'cancelled'; if (!bot.text) bot.text = 'Przerwano.'; }
    else bot.error = friendly(e);
    fillBubble(bot.node, bot);
  } finally {
    S.ctrl = null; setBusy(false); saveChat(); scrollChat(false);
  }
}
async function stopChat() {
  if (S.ctrl) S.ctrl.abort();
  try { await api('api/control', { method: 'POST', body: { action: 'cancel' } }); } catch { /* ignoruj */ }
}
function speak(text) {
  if (!S.speak || !('speechSynthesis' in window) || !text) return;
  try { speechSynthesis.cancel(); const u = new SpeechSynthesisUtterance(String(text).slice(0, 700)); u.lang = 'pl-PL'; speechSynthesis.speak(u); } catch { /* brak TTS */ }
}
function initMic() {
  const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
  const native = Native && Native.hasVoice();
  if (!native && !SR) return;
  ui.btnMic.hidden = false;
  ui.btnMic.addEventListener('click', () => {
    if (native) { Native.startVoice(); return; }
    try {
      const r = new SR(); r.lang = 'pl-PL'; r.interimResults = false; r.maxAlternatives = 1;
      ui.btnMic.classList.add('on');
      r.onresult = ev => { const t = ev.results[0][0].transcript; if (t) window.onNativeVoice(t); };
      r.onend = () => ui.btnMic.classList.remove('on');
      r.onerror = () => ui.btnMic.classList.remove('on');
      r.start();
    } catch { toast('Mikrofon niedostępny', 'Przeglądarka nie udostępnia rozpoznawania mowy.', 'warn'); }
  });
}
window.onNativeVoice = text => { ui.input.value = text; autosize(); sendChat(text); };

/* ───────────── zadania ───────────── */
async function loadTasks() {
  try { S.tasks = await api('api/tasks'); renderTasks(); } catch (e) { if (!(e instanceof ApiError && e.status === 401)) toast('Zadania', friendly(e), 'warn'); }
}
function renderTasks() {
  const t = S.tasks; if (!t) return;
  const list = $('#taskList'); list.replaceChildren();
  $('#taskCount').textContent = t.tasks.length ? '(' + t.tasks.length + ')' : '';
  t.tasks.forEach(x => {
    const chips = [];
    if (x.priority === 'wysoki') chips.push(h('span', { class: 'chip hi', text: 'wysoki' }));
    if (x.priority === 'niski') chips.push(h('span', { class: 'chip lo', text: 'niski' }));
    if (x.status === 'w toku') chips.push(h('span', { class: 'chip', text: 'w toku' }));
    list.append(h('div', { class: 'item' },
      h('button', { class: 'check', type: 'button', 'aria-label': 'Oznacz jako zrobione', onclick: () => setTask(x.id, 'zrobione') }, icon('i-check')),
      h('div', { class: 'grow' }, h('div', { class: 't', text: x.title }), h('div', { class: 'm' }, chips, x.due ? fmtWhen(x.due) : '')),
      h('button', { class: 'mini', type: 'button', 'aria-label': 'Usuń zadanie', onclick: () => delTask(x.id, x.title) }, icon('i-trash'))));
  });
  if (!t.tasks.length) list.append(h('div', { class: 'empty', text: 'Nic do zrobienia. Miłego dnia!' }));
  const rl = $('#remList'); rl.replaceChildren();
  $('#remCount').textContent = t.reminders.length ? '(' + t.reminders.length + ')' : '';
  t.reminders.forEach(x => rl.append(h('div', { class: 'item' },
    h('span', { class: 'dot ' + (x.missed ? 'warn' : 'info') }),
    h('div', { class: 'grow' }, h('div', { class: 't', text: x.text }), h('div', { class: 'm', text: (x.missed ? 'ominięte · ' : '') + fmtWhen(x.at) })),
    h('button', { class: 'mini', type: 'button', 'aria-label': 'Usuń przypomnienie', onclick: () => delRem(x.id) }, icon('i-trash')))));
  if (!t.reminders.length) rl.append(h('div', { class: 'empty', text: 'Brak przypomnień.' }));
}
async function setTask(id, status) { try { await api('api/tasks/status', { method: 'POST', body: { id, status } }); await loadTasks(); } catch (e) { toast('Zadanie', friendly(e), 'error'); } }
async function delTask(id, title) { if (!(await ask('Usunąć zadanie „' + title + '”?', 'Usuń'))) return; try { await api('api/tasks/delete', { method: 'POST', body: { id } }); await loadTasks(); } catch (e) { toast('Zadanie', friendly(e), 'error'); } }
async function delRem(id) { try { await api('api/reminders/delete', { method: 'POST', body: { id } }); await loadTasks(); } catch (e) { toast('Przypomnienie', friendly(e), 'error'); } }
const localIso = v => v ? new Date(v).toISOString() : null;

/* ───────────── notatki ───────────── */
let noteTimer = 0;
async function loadNotes() {
  try { S.notes = await api('api/notes?q=' + encodeURIComponent(ui.noteSearch.value.trim())); renderNotes(); } catch (e) { if (!(e instanceof ApiError && e.status === 401)) toast('Notatki', friendly(e), 'warn'); }
}
function renderNotes() {
  const list = $('#noteList'); list.replaceChildren();
  (S.notes ? S.notes.notes : []).forEach(n => list.append(h('div', { class: 'item' },
    h('div', { class: 'grow' }, h('div', { class: 't', text: n.text }),
      h('div', { class: 'm' }, n.pinned ? h('span', { class: 'chip pin', text: 'przypięta' }) : null, n.category ? h('span', { class: 'chip', text: n.category }) : null, fmtWhen(n.at))))));
  if (!list.children.length) list.append(h('div', { class: 'empty', text: ui.noteSearch.value ? 'Nic nie znaleziono.' : 'Brak notatek. Dodaj pierwszą powyżej.' }));
}

/* ───────────── alerty ───────────── */
function updateBadge() {
  const unread = S.alerts.filter(a => a.id > S.alertSeen).length;
  ui.badge.hidden = unread === 0; ui.badge.textContent = unread > 99 ? '99+' : String(unread);
}
function ingestAlerts(list, initial) {
  const fresh = [];
  for (const a of list) if (!S.alerts.some(x => x.id === a.id)) { S.alerts.push(a); fresh.push(a); }
  S.alerts.sort((x, y) => y.id - x.id); S.alerts = S.alerts.slice(0, 100);
  if (list.length) S.alertLast = Math.max(S.alertLast, ...list.map(a => a.id));
  if (!initial && S.tab !== 'alerts') fresh.slice(-2).forEach(a => toast(a.title, a.text, a.level === 'error' ? 'error' : a.level === 'warn' ? 'warn' : 'info'));
  if (S.tab === 'alerts') { const seen = S.alertSeen; renderAlerts(seen); markAlertsRead(); }
  updateBadge();
}
function markAlertsRead() { if (S.alerts.length) { S.alertSeen = Math.max(S.alertSeen, S.alerts[0].id); store.set('alertSeen', S.alertSeen); } updateBadge(); }
function renderAlerts(seen = S.alertSeen) {
  const list = $('#alertList'); list.replaceChildren();
  S.alerts.forEach(a => list.append(h('div', { class: 'item' + (a.id > seen ? ' unread' : '') },
    h('span', { class: 'dot ' + (a.level || 'info') }),
    h('div', { class: 'grow' }, h('div', { class: 't', text: a.title }), h('div', { class: 'm', text: a.text })),
    h('div', { class: 'r', text: fmtWhen(a.at) }))));
  if (!list.children.length) list.append(h('div', { class: 'empty', text: 'Spokojnie — brak alertów.' }));
}
let alertsRunning = false;
async function alertLoop() {
  if (alertsRunning) return; alertsRunning = true;
  try {
    let initial = true;
    while (S.token && !document.hidden) {
      try {
        const r = await api(`api/alerts?after=${initial ? 0 : S.alertLast}&wait=${initial ? 0 : 20}`);
        ingestAlerts(r.alerts || [], initial); initial = false;
        if (!(r.alerts || []).length) await sleep(initial ? 0 : 400);
      } catch (e) { if (e instanceof ApiError && e.status === 401) break; await sleep(3500); }
    }
  } finally { alertsRunning = false; }
}

/* ───────────── ustawienia ───────────── */
function kv(k, v) { return h('div', { class: 'kv' }, h('span', { text: k }), h('span', { text: v })); }
function renderSettings() {
  const s = S.state || {}; const pc = s.pc || {};
  const info = $('#setInfo'); info.replaceChildren(
    kv('Komputer', S.pcName || '–'), kv('Adres', location.host), kv('Wersja Sentinel', pc.version || '–'),
    kv('Ten telefon', S.device || '–'), kv('Połączenie', location.protocol === 'https:' ? 'szyfrowane (TLS)' : 'bez szyfrowania'),
    kv('Aplikacja', Native ? 'Android' : 'przeglądarka'));
  $('#optSpeak').checked = S.speak;
  $('#btnWake').hidden = !(Native && Native.canWake());
  $('#btnFind').hidden = !Native;
  const about = $('#setAbout');
  about.textContent = Native ? 'Aplikacja sprawdza alerty w tle co kilkanaście minut i pokazuje je jako powiadomienia.'
    : /iPhone|iPad/.test(navigator.userAgent) ? 'Na iPhonie: Udostępnij → „Do ekranu początkowego”, aby mieć Sentinel jak aplikację.'
      : 'Wskazówka: w menu przeglądarki wybierz „Dodaj do ekranu głównego”.';
  api('api/devices').then(r => {
    const box = $('#setDevices'); box.replaceChildren(h('div', { class: 'kicker', style: 'padding-top:10px', text: 'Sparowane telefony' }));
    (r.devices || []).forEach(d => box.append(h('div', { class: 'item' }, h('div', { class: 'grow' }, h('div', { class: 't', text: d.name + (d.current ? ' (ten telefon)' : '') }), h('div', { class: 'm', text: 'ostatnio: ' + fmtWhen(d.lastSeen) })))));
  }).catch(() => {});
}
async function unpair() {
  if (!(await ask('Odłączyć ten telefon od komputera? Aby wrócić, trzeba będzie zatwierdzić parowanie na komputerze.', 'Odłącz'))) return;
  try { await api('api/unpair', { method: 'POST', body: {} }); } catch { /* i tak wyczyścimy lokalnie */ }
  S.token = ''; store.del('token'); if (Native) Native.clearSession();
  showPair('Telefon odłączony.');
}

/* ───────────── start ───────────── */
function enterMain() {
  S.booted = true;
  go(S.tab === 'pair' || !S.tab ? 'chat' : S.tab);
  renderChat(); renderHeader(); scrollChat(true);
  alertLoop();
}
function bind() {
  Object.assign(ui, {
    pcName: $('#pcName'), pcSub: $('#pcSub'), netPill: $('#netPill'), netText: $('#netText'), banner: $('#banner'), toasts: $('#toasts'),
    tabs: $('#tabs'), composer: $('#composer'), chatList: $('#chatList'), input: $('#input'), btnSend: $('#btnSend'), btnMic: $('#btnMic'),
    btnSettings: $('#btnSettings'), pairName: $('#pairName'), btnPair: $('#btnPair'), pairMsg: $('#pairMsg'), badge: $('#badgeAlerts'), noteSearch: $('#noteSearch')
  });
  $$('#tabs button').forEach(b => b.addEventListener('click', () => go(b.dataset.tab)));
  ui.btnSettings.addEventListener('click', () => go(S.tab === 'settings' ? 'chat' : 'settings'));
  ui.btnPair.addEventListener('click', startPair);
  $('#btnPairCancel').addEventListener('click', () => { stopPair(); showPair(); });
  ui.composer.addEventListener('submit', e => { e.preventDefault(); if (S.busy) stopChat(); else sendChat(ui.input.value); });
  ui.input.addEventListener('input', autosize);
  ui.input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); if (!S.busy) sendChat(ui.input.value); } });
  const chips = $('#chips'); CHIPS.forEach(c => chips.append(h('button', { type: 'button', text: c, onclick: () => sendChat(c) })));
  $('#taskForm').addEventListener('submit', async e => {
    e.preventDefault(); const title = $('#taskTitle').value.trim(); if (!title) return;
    try { await api('api/tasks', { method: 'POST', body: { title, priority: $('#taskPrio').value, due: localIso($('#taskDue').value) } }); $('#taskTitle').value = ''; $('#taskDue').value = ''; await loadTasks(); toast('Dodano zadanie', title, 'ok', 2500); }
    catch (err) { toast('Zadanie', friendly(err), 'error'); }
  });
  $('#remForm').addEventListener('submit', async e => {
    e.preventDefault(); const text = $('#remText').value.trim(); const at = $('#remAt').value; if (!text || !at) return;
    try { await api('api/reminders', { method: 'POST', body: { text, at: localIso(at) } }); $('#remText').value = ''; $('#remAt').value = ''; await loadTasks(); toast('Dodano przypomnienie', text, 'ok', 2500); }
    catch (err) { toast('Przypomnienie', friendly(err), 'error'); }
  });
  $('#noteForm').addEventListener('submit', async e => {
    e.preventDefault(); const text = $('#noteText').value.trim(); if (!text) return;
    try {
      const r = await api('api/notes', { method: 'POST', body: { text } });
      $('#noteText').value = '';
      const msg = { Added: 'Zapisano notatkę', Duplicate: 'Taka notatka już istnieje', Limit: 'Osiągnięto limit notatek', Disabled: 'Zapis wspomnień jest wyłączony na komputerze', Invalid: 'Niepoprawna notatka', StaleDuplicate: 'Podobna notatka już istnieje' }[r.result] || 'Gotowe';
      toast(msg, '', r.result === 'Added' ? 'ok' : 'warn', 2800); await loadNotes();
    } catch (err) { toast('Notatka', friendly(err), 'error'); }
  });
  ui.noteSearch.addEventListener('input', () => { clearTimeout(noteTimer); noteTimer = setTimeout(loadNotes, 300); });
  $('#btnAlertsRead').addEventListener('click', () => { markAlertsRead(); renderAlerts(S.alertSeen); });
  $('#btnStop').addEventListener('click', async () => {
    const stopped = S.state && S.state.assistant && S.state.assistant.stopped;
    try { await api('api/control', { method: 'POST', body: { action: stopped ? 'resume' : 'stop' } }); await refreshState(true); toast(stopped ? 'Asystent wznowiony' : 'Asystent zatrzymany', '', stopped ? 'ok' : 'warn', 2500); }
    catch (err) { toast('STOP', friendly(err), 'error'); }
  });
  $('#optSpeak').addEventListener('change', e => { S.speak = e.target.checked; store.set('speak', S.speak ? '1' : '0'); if (!S.speak && 'speechSynthesis' in window) speechSynthesis.cancel(); });
  $('#btnUnpair').addEventListener('click', unpair);
  $('#btnWake').addEventListener('click', () => { Native.wakePc(); toast('Wysłano sygnał budzenia', 'Komputer włączy się za chwilę, o ile ma włączone Wake-on-LAN.', 'info'); });
  $('#btnFind').addEventListener('click', () => { Native.rediscover(); toast('Szukam komputera w sieci…', '', 'info', 2500); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden && S.token) { refreshState(); alertLoop(); } });
  window.addEventListener('online', () => { if (S.token) refreshState(true).catch(() => {}); });
  if (window.visualViewport) {
    const vv = () => document.documentElement.style.setProperty('--vvh', window.visualViewport.height + 'px');
    window.visualViewport.addEventListener('resize', vv); vv();
  }
}
async function boot() {
  bind(); loadChat(); initMic(); setBusy(false); renderNet();
  // The app keeps a copy of the token: when the router gives the PC a new address the page origin changes and localStorage starts empty,
  // but the phone must not ask for a new pairing just because of that.
  if (!S.token && Native) { const saved = Native.savedToken(); if (saved) { S.token = saved; store.set('token', saved); } }
  if (!S.token) { showPair(); return; }
  try { await refreshState(true); enterMain(); }
  catch (e) { if (e instanceof ApiError && e.status === 401) return; enterMain(); }
}
/* odświeżanie: co 3 s w widoku "Komputer", w pozostałych co ~15 s (pasek u góry) */
let tick = 0;
setInterval(() => { if (!S.token || document.hidden) return; tick++; if (S.tab === 'pc' || tick % 5 === 0) refreshState().catch(() => {}); }, 3000);

window.__sx = { computeSas, SHA, b64uEnc, b64uDec, S }; // do testów
boot();
})();
