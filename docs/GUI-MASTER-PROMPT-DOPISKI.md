DOPISKI — dopisać na koniec prompta (osobno, nie wplecione w skrót)

1. Wersja to asercja, nie tekst. Core/AppConstants.cs ma Version = „0.93 · PORZĄDKI", a UiSmokeTestRunner.cs:155 sprawdza, że polecenie „wersja" zwraca 0.93, a „co nowego" zwraca PORZĄDKI. Nie odświeżaj napisów przy okazji GUI. Bump wersji to osobna zmiana: AppConstants + ta asercja + Title w legacy + csproj, i wymaga mojej zgody.

2. Etykiety pól ustawień to kontrakt. SettingsCatalog.Create(...) czytają ViewModels/SettingsViewModel.cs:64, MainWindow.Settings.cs:20, SettingsEditorView.cs:37, UpgradeRegressionRunner.cs:35 i tests/BackendRegression.cs:41, a te dwa ostatnie robią Single(x => x.Label == "Próg VAD"). Wolno dopisywać nowe pozycje, nie wolno zmieniać istniejących Label — nawet źle nazwane zgłoś osobno.

3. Ui.SelectedPage jest współdzielone z legacy. ShowPage (MainWindow.xaml.cs:192) ma białą listę Chat, System, Tools, Voice, Gaming, Memory, Settings, AI, Actions, Programs i dla wartości spoza listy PO CICHU wraca do „Chat". Klucze shellu (command, memory, projects, settings) zapisane w tym polu nie wywrócą aplikacji — ukradną legacy ostatnią stronę, bez błędu i bez logu. Dlatego: albo nie zapisuj SelectedPage z shellu, albo zrób jawne mapowanie w obie strony i opisz to w raporcie. Nie dokładaj nowego pola do UiSettings bez mojej zgody.

4. Nowa strona to trzy pliki spoza Twojego zakresu: rejestracja DI w Core/ServiceLocator.cs:77-94, DataTemplate w Views/MainWindow.xaml i asercja „dokładnie 12" w scripts/check-architecture.py. Panel archiwum z backlogu (P0#3) jest tym przypadkiem — najpierw pytaj, potem dodawaj.

5. docs/MEMORY.md zawiera reguły dotyczące UI, nie tylko danych: sześć niezależnych przełączników prywatności oraz usunięcie pamięci jako ryzyko HIGH z jednorazową zgodą klawiaturową, wygasającą po 10 minutach (głos nie zatwierdza). Czas wygaśnięcia na karcie zgody jest obowiązkowy, nie opcjonalny.

6. Znalezione rozjazdy do odnotowania w raporcie, NIE do naprawy: app.manifest ma assemblyIdentity 0.85.0.0, tytuł legacy okna to „Sentinel X • 0.85", stopka legacy „Wersja 0.82", a AppConstants to 0.93. Nie synchronizuj tego przy przebudowie GUI.
