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
  function compress(H, bytes, offset) {
    const dv = new DataView(bytes.buffer, bytes.byteOffset + offset, 64), w = new Uint32Array(64);
    for (let i = 0; i < 16; i++) w[i] = dv.getUint32(i * 4, false);
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
    H[0] = (H[0] + a) >>> 0; H[1] = (H[1] + b) >>> 0; H[2] = (H[2] + c) >>> 0; H[3] = (H[3] + d) >>> 0;
    H[4] = (H[4] + e) >>> 0; H[5] = (H[5] + f) >>> 0; H[6] = (H[6] + g) >>> 0; H[7] = (H[7] + hh) >>> 0;
  }
  function create() {
    const H = Uint32Array.from(H0), tail = new Uint8Array(64);
    let tailLength = 0, byteLength = 0, finished = false;
    const hasher = {
      update(input) {
        if (finished) throw new Error('SHA-256 state is already finalized');
        const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
        byteLength += bytes.length;
        let offset = 0;
        if (tailLength) {
          const take = Math.min(64 - tailLength, bytes.length);
          tail.set(bytes.subarray(0, take), tailLength); tailLength += take; offset += take;
          if (tailLength === 64) { compress(H, tail, 0); tailLength = 0; }
        }
        while (offset + 64 <= bytes.length) { compress(H, bytes, offset); offset += 64; }
        if (offset < bytes.length) { tail.set(bytes.subarray(offset), 0); tailLength = bytes.length - offset; }
        return hasher;
      },
      digest() {
        if (finished) throw new Error('SHA-256 state is already finalized');
        finished = true;
        const padded = new Uint8Array(tailLength < 56 ? 64 : 128);
        padded.set(tail.subarray(0, tailLength)); padded[tailLength] = 0x80;
        const dv = new DataView(padded.buffer), bits = byteLength * 8;
        dv.setUint32(padded.length - 8, Math.floor(byteLength / 536870912) >>> 0, false);
        dv.setUint32(padded.length - 4, bits >>> 0, false);
        for (let offset = 0; offset < padded.length; offset += 64) compress(H, padded, offset);
        const out = new Uint8Array(32), outView = new DataView(out.buffer);
        H.forEach((value, i) => outView.setUint32(i * 4, value, false));
        return out;
      }
    };
    return hasher;
  }
  const sha256 = bytes => create().update(bytes).digest();
  sha256.create = create;
  return sha256;
})();
const b64uEnc = b => btoa(String.fromCharCode(...b)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const b64uDec = s => { s = s.replace(/-/g, '+').replace(/_/g, '/'); while (s.length % 4) s += '='; return Uint8Array.from(atob(s), c => c.charCodeAt(0)); };
const hexDec = s => Uint8Array.from((s.match(/../g) || []).map(x => parseInt(x, 16)));
const freshRequestId = () => b64uEnc(crypto.getRandomValues(new Uint8Array(24)));
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
    offlineMode: () => !!call('isOfflineMode'),
    networkStatus: () => {
      try { const value = JSON.parse(String(call('networkStatusJson') || '{}')); return value && typeof value === 'object' ? value : {}; }
      catch { return {}; }
    },
    tlsFingerprint: () => String(call('tlsFingerprint') || ''),
    deviceName: () => String(call('deviceName') || ''),
    savedToken: () => String(call('savedToken') || ''),
    phoneCapabilities: () => {
      try {
        const values = JSON.parse(String(call('capabilitiesJson') || '[]'));
        const known = new Set(['notifications', 'voiceInput', 'wakeOnLan']);
        return Array.isArray(values) ? [...new Set(values.filter(x => typeof x === 'string' && known.has(x)))] : [];
      } catch { return []; }
    },
    saveSession: (token, name, pc) => {
      const result = call('saveSession', token, name, pc);
      return result === undefined || result === true; // an older paired Android shell returned void here
    },
    saveMac: mac => call('saveMac', mac),
    clearSession: () => call('clearSession'),
    hasVoice: () => !!call('hasVoice'),
    startVoice: () => call('startVoice'),
    canWake: () => !!call('canWake'),
    wakePc: () => call('wakePc'),
    startDownload: (id, name, sha256) => call('downloadFile', id, name, sha256) === true,
    cancelDownload: () => call('cancelDownload'),
    rediscover: () => call('rediscover')
  };
})();

function reportedPhoneCapabilities() {
  if (Native) return Native.phoneCapabilities();
  return (window.SpeechRecognition || window.webkitSpeechRecognition) ? ['voiceInput'] : [];
}

/* ───────────── stan ───────────── */
const S = {
  token: Native ? Native.savedToken() : store.get('token'), device: store.get('device'), pcName: store.get('pc'),
  tab: store.get('lastTab', 'home'), online: !(Native && Native.offlineMode()), busy: false, ctrl: null,
  speak: store.get('speak') === '1', offlineMode: !!(Native && Native.offlineMode()),
  state: null, cachedSnapshot: null, lastLatencyMs: null, wakeRequestedAt: Number(store.get('wakeRequestedAt', '0')) || 0,
  tasks: null, notes: null, alerts: [], alertLast: 0, alertSeen: Number(store.get('alertSeen', '0')) || 0,
  files: null, fileChunkBytes: 40 * 1024, fileUploadId: '', fileAbortController: null, nativeDownloadActive: false,
  automations: null, automationDraftActions: [], automationEditingId: '',
  chat: [], pair: null, offlineSince: Native && Native.offlineMode() ? Date.now() : 0, booted: false
};
try { S.cachedSnapshot = JSON.parse(store.get('dashboardSnapshot', 'null')); } catch { S.cachedSnapshot = null; }
try {
  const cachedAlerts = JSON.parse(store.get('alertsCache', '[]'));
  if (Array.isArray(cachedAlerts)) { S.alerts = cachedAlerts.slice(0, 100); S.alertLast = S.alerts.reduce((last, item) => Math.max(last, Number(item.id) || 0), 0); }
} catch { S.alerts = []; }
if (!['home', 'pc', 'network', 'ai', 'menu', 'station', 'files', 'automations', 'tasks', 'notes', 'alerts', 'settings'].includes(S.tab)) S.tab = 'home';
const ui = {}; // referencje do elementów

class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }

