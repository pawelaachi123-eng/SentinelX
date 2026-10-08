# SENTINEL X — plan 500 funkcji (rewizja 2026-10-08)

Statusy: **W TOKU** (kod w repo, nie w pełni gotowe), **PLAN** (do zrobienia), **ZABLOKOWANE** (wymaga konta, klucza, sprzętu lub decyzji właściciela — podany powód).

Zasady: wszystko w istniejących zakładkach i oknach, bez nowych zakładek. Bez funkcji nielegalnych. Bez pozycji, których Sentinel nie może wiarygodnie zrobić (usunięto np. stan drzwi, sterowanie wentylatorami, FPS bez nakładki, ustawienia routera, scraping cen). Bez manifestu aktualizacji.

## Pogoda i finanse

1. Pogoda: okno „pokaż pogodę” z wykresem 24 h, tabelą godzinową (np. „16:00 — 30 °C, wilgotność 16%”), prognozą 7 dni, jakością powietrza, UV, wiatrem, alertami i odczytem głosowym; push na telefon zawsze przy deszczu, mrozie, upale, burzy i smogu — **W TOKU** (tekst godzinowy z routingiem gotowy; wykres, okno, prognoza 7 dni i push na telefon są w planie)
2. Waluty i finanse: kursy NBP na żywo, push z kursami wybranych walut zawsze na telefon, kalkulator kantorowy, alerty progów, złoto, wykres 30/90 dni, budżet, wydatki, subskrypcje, eksport CSV — **W TOKU** (kursy NBP tekstem z routingiem gotowe; push na telefon, wykres, alerty i budżet w planie)
3. GitHub: powiadomienia, PR do review, status CI repozytoriów z alertem o błędzie — ZABLOKOWANE: konto GitHub i zgoda
4. Gmail: liczba nieprzeczytanych i nadawcy w jednym widoku — ZABLOKOWANE: własny klucz OAuth i zgoda
5. Gmail: szukanie wiadomości i załączników po frazie, nadawcy i dacie — ZABLOKOWANE: konto Gmail
6. Gmail: streszczenie nieprzeczytanych lokalnym modelem, bez wysyłania treści poza PC — ZABLOKOWANE: konto Gmail
7. Gmail: szablony i wysyłka krótkiej wiadomości po potwierdzeniu — ZABLOKOWANE: konto Gmail
8. Gmail: faktury z terminem płatności i alert przed terminem — ZABLOKOWANE: konto Gmail
9. Kalendarz Google: dziś i jutro, przypomnienia i poranny briefing — ZABLOKOWANE: konto Google
10. GitHub: nowe wydania obserwowanych repozytoriów z alertem — ZABLOKOWANE: konto GitHub
11. GitHub: lista issue przypisanych do Ciebie z przypomnieniami — ZABLOKOWANE: konto GitHub
12. GitHub: podsumowanie zmian w PR lokalnym modelem — ZABLOKOWANE: konto GitHub
13. Archiwum rozmów w Pamięci: podgląd, przywracanie, usuwanie — PLAN
14. Wyszukiwanie w Pamięci z tolerancją literówek i wynikami z treści — PLAN
15. Gałęzie rozmowy z czatem AI i edycja wcześniejszych wiadomości — PLAN
16. Makra z sekwencji akcji w Akcjach, np. „tryb nauki” jednym poleceniem — PLAN
17. Timery zasilania: uśpienie, wyłączenie, restart o godzinie lub po czasie — PLAN
18. Minutnik i pomodoro w Zadaniach z powiadomieniem i raportem czasu — PLAN
19. Przypomnienia przetrwające restart PC i wysyłane na telefon — PLAN
20. Panel procesów w System: sortowanie po CPU i RAM, zamykanie za potwierdzeniem — PLAN
21. Progi alertów w Diagnostyce: CPU, RAM, dysk, temperatura — PLAN
22. Ping, traceroute i publiczne IP w Diagnostyce — PLAN
23. Zrzuty ekranu (całość, okno, obszar) z automatyczną nazwą i folderem miesięcznym — PLAN
24. Głośność systemu i aplikacji z Narzędzi i głosu — PLAN
25. Historia schowka z szybkim wklejaniem i wysyłką na telefon — PLAN
26. Czytanie tekstu na głos z wyborem głosu i tempa — PLAN
27. Podgląd zawartości archiwów ZIP i 7z bez rozpakowywania — PLAN
28. Kalendarz świąt PL offline w Narzędziach i Centrum — PLAN
29. Tłumacz offline (model lokalny) w Narzędziach i czacie — PLAN
30. Szukanie tekstu w plikach z podglądem wyników — PLAN
31. Porównanie dwóch plików lub folderów z podglądem różnic — PLAN
32. Konfigurowalne skróty klawiszowe w Ustawieniach — PLAN
33. Przeliczanie VAT, netto i brutto w Narzędziach — PLAN
34. Czas w innych strefach i miastach w Narzędziach — PLAN
35. Motyw szklany (Jarvis) w Ustawieniach z przełącznikiem kontrastu — PLAN
36. Tryb skupienia: wyciszenie powiadomień i blokada rozpraszających aplikacji — PLAN
37. Nawyki z serią dni i przypomnieniem w Zadaniach — PLAN
38. Cele z postępem i wykresem w Zadaniach — PLAN
39. Dziennik dnia z szablonem i eksportem do Pamięci — PLAN
40. Widok Kanban w Zadaniach z przełącznikiem widoku — PLAN
41. Kalendarz miesięczny z terminami i przypomnieniami w Zadaniach — PLAN
42. Zadania cykliczne („co poniedziałek”) w Zadaniach — PLAN
43. Podzadania i macierz Eisenhowera w Zadaniach — PLAN
44. Lista zakupów z kategoriami i podpowiedziami najczęstszych produktów — PLAN
45. Urodziny i ważne daty z alertem na 7 i 1 dzień przed — PLAN
46. Lektury z postępem stron i przypomnieniem — PLAN
47. Watchlista filmów i seriali z ocenami — PLAN
48. Fiszki z powtórkami metodą Leitnera w Pamięci — PLAN
49. Notatki głosowe zapisywane do Pamięci po lokalnej transkrypcji — PLAN
50. Lokalna transkrypcja nagrań modelem Whisper w Głosie — PLAN
51. Dyktowanie tekstu do aktywnego okna komendą głosową — PLAN
52. Komendy łańcuchowe głosem, np. „pogoda i zablokuj komputer” — PLAN
53. Budzik głosowy z dźwiękiem, drzemką i potwierdzeniem z telefonu — PLAN
54. „Przypomnij za X minut” głosem i z telefonu — PLAN
55. Sterowanie mediami („pauza”, „następna”) w każdej aplikacji — PLAN
56. Minimalizowanie i przełączanie okien głosem za zgodą — PLAN
57. Czytanie aktywnego okna na głos — PLAN
58. Tryb samochodu: duży zegar, pogoda i obsługa głosowa — PLAN
59. Pomoc głosowa: lista komend czytana na głos — PLAN
60. Przeliterowanie wyniku, np. hasła, głosem — PLAN
61. Globalny skrót otwierający paletę komend z dowolnego miejsca — PLAN
62. Przeglądanie i przywracanie plików z Kosza — PLAN
63. Sortowanie zdjęć po dacie EXIF z podglądem przed zmianą — PLAN
64. Wykrywanie duplikatów zdjęć z miniaturami do weryfikacji — PLAN
65. Łączenie i dzielenie PDF lokalnie — PLAN
66. OCR: PDF i zdjęcia na tekst lokalnie — PLAN
67. Zdjęcia do PDF w jednym kroku — PLAN
68. Kompresja zdjęć z podglądem rozmiaru przed zapisem — PLAN
69. Konwersja i zmiana rozmiaru wsadowa z podglądem — PLAN
70. Edycja tagów MP3 i playlisty M3U — PLAN
71. Informacje o wideo: kodek, długość, rozdzielczość, bitrate — PLAN
72. Porządek na pulpicie z zapisanym układem i cofaniem zmian — PLAN
73. Lista pustych folderów z usuwaniem za potwierdzeniem — PLAN
74. Analiza miejsca na dysku: wykres i przechodzenie w głąb folderów — PLAN
75. Monitor folderu z powiadomieniem o nowym pliku, np. w Pobranych — PLAN
76. Szyfrowanie pliku hasłem AES z ostrzeżeniem o utracie hasła — PLAN
77. Bezpieczne usuwanie plików z nadpisaniem, tylko za wyraźną zgodą — PLAN
78. Migawki dokumentów z przywracaniem poprzednich wersji — PLAN
79. Kopia przyrostowa folderów z harmonogramem — PLAN
80. Synchronizacja folderów z podglądem różnic i zgodą — PLAN
81. Manifest SHA-256 folderu: sprawdzenie, czy coś się zmieniło — PLAN
82. Zadania i przypomnienia w aplikacji mobilnej — PLAN
83. Kafelki szybkich ustawień na telefonie, np. „zablokuj PC” — PLAN
84. Widget na pulpicie telefonu z zadaniami i statusem PC — PLAN
85. Blokowanie PC z telefonu przez Sentinela — PLAN
86. Wysyłanie plików i tekstu z PC na telefon — PLAN
87. Wysyłanie plików z telefonu na PC — PLAN
88. Schowek PC i telefonu w obie strony — PLAN
89. Powiadomienia z telefonu widoczne na PC (opt-in) — PLAN
90. Stan baterii telefonu na PC z alertem przy niskim poziomie — PLAN
91. Dzwonienie na telefon z PC, gdy telefon jest w sieci domowej — PLAN
92. Automatyczne przenoszenie zdjęć z telefonu na PC z wybranych folderów — PLAN
93. Zrzut ekranu PC wysyłany na telefon na żądanie — PLAN
94. Wyłączanie PC z telefonu po potwierdzeniu PIN-em — PLAN
95. Lista zakupów odhaczana na telefonie i synchronizowana — PLAN
96. Dodawanie i odhaczanie zadań na telefonie — PLAN
97. Przypomnienie po powrocie w zdefiniowaną strefę domu (opt-in) — PLAN
98. Harmonogram trybu cichego na telefonie, np. „9–17 wycisz” — PLAN
99. Dyktafon w telefonie z wysyłką nagrania i transkrypcji na PC — PLAN
100. Wysyłanie SMS z PC przez telefon — PLAN
101. Sterowanie latarką telefonu z PC — PLAN
102. Sterowanie głośnością telefonu z PC — PLAN
103. Ustawianie alarmu na telefonie z PC — PLAN
104. Podgląd ostatnich powiadomień telefonu z komputera — PLAN
105. Lokalizacja telefonu na mapie w Centrum (opt-in, bez chmury) — PLAN
106. Statusy urządzeń Base widoczne na telefonie — PLAN
107. Podgląd z kamery telefonu w pokoju, tylko w sieci lokalnej i za zgodą — PLAN
108. Tryb „wychodzę/wracam” łączący scenę Base z blokadą PC — PLAN
109. Sceny domowe wg pory dnia i wschodu/zachodu słońca — PLAN
110. Wykresy temperatury i wilgotności czujników Base z ostatnich 24 h — PLAN
111. Lista czujników Base z ostatnim odczytem i czasem odczytu — PLAN
112. Powiadomienie, gdy czujnik Base przestaje wysyłać dane przez 15 min — PLAN
113. Harmonogram włączania i wyłączania urządzeń Base — PLAN
114. Edytor timerów Base w UI zamiast samych komend — PLAN
115. Historia zdarzeń Base z filtrem po typie — PLAN
116. Test połączenia z Base i siły sygnału w Diagnostyce — PLAN
117. Watchdog Base z alertem „Base nie odpowiada” — PLAN
118. Backup i import konfiguracji Base w formacie JSON — PLAN
119. Obsługa kilku modułów Base (dom, garaż) z przełącznikiem — PLAN
120. Czasowy dostęp gościnny z logiem wejść — PLAN
121. Asystent aktualizacji firmware Base z kopią zapasową i weryfikacją — ZABLOKOWANE: sprzęt Base do testów
122. Sekcja IoT w Centrum z żywym statusem urządzeń — PLAN
123. Scena „wyłącz wszystko” jednym przyciskiem — PLAN
124. Wake-on-LAN: budzenie PC z telefonu o zadanej godzinie — PLAN
125. Budzik przez Base z sygnałem świetlnym i dźwiękiem — ZABLOKOWANE: sprzęt Base do testów
126. Dzwonek Base wysyłający powiadomienie na telefon — ZABLOKOWANE: sprzęt Base do testów
127. Powiadomienie wysyłane przez Base, gdy PC przechodzi offline — ZABLOKOWANE: sprzęt Base do testów
128. Eksport całej konfiguracji Sentinela do pliku z manifestem — PLAN
129. Import konfiguracji z kreatorem i podglądem zmian — PLAN
130. Tryb offline: jeden przełącznik wyłącza wszystkie funkcje online — PLAN
131. Log połączeń sieciowych: co Sentinel wysyła i dokąd — PLAN
132. Panel zgód per funkcja z datą zgody — PLAN
133. Czyszczenie śladów: historia, logi i pliki tymczasowe wg daty — PLAN
134. Dziennik odblokowań (PIN, Windows Hello) z datą — PLAN
135. Alert nowego wpisu w autostarcie z opcją usunięcia — PLAN
136. Sprawdzanie hashy instalatorów, z opcją VirusTotal z własnym kluczem — ZABLOKOWANE: własny klucz VirusTotal
137. Otwieranie podejrzanego pliku w Windows Sandbox, jeśli dostępny — PLAN
138. Przypomnienie o kopii zapasowej po 30 dniach bez backupu — PLAN
139. Podsumowanie roku z Sentinelem: użycie, czas pracy, oszczędności — PLAN
140. Centrum dowodów: zrzuty, logi i hashe spakowane z datą — PLAN
141. Pakiet podróżny: czas, pogoda, waluta, święta i gniazdka dla kraju — PLAN
142. Sejf haseł lokalnie szyfrowany DPAPI z generatorem — PLAN
143. Kopiowanie hasła z automatycznym czyszczeniem schowka — PLAN
144. Kody TOTP 2FA lokalnie w Ustawieniach — PLAN
145. Sprawdzenie wycieku hasła: wysyłany tylko prefiks hasha — PLAN
146. Windows Hello wymagany przy czułych akcjach — PLAN
147. Autoblokada po bezczynności z przypomnieniem — PLAN
148. Ukryte projekty i notatki za PIN-em — PLAN
149. Szyfrowane notatki AES z ostrzeżeniem o utracie hasła — PLAN
150. Audyt prywatności: co, gdzie leży i jak to wyczyścić — PLAN
151. Limit dzienny czasu w grach z alertem „grasz już 2 h” — PLAN
152. Raport czasu grania dziennie i tygodniowo — PLAN
153. Biblioteka gier ze Steam i wykrytych plików wykonywalnych z czasem gry — PLAN
154. Profil zasilania w grze z przywracaniem zamkniętych programów — PLAN
155. Monitor temperatur CPU i GPU z wykresem i alertem przegrzania — PLAN
156. Stan dysków (SMART) z alertem przed awarią — PLAN
157. Bezpieczny test odczytu dysku bez zapisu — PLAN
158. Stan baterii laptopa: zdrowie, cykle i profil oszczędzania — PLAN
159. Jasność ekranu z komendy, np. „jasność 50%” — PLAN
160. Wybór urządzenia audio i test mikrofonu oraz kamery — PLAN
161. Menedżer autostartu z wyłączaniem za zgodą — PLAN
162. Podgląd harmonogramu zadań z włączaniem za zgodą — PLAN
163. Czyszczenie plików tymczasowych z podglądem rozmiaru — PLAN
164. Raport sprzętu: CPU, GPU, RAM, płyta, BIOS, dyski — PLAN
165. Wersje sterowników i linki do producentów, bez auto-instalacji — PLAN
166. Wi-Fi: lista sieci, siła sygnału, połącz i zapomnij — PLAN
167. Bluetooth: lista urządzeń, połącz i rozłącz — PLAN
168. USB: lista urządzeń i bezpieczne usuwanie — PLAN
169. Kolejka drukarek i test wydruku — PLAN
170. Skanowanie dokumentu do PDF ze skanera — PLAN
171. Tryb prezentacji: cisza powiadomień i blokada uśpienia — PLAN
172. Pomocnik decyzji: za i przeciw z wagami i werdyktem — PLAN
173. Kalkulator kredytu z harmonogramem i nadpłatami — PLAN
174. Porównanie ofert kredytów i lokat — PLAN
175. Liczniki prądu, gazu i wody z prognozą rachunku — PLAN
176. Harmonogram wywozu śmieci i przypomnienie „wystaw śmieci” — PLAN
177. Przypomnienia o OC, AC i przeglądzie na 30, 7 i 1 dzień przed terminem — PLAN
178. Subskrypcje: lista, przypomnienie przed płatnością i suma roczna — PLAN
179. Paragon zamieniany na kwotę wydatku przez lokalny OCR — PLAN
180. Budżet miesięczny z progiem 80% i 100% — PLAN
181. Limit dzienny wydatków z ostrzeżeniem — PLAN
182. Wydatki dyktowane głosem z kategoriami — PLAN
183. Miesięczne podsumowanie wydatków z wykresem — PLAN
184. Eksport finansów do CSV i raport roczny — PLAN
185. Lokaty: odsetki i alert przed końcem — PLAN
186. Alert kursu, np. „EUR poniżej 4,20”, z pushem na telefon — PLAN
187. Kurs z dowolnej daty wstecz z NBP — PLAN
188. Cena złota z NBP z wykresem i alertem — PLAN
189. BTC i ETH z alertem procentowym (online) — ZABLOKOWANE: wymaga zgody na połączenie z usługą cen
190. Akcje GPW z alertem (online) — ZABLOKOWANE: wymaga wybrania źródła danych giełdowych
191. Kantor: kupno, sprzedaż i spread wg własnych stawek — PLAN
192. Przeliczanie walut w czasie rzeczywistym w oknie — PLAN
193. Stopy procentowe NBP i komunikaty w Narzędziach — PLAN
194. Wikipedia: streszczenie hasła z linkiem — PLAN
195. Wikisłownik: definicje i odmiana słowa — PLAN
196. Czytnik RSS z cache offline — PLAN
197. Hacker News: najpopularniejsze wpisy i streszczenia dyskusji — PLAN
198. YouTube: nowe filmy z subskrypcji — ZABLOKOWANE: własny klucz API i konto
199. Steam: alert zniżki z listy życzeń — ZABLOKOWANE: konto Steam
200. Steam: premiery i nowości tygodnia — PLAN
201. Sprawdzanie znanych luk CVE dla nazwy oprogramowania — PLAN
202. Skaner QR z ekranu lub kamery oraz generator kodu Wi-Fi dla gości — PLAN
203. Generator vCard z danych kontaktu — PLAN
204. Odliczanie do wydarzeń w Centrum — PLAN
205. Widok „co dziś” w Centrum: zadania, kalendarz, kursy i podsumowanie dnia — PLAN
206. Poranny briefing na telefon o ustalonej godzinie — PLAN
207. Tryb nocny: ciemny motyw i ograniczenie jasności — PLAN
208. Tryb dziecka: limit czasu i lista dozwolonych aplikacji na tym PC — PLAN
209. Kontrola rodzicielska z logiem i zgodą, działająca lokalnie — PLAN
210. Przypomnienie o przerwie przy długiej pracy — PLAN
211. Ćwiczenia oddechowe w oknie z dźwiękiem — PLAN
212. Nawodnienie: przypomnienia i licznik szklanek — PLAN
213. Dziennik snu z ręcznym wpisem i wykresem — PLAN
214. Tracker nastroju z wykresem tygodnia — PLAN
215. Przypomnienia o lekach z potwierdzeniem — PLAN
216. Lista leków i dat ważności w Pamięci — PLAN
217. Kalkulator BMI i kalorii z wykresem tygodnia — PLAN
218. Przepisy: składniki, przeliczanie porcji i minutniki kroków — PLAN
219. Planer posiłków na tydzień z listą zakupów — PLAN
220. Generator haseł i fraz z oceną siły — PLAN
221. Generator nazw plików i folderów wg szablonu — PLAN
222. Konwerter jednostek: długość, masa, temperatura, paliwo — PLAN
223. Kalkulator spalania z kosztem trasy — PLAN
224. Planowanie trasy: czas przejazdu i koszt paliwa — PLAN
225. Przypomnienie o końcu parkowania — PLAN
226. Zdjęcie miejsca parkingowego zapisane z czasem i lokalizacją — PLAN
227. Zapamiętywanie miejsca zaparkowania lokalnie — PLAN
228. Mapa zdjęć z lokalizacją EXIF offline — PLAN
229. Dziennik podróży ze zdjęciami i notatkami — PLAN
230. Lista rzeczy do spakowania na podróż — PLAN
231. Przypomnienie o ważności dokumentów, np. paszportu i dowodu — PLAN
232. Alert o wygaśnięciu ubezpieczeń i gwarancji sprzętu — PLAN
233. Rejestr gwarancji i paragonów ze zdjęciami — PLAN
234. Rejestr licencji oprogramowania i kluczy — PLAN
235. Inwentarz sprzętu domowego z wartością — PLAN
236. Dziennik serwisowy samochodu z przypomnieniami o wymianach — PLAN
237. Przeglądy i wymiany (filtry, opony) z alertami — PLAN
238. Przypomnienie o wymianie filtra wody lub baterii — PLAN
239. Dziennik ogrodu: podlewanie i nawożenie z przypomnieniem — PLAN
240. Kalendarz sadzenia wg pory roku i pogody — PLAN
241. Notatnik kart lojalnościowych lokalnie, bez haseł — PLAN
242. Lista prezentów z budżetem i wydatkami — PLAN
243. Organizator wydarzeń rodzinnych z zaproszeniami — PLAN
244. Wspólna lista zakupów z domownikami w sieci lokalnej — PLAN
245. Podział wydatków ze znajomymi z rozliczeniem — PLAN
246. Zbiórka składek: kto ile wpłacił, lokalnie — PLAN
247. Kalkulator napiwków i podziału rachunku — PLAN
248. Szacunek PIT: netto i brutto — PLAN
249. Szacunek składek ZUS z przypomnieniem o terminach — PLAN
250. Terminy podatkowe PL w kalendarzu z alertami — PLAN
251. Faktury: szablon, numeracja i eksport PDF — PLAN
252. Lista klientów i płatności lokalnie — PLAN
253. Czasomierz rozliczeń godzinowych dla klientów — PLAN
254. Kwartalny raport przychodów i kosztów — PLAN
255. Przypomnienie o wygaśnięciu certyfikatów i domen — PLAN
256. Monitor dostępności stron WWW z alertem — PLAN
257. Monitor certyfikatu SSL: data wygaśnięcia — PLAN
258. Monitor odpowiedzi HTTP z wykresem — PLAN
259. Szybki test prędkości internetu z wykresem — PLAN
260. Wykres jakości łącza i utraconych pakietów — PLAN
261. Lista urządzeń w sieci domowej: IP, MAC i producent — PLAN
262. Alert o nowym urządzeniu w sieci domowej — PLAN
263. Lista urządzeń w sieci oznaczona jako zaufane lub nieznane, bez blokowania — PLAN
264. Limit czasu aplikacji na PC dla dziecka z logiem — PLAN
265. Zużycie transferu danych per aplikacja — PLAN
266. VPN WireGuard z przełącznikiem, na własnym serwerze — ZABLOKOWANE: własny serwer VPN
267. Test wycieku DNS dla własnego połączenia — PLAN
268. Zmiana DNS z przełącznikiem i testem szybkości — PLAN
269. Przełącznik profilu sieci: dom, praca, publiczna — PLAN
270. Hotspot Wi-Fi z ustawionym hasłem — PLAN
271. Udostępnianie plików w sieci lokalnej, tylko z PIN-em — PLAN
272. Serwer plików LAN z dostępem z telefonu — PLAN
273. Skan otwartych portów własnego PC z zamykaniem zbędnych — PLAN
274. Lista otwartych portów z opisem usług — PLAN
275. Podgląd reguł zapory i dodawanie własnych — PLAN
276. Alert zmiany pliku hosts — PLAN
277. Monitor zmian w rejestrze autostartu — PLAN
278. Historia logowań do Windows z datą — PLAN
279. Lista zainstalowanych programów z wersjami i aktualizacjami — PLAN
280. Stan aktualizacji Windows i przypomnienie o restarcie — PLAN
281. Ręczne sprawdzenie wydań na GitHub, bez automatycznej instalacji i bez manifestu — PLAN
282. Przywracanie punktu przywracania przed zmianą — PLAN
283. Kreator punktu przywracania z opisem — PLAN
284. Monitor ustawień prywatności Windows z zaleceniami — PLAN
285. Kreator czyszczenia prywatności z podglądem — PLAN
286. Lista kont użytkowników i ich statusu, bez tworzenia haseł — PLAN
287. Monitor zadań w tle z podglądem i wyłączaniem za zgodą — PLAN
288. Lista usług Windows z opisami i trybem startu — PLAN
289. Analiza czasu startu systemu i wskazanie winowajców — PLAN
290. Podgląd pamięci z zaleceniami, bez agresywnego czyszczenia — PLAN
291. Wykres RAM i CPU z historią 24 h — PLAN
292. Historia zajętości dysku z prognozą zapełnienia — PLAN
293. Czyszczenie pamięci podręcznej przeglądarek za zgodą — PLAN
294. Lista rozszerzeń przeglądarki z wyłączaniem — PLAN
295. Lista zapisanych sieci Wi-Fi z hasłami, tylko po Windows Hello — PLAN
296. Przypomnienie o zmianie hasła co 90 dni — PLAN
297. Monitor certyfikatów systemowych i ich wygaśnięć — PLAN
298. Lista aplikacji z dostępem do kamery i mikrofonu — PLAN
299. Przełącznik blokady kamery i mikrofonu — PLAN
300. Wskazywanie programu, który używa kamery — PLAN
301. Eksport logów Sentinela do ZIP z hashami — PLAN
302. Tygodniowy raport bezpieczeństwa na telefon — PLAN
303. Podsumowanie zmian w systemie od ostatniego uruchomienia — PLAN
304. Dziennik zdarzeń z filtrem i wyszukiwaniem — PLAN
305. Tryb audytu: co zmieniła aplikacja, z podglądem — PLAN
306. Wykrywanie anomalii CPU od nieznanego procesu — PLAN
307. Alert przy masowej zmianie plików, podobnej do ransomware — PLAN
308. Chroniony folder: aplikacje muszą prosić o zgodę — PLAN
309. Kopia zapasowa przed masową zmianą plików — PLAN
310. Przywracanie plików z kopii z kalendarzem — PLAN
311. Lista kopii zapasowych z weryfikacją hashem — PLAN
312. Suchy test odtworzenia kopii — PLAN
313. Backup na dysk zewnętrzny po podłączeniu, z pytaniem — PLAN
314. Backup do folderu synchronizowanego przez klienta chmury — PLAN
315. Szyfrowanie kopii zapasowej — PLAN
316. Sejf haseł do Wi-Fi i routerów, tylko lokalnie — PLAN
317. Test połączenia z internetem i czas od ostatniego zerwania — PLAN
318. Zdalny dostęp do PC przez własny VPN (opt-in) — ZABLOKOWANE: własny serwer VPN
319. Podgląd zdarzeń Base w telefonie z filtrem — ZABLOKOWANE: sprzęt Base do testów
320. Lista zdarzeń na żywo z kolorami i alertami — PLAN
321. Wykres aktywności PC w godzinach pracy z tygodnia — PLAN
322. Raport produktywności: aplikacje i czas aktywnego okna — PLAN
323. Notatki kontekstowe przypisane do sesji pracy — PLAN
324. Historia projektów z czasem pracy i plikami — PLAN
325. Szablony projektów z listą kroków — PLAN
326. Oś czasu projektu z terminami i kamieniami milowymi — PLAN
327. Eksport projektu do PDF lub Markdown — PLAN
328. Lokalna wiki z linkami wewnętrznymi — PLAN
329. Tagi i wyszukiwanie po tagach w Pamięci — PLAN
330. Zakładki stron z czytaniem offline — PLAN
331. Kolekcja cytatów z autorem i źródłem — PLAN
332. Słowniczek skrótów do szybkiego wklejania — PLAN
333. Szablony wiadomości ze zmiennymi — PLAN
334. Szablony odpowiedzi na częste pytania klientów — PLAN
335. Poprawa stylu tekstu lokalnym modelem — PLAN
336. Streszczenie długiego pliku lokalnym modelem — PLAN
337. Tłumaczenie dokumentu z zachowaniem formatowania — PLAN
338. Korekta pisowni i gramatyki PL w czacie — PLAN
339. Podpis e-mail z szablonem — PLAN
340. Przepisywanie tekstu na prostszy język — PLAN
341. Wyjaśnienie błędu z logu przez lokalnego asystenta kodu — PLAN
342. Analiza logów: znajdowanie błędów i grupowanie ich — PLAN
343. Zapytania do SQLite z podglądem wyników — PLAN
344. Edytor JSON i YAML z walidacją — PLAN
345. Tester wyrażeń regularnych z podglądem na tekście — PLAN
346. Formatowanie i minifikacja JSON i XML — PLAN
347. Konwerter Base64, URL, hex i Unicode — PLAN
348. Sumy kontrolne CRC, MD5 i SHA dla plików — PLAN
349. Podgląd nagłówka pliku i rozpoznanie typu — PLAN
350. Porównanie obrazów piksel po pikselu z raportem różnic — PLAN
351. Konwersja Word i Excel do PDF lokalnie — PLAN
352. Edytor CSV z filtrowaniem i sortowaniem — PLAN
353. Podgląd arkuszy Excel bez pakietu Office — PLAN
354. Wykres z danych CSV w oknie — PLAN
355. Generator tabel z tekstu — PLAN
356. Edytor Markdown z podglądem — PLAN
357. Notatnik z szybkim wstawianiem daty i czasu — PLAN
358. Wielokrotna lista kontrolna do wielokrotnego użycia — PLAN
359. Nagrywanie makr klawiatury z odtwarzaniem — PLAN
360. Reguła: „gdy plik trafi do Pobranych, przenieś do Dokumentów” — PLAN
361. Reguły porządkowania plików wg typu i wieku — PLAN
362. Automatyczne nazwy zrzutów i plików z datą i opisem — PLAN
363. Dziennik zmian w folderze z historią — PLAN
364. Podgląd kopiowania z szacowanym czasem — PLAN
365. Kolejka kopiowania z wstrzymywaniem — PLAN
366. Szybka wyszukiwarka plików po nazwie z indeksem lokalnym — PLAN
367. Zakładki folderów w Narzędziach — PLAN
368. Szybkie otwieranie ostatnio używanych plików — PLAN
369. Podgląd miniatur folderu bez otwierania — PLAN
370. Zmiana nazw wsadowa z podglądem i cofaniem — PLAN
371. Konwersja dźwięku (MP3, WAV, FLAC) wsadowo — PLAN
372. Normalizacja głośności nagrań — PLAN
373. Usuwanie szumu z nagrań lokalnie — PLAN
374. Cięcie i łączenie nagrań audio — PLAN
375. Podcast: zapis i eksport rozdziałów — PLAN
376. Edycja napisów SRT z synchronizacją — PLAN
377. Konwersja wideo do MP4 z presetami, lokalnie — PLAN
378. Wycinanie fragmentu wideo bez utraty jakości — PLAN
379. Miniatury wideo z klatek — PLAN
380. Lista zdjęć i filmów z datą i miejscem (EXIF) — PLAN
381. Zdjęcie z kamery PC na żądanie z podglądem — PLAN
382. Nagrywanie ekranu z dźwiękiem z przycisku w Narzędziach — PLAN
383. GIF z nagrania ekranu — PLAN
384. Pokaz slajdów ze zrzutów — PLAN
385. Lupa ekranu i pipeta koloru — PLAN
386. Linijka i miarka na ekranie — PLAN
387. Podświetlanie kursora i klawiszy w nagraniach — PLAN
388. Podpowiedzi skrótów dla aktywnej aplikacji — PLAN
389. Przypomnienie o zapisie pracy co N minut — PLAN
390. Autozapis notatnika przy bezczynności — PLAN
391. Odzyskiwanie niezapisanych dokumentów, jeśli aplikacja to umożliwia — PLAN
392. Szybkie notatki przyklejone na pulpicie — PLAN
393. Kolorowe etykiety plików i folderów — PLAN
394. Ukrywanie zawartości ekranu przy przerwie — PLAN
395. Podgląd powiadomień z ostatniej godziny — PLAN
396. Ranking najczęściej używanych aplikacji — PLAN
397. Zestawienie czasu pracy w tygodniu z celami — PLAN
398. Harmonogram wizyt u lekarza z przypomnieniami — PLAN
399. Przypomnienie o zamówieniu leków — PLAN
400. Lista domowych wydatków na zakupy z podsumowaniem kategorii — PLAN
401. Lista ulubionych przepisów z kosztem składników — PLAN
402. Kalkulator kosztu gotowania na porcję — PLAN
403. Lista domowych napraw i ich kosztów — PLAN
404. Harmonogram cyklicznych zadań domowych — PLAN
405. Liczniki zużycia kosmetyków i żywności z alertem — PLAN
406. Kalendarz odpadów segregowanych z przypomnieniem — PLAN
407. Przypomnienie o odczycie liczników co miesiąc — PLAN
408. Miesięczne podsumowanie kosztów domu — PLAN
409. Wizualizacja budżetu domowego jako wykres kołowy — PLAN
410. Kalkulator oszczędzania: ile odłożysz w 12 miesięcy, z procentem składanym — PLAN
411. Symulator emerytury i oszczędności (szacunek) — PLAN
412. Porównanie kosztu samochodu: leasing i kupno — PLAN
413. Kalkulator wynajmu i kupna mieszkania — PLAN
414. Lista celów finansowych z postępem — PLAN
415. Dziennik wdzięczności z wieczornym przypomnieniem — PLAN
416. Dziennik myśli z wieczornym szablonem — PLAN
417. Plan dnia z blokami czasu i widokiem tygodnia — PLAN
418. Planowanie tygodnia z priorytetami — PLAN
419. Przegląd tygodnia z pytaniami i zapisem wniosków — PLAN
420. Przegląd miesiąca z wykresem celów — PLAN
421. Nauka języka: fiszki z wymową TTS — PLAN
422. Quiz z materiału z Pamięci, z własnymi pytaniami — PLAN
423. Plan powtórek z liczbą fiszek na dzień — PLAN
424. Ćwiczenie pamięci: ciągi cyfr i wynik — PLAN
425. Sudoku i szachy offline w Narzędziach — PLAN
426. Gry logiczne offline w Narzędziach — PLAN
427. Tabela wyników gier w Gaming — PLAN
428. Lista kontrolna przed grą: ustawienia i ostrzeżenia — PLAN
429. Dziennik wydajności gry: start, koniec, średnie zużycie CPU i GPU — PLAN
430. Wykres klatek z pliku logu wydajności gry — PLAN
431. Podgląd presetów ustawień gry — PLAN
432. Kolejka pobierania gier i aktualizacji z terminarzem — PLAN
433. Przypomnienie o zapisie gry przed wyłączeniem PC — PLAN
434. Lista wydarzeń e-sportowych z kalendarzem (online) — ZABLOKOWANE: wymaga źródła danych
435. Lista znajomych ze Steam: statusy i zaproszenia — ZABLOKOWANE: konto Steam
436. Statystyki sprzętu do gier: rozdzielczość i odświeżanie — PLAN
437. Porównanie konfiguracji z wymaganiami gry, wpisanymi ręcznie — PLAN
438. Lista ulubionych serwerów z pingiem — PLAN
439. Test pingu do serwerów gier — PLAN
440. Wykres pingu i utraty pakietów podczas gry — PLAN
441. Tryb cichy podczas gry: wyciszenie powiadomień i komunikatorów — PLAN
442. Nagrywanie ostatnich 30 sekund gry w buforze — PLAN
443. Zrzut ekranu po wyniku w grze, na ręczne polecenie — PLAN
444. Przełącznik trybu wydajności i cichego chłodzenia — PLAN
445. Podgląd wartości czujników temperatury i napięcia, bez sterowania — PLAN
446. Podgląd i zapis profilu zasilania — PLAN
447. Szacunek zużycia prądu przez PC — PLAN
448. Kalkulator kosztu prądu PC za miesiąc — PLAN
449. Harmonogram wygaszania monitora po bezczynności — PLAN
450. Przypomnienie o przerwach od ekranu dla oczu — PLAN
451. Filtr niebieskiego światła wg pory dnia — PLAN
452. Przypomnienie o pozycji i rozciąganiu przy pracy — PLAN
453. Test czytania i powiększenie tekstu — PLAN
454. Powiększenie ekranu i czytnik kontrastu — PLAN
455. Duży tekst i wysoki kontrast w Ustawieniach — PLAN
456. Obsługa całego Sentinela klawiaturą — PLAN
457. Klawisze lepkie i długie naciśnięcie dla osób z ograniczoną motoryką — PLAN
458. Mowa na tekst w polach tekstowych (opt-in) — PLAN
459. Lektor ekranowy dla powiadomień — PLAN
460. Sygnalizacja wizualna zamiast dźwiękowej — PLAN
461. Wybór wyjścia audio dla TTS — PLAN
462. Tryb dla słabo widzących: duże kafelki i ikony — PLAN
463. Podręczna pomoc: pytanie o funkcję z odpowiedzią i skrótem — PLAN
464. Wyszukiwarka ustawień z wynikiem i skrótem — PLAN
465. Zapis i odtwarzanie ustawień między komputerami — PLAN
466. Profile użytkowników na jednym PC z przełączaniem — PLAN
467. Dostęp gościa z ograniczonym zestawem funkcji — PLAN
468. Wersje robocze konfiguracji z cofaniem — PLAN
469. Dziennik zmian ustawień z datą — PLAN
470. Porównanie dwóch zapisanych konfiguracji — PLAN
471. Automatyczna kopia ustawień przed zmianą — PLAN
472. Walidator ustawień wykrywający sprzeczne opcje — PLAN
473. Test wszystkich modułów z raportem — PLAN
474. Raport diagnostyczny na pulpit, bez danych osobowych — PLAN
475. Lista błędów z podpowiedzią naprawy — PLAN
476. Lokalny raport błędu do ręcznego wysłania — PLAN
477. Samouczek przy pierwszym uruchomieniu — PLAN
478. Podpowiedzi kontekstowe w każdym oknie — PLAN
479. Lista nowości po aktualizacji — PLAN
480. Motyw kolorystyczny z podglądem i zapisem — PLAN
481. Zmiana układu kafelków w Centrum — PLAN
482. Układ Centrum z kolumnami i przypiętymi kafelkami — PLAN
483. Przypinanie wybranych funkcji do paska zadań — PLAN
484. Ikona w zasobniku z szybkimi akcjami — PLAN
485. Menu kontekstowe Eksploratora: „przenieś do Sentinela” — PLAN
486. Widok dnia z kalendarza Windows w Centrum — PLAN
487. Powiadomienia Windows w Sentinelu z własnym kanałem — PLAN
488. Obsługa wielu monitorów: zrzut i układ okien — PLAN
489. Presety układu okien jednym skrótem — PLAN
490. Przypinanie okna nad innymi (opt-in) — PLAN
491. Zapis i przywracanie układu okien po restarcie — PLAN
492. Tryb pełnoekranowy Centrum na drugim monitorze — PLAN
493. Ikona w zasobniku pokazująca tryb: skupienie, cicho, online lub offline — PLAN
494. Mini-panel szybkich przełączników: Wi-Fi, Bluetooth, skupienie — PLAN
495. Zakładka Pomoc z dokumentacją offline — PLAN
496. Wersja językowa PL i EN z przełącznikiem — PLAN
497. Eksport wszystkich danych do JSON — PLAN
498. Usunięcie wszystkich danych jednym przyciskiem po potwierdzeniu PIN-em — PLAN
499. Wyszukiwarka wszystkich funkcji w Ustawieniach — PLAN
500. Historia zmian listy funkcji Sentinela z datą wdrożenia — PLAN

## Usunięte z poprzedniej wersji (i dlaczego)

- Stan drzwi, otwarte/zamknięte drzwi, alert zalania i dymu z drzwi: Sentinel nie ma czujnika stanu drzwi i nie wie tego sam z siebie.
- Kontrola wentylatorów: nie działa uniwersalnie bez sterowników producenta.
- Monitor FPS poza nakładką: nie da się go zrobić uczciwie bez hooków w grę.
- Sprawdzanie i zmiana ustawień routera: zależy od modelu, nie da się zrobić ogólnie.
- Alerty cen ze sklepów: scraping jest kruchy i łamie warunki usług.
- Automatyczna aktualizacja z podpisem i manifestem: manifest aktualizacji wyłączony na polecenie właściciela.
- Blokowanie urządzeń w routerze i limit transferu hotspotu: zależą od routera.
- Zdalna kontrola dostępu do internetu dla dzieci przez router: zastąpiona limitem aplikacji na tym PC.
