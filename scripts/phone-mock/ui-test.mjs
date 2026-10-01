/* Test interfejsu telefonu w prawdziwej przeglądarce (headless Chromium) na atrapie serwera.
 *   cd scripts/phone-mock && npm i --no-save puppeteer-core @sparticuz/chromium && AWS_EXECUTION_ENV=AWS_Lambda_nodejs22.x node ui-test.mjs
 * Zrzuty ekranu trafiają do $SHOTS_DIR (domyślnie /tmp/sx-shots). Nie uruchamia się w CI — to narzędzie deweloperskie. */
import chromium from '@sparticuz/chromium';
import puppeteer from 'puppeteer-core';
import crypto from 'node:crypto';
process.env.MOCK_APPROVE_MS = '1500';
const { start, sasFor } = await import('./server.mjs');
const { server, port, pushAlert } = await start(0);
const base = `http://127.0.0.1:${port}/`;
const out = (process.env.SHOTS_DIR || '/tmp/sx-shots') + '/';
import fs from 'node:fs'; fs.mkdirSync(out, { recursive: true });
const problems = [];
const ok = (cond, msg) => { console.log((cond ? 'PASS ' : 'FAIL ') + msg); if (!cond) problems.push(msg); };

const browser = await puppeteer.launch({ args: [...chromium.args, '--no-sandbox'], executablePath: await chromium.executablePath(), headless: 'shell' });
const page = await browser.newPage();
await page.setUserAgent('Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0 Mobile Safari/537.36');
await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
page.on('pageerror', e => { problems.push('pageerror: ' + e); console.log('PAGEERROR', e); });
page.on('console', m => { if (m.type() === 'error') { problems.push('console.error: ' + m.text()); console.log('CONSOLE.ERROR', m.text()); } });
const text = sel => page.$eval(sel, e => e.textContent.trim());
const shot = n => page.screenshot({ path: out + n + '.png' });
const wait = ms => new Promise(r => setTimeout(r, ms));

// ---- SHA-256 / SAS zgodność z Node
await page.goto(base, { waitUntil: 'load' });
const shaOk = await page.evaluate(() => {
  const res = [];
  for (const n of [0, 1, 3, 55, 56, 63, 64, 65, 119, 120, 200, 1000]) {
    const b = new Uint8Array(n); for (let i = 0; i < n; i++) b[i] = (i * 31 + 7) & 255;
    res.push([n, Array.from(window.__sx.SHA(b)).map(x => x.toString(16).padStart(2, '0')).join('')]);
  }
  return res;
});
let shaAll = true;
for (const [n, hex] of shaOk) { const b = Buffer.alloc(n); for (let i = 0; i < n; i++) b[i] = (i * 31 + 7) & 255; if (crypto.createHash('sha256').update(b).digest('hex') !== hex) { shaAll = false; console.log('SHA mismatch at', n); } }
ok(shaAll, 'SHA-256 w JS zgodny z Node dla długości 0..1000');
const fp = crypto.createHash('sha256').update('sentinelx-mock-cert').digest('hex');
const nA = crypto.randomBytes(16).toString('base64url'), nB = crypto.randomBytes(16).toString('base64url');
const sasBrowser = await page.evaluate((fp, a, b) => window.__sx.computeSas(fp, a, b), fp, nA, nB);
ok(sasBrowser.replace(' ', '') === sasFor(fp, nA, nB), `SAS w JS (${sasBrowser}) zgodny z referencją Node (${sasFor(fp, nA, nB)})`);

// ---- wektor testowy wspólny dla C# (LinkRegression), Javy i JS: te same wejścia muszą dać ten sam kod
const vec = await page.evaluate(() => {
  const fp = Array.from({ length: 32 }, (_, i) => i.toString(16).padStart(2, '0')).join('');
  return window.__sx.computeSas(fp, 'oKGio6SlpqeoqaqrrK2urw', 'sLGys7S1tre4ubq7vL2-vw');
});
ok(vec === '078 914', 'kod potwierdzenia dla wektora testowego = 078 914 (zgodny z C# i Pythonem): ' + vec);

