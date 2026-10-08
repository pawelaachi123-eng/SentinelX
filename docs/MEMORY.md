# Pamięć SentinelX — format v3 i zasady działania (0.86)

Wszystkie dane pamięci leżą lokalnie w `%LOCALAPPDATA%\SentinelX\Memory\` (w testach: katalog z `SENTINEL_DATA_DIR`). Nic nie jest wysyłane do chmury.

## Pliki

- `conversation-memory.json` — jedyny aktywny magazyn: wiadomości (`Entries`), trwałe wspomnienia (`Notes`), profil (`Profile`), indeks rozmów (`Conversations`), dziennik zmian wspomnień (`Changes`), niewysłany szkic (`Draft`).
- `conversation-memory.json.tmp` — bufor zapisu atomowego (zapis → przeniesienie nadpisujące, `File.Move` z `overwrite`).
- `conversation-memory.json.damaged-*` — kopia utworzonego pliku wykrytego jako uszkodzony. Uszkodzony plik **nigdy** nie jest nadpisywany pustym stanem: podmiana następuje dopiero przy pierwszym udanym zapisie nowych danych.
- `conversation-memory.json.v2-backup-*` (oraz `.v1-backup-*`) — kopia oryginalnego pliku tworzona **przed pierwszą migracją** do v3.
- `Exports\pamiec-*.json` — eksporty tworzone ręcznie z panelu Pamięć lub poleceniem `eksportuj pamięć`.

## Format v3

Każdy wpis wspomnienia ma: stabilny `Id` (12 znaków), `Category` (`notatka | preferencja | fakt | decyzja | zadanie | narzędzie`), `Source`, `Timestamp`, opcjonalne `UpdatedAt`, `Pinned`, opcjonalne `SupersededAt` (nieaktualne — poza kontekstem AI, ale nie skasowane).

`Conversations` to indeks nazwanych, wznawianych rozmów: `Id`, `Title` (automatycznie z pierwszej wiadomości, można zmienić), `CreatedAt`, `LastActiveAt`. Treść rozmów pozostaje w `Entries` z `SessionId` — przełączenie rozmowy zmienia jedynie `ActiveSessionId`, więc konteksty rozmów nie mieszają się.

Migracja v1/v2 → v3 następuje przy starcie na kopii w pamięci: nadaje identyfikatory, domyślne kategorie i odbudowuje indeks rozmów z `SessionId` wpisów. Przed pierwszym zapisem w nowym formacie powstaje kopia zapasowa oryginału.

## Niezależne przełączniki prywatności (Settings → Pamięć)

| Przełącznik | Skutek wyłączenia |
|---|---|
| Zapisuj rozmowy | Polecenia/odpowiedzi nie trafiają do `Entries` ani do audytu (audyt zapisuje marker „treść niezapisana”). |
| AI używa historii rozmów | Ostatnie wypowiedzi nie trafiają do kontekstu modelu (także dopowiedzeń). |
| Zapisuj wspomnienia | `zapamiętaj…` i panel odmawiają zapisu (istniejące wpisy zostają). |
| AI używa wspomnień | Profil i wspomnienia nie trafiają do kontekstu modelu. |
| Retencja rozmów (dni) | >0: `Entries` starsze niż limit są usuwane przy zapisie. Wspomnienia i profil **nie** podlegają retencji. |
| Podgląd kontekstu AI | Gdy wyłączony, nie rejestrujemy śladu kontekstu (etykiet i powodów). Pełne prompty nie są rejestrowane nigdy. |

**Tryb prywatny** jest przełącznikiem sesyjnym (nigdy niezapisywanym): podczas gdy jest włączony, nic z treści rozmowy nie trafia na dysk — ani rozmowa, ani szkic, ani treść audytu. Po restarcie nie ma czego przywrócić i aplikacja mówi to wprost.

## Kontekst AI i jego podgląd

Kontekst składa: profil (gdy dozwolone), wspomnienia przypięte zawsze w budżecie, potem najświeższe aktywne wspomnienia (max 30), a dla dopowiedzeń — najnowsze wypowiedzi aktywnej rozmowy w ramach budżetu ok. 12 000 znaków. Wspomnienia nieaktualne są pomijane. Ślad `ContextSlice` (rodzaj, id/etykieta, powód) trafia do panelu „Kontekst ostatniego zapytania AI” oraz komendy `co poszło do modelu`.

## Ryzyko i zgody

Usuwanie pamięci (całość, historia rozmowy, dopasowanie tekstem, pojedyncze wspomnienie po ID) to ryzyko **HIGH**: zawsze przez centrum zgód z jednorazowym, wygasającym po 10 minutach zatwierdzeniem klawiaturowym (głos nie zatwierdza). Edycja, przypinanie, oznaczanie aktualności, import i dodawanie są mutacjami mniejszego ryzyka — wykonują się od razu, każda z audytem `VERIFIED` dopiero po odczycie zwrotnym pliku; przy braku potwierdzenia status to `FAILED`, bez udawanego sukcesu.

## Eksport i import

Eksport zapisuje pełny stan JSON (zawiera prywatne treści — przechowuj jak wrażliwe); przed zapisem treść przechodzi przez `TextScrubber` (klucze PEM, hashe 64-hex, pary sekret=wartość zamieniane na znaczniki, kształt JSON zachowany). Import przyjmuje plik eksportu (obiekt z `Notes`/`Profile`): pokazuje podgląd zmian bez zapisu, dodaje tylko nowe wspomnienia i brakujące klucze profilu, pomija duplikaty (podobieństwo po znormalizowanej treści), odrzuca niepoprawne wiersze i **celowo nie importuje historii rozmów** (`Entries`).

## Czego ten moduł (jeszcze) nie robi

- Brak semantycznych embeddingów; wyszukiwanie jest tekstowe (normalizacja odmian diakrytycznych i wielkości liter). Zobacz `docs/BACKLOG.md`.
- Brak podsumowań rozmów z odnośnikami do wiadomości; `Changes` dokumentuje zmiany wspomnień, nie AI-streszczenia.
- Brak osobnej pamięci projektów — to część planowanego modułu Projektów.
