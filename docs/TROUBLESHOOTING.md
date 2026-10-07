# Rozwiązywanie problemów

Base niewidoczna: ta sama prywatna sieć, UDP 42421 i zgodny discovery; ręczny adres też działa. Nie ufaj pinowi wyłącznie z UDP.
Auth/replay/revoked: połączenie blokowane. Sprawdź zegary (±5 s), provisionowanie osobnego ID/sekretu i ważny certyfikat. Unieważnij stary sekret po obu stronach i sparuj ponownie.
unsupported/opcode: firmware nie negocjuje typu/rozszerzenia 201. Stock master 4.2.1 nie daje gotowego transportu tego rozszerzenia.
PC locked/offline: zadanie czeka na odblokowaną sesję do expiry. Power/Steam install wymaga lokalnej zgody, która także wygasa.
Steam interaction_required oznacza tylko przekazanie do Steam; aplikacja nie potwierdza instalacji/logowania bez dowodu.
Ollama unavailable: zainstaluj lokalnie i sprawdź loopback 11434; ensure nie ładuje modelu.
Updater hash/signature/path/version/health: pakiet odrzucony, aktywna wersja zachowana. Nie wyłączaj weryfikacji, aby przepchnąć wadliwy pakiet.
Debug APK nie aktualizuje starej instalacji podpisanej innym kluczem. Wersja produkcyjna wymaga własnego stałego klucza. Nie nazywaj debug APK produkcyjnym.