async function api(path, { method = 'GET', body, signal, raw = false } = {}) {
  const headers = { Accept: 'application/json' };
  if (S.token) headers.Authorization = 'Bearer ' + S.token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase())) headers['X-Sentinel-Request-Id'] = freshRequestId();
  let res; const started = performance.now();
  try {
    res = await fetch(path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined, signal, cache: 'no-store' });
  } catch (e) {
    if (e.name !== 'AbortError') setOnline(false);
    throw e;
  }
  if (!path.startsWith('api/alerts')) S.lastLatencyMs = Math.max(0, Math.round(performance.now() - started));
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
  return 'Brak połączenia z PC. Sprawdź lokalną sieć (Wi‑Fi/Ethernet), zaporę lub skonfigurowany prywatny VPN.';
}

/* ───────────── łączność ───────────── */
function setOnline(on) {
  if (S.online === on) { if (S.tab === 'home') renderHome(); return; }
  S.online = on;
  S.offlineSince = on ? 0 : Date.now();
  renderNet();
  if (S.tab === 'home') renderHome();
  if (S.tab === 'pc') renderPc();
  if (S.tab === 'network') renderNetwork();
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
    ui.banner.textContent = 'PC jest nieosiągalny. Sprawdź lokalną sieć (telefon może używać Wi‑Fi, a PC Ethernet) albo własny VPN — próbuję ponownie z oszczędnym odstępem…';
  }
}
let retrying = false;
async function retryLoop() {
  if (retrying) return; retrying = true;
  try {
    while (!S.online) {
      await sleep(document.hidden ? 60000 : (Native ? 30000 : 12000));
      if (document.hidden) break;
      const probe = new AbortController(), timeout = setTimeout(() => probe.abort(), 5000);
      try { const response = await fetch('api/hello', { cache: 'no-store', signal: probe.signal }); if (response.ok) setOnline(true); }
      catch { /* następna próba z ograniczoną częstotliwością */ }
      finally { clearTimeout(timeout); }
      if (!S.online && Native && Date.now() - S.offlineSince > 90000) { Native.rediscover(); S.offlineSince = Date.now(); }
    }
    if (S.token && S.online) { refreshState(); if (S.tab !== 'ai') loadTab(S.tab); }
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
const VIEWS = ['pair', 'home', 'pc', 'station', 'network', 'ai', 'files', 'automations', 'menu', 'tasks', 'notes', 'alerts', 'settings'];
const MENU_VIEWS = new Set(['station', 'files', 'automations', 'tasks', 'notes', 'alerts', 'settings']);
function showView(name) {
  $('#app').dataset.state = name === 'pair' ? 'pair' : 'main';
  for (const view of VIEWS) $('#view-' + view).hidden = view !== name;
  const inMain = name !== 'pair';
  ui.tabs.hidden = !inMain;
  ui.composer.hidden = name !== 'ai';
  ui.btnSettings.classList.toggle('on', name === 'settings');
  const navSelection = MENU_VIEWS.has(name) ? 'menu' : name;
  $$('#tabs button').forEach(button => button.classList.toggle('on', button.dataset.tab === navSelection));
}
function go(tab) {
  if (!VIEWS.includes(tab) || tab === 'pair') return;
  S.tab = tab; store.set('lastTab', tab);
  showView(tab);
  loadTab(tab);
  if (tab === 'ai') scrollChat(true);
}
function loadTab(tab) {
  if (tab === 'home') renderHome();
  else if (tab === 'pc') { refreshState(); renderPc(); }
  else if (tab === 'station') renderStation();
  else if (tab === 'network') renderNetwork();
  else if (tab === 'ai') { renderChat(); if (S.token) refreshState(); }
  else if (tab === 'files') loadFiles();
  else if (tab === 'automations') loadAutomations();
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
    const r = await api('api/pair/request', { method: 'POST', body: { device: name, nonce, capabilities: reportedPhoneCapabilities() } });
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
        S.token = st.token;
        if (Native) {
          store.del('token'); // the Android shell stores this credential encrypted by Android Keystore
          if (!Native.saveSession(st.token, name, S.pcName || ''))
            toast('Sesja tymczasowa', 'Nie udało się bezpiecznie zapisać tokenu. Po zamknięciu aplikacji może być konieczne ponowne parowanie.', 'warn');
        } else store.set('token', st.token);
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
    if (S.wakeRequestedAt) { S.wakeRequestedAt = 0; store.del('wakeRequestedAt'); }
    if (s.pc && s.pc.name) { S.pcName = s.pc.name; store.set('pc', s.pc.name); }
    if (s.pc && s.pc.mac && s.pc.mac.length && Native) Native.saveMac(s.pc.mac[0]);
    S.cachedSnapshot = {
      pc: { name: s.pc?.name || S.pcName, version: s.pc?.version || '', uptime: s.pc?.uptime || '', time: s.pc?.time || '' },
      metrics: { cpu: s.metrics?.cpu, ramUsed: s.metrics?.ramUsed, ramTotal: s.metrics?.ramTotal, network: s.metrics?.network || '' },
      savedAt: Date.now()
    };
    store.set('dashboardSnapshot', JSON.stringify(S.cachedSnapshot));
    renderHeader();
    if (S.tab === 'home') renderHome();
    if (S.tab === 'pc') renderPc();
    if (S.tab === 'network') renderNetwork();
    if (S.tab === 'settings') renderSettings();
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
  const s = S.state;
  if (!s || !s.metrics) {
    const details = $('#pcDetails'); details.replaceChildren(
      kv('Status', pcStatusLabel()), kv('Komputer', S.pcName || 'Nieznany'),
      kv('Ostatni pomiar', S.cachedSnapshot?.savedAt ? fmtWhen(new Date(S.cachedSnapshot.savedAt).toISOString()) : 'Brak danych'),
      kv('Temperatura', 'Niedostępna — bieżący PC backend nie udostępnia czujnika temperatury'));
    $('#btnStop').hidden = true; $('#btnStop').disabled = true;
    for (const id of ['btnLockPc', 'btnRestartPc', 'btnShutdownPc']) { $('#' + id).hidden = true; $('#' + id).disabled = true; }
    return;
  }
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
  const details = $('#pcDetails'); details.replaceChildren(
    kv('Status', pcStatusLabel()), kv('Uptime', s.pc?.uptime || 'Niedostępny'),
    kv('Sieć PC', m.network || 'Brak danych'),
    kv('Adresy PC', (s.pc?.addresses || []).join(' · ') || 'Brak adresów LAN'),
    kv('Ostatni pomiar', s.pc?.time ? fmtWhen(s.pc.time) : 'Brak znacznika czasu'),
    kv('Temperatura', 'Niedostępna — aktualny dostawca telemetryczny nie raportuje temperatury'));
  const a = s.assistant || {};
  const stop = $('#btnStop');
  stop.hidden = Array.isArray(s.capabilities) && !s.capabilities.includes('assistantControl');
  stop.disabled = !S.online;
  stop.textContent = a.stopped ? 'Wznów asystenta' : 'Awaryjny STOP asystenta';
  stop.className = a.stopped ? 'btn primary' : 'btn danger';
  const powerAvailable = Array.isArray(s.capabilities) && s.capabilities.includes('powerControl');
  for (const id of ['btnLockPc', 'btnRestartPc', 'btnShutdownPc']) {
    const button = $('#' + id); button.hidden = !powerAvailable; button.disabled = !S.online;
  }
}

function pcStatusLabel() {
  if (S.online && S.token) return 'Online';
  if (S.wakeRequestedAt && Date.now() - S.wakeRequestedAt < 120000) return 'Uruchamianie';
  if (!S.token) return Native && Native.offlineMode() ? 'Offline — bez sparowania' : 'Nie sparowano';
  if (S.offlineSince && Date.now() - S.offlineSince > 120000) return 'Nieosiągalny';
  return 'Offline';
}
function renderHome() {
  const status = pcStatusLabel();
  const dot = $('#homePcDot');
  dot.className = 'dot ' + (status === 'Online' ? 'ok' : status === 'Uruchamianie' ? 'warn' : 'error');
  $('#homePcState').textContent = status;
  $('#homePcHint').textContent = S.online && S.state?.pc?.time
    ? 'Ostatnia synchronizacja: ' + fmtWhen(S.state.pc.time)
    : S.cachedSnapshot?.savedAt ? 'Ostatnie znane dane: ' + fmtWhen(new Date(S.cachedSnapshot.savedAt).toISOString()) : 'Brak danych z PC — panel i ustawienia nadal są dostępne.';
  const metrics = S.state?.metrics || S.cachedSnapshot?.metrics || {};
  const cpu = Number.isFinite(metrics.cpu) ? metrics.cpu : NaN;
  const ram = Number.isFinite(metrics.ramUsed) && Number.isFinite(metrics.ramTotal) && metrics.ramTotal > 0 ? metrics.ramUsed / metrics.ramTotal * 100 : NaN;
  $('#homeCpu').textContent = Number.isFinite(cpu) ? nf(cpu) + '%' : '–'; setBar('homeCpuBar', cpu);
  $('#homeRam').textContent = Number.isFinite(ram) ? nf(ram) + '%' : '–'; setBar('homeRamBar', ram);
  $('#homeMetricTime').textContent = S.state?.pc?.time ? fmtTime(S.state.pc.time) : S.cachedSnapshot?.savedAt ? fmtWhen(new Date(S.cachedSnapshot.savedAt).toISOString()) : 'brak pomiaru';
  const events = $('#homeEvents'); events.replaceChildren();
  S.alerts.slice(0, 4).forEach(a => events.append(h('div', { class: 'item' }, h('span', { class: 'dot ' + (a.level || 'info') }),
    h('div', { class: 'grow' }, h('div', { class: 't', text: a.title }), h('div', { class: 'm', text: a.text })), h('div', { class: 'r', text: fmtWhen(a.at) }))));
  if (!events.children.length) events.append(h('div', { class: 'empty', text: S.online ? 'Brak nowych zdarzeń.' : 'Zdarzenia pokażemy ponownie po odzyskaniu łącza; nie generujemy przykładowych wpisów.' }));
  $('#btnHomeWake').hidden = !(Native && Native.canWake());
  $('#btnHomeReconnect').textContent = Native ? 'Znajdź PC' : 'Odśwież';
  $('#btnHomeReconnect').disabled = !Native;
}
function renderStation() {
  $('#homeStationState').textContent = 'Brak sparowanej Station/API. Odczyty i sterowanie pozostają niedostępne.';
}
function renderNetwork() {
  const phone = $('#networkPhone'); phone.replaceChildren();
  const local = Native ? Native.networkStatus() : {};
  const labels = { wifi: 'Wi‑Fi', ethernet: 'Ethernet', vpn: 'VPN', cellular: 'Sieć komórkowa', bluetooth: 'Bluetooth', offline: 'Brak aktywnej sieci', unknown: 'Nieznany' };
  phone.append(kv('Łącze', labels[local.type] || (Native ? 'Inne' : 'PWA — informacje ograniczone')),
    kv('Sieć aktywna', local.connected ? 'Tak' : Native ? 'Nie' : 'Nieznane'),
    kv('Dostęp do Internetu', local.internet ? 'Potwierdzony' : local.connected ? 'Brak / niepotwierdzony — LAN może nadal działać' : 'Nieznany'),
    kv('Adresy telefonu', Array.isArray(local.addresses) && local.addresses.length ? local.addresses.join(' · ') : Native ? 'Brak lokalnego adresu' : 'Prywatność przeglądarki ukrywa lokalny adres'),
    kv('Skanowanie sieci', 'Nie skanujemy urządzeń ani obcych podsieci'));
  const pc = S.state?.pc || S.cachedSnapshot?.pc || {};
  const addresses = S.state?.pc?.addresses || [];
  const net = $('#networkPc'); net.replaceChildren(
    kv('Połączenie z PC', S.online && S.token ? 'TLS · sparowany komputer' : pcStatusLabel()),
    kv('Nazwa PC', pc.name || S.pcName || '—'),
    kv('Adresy PC', addresses.length ? addresses.join(' · ') : 'Dostępne po połączeniu z PC'),
    kv('Interfejs PC', S.state?.metrics?.network || S.cachedSnapshot?.metrics?.network || 'Brak raportu'),
    kv('Latencja API', S.lastLatencyMs == null ? 'Brak pomiaru' : S.lastLatencyMs + ' ms'),
    kv('TLS', location.protocol === 'https:' ? 'Szyfrowane; Android przypina certyfikat PC' : 'Połączenie nie jest TLS'));
}
function rediscoverPc() {
  if (Native) { Native.rediscover(); toast('Szukam komputera', 'Discovery lokalne; adres może się zmieniać przez DHCP.', 'info', 3000); }
  else location.reload();
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
  if (fresh.length) store.set('alertsCache', JSON.stringify(S.alerts));
  if (!initial && S.tab !== 'alerts') fresh.slice(-2).forEach(a => toast(a.title, a.text, a.level === 'error' ? 'error' : a.level === 'warn' ? 'warn' : 'info'));
  if (S.tab === 'alerts') { const seen = S.alertSeen; renderAlerts(seen); markAlertsRead(); }
  if (S.tab === 'home') renderHome();
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

/* ───────────── bezpieczny transfer plików ───────────── */
const MAX_PHONE_FILE_BYTES = 25 * 1024 * 1024;
function bytesHex(bytes) { return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join(''); }
function bytesBase64(bytes) {
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 0x8000)
    binary += String.fromCharCode(...bytes.subarray(offset, Math.min(bytes.length, offset + 0x8000)));
  return btoa(binary);
}
function formatBytes(size) { return size < 1024 * 1024 ? nf(size / 1024, 1) + ' KiB' : nf(size / (1024 * 1024), 2) + ' MiB'; }
function setFileProgress(text, percent) {
  const row = $('#fileUploadProgress'); row.hidden = false;
  $('#fileProgressLabel').textContent = text;
  $('#fileProgressBar').style.width = Math.max(0, Math.min(100, percent)) + '%';
}
window.onNativeDownloadProgress = (received, total) => {
  if (!S.nativeDownloadActive) return;
  const done = Math.max(0, Number(received) || 0), size = Math.max(0, Number(total) || 0);
  setFileProgress('Pobieranie i kontrola SHA‑256 · ' + formatBytes(done) + (size ? ' / ' + formatBytes(size) : ''), size ? done * 100 / size : 0);
};
window.onNativeDownloadComplete = (ok, cancelled, message) => {
  if (!S.nativeDownloadActive) return;
  S.nativeDownloadActive = false; S.fileAbortController = null;
  $('#btnUpload').disabled = !S.token || !S.online; $('#fileInput').disabled = !S.token || !S.online;
  $('#btnUploadCancel').hidden = true; $('#fileUploadProgress').hidden = true;
  toast(ok ? 'Plik pobrany i zweryfikowany' : cancelled ? 'Pobieranie anulowane' : 'Pobieranie pliku', String(message || ''), ok ? 'ok' : cancelled ? 'warn' : 'error', 5000);
};
async function sha256File(file, signal) {
  const state = SHA.create(), step = 256 * 1024;
  for (let offset = 0; offset < file.size; offset += step) {
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError');
    const bytes = new Uint8Array(await file.slice(offset, Math.min(file.size, offset + step)).arrayBuffer());
    state.update(bytes);
    setFileProgress('Sprawdzanie SHA-256 · ' + formatBytes(Math.min(file.size, offset + bytes.length)) + ' / ' + formatBytes(file.size), 20 * Math.min(1, (offset + bytes.length) / file.size));
  }
  return bytesHex(state.digest());
}
async function loadFiles() {
  if (!S.token) { S.files = null; renderFiles(); return; }
  try {
    S.files = await api('api/files');
    S.fileChunkBytes = Number(S.files.chunkBytes) || 40 * 1024;
    renderFiles();
  } catch (e) {
    S.files = null; renderFiles();
    if (!(e instanceof ApiError && e.status === 401)) toast('FILES', friendly(e), 'warn');
  }
}
function renderFiles() {
  const list = $('#fileList'); list.replaceChildren();
  const transferBlocked = !S.token || !S.online || !!S.fileAbortController;
  $('#fileInput').disabled = transferBlocked;
  $('#btnUpload').disabled = transferBlocked;
  if (!S.token) { list.append(h('div', { class: 'empty', text: 'Sparuj telefon z PC, aby rozpocząć transfer.' })); return; }
  if (!S.online) list.append(h('div', { class: 'empty', text: 'Transfer wyłączony do czasu odzyskania bezpiecznego połączenia z PC.' }));
  if (S.files && Array.isArray(S.files.files)) S.files.files.forEach(file => list.append(h('div', { class: 'item file-row' },
    h('div', { class: 'grow' }, h('div', { class: 't', text: file.name }),
      h('div', { class: 'm', text: formatBytes(file.size) + ' · ' + fmtWhen(file.uploadedAt) }),
      h('div', { class: 'm mono', text: 'SHA‑256 ' + String(file.sha256 || '').slice(0, 16) + '…' })),
    h('button', { class: 'mini', type: 'button', 'aria-label': 'Pobierz ' + file.name, onclick: () => downloadFile(file) }, h('span', { text: '↓' })),
    h('button', { class: 'mini', type: 'button', 'aria-label': 'Usuń ' + file.name, onclick: () => deleteFile(file) }, icon('i-trash')))));
  if (!S.files?.files?.length) list.append(h('div', { class: 'empty', text: S.online ? 'Nie ma jeszcze wysłanych plików.' : 'Historia plików pojawi się po połączeniu z PC.' }));
}
async function uploadFile(file) {
  if (!file || !S.token || !S.online || S.fileAbortController) return;
  if (file.size < 1 || file.size > MAX_PHONE_FILE_BYTES) { toast('FILES', 'Limit pliku to 25 MiB.', 'warn'); return; }
  const ctrl = new AbortController(); S.fileAbortController = ctrl; S.fileUploadId = '';
  $('#btnUpload').disabled = true; $('#btnUploadCancel').hidden = false;
  try {
    setFileProgress('Przygotowanie skrótu…', 0);
    const sha256 = await sha256File(file, ctrl.signal);
    const started = await api('api/files/upload/start', { method: 'POST', body: { name: file.name, size: file.size, sha256 }, signal: ctrl.signal });
    S.fileUploadId = started.id;
    const chunkBytes = Math.min(40 * 1024, Number(started.chunkBytes) || S.fileChunkBytes);
    const count = Math.ceil(file.size / chunkBytes);
    for (let index = 0, offset = 0; offset < file.size; index++, offset += chunkBytes) {
      if (ctrl.signal.aborted) throw new DOMException('Aborted', 'AbortError');
      const data = new Uint8Array(await file.slice(offset, Math.min(file.size, offset + chunkBytes)).arrayBuffer());
      const chunk = await api('api/files/upload/chunk', { method: 'POST', body: { id: started.id, index, data: bytesBase64(data) }, signal: ctrl.signal });
      const sent = Math.min(file.size, offset + data.length);
      setFileProgress(`Wysyłanie · część ${index + 1}/${count} · ${formatBytes(sent)} / ${formatBytes(file.size)}`, 20 + (80 * sent / file.size));
      if (chunk.done) S.fileUploadId = '';
    }
    toast('Plik zweryfikowany', file.name + ' · SHA‑256 zgodny', 'ok');
    await loadFiles();
  } catch (e) {
    if (S.fileUploadId) {
      const id = S.fileUploadId; S.fileUploadId = '';
      try { await api('api/files/upload/cancel', { method: 'POST', body: { id } }); } catch { /* limit czasowy PC posprząta niedokończony transfer */ }
    }
    toast(e?.name === 'AbortError' ? 'Transfer anulowany' : 'Transfer pliku', friendly(e), e?.name === 'AbortError' ? 'warn' : 'error');
    if (e?.name === 'AbortError') await loadFiles();
  } finally {
    S.fileAbortController = null; $('#btnUpload').disabled = !S.token || !S.online; $('#btnUploadCancel').hidden = true;
    $('#fileInput').disabled = !S.token || !S.online; $('#fileInput').value = ''; $('#fileUploadProgress').hidden = true;
  }
}
async function downloadFile(file) {
  if (!S.token || !S.online) { toast('FILES', 'Pobieranie wymaga połączenia z PC.', 'warn'); return; }
  if (S.fileAbortController) { toast('FILES', 'Zakończ lub anuluj bieżący transfer.', 'warn'); return; }
  if (Native) {
    S.nativeDownloadActive = true; S.fileAbortController = { abort: () => Native.cancelDownload() };
    $('#btnUpload').disabled = true; $('#fileInput').disabled = true; $('#btnUploadCancel').hidden = false;
    setFileProgress('Wybierz miejsce zapisu na telefonie…', 0);
    if (!Native.startDownload(file.id, file.name, file.sha256)) {
      S.nativeDownloadActive = false; S.fileAbortController = null; $('#btnUpload').disabled = false; $('#fileInput').disabled = false; $('#btnUploadCancel').hidden = true; $('#fileUploadProgress').hidden = true;
      toast('Pobieranie pliku', 'Ta wersja Androida nie udostępnia bezpiecznego zapisu plików.', 'error');
    }
    return;
  }
  const ctrl = new AbortController(); S.fileAbortController = ctrl; $('#btnUpload').disabled = true; $('#fileInput').disabled = true; $('#btnUploadCancel').hidden = false;
  try {
    setFileProgress('Pobieranie „' + file.name + '”…', 0);
    const response = await api('api/files/download?id=' + encodeURIComponent(file.id), { raw: true, signal: ctrl.signal });
    if (!response.ok) { let message = 'Nie udało się pobrać pliku.'; try { message = (await response.json()).error || message; } catch { } throw new ApiError(response.status, message); }
    const parts = [], hasher = SHA.create(), reader = response.body?.getReader();
    let received = 0; const total = Number(response.headers.get('content-length')) || Number(file.size) || 0;
    if (reader) {
      while (true) {
        if (ctrl.signal.aborted) throw new DOMException('Aborted', 'AbortError');
        const { value, done } = await reader.read(); if (done) break;
        parts.push(value); hasher.update(value); received += value.length;
        setFileProgress('Pobieranie i kontrola · ' + formatBytes(received) + (total ? ' / ' + formatBytes(total) : ''), total ? 100 * received / total : 40);
      }
    } else {
      const bytes = new Uint8Array(await response.arrayBuffer()); parts.push(bytes); hasher.update(bytes); received = bytes.length;
    }
    const actualHash = bytesHex(hasher.digest());
    if (!file.sha256 || actualHash.toLowerCase() !== String(file.sha256).toLowerCase()) throw new ApiError(409, 'SHA‑256 pliku nie zgadza się z historią PC; plik nie został zapisany.');
    const url = URL.createObjectURL(new Blob(parts));
    const anchor = h('a', { href: url, download: file.name }); document.body.append(anchor); anchor.click(); anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    toast('Plik pobrany i zweryfikowany', file.name + ' · ' + formatBytes(received), 'ok', 4000);
  } catch (e) { toast(e?.name === 'AbortError' ? 'Pobieranie anulowane' : 'Pobieranie pliku', friendly(e), e?.name === 'AbortError' ? 'warn' : 'error'); }
  finally { S.fileAbortController = null; $('#btnUpload').disabled = !S.token || !S.online; $('#fileInput').disabled = !S.token || !S.online; $('#btnUploadCancel').hidden = true; $('#fileUploadProgress').hidden = true; }
}
async function deleteFile(file) {
  if (!(await ask('Usunąć „' + file.name + '” z katalogu transferów PC?', 'Usuń plik'))) return;
  try { await api('api/files/delete', { method: 'POST', body: { id: file.id } }); await loadFiles(); toast('Plik usunięty', file.name, 'ok', 2500); }
  catch (e) { toast('FILES', friendly(e), 'error'); }
}

/* ───────────── automatyzacje PC z katalogu typowanych akcji ───────────── */
const triggerNames = { manual: 'Ręcznie', applicationStartup: 'Przy starcie PC', dailySchedule: 'Codziennie' };
async function loadAutomations() {
  if (!S.token) { S.automations = null; renderAutomations(); return; }
  try { S.automations = await api('api/automations'); renderAutomations(); }
  catch (e) { S.automations = null; renderAutomations(); if (!(e instanceof ApiError && e.status === 401)) toast('AUTOMATIONS', friendly(e), 'warn'); }
}
function renderAutomationDraft() {
  const box = $('#automationDraft'); box.replaceChildren();
  S.automationDraftActions.forEach((step, index) => {
    const descriptor = (S.automations?.actions || []).find(x => x.id === step.actionId);
    box.append(h('div', { class: 'item' }, h('div', { class: 'grow' },
      h('div', { class: 't', text: descriptor?.name || step.actionId }), h('div', { class: 'm', text: step.parameter || '(bez parametru)' })),
      h('button', { class: 'mini', type: 'button', 'aria-label': 'Usuń akcję', onclick: () => { S.automationDraftActions.splice(index, 1); renderAutomationDraft(); } }, icon('i-trash'))));
  });
  if (!S.automationDraftActions.length) box.append(h('div', { class: 'empty', text: 'Dodaj co najmniej jedną akcję z katalogu PC.' }));
}
function renderAutomations() {
  const catalog = S.automations;
  const select = $('#automationAction'), oldAction = select.value; select.replaceChildren();
  (catalog?.actions || []).forEach(action => select.append(h('option', { value: action.id, text: action.name + ' · ' + action.category })));
  if ((catalog?.actions || []).some(x => x.id === oldAction)) select.value = oldAction;
  const list = $('#automationList'); list.replaceChildren();
  (catalog?.rules || []).forEach(item => {
    const rule = item.rule, running = !!item.running;
    const trigger = triggerNames[rule.trigger] || rule.trigger;
    const details = trigger === 'Codziennie' ? trigger + ' o ' + rule.scheduleTime : trigger;
    const row = h('div', { class: 'automation-row' },
      h('div', { class: 'item' }, h('span', { class: 'dot ' + (running ? 'warn' : rule.enabled ? 'ok' : 'info') }),
        h('div', { class: 'grow' }, h('div', { class: 't', text: rule.name }), h('div', { class: 'm', text: details + ' · ' + (rule.actions || []).length + ' akcji · ' + (rule.lastRunStatus || 'bez wykonań') })),
        h('button', { class: 'mini', type: 'button', 'aria-label': 'Edytuj ' + rule.name, onclick: () => editAutomation(rule) }, h('span', { text: '✎' }))),
      h('div', { class: 'row automation-actions' },
        h('label', { class: 'mini-toggle' }, h('input', { type: 'checkbox', checked: !!rule.enabled, 'aria-label': 'Włącz ' + rule.name, onchange: e => setAutomationEnabled(rule.id, e.target.checked) }), h('span', { text: 'Włącz' })),
        running ? h('button', { class: 'btn ghost sm', type: 'button', text: 'Anuluj', onclick: () => cancelAutomation(rule.id) })
          : h('button', { class: 'btn ghost sm', type: 'button', text: 'Uruchom', onclick: () => runAutomation(rule) }),
        h('button', { class: 'btn ghost sm', type: 'button', text: 'Usuń', onclick: () => deleteAutomation(rule) })));
    list.append(row);
  });
  if (!list.children.length) list.append(h('div', { class: 'empty', text: catalog?.available ? 'Brak zapisanych reguł.' : 'Połącz się z PC; obecna wersja nie udostępnia katalogu automatyzacji.' }));
  const history = $('#automationHistory'); history.replaceChildren();
  (catalog?.history || []).slice(0, 20).forEach(run => {
    history.append(h('div', { class: 'item' },
      h('span', { class: 'dot ' + (run.status === 'SUCCESS' ? 'ok' : run.status === 'CANCELLED' ? 'warn' : 'error') }),
      h('div', { class: 'grow' },
        h('div', { class: 't', text: run.ruleName + ' · ' + run.status }),
        h('div', { class: 'm', text: (run.result || run.error || '') + ' · ' + fmtWhen(run.startedAt) }))));
  });
  if (!history.children.length) history.append(h('div', { class: 'empty', text: 'Brak wykonań.' }));
  const enabled = !!catalog?.available && Array.isArray(catalog.actions) && catalog.actions.length > 0;
  $('#automationForm').querySelectorAll('input,select,button').forEach(control => { if (control.id !== 'btnAutomationClear') control.disabled = !enabled; });
  if (catalog?.actions?.length) updateAutomationActionHelp();
  renderAutomationDraft();
}
function updateAutomationActionHelp() {
  const action = (S.automations?.actions || []).find(x => x.id === $('#automationAction').value);
  $('#automationActionHelp').textContent = action ? action.description + (action.requiredPermission ? ' · Uprawnienie: ' + action.requiredPermission : '') : 'Wybierz akcję z katalogu PC.';
}
function editAutomation(rule) {
  S.automationEditingId = rule.id; S.automationDraftActions = (rule.actions || []).map(x => ({ actionId: x.actionId, parameter: x.parameter }));
  $('#automationName').value = rule.name; $('#automationTrigger').value = rule.trigger;
  $('#automationTime').value = rule.scheduleTime || ''; $('#automationTime').hidden = rule.trigger !== 'dailySchedule'; $('#automationTime').required = rule.trigger === 'dailySchedule';
  $('#automationEnabled').checked = !!rule.enabled; $('#automationFormTitle').textContent = 'Edytuj automatyzację';
  $('#btnAutomationSave').textContent = 'Zapisz zmiany'; renderAutomationDraft(); $('#automationForm').scrollIntoView({ behavior: 'smooth', block: 'start' });
}
function clearAutomationForm() {
  S.automationEditingId = ''; S.automationDraftActions = [];
  $('#automationForm').reset(); $('#automationTime').hidden = true; $('#automationTime').required = false; $('#automationFormTitle').textContent = 'Nowa automatyzacja';
  $('#btnAutomationSave').textContent = 'Zapisz regułę'; renderAutomationDraft();
}
async function saveAutomation(event) {
  event.preventDefault();
  const payload = {
    id: S.automationEditingId || null, name: $('#automationName').value.trim(), trigger: $('#automationTrigger').value,
    scheduleTime: $('#automationTime').value, enabled: $('#automationEnabled').checked, actions: S.automationDraftActions
  };
  if (!payload.actions.length) { toast('AUTOMATIONS', 'Dodaj co najmniej jedną akcję.', 'warn'); return; }
  try { await api('api/automations/save', { method: 'POST', body: payload }); toast('Automatyzacja zapisana', 'PC zweryfikował regułę i jej akcje.', 'ok'); clearAutomationForm(); await loadAutomations(); }
  catch (e) { toast('Nie zapisano reguły', friendly(e), 'error'); }
}
async function runAutomation(rule) {
  if (!(await ask('Uruchomić teraz „' + rule.name + '”? PC wykona tylko zapisane akcje z katalogu.', 'Uruchom'))) return;
  try { const result = await api('api/automations/run', { method: 'POST', body: { id: rule.id } }); toast('Automatyzacja · ' + result.status, result.message, result.status === 'SUCCESS' ? 'ok' : 'warn'); await loadAutomations(); }
  catch (e) { toast('AUTOMATIONS', friendly(e), 'error'); }
}
async function cancelAutomation(id) {
  try { await api('api/automations/cancel', { method: 'POST', body: { id } }); toast('Wysłano anulowanie', 'Ukończone efekty nie są cofane.', 'warn'); await loadAutomations(); }
  catch (e) { toast('AUTOMATIONS', friendly(e), 'error'); }
}
async function setAutomationEnabled(id, enabled) {
  try { await api('api/automations/enabled', { method: 'POST', body: { id, enabled } }); await loadAutomations(); }
  catch (e) { toast('AUTOMATIONS', friendly(e), 'error'); await loadAutomations(); }
}
async function deleteAutomation(rule) {
  if (!(await ask('Usunąć regułę „' + rule.name + '”?', 'Usuń'))) return;
  try { await api('api/automations/delete', { method: 'POST', body: { id: rule.id } }); await loadAutomations(); }
  catch (e) { toast('AUTOMATIONS', friendly(e), 'error'); }
}

/* ───────────── zdalne zasilanie: challenge jednorazowy + podwójne potwierdzenie ───────────── */
async function runPowerAction(action) {
  const labels = { lock: 'zablokować', restart: 'uruchomić ponownie', shutdown: 'wyłączyć' };
  const label = labels[action]; if (!label || !S.online) return;
  const destructive = action !== 'lock';
  if (!(await ask(`Czy na pewno chcesz ${label} komputer ${S.pcName || 'Sentinel X'}?`, destructive ? 'Przygotuj operację' : 'Dalej'))) return;
  try {
    const prepared = await api('api/power/prepare', { method: 'POST', body: { action } });
    if (!prepared.challenge) throw new ApiError(502, 'PC nie przygotował jednorazowego potwierdzenia.');
    if (!(await ask(`Ostatnie potwierdzenie: ${label} komputer. ${destructive ? 'Operacja jest opóźniona o 30 sekund.' : 'Zablokuje to bieżącą sesję użytkownika.'}`, destructive ? 'Potwierdź i wykonaj' : 'Zablokuj PC'))) return;
    const result = await api('api/power/execute', { method: 'POST', body: { action, challenge: prepared.challenge } });
    if (action === 'restart') { S.wakeRequestedAt = Date.now(); store.set('wakeRequestedAt', S.wakeRequestedAt); }
    toast(result.ok ? 'PC przyjął operację' : 'Operacja odrzucona', result.message || '', result.ok ? 'warn' : 'error', 7000);
  } catch (e) { toast('Sterowanie PC', friendly(e), 'error'); }
}

/* ───────────── ustawienia ───────────── */
function kv(k, v) { return h('div', { class: 'kv' }, h('span', { text: k }), h('span', { text: v })); }
function renderSettings() {
  const s = S.state || {}; const pc = s.pc || {};
  const info = $('#setInfo'); info.replaceChildren(
    kv('Komputer', S.pcName || '–'), kv('Adres', Native && Native.offlineMode() ? 'Panel lokalny (offline)' : location.host), kv('Wersja Sentinel', pc.version || '–'),
    kv('Ten telefon', S.device || '–'), kv('Połączenie', location.protocol === 'https:' ? 'TLS' : 'Brak TLS w przeglądarce'),
    kv('Aplikacja', Native ? 'Android' : 'przeglądarka'));
  $('#optSpeak').checked = S.speak;
  $('#btnWake').hidden = !(Native && Native.canWake());
  $('#btnFind').hidden = !Native;
  $('#btnRotateToken').hidden = !S.token;
  $('#btnRotateToken').disabled = !S.online;
  $('#btnUnpair').hidden = !S.token;
  const about = $('#setAbout');
  about.textContent = Native ? 'Token Androida jest przechowywany przez Keystore. Alerty w tle odświeża harmonogram systemowy, a widok sieci nie odczytuje SSID ani MAC.'
    : /iPhone|iPad/.test(navigator.userAgent) ? 'Na iPhonie: Udostępnij → „Do ekranu początkowego”, aby mieć Sentinel jak aplikację.'
      : 'Wskazówka: w menu przeglądarki wybierz „Dodaj do ekranu głównego”. Token przeglądarki pozostaje w jej lokalnym magazynie.';
  const box = $('#setDevices');
  if (!S.token) { box.replaceChildren(h('div', { class: 'empty', text: 'Lista urządzeń jest dostępna po sparowaniu.' })); return; }
  box.replaceChildren(h('div', { class: 'kicker', style: 'padding-top:10px', text: 'Sparowane telefony' }));
  api('api/devices').then(r => {
    box.replaceChildren(h('div', { class: 'kicker', style: 'padding-top:10px', text: 'Sparowane telefony' }));
    (r.devices || []).forEach(d => box.append(h('div', { class: 'item' },
      h('div', { class: 'grow' }, h('div', { class: 't', text: d.name + (d.current ? ' (ten telefon)' : '') }), h('div', { class: 'm', text: 'ostatnio: ' + fmtWhen(d.lastSeen) })),
      d.current ? null : h('button', { class: 'btn danger sm', type: 'button', text: 'Unieważnij', onclick: () => revokeDevice(d) }))));
    if (!r.devices?.length) box.append(h('div', { class: 'empty', text: 'Brak sparowanych urządzeń.' }));
  }).catch(e => { box.replaceChildren(h('div', { class: 'empty', text: friendly(e) })); });
}
async function rotateToken() {
  if (!(await ask('Odnowić token tego telefonu? Stary token zostanie unieważniony od razu.', 'Odśwież token'))) return;
  const button = $('#btnRotateToken'); button.disabled = true;
  try {
    const response = await api('api/devices/rotate', { method: 'POST', body: {} });
    if (!response.token) throw new Error('PC nie zwrócił nowego tokenu.');
    S.token = response.token;
    if (Native) {
      store.del('token');
      if (!Native.saveSession(response.token, S.device || Native.deviceName(), S.pcName || ''))
        toast('Token odnowiony tymczasowo', 'Nie udało się zapisać go w Android Keystore. Pozostaw aplikację otwartą i ponownie sparuj po jej zamknięciu.', 'warn', 8000);
      else toast('Token odnowiony', 'Stary token został unieważniony.', 'ok');
    } else { store.set('token', response.token); toast('Token odnowiony', 'Stary token został unieważniony.', 'ok'); }
    await refreshState(true); renderSettings();
  } catch (e) { toast('Nie odnowiono tokenu', friendly(e), 'error'); }
  finally { button.disabled = !S.online; }
}
async function revokeDevice(device) {
  if (!(await ask('Unieważnić token telefonu „' + device.name + '”? Jego aktywne transfery zostaną anulowane.', 'Unieważnij'))) return;
  try { await api('api/devices/revoke', { method: 'POST', body: { id: device.id } }); toast('Telefon odłączony', device.name, 'ok'); renderSettings(); }
  catch (e) { toast('Nie odłączono telefonu', friendly(e), 'error'); }
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
  go(S.tab === 'pair' || !S.tab ? 'home' : S.tab);
  renderChat(); renderHeader(); scrollChat(true); updateBadge();
  alertLoop();
  if (!S.online) retryLoop();
}
function bind() {
  Object.assign(ui, {
    pcName: $('#pcName'), pcSub: $('#pcSub'), netPill: $('#netPill'), netText: $('#netText'), banner: $('#banner'), toasts: $('#toasts'),
    tabs: $('#tabs'), composer: $('#composer'), chatList: $('#chatList'), input: $('#input'), btnSend: $('#btnSend'), btnMic: $('#btnMic'),
    btnSettings: $('#btnSettings'), pairName: $('#pairName'), btnPair: $('#btnPair'), pairMsg: $('#pairMsg'), badge: $('#badgeAlerts'), noteSearch: $('#noteSearch')
  });
  $$('#tabs button').forEach(b => b.addEventListener('click', () => go(b.dataset.tab)));
  $$('[data-goto]').forEach(button => button.addEventListener('click', () => go(button.dataset.goto)));
  ui.btnSettings.addEventListener('click', () => go(S.tab === 'settings' ? 'home' : 'settings'));
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
  $('#btnLockPc').addEventListener('click', () => runPowerAction('lock'));
  $('#btnRestartPc').addEventListener('click', () => runPowerAction('restart'));
  $('#btnShutdownPc').addEventListener('click', () => runPowerAction('shutdown'));
  $('#btnHomeWake').addEventListener('click', () => { if (Native && Native.canWake()) { Native.wakePc(); S.wakeRequestedAt = Date.now(); store.set('wakeRequestedAt', S.wakeRequestedAt); toast('Wysłano sygnał Wake-on-LAN', 'PC musi obsługiwać i mieć włączone WoL.', 'info'); renderHome(); } });
  $('#btnWake').addEventListener('click', () => { if (Native && Native.canWake()) { Native.wakePc(); S.wakeRequestedAt = Date.now(); store.set('wakeRequestedAt', S.wakeRequestedAt); toast('Wysłano sygnał Wake-on-LAN', 'PC musi obsługiwać i mieć włączone WoL.', 'info'); } });
  $('#btnFind').addEventListener('click', rediscoverPc);
  $('#btnHomeReconnect').addEventListener('click', rediscoverPc);
  $('#btnNetworkFind').addEventListener('click', rediscoverPc);
  $('#btnNetworkRefresh').addEventListener('click', () => { renderNetwork(); if (S.token) refreshState(true).catch(() => {}); });
  $('#btnFilesRefresh').addEventListener('click', loadFiles);
  $('#fileUploadForm').addEventListener('submit', event => { event.preventDefault(); uploadFile($('#fileInput').files?.[0]); });
  $('#btnUploadCancel').addEventListener('click', () => { if (S.fileAbortController) S.fileAbortController.abort(); });
  $('#automationForm').addEventListener('submit', saveAutomation);
  $('#automationTrigger').addEventListener('change', event => { $('#automationTime').hidden = event.target.value !== 'dailySchedule'; $('#automationTime').required = event.target.value === 'dailySchedule'; });
  $('#automationAction').addEventListener('change', updateAutomationActionHelp);
  $('#btnAutomationAddStep').addEventListener('click', () => {
    const actionId = $('#automationAction').value, parameter = $('#automationParameter').value.trim();
    if (!actionId || !parameter) { toast('AUTOMATIONS', 'Wybierz akcję i podaj jej parametr.', 'warn'); return; }
    if (S.automationDraftActions.length >= 5) { toast('AUTOMATIONS', 'Limit to 5 akcji na regułę.', 'warn'); return; }
    S.automationDraftActions.push({ actionId, parameter }); $('#automationParameter').value = ''; renderAutomationDraft();
  });
  $('#btnAutomationClear').addEventListener('click', clearAutomationForm);
  $('#btnRotateToken').addEventListener('click', rotateToken);
  $('#optSpeak').addEventListener('change', e => { S.speak = e.target.checked; store.set('speak', S.speak ? '1' : '0'); if (!S.speak && 'speechSynthesis' in window) speechSynthesis.cancel(); });
  $('#btnUnpair').addEventListener('click', unpair);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden) { if (S.token) refreshState(); if (S.token) alertLoop(); if (!S.online) retryLoop(); }
  });
  window.addEventListener('online', () => { if (S.token) refreshState(true).catch(() => {}); if (!S.online) retryLoop(); });
  if (window.visualViewport) {
    const vv = () => document.documentElement.style.setProperty('--vvh', window.visualViewport.height + 'px');
    window.visualViewport.addEventListener('resize', vv); vv();
  }
}
async function boot() {
  bind(); loadChat(); initMic(); setBusy(false); renderNet();
  // Restore the native session from encrypted app storage. On upgrade, securely migrate an older WebView localStorage token once,
  // then erase that plaintext copy; browsers without the bridge continue using origin-scoped localStorage.
  if (Native) {
    const legacyWebToken = store.get('token');
    if (!S.token && legacyWebToken && Native.saveSession(legacyWebToken, Native.deviceName(), S.pcName || ''))
      S.token = legacyWebToken;
    store.del('token');
  }
  if (!S.token) {
    if (Native && Native.offlineMode()) { S.online = false; enterMain(); return; }
    showPair(); return;
  }
  try { await refreshState(true); enterMain(); }
  catch (e) { if (e instanceof ApiError && e.status === 401) return; enterMain(); }
}
/* Niski polling: PC co 15 s w otwartym widoku, tło panelu co 60 s; alerty korzystają z long-poll. */
let tick = 0;
setInterval(() => {
  if (!S.token || document.hidden || !S.online) return;
  tick++;
  if (S.tab === 'pc' || tick % 4 === 0) refreshState().catch(() => {});
}, 15000);

window.__sx = { computeSas, SHA, b64uEnc, b64uDec, S }; // do testów
boot();
})();
