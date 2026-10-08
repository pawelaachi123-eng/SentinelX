# SX4 — rzeczywisty kontrakt binarny

Zgodność binarna z dostarczonym V4/4.2.1: 49-bajtowy nagłówek.

| Offset | Pole | Format |
|---|---|---|
|0|deviceId|uint16 big endian|
|2|opcode|uint8|
|3|nonce|uint32 big endian|
|7|timestamp|uint64 big endian, Unix ms|
|15|payload length|uint16 big endian, max 16384|
|17|HMAC|SHA-256, 32 bajty|
|49|payload|surowe bajty|

HMAC obejmuje pierwsze 17 bajtów + surowy payload. Porównanie stałoczasowe. Timestamp ±5 s; replay cache 10 s; limit 120/min; klucz 32 bajty osobny dla urządzenia.
OpCode **201** jest rozszerzeniem dla typed JSON. V4.2.1 opisuje opcodes 1–200, dlatego obsługa 201 musi być jawnie wdrożona w Base; nie deklarujemy zgodności pełnej usługi ze stock firmware.
Payload ma dokładnie: version=4, type, requestId UUID, correlationId pusty albo UUID, expiresAt Unix ms do 5 min, data object. UTF-8 jest ścisłe; duplikaty nazw (także w zagnieżdżeniach), nadmiarowe pola i głębokość >16 są odrzucane.
Przykład bez sekretów: `{"version":4,"type":"status","requestId":"00000000-0000-0000-0000-000000000001","correlationId":"","expiresAt":<przyszły Unix ms>,"data":{}}`.
Odrzucane: version=3, powtórzony nonce, `type=../shell`, wygasła komenda, zły HMAC, response z innym correlationId.
TLS 1.2/1.3 pin SHA-256 ważnego certyfikatu. session.prove niesie losowy challenge; Base odpowiada session.proved ze zgodnym echo i HMAC. Base ustala role/granty z wcześniej provisionowanego deviceId, nigdy z deklaracji klienta.
Negocjacja capabilities zwraca types (max 32). Dopiero zadeklarowane typy można wywoływać. Każdy wynik ma type + .result albo error/code; zadania: accepted, running, succeeded, failed, expired, cancelled.
LAN UDP 42421: SX4_DISCOVER_V421:<nonce>; JSON protocol, nonce, baseId, port, fingerprint, name. Discovery nie ustanawia zaufania — fingerprint porównuje się z fizyczną Base.
Po IO błędzie retry 1–60 s + jitter, rediscovery tylko dla zgodnego BaseId i pinu. Auth/revocation/protocol powodują blokadę do ponownego parowania. Komendy telefonu nie są ponawiane automatycznie.
