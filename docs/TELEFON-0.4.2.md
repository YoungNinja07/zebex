# Zebex Phone / Windows 0.4.2

Aktualizacja poprawia zgodność klucza TLS z Windows (import UserKeySet zamiast
EphemeralKeySet, którego Windows Schannel może nie obsłużyć). Zapisany certyfikat
jest zachowany: nie resetuje parowania ani danych.

Przed pokazaniem QR program wykonuje lokalny handshake TLS 1.2 z własnym
serwerem i sprawdza przypięty certyfikat. Błędy uwierzytelniania TLS w Windows
zawierają teraz przyczynę wewnętrzną w dzienniku.

APK rozróżnia inną tożsamość komputera, nieważną datę certyfikatu oraz pozostałe
błędy TLS. W ostatnim przypadku pokazuje typ i szczegóły wyjątku, zamiast
błędnie żądać ponownego skanowania QR przy każdym błędzie szyfrowania.
Sprawdzanie odcisku SHA-256 oraz daty ważności pozostaje obowiązkowe.

## Aktualizacja

1. Zamknij stary program Windows. Rozpakuj cały nowy ZIP do jednego folderu.
2. Uruchom ZebexSimulator.exe 0.4.2. Dane pozostają w LocalAppData.
3. W zakładce Telefon Android / Wi-Fi zezwól na Wi-Fi w Windows, jeżeli EXE
   znajduje się w nowym folderze, i włącz połączenie. Test TLS musi przejść.
4. Zainstaluj APK 0.4.2 jako aktualizację bez odinstalowywania starego.
5. Połącz telefon aktualnym QR. Nie usuwaj aplikacji, danych ani certyfikatów.

Jeśli nadal występuje błąd, potrzebny jest tekst „Szczegóły błędu” z telefonu
oraz wpis „Telefon TLS” z dziennika programu Windows.

Weryfikacja w chmurze obejmuje kompilację obu pakietów, testy Android JVM,
serwer TLS i transmisję przez pomocnik SUZ. Nie obejmuje fizycznego Redmi,
Windows Schannel ani uruchomionego Subiekta nexo.