// ---- ekran parowania
await page.evaluate(() => localStorage.clear()); await page.reload({ waitUntil: 'load' });
await page.waitForSelector('#view-pair:not([hidden])');
ok((await text('#pcName')) === 'Sentinel X', 'ekran parowania pokazuje markę');
ok((await page.$eval('#pairName', e => e.value)) === 'Pixel 7', 'nazwa telefonu odgadnięta z UA: Pixel 7');
await shot('01-pair');
await page.click('#btnPair');
await page.waitForSelector('#pairWait:not([hidden])');
const sasShown = (await text('#sas')).replace(/\s/g, '');
ok(/^\d{6}$/.test(sasShown), 'kod potwierdzenia ma 6 cyfr: ' + sasShown);
await shot('02-wait');
await page.waitForSelector('#view-chat:not([hidden])', { timeout: 15000 });
ok((await text('#pcName')) === 'PAWEL-PC', 'po zatwierdzeniu nagłówek pokazuje nazwę komputera');
await wait(400);
await shot('03-chat-empty');

// ---- czat: komenda deterministyczna
await page.click('#chips button:nth-child(1)');
await page.waitForFunction(() => document.querySelectorAll('.msg.bot .meta.ok').length >= 1, { timeout: 8000 });
ok((await page.$$eval('.msg.bot', els => els[0].textContent)).includes('GB'), 'odpowiedź RAM zawiera GB');
ok((await page.$$eval('.msg.bot .meta.ok', els => els[0].textContent)).includes('zweryfikowano'), 'status: zweryfikowano dowodem');
// ---- czat: strumień
await page.type('#input', 'Opowiedz mi, co potrafisz?');
await page.keyboard.press('Enter');
await page.waitForFunction(() => document.querySelectorAll('.msg.bot').length >= 2, { timeout: 4000 });
await wait(700);
const mid = await page.$$eval('.msg.bot', els => els[els.length - 1].textContent);
ok(mid.length > 0 && mid.length < 300, 'odpowiedź przychodzi strumieniowo (w trakcie: ' + mid.length + ' znaków)');
await page.waitForFunction(() => !document.querySelector('.msg.bot .meta:not(.ok):not(.warn):not(.bad)') && document.querySelector('#btnSend:not(.stop)'), { timeout: 10000 });
const fin = await page.$$eval('.msg.bot', els => els[els.length - 1].innerHTML);
ok(fin.includes('<strong>RAM</strong>') && fin.includes('<pre>'), 'formatowanie: pogrubienie i blok kodu');
ok(!(await page.$eval('#btnSend', e => e.classList.contains('stop'))), 'przycisk wraca do "Wyślij"');
// ---- czat: wymaga zgody na PC
await page.type('#input', 'zamknij notatnik'); await page.keyboard.press('Enter');
await page.waitForFunction(() => /czeka na Twoją decyzję/.test(document.body.innerText), { timeout: 5000 });
ok(true, 'status "czeka na decyzję na komputerze" widoczny');
// ---- czat: potwierdzenie z telefonu blokowane
await page.type('#input', 'potwierdź'); await page.keyboard.press('Enter');
await page.waitForFunction(() => /wyłącznie przyciskiem lub klawiaturą/.test(document.body.innerText), { timeout: 5000 });
ok(true, 'potwierdzenie z telefonu jest blokowane (tak jak głos)');
await wait(300); await shot('04-chat');

// ---- komputer
await page.click('#tabs button[data-tab=pc]');
await page.waitForFunction(() => document.querySelector('#mCpu').textContent.includes('%'), { timeout: 5000 });
ok((await text('#engine')).includes('qwen3:4b-instruct'), 'karta silnika AI pokazuje model');
ok((await text('#care')).includes('działa samo'), 'karta autopilota widoczna');
await wait(500); await shot('05-pc');
await page.evaluate(() => document.querySelector('#view-pc').scrollTo(0, 9999)); await wait(300); await shot('05b-pc-bottom');

// ---- zadania
await page.click('#tabs button[data-tab=tasks]');
await page.waitForSelector('#taskList .item');
await page.type('#taskTitle', 'Kupić kabel HDMI');
await page.click('#taskForm button[type=submit]');
await page.waitForFunction(() => document.querySelector('#taskList').textContent.includes('kabel HDMI'), { timeout: 4000 });
await page.click('#taskList .item:last-child .check');
await page.waitForFunction(() => !document.querySelector('#taskList').textContent.includes('kabel HDMI'), { timeout: 4000 });
ok(true, 'zadanie dodane i oznaczone jako zrobione');
const future = new Date(Date.now() + 2 * 3600e3); const pad = n => String(n).padStart(2, '0');
await page.type('#remText', 'Odebrać paczkę');
await page.$eval('#remAt', (e, v) => { e.value = v; e.dispatchEvent(new Event('input', { bubbles: true })); }, `${future.getFullYear()}-${pad(future.getMonth() + 1)}-${pad(future.getDate())}T${pad(future.getHours())}:${pad(future.getMinutes())}`);
await page.click('#remForm button[type=submit]');
await page.waitForFunction(() => document.querySelector('#remList').textContent.includes('Odebrać paczkę'), { timeout: 4000 });
ok(true, 'przypomnienie dodane'); await wait(300); await shot('06-tasks');

