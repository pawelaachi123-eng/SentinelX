# AUDYT 1550 — stan kodu i jawne braki

Data: 2026-09-27. Gałąź: `arena/01a0e28e-sentinelx`.

Kod `845f4b6`: [Windows build and WPF smoke](https://github.com/pawelaachi123-eng/SentinelX/actions/runs/36347938885) oraz [Build Windows app (.NET 10)](https://github.com/pawelaachi123-eng/SentinelX/actions/runs/36347938810) — **success**.

## Powtarzalne liczby

`python scripts/check-architecture.py`: PASS. `python scripts/audit-1550.py`: **713 wyzwalaczy w objętych skryptem modułach**, **601 fraz**, **90 wpisów palety**. `UiSmokeTestRunner.RunAsync`: **36 zestawów regresji**, **13 stron**.

Skrypt nie skanuje całego centralnego routera: `rag pytaj`, `rag archiwa`, `rag zapisz`, `rag wczytaj` to 4 dodatkowe rodziny, nie doliczane do 713. Ten sam wyzwalacz nie jest funkcją z master listy. `docs/LISTA-1550.md` nie jest dostępny, dlatego nie podajemy procentu pokrycia ani deklaracji ukończenia całych sekcji.

## Inwentarz per sekcja

Liczby obejmują wyzwalacze i wzorce z raportu automatycznego. Wspólne moduły nie są liczone drugi raz.

| Sekcja | Licznik skryptu | Co jest | Co pozostaje |
|---|---:|---|---|
| 1. Rdzeń | 86 | 18 modułów runtime i polecenia | workflow planuje graf; brak pełnego panelu runtime |
| 2. Modele | 33 | katalog i zarządzanie przez router; panel AI | benchmark sprzętu i jakości; brak automatycznego pobierania bez zgody |
| 3. Prywatność | 33 | mapa danych, podgląd retencji; archiwa RAG DPAPI | szyfrowanie całej historii i pełny audyt uprawnień |
| 4. Kodowanie | 247 | Developer 51 + CodeInsights 17 + Utility 179 | pełna analiza składni/przepływu, IDE i kompilator |
| 5. Architektura | 14 | graf, cykle, warstwy, ADR i dług | diagramy graficzne i analiza całego repo |
| 6. Full-stack | 18 | generatory API, SQL, TS/C#, compose | uruchomione usługi i pełny frontend |
| 7. Wielojęzyczność | wspólnie z §13 | polskie frazy; narzędzia język/i18n | pełne tłumaczenie UI |
| 8. Wiedza i RAG | 24 + 4 rodziny w routerze | Knowledge 9 + Index 8 + RAG 7; archiwum, pytaj, indeks | PDF/DOCX, przenośne archiwum i badanie jakości |
| 9. Głos | 8 wspólnie z §10 | Whisper/NAudio i narzędzia audio | wake-word i streaming ASR |
| 10. Wizja | wspólne 8 z §9 | kontrast/PPI/proporcje/bitrate | OCR, YOLO, analiza kamery |
| 11. Automatyzacja | 4 | rutyny, kolejka i cron | pełny plan wykonania z wznawianiem i edytor GUI |
| 12. Research | 12 | cytowania i szablony offline | pobieranie stron — świadomie poza zakresem |
| 13. Produktywność | 21 | narzędzia, sesje, zadania i plan tygodnia | integracje poczty i kalendarza |
| 14. Komunikacja | 31 wspólnie z §17 | SMS, szkice i czytelność | wysyłanie wiadomości — brak |
| 15. Analiza danych | 49 | statystyki, korelacje i regresje; PNG przez router | pełny eksport wyników i wykresy interaktywne |
| 16. Finanse | wspólnie z §13/§4 | arytmetyka i podane założenia | bieżące kursy, podatki i portfel |
| 17. Zdrowie | wspólne 31 z §14 | wzory BMR/TDEE/tempo itp. | sprzęt medyczny, dziennik i indywidualne porady |
| 18. Smart home | 11 | energia, plany i YAML/MQTT offline | sterowanie urządzeniami |
| 19. GUI / system | 114 | 13 stron, Studio, panel modeli i narzędzia systemowe | edytor układu i ulubione użytkownika |
| 20. Agentic | 8 | cele, ryzyko i szablony planów | autonomiczne planowanie przez model z budżetem |

Bonus (1501–1550): dokumentacja i 36 zestawów regresji; brak dowodu mapowania wszystkich 50 pozycji. Pełny spis modułów: [AUDYT-1550-AUTO.md](AUDYT-1550-AUTO.md).

## Trzy przyrosty tej zmiany

1. Archiwum RAG DPAPI: zapis/wczytanie po planie i zgodzie, wersjonowany format, walidacja, atomowa publikacja bez nadpisania, ochrona RAM przy błędzie, zakaz zapisu w trybie prywatnym.
2. Odpowiedzi `rag pytaj`: do 3 trafień, lokalny model, odsyłacze oraz literalne cytaty. Numery są sprawdzane; prawdziwość każdego zdania nie jest automatycznie udowadniana.
3. Panel modeli: istniejąca strona AI, bez dodawania czternastej strony; szczegóły, procesy, plany, potwierdzenie, postęp i anulowanie przez dotychczasowy silnik.

Każdy ma osobny zestaw regresji w Windows CI. Realna jakość generowania, rzeczywiste pobieranie modelu, odczyt na innym koncie Windows i ocena wizualna na komputerze użytkownika pozostają niezweryfikowane.

Pakowanie i publikacja EXE/instalatora: kroki pominięte na zwykłym pushu; bramki `workflow_dispatch` nie były zmieniane. Standardowa kompilacja i uruchomienie aplikacji na potrzeby CI pozostają częścią weryfikacji.
