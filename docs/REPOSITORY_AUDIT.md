# Audyt Build 1.0

Repo bazowe: main 3d202c53041d31f24537bf91bc89aba4f3bbbb90 (0.96.0).
Windows: C# / .NET 10 WPF, Microsoft DI, CommunityToolkit MVVM. Android: Java 17, AGP 8.7.3, SDK 35, Gradle 8.9.
Wejścia: App.xaml.cs; MainActivity (dotychczasowy panel PC); BaseActivity (natywna Base). Agent: WindowsBaseService.
Zachowane: Brain/Router, Tools, legacy UI, polski głos i wake word, pamięć, tray, single-instance, telefon/PWA, wbudowany llama.cpp.
Usunięto z bieżącego drzewa prywatny P12 i hasła podpisu, wyłączanie błędów lint. Starsza historia zawiera stary klucz; nie należy używać go do produkcji.
Pierwszy build wykrył nullable query i użycie wygenerowanych pól MVVM. Naprawiono. Nowe widoki wymagają 15 stron w kontroli architektury.
Nie przepisywano frameworków. Fizyczna Base nie znajduje się w repo. Dostarczony master 4.2.1 ma transport TLS/relay pozostawiony jako TODO.
