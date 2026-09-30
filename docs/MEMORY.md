# Pamięć SentinelX — format v4 i zasady działania

Wszystkie dane pamięci leżą lokalnie w `%LOCALAPPDATA%\SentinelX\Memory\` (w testach: katalog z `SENTINEL_DATA_DIR`). Nic nie jest wysyłane do chmury.

## Pliki

- `conversation-memory.json` — jedyny aktywny magazyn: wiadomości (`Entries`), trwałe wspomnienia (`Notes`), profil (`Profile`), indeks rozmów (`Conversations`), dziennik zmian wspomnień (`Changes`), niewysłany szkic (`Draft`).
- `conversation-memory.json.tmp` — bufor zapisu atomowego (zapis → przeniesienie nadpisujące, `File.Move` z `overwrite`).
- `conversation-memory.json.damaged-*` — kopia utworzonego pliku wykrytego jako uszkodzony. Uszkodzony plik **nigdy** nie jest nadpisywany pustym stanem: podmiana następuje dopiero przy pierwszym udanym zapisie nowych danych.
- `conversation-memory.json.v2-backup-*` (oraz `.v1-backup-*`) — kopie tworzone przed wcześniejszą migracją z v1/v2 do v3. Dodanie metadanych v4 jest addytywne i nie wymaga przepisywania starszego pliku przy samym odczycie.
- `Exports\pamiec-*.json` — eksporty tworzone ręcznie z panelu Pamięć lub poleceniem `eksportuj pamięć`.

## Format v4

Każdy wpis wspomnienia ma: stabilny `Id` (12 znaków), `Category` (`notatka | preferencja | fakt | decyzja | zadanie | narzędzie | umiejętność`), `Source`, `Timestamp` (utworzenie), opcjonalne `UpdatedAt`, opcjonalne `LastConfirmedAt`, `Confidence` (konserwatywna ocena jakości/pochodzenia, nie prawdopodobieństwo obiektywnej prawdy), `Pinned`, opcjonalne `SupersededAt` (nieaktualne — poza kontekstem AI, ale nie skasowane), a `FactKey` identyfikuje wybrane fakty o stabilnym kluczu. `Relevance` jest wyliczana dla konkretnego zapytania, nie przechowywana jako stała cecha wpisu.

Wspomnienia użytkownika mają wyższą confidence niż importowane lub syntetyzowane briefy; ekstrakcja stron nie jest niezależnym potwierdzeniem. Jawne preferencje w `Profile` mają analogiczne metadane źródła, utworzenia, ostatniego potwierdzenia i confidence. Oznaczenie wpisu jako nieaktualnego obniża jego confidence, a aktualne polecenia odczytu systemu korzystają z bieżących pomiarów zamiast przedstawiać dawną notatkę jako stan komputera.

`Conversations` to indeks nazwanych, wznawianych rozmów: `Id`, `Title` (automatycznie z pierwszej wiadomości, można zmienić), `CreatedAt`, `LastActiveAt`. Treść rozmów pozostaje w `Entries` z `SessionId` — przełączenie rozmowy zmienia jedynie `ActiveSessionId`, więc konteksty rozmów nie mieszają się.

Migracja v1/v2 → v3 nadaje identyfikatory, domyślne kategorie i odbudowuje indeks rozmów z `SessionId` wpisów; przed pierwszym zapisem v3 powstaje kopia oryginału. v4 dodaje confidence i datę ostatniego potwierdzenia jako pola addytywne, z konserwatywnymi wartościami dla wpisów starszych.

## Niezależne przełączniki prywatności (Settings → Pamięć)

| Przełącznik | Skutek wyłączenia |
|---|---|
| Zapisuj rozmowy | Polecenia/odpowiedzi nie trafiają do `Entries`; nowe wpisy tekstowe audytu są pomijane, bez markera treści. |
| AI używa historii rozmów | Ostatnie wypowiedzi nie trafiają do kontekstu modelu (także dopowiedzeń). |
| Zapisuj wspomnienia | `zapamiętaj…` i panel odmawiają zapisu (istniejące wpisy zostają). |
| AI używa wspomnień | Profil i wspomnienia nie trafiają do kontekstu modelu. |
| Retencja rozmów (dni) | >0: `Entries` starsze niż limit są usuwane przy zapisie. Wspomnienia i profil **nie** podlegają retencji. |
| Podgląd kontekstu AI | Gdy wyłączony, nie rejestrujemy śladu kontekstu (etykiet i powodów). Pełne prompty nie są rejestrowane nigdy. |

**Tryb prywatny** jest przełącznikiem sesyjnym (nigdy niezapisywanym): podczas gdy jest włączony, nowe treści rozmów, audytu i szkicu nie są zapisywane na dysk. Istniejące pliki nie są przez samo włączenie trybu usuwane. Po restarcie sesyjna pamięć nie wraca.

## Kontekst AI i jego podgląd

Kontekst składa: wybrane pola profilu (gdy dozwolone), maksymalnie 5 aktywnych wspomnień wyszukanych względem bieżącego zapytania (przypięcie rozstrzyga remis, nie omija trafności), ograniczony kontekst bieżącego zadania oraz dla dopowiedzeń — najnowsze wypowiedzi aktywnej rozmowy w budżecie. Przy pustym zapytaniu domyślny zestaw ma maksymalnie 3 wpisy. Wspomnienia nieaktualne są pomijane; confidence i query-specific relevance są dołączane do kontekstu jako metadane. Ślad `ContextSlice` (rodzaj, id/etykieta, powód i trafność) trafia do panelu „Kontekst ostatniego zapytania AI” oraz komendy `co poszło do modelu`.

## Ryzyko i zgody

Usuwanie pamięci (całość, historia rozmowy, dopasowanie tekstem, pojedyncze wspomnienie po ID) to ryzyko **HIGH**: zawsze przez centrum zgód z jednorazowym, wygasającym po 10 minutach zatwierdzeniem klawiaturowym (głos nie zatwierdza). Edycja, przypinanie, oznaczanie aktualności, import i dodawanie są mutacjami mniejszego ryzyka — wykonują się od razu, każda z audytem `VERIFIED` dopiero po odczycie zwrotnym pliku; przy braku potwierdzenia status to `FAILED`, bez udawanego sukcesu.

## Eksport i import

Eksport zapisuje pełny stan JSON (zawiera prywatne treści — przechowuj jak wrażliwe). Import przyjmuje plik eksportu (obiekt z `Notes`/`Profile`): pokazuje podgląd zmian bez zapisu, dodaje tylko nowe wspomnienia i brakujące klucze profilu, pomija duplikaty (podobieństwo po znormalizowanej treści), odrzuca niepoprawne wiersze i **celowo nie importuje historii rozmów** (`Entries`).

## Czego ten moduł (jeszcze) nie robi

- Brak semantycznych embeddingów; wyszukiwanie jest tekstowe (normalizacja odmian diakrytycznych i wielkości liter). Zobacz `docs/BACKLOG.md`.
- Brak podsumowań rozmów z odnośnikami do wiadomości; `Changes` dokumentuje zmiany wspomnień, nie AI-streszczenia.
- Brak osobnej pamięci projektów — to część planowanego modułu Projektów.
