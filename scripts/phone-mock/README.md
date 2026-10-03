# Atrapa serwera i test interfejsu telefonu

Interfejs telefonu (`Phone/web`) to zwykła strona (HTML + CSS + JS, bez budowania), którą komputer
serwuje sam z siebie (`Services/Link`). Żeby rozwijać ją bez Windowsa:

```bash
node scripts/phone-mock/server.mjs 8088          # atrapa API: http://127.0.0.1:8088/
MOCK_APPROVE_MS=0 node scripts/phone-mock/server.mjs   # parowanie czeka na ręczne zatwierdzenie (nigdy nie zatwierdza samo)
```

Test w prawdziwej przeglądarce (headless Chromium z paczki npm, widok telefonu 390×844):

```bash
cd scripts/phone-mock
npm i --no-save puppeteer-core @sparticuz/chromium
AWS_EXECUTION_ENV=AWS_Lambda_nodejs22.x node ui-test.mjs
```

Sprawdza m.in.: zgodność SHA-256 (jednorazowo i przyrostowo)/kodu potwierdzenia (SAS), parowanie, dashboard, czat SSE,
typowane zasilanie z podwójnym potwierdzeniem, upload/download plików z kontrolą integralności, automatyzacje z katalogu,
zadania, notatki, alerty (long-poll), rotację tokenu, odłączenie, panel offline i brak błędów w konsoli.
Atrapa wymaga unikalnego `X-Sentinel-Request-Id` dla każdej mutacji. Kontrakt API opisuje `docs/PHONE-LINK.md`; atrapa i serwer w C# muszą się z nim zgadzać.