// ---- notatki
await page.click('#tabs button[data-tab=notes]');
await page.waitForSelector('#noteList .item');
await page.type('#noteText', 'Zielony kabel to zasilanie monitora');
await page.click('#noteForm button[type=submit]');
await page.waitForFunction(() => document.querySelector('#noteList').textContent.includes('Zielony kabel'), { timeout: 4000 });
await page.type('#noteSearch', 'motyw');
await page.waitForFunction(() => document.querySelectorAll('#noteList .item').length === 1, { timeout: 4000 });
ok(true, 'notatka dodana, wyszukiwanie działa'); await wait(200); await shot('07-notes');

// ---- alerty
await page.click('#tabs button[data-tab=chat]');
pushAlert('warn', 'Wysokie obciążenie', 'CPU 96% · RAM 91% przez dłużej niż 15 s.');
await page.waitForFunction(() => !document.querySelector('#badgeAlerts').hidden, { timeout: 8000 });
ok(true, 'nowy alert pojawił się jako znaczek na zakładce (long-poll)');
await page.waitForSelector('.toast'); await shot('08-toast');
await page.click('#tabs button[data-tab=alerts]'); await wait(300);
ok((await text('#alertList')).includes('Wysokie obciążenie'), 'alert widoczny na liście');
await shot('09-alerts');

// ---- aplikacja na Androida: kopia tokenu w aplikacji chroni przed ponownym parowaniem po zmianie adresu IP komputera
const savedToken = await page.evaluate(() => localStorage.getItem('sx.token'));
ok(!!savedToken, 'token jest w pamięci strony');
await page.evaluateOnNewDocument(tok => {
  window.SXNative = {
    savedToken: () => tok, tlsFingerprint: () => '', deviceName: () => 'Pixel test', saveSession() {}, saveMac() {}, clearSession() {},
    hasVoice: () => false, startVoice() {}, canWake: () => true, wakePc() { window.__woke = true; }, rediscover() { window.__rediscovered = true; }
  };
}, savedToken);
await page.evaluate(() => localStorage.clear());
await page.reload({ waitUntil: 'load' });
await page.waitForSelector('#view-chat:not([hidden])', { timeout: 8000 });
ok(true, 'po zmianie adresu (pusty localStorage) aplikacja wraca do czatu bez ponownego parowania');
await page.click('#btnSettings'); await page.waitForSelector('#setInfo .kv');
ok((await text('#setInfo')).includes('Android') && !(await page.$eval('#btnWake', e => e.hidden)), 'w aplikacji ustawienia pokazują Androida i przycisk wybudzania');
await page.click('#btnWake'); ok(await page.evaluate(() => window.__woke === true), 'przycisk wybudzania woła most natywny');
await page.click('#btnSettings'); // przełącznik: wyjdź z ustawień, dalszy test otworzy je ponownie

// ---- ustawienia + odłączenie
await page.click('#btnSettings'); await page.waitForSelector('#setInfo .kv'); await wait(400); await shot('10-settings');
await page.click('#btnUnpair'); await page.waitForSelector('.modal .btn.danger'); await shot('11-confirm');
await page.click('.modal .btn.danger');
await page.waitForSelector('#view-pair:not([hidden])', { timeout: 4000 });
ok(true, 'odłączenie wraca do ekranu parowania');
ok(!(await page.evaluate(() => localStorage.getItem('sx.token'))), 'token usunięty z pamięci telefonu');

await browser.close(); server.close();
console.log(problems.length ? `\n${problems.length} PROBLEM(Y):\n - ` + problems.join('\n - ') : '\nWSZYSTKIE TESTY INTERFEJSU PRZESZŁY');
process.exit(problems.length ? 1 : 0);
