# Integracja SUZ — stan prac

Rozszerzenie0.4 dodaje telefon Android i serwer Wi-Fi. Podstawowy protokół SUZ oraz most pozostają opisane poniżej. Telefon przesyła niezmienne dokumenty z UUID przez TLS, parowane QR z pinem SHA256 certyfikatu. Tokeny dostępu i klucz certyfikatu Windows są chronione DPAPI CurrentUser. Import zapisuje dokument i pokwitowanie do jednego atomowo podmienianego JSON przed odpowiedzią. Historia UUID zapobiega powieleniu dokumentu po retry, usunięciu i restarcie. Odczyty telefonu są zamknięte do edycji i nie są zerowane przy przyjęciu katalogu. Komunikacja z CollectorSession jest wykonywana na wątku UI. MainForm uzbraja najstarszy oczekujący dokument, a potwierdzenie Time ustawia status suz.

Walidacja0.4:118 testów .NET i10 testów Robolectric. tests/phone-integration.py prowadzi klienta Link z kodu APK przez rzeczywisty serwer TLS, dwa PTY i proces SUZ bridge, weryfikując plik i końcowy status. Test odrzuca błędny pin certyfikatu. Aparat fizycznego telefonu, okno Windows i SUZ/nexo wymagają weryfikacji na urządzeniach użytkownika. [Instrukcja telefonu](TELEFON-Android.md).

Docelowa wersja użytkownika: SUZ 19.2, model „Elzab Kolektor Plus (Zebex)”. Wersja 0.3 implementuje transmisję dokumentów, katalogu i test połączenia. Wymaga własnego mostu SUZ, instalowanego przyciskiem w EXE, ponieważ oryginalne narzędzia wymuszają obecność fizycznego USB. Test pełnego programu SUZ/nexo na Windows pozostaje do wykonania.

## Potwierdzone informacje

- Oficjalna instrukcja użytkownika opisuje EKP i wymaga transmisji 115200, ale nie zawiera komend, ramek ani potwierdzeń protokołu.
- W logu Windows test pary SUZ COM24 ↔ symulator COM21 przeszedł w obu kierunkach. Brak RX podczas próby połączenia nie rozstrzyga sposobu rozpoczynania transferu.
- Model kolektora w ogólnej bibliotece SUZ implementuje IPassiveDevice. Właściwy sterownik modelowy definiuje odczyt konfiguracji, stanu bufora, dokumentów, potwierdzenie odbioru i wysyłanie produktów. Te operacje nie są określone w bibliotece ogólnej.
- Domyślny katalog sterowników w SUZ 19.2: `C:\ProgramData\InsERT\SUZ\Drivers`. Może być zmieniony przez konfigurację ApplicationDataFolder.
- Sprawdzono oficjalne pakiety SUZ 19.2, nexo 61.1.1, sterowników GT i ich zagnieżdżone archiwa oraz zasoby. Nie znaleziono w nich biblioteki modelowej EKP. Nie wnioskuj z grafiki DC.Elzab.KolektorPlusZebex.png, że zasób zawiera protokół.
- Artykuł InsERT e-pomocy 13762 pokazuje Novitus Inwentaryzator 3.1. Jego numeracji menu nie wolno przenosić na EKP.

## Analiza biblioteki przesłanej przez użytkownika

Otrzymano `InsERT.Devices.Drivers.DC.Text.dll` (103968 bajtów, SHA-256 `57ffcd738e18546596305a6fb4472511c76b233553514e87eb65317cf4aa95a8`). Biblioteka zawiera właściwy profil ElzabKolektorPlusZebex. Analiza dotyczy plików danych i wywołań narzędzi; sama biblioteka nie zawiera transmisji szeregowej EKP.

Sprawdzanie połączenia wysyła katalog z jednym produktem `kod_testowy`, używając tej samej operacji co wysyłanie produktów. Wymaga zatem rzeczywistego odbiornika katalogu i potwierdzeń. Test pary COM oraz otwarcie portu nie zastępują tej operacji.

Sterownik uruchamia programy względem folderu zawierającego DLL:

| Operacja | Program | Argumenty |
| --- | --- | --- |
| Odczyt dokumentów | `Binaries\Elzab\KolektorPlusZebex\zac_dataReader.exe` | port, prędkość, `3`, `0`, `1`, ścieżka pliku |
| Wysyłanie katalogu | `Binaries\Elzab\KolektorPlusZebex\zac_dlookup.exe` | port, prędkość, `1`, ścieżka pliku |

Sukces odczytu rozpoznaje po komunikacie `Receive data completed`, a sukces wysyłania katalogu po `Send lookup table completed`. Są to komunikaty procesu na komputerze, nie komendy ani potwierdzenia przesyłane do kolektora.

Potwierdzone formaty pośrednich plików, bez nagłówka, z separatorem TAB i kropką dziesiętną:

| Dane | Pola |
| --- | --- |
| Skanuj | kod, `----------------`, ilość |
| Skanuj+ | kod, ilość |
| Szybki skan | kod, `I:1` |
| Osobny dokument bez sklejania | nazwa dokumentu, kod, `----------------`, ilość |
| Nazwany dokument ze sklejaniem | nazwa dokumentu, kod, ilość |
| Nazwany szybki dokument | nazwa dokumentu, kod, `I:1` |
| Domyślny katalog SUZ → narzędzie native | kod, nazwa, stan, cena, `0` |

Odczyt dokumentów używa ASCII. Kolejny parser CSV przycina pola i interpretuje cudzysłowy. Własny encoder odrzuca wartości, które utraciłyby znaki lub zmieniły znaczenie po tej konwersji. Katalog jest zapisany jako UTF-8; sterownik usuwa polskie znaki diakrytyczne oraz TAB z pól tekstowych przed zapisaniem. Schemat katalogu określa szerokości 20/32/10/10/1, ale nie stanowi to dowodu na sposób kodowania ramek COM.

Źródła: [SUZ](https://www.insert.com.pl/dla_uzytkownikow/subiekt_nexo/ulepszenia/serwer_urzadzen_zewnetrznych.html), [obsługiwane urządzenia](https://www.insert.com.pl/programy_dla_firm/sprzedaz/subiekt_nexo/urzadzenia_zewnetrzne.html), [diagnostyka transmisji SUZ](https://www.insert.com.pl/dla_uzytkownikow/e-pomoc_techniczna/2515.html).

## Przygotowany kod

CollectorSession zapewnia niezmienne kopie dokumentów, identyfikatory sesji i rewizje, jednorazowe potwierdzenia oraz atomowe przyjęcie katalogu. Nie kasuje dokumentów po błędzie, anulowaniu ani starym potwierdzeniu. Katalog odebrany w czasie nowych skanów nie wyzeruje tych danych.

CollectorView udostępnia zdarzenia SendDocumentRequested, ReceiveProductsRequested i CommunicationCancelled oraz metody przyjęcia zweryfikowanego wyniku. MainForm obsługuje ZacDevice na wątku UI; odbiór COM jest przekazywany przez BeginInvoke. Anulowanie i zamknięcie portu zatrzymują silnik. Nie ma ręcznego udawania zdalnego potwierdzenia.

EkpDataFiles koduje zamrożony dokument w formacie pośrednim czytanym przez sterownik oraz parsuje kompletny domyślny katalog. Nie zmienia sesji ani nie udaje potwierdzeń transmisji. Parser sprawdza cały katalog, w tym duplikaty, przed udostępnieniem go warstwie przyjęcia danych. Format pliku nie jest protokołem COM.

Walidacja: 30 dotychczasowych testów, 19 testów bezpieczeństwa transferów, 23 testy formatów EKP oraz kompilacja Windows EXE przeszły. Dodatkowy tymczasowy test wywołał oryginalną przesłaną DLL: `EncodeReaderOutput` → rzeczywiste `Load` i `Validate` dla sześciu kombinacji nazwy/trybu. Nazwy, kody, liczba pozycji i ilości zgadzają się z zamrożonymi dokumentami, także dla powtórzeń, zera i ilości ujemnych. Parser katalogu przyjął plik wygenerowany przez oryginalne `Validate`/`Save`, w tym pustą nazwę produktu `kod_testowy`, BOM, cudzysłowy i długie wartości. Potwierdzono także, że sam zapis tego kontenera nie egzekwuje szerokości ze schematu.

Oryginalne kontenery służą do niezależnego sprawdzania plików. Biblioteki i zdekompilowany kod producenta pozostają poza repozytorium w `/tmp` i katalogu załączników. Weryfikację procesu mostu i transmisji opisano poniżej; pełny test klienta SUZ wymaga Windows.

## Protokół z przesłanych narzędzi i most SUZ 0.3

Użytkownik przesłał ZIP zawierający oba narzędzia. Są to programy .NET, a nie nieczytelne biblioteki native. Analiza potwierdziła:

- `zac_dataReader.exe` SHA256 `c769d0f39b7537264ad5444ca000e02dd6104f4010123a60ee06cdcad27fb1f2`.
- `zac_dlookup.exe` SHA256 `965978f7e23a2b10167a8ed0167c643dcd2769488a0a6793adbe3085a653c473`.
- CheckArguments bezwarunkowo wykonuje SearchDevice_VCOM, szukając VID 9450 (24EA) / PID 768 (0300) przez WMI Win32_USBControllerDevice. Podany COM nie wyłącza wyszukiwania. To wyjaśnia brak RX w logach wirtualnej pary.
- Odczyt: host `READ\r` → kolektor `ACK\r` → host `ACK\r`; rekordy i kolejne `ACK\r` / `NAK\r`; koniec `OVER\r` → host `TimeyyyyMMddHHmmss\r`. Reader usuwa pierwszy bajt oraz ostatnie trzy bajty rekordu przed zapisaniem jego tekstu. Oryginalny reader nie sprawdza tych bajtów; własny most używa nagłówka 02 i weryfikuje checksum payloadu.
- Katalog: host `CIPHER\r` → `ACK\r`; tekst rekordu, dwa bajty sumy, CR → `ACK\r` lub `NAK\r`; `OVER\r` → `ACK\r`. Checksum to 16-bitowa suma bajtów tekstu, starszy bajt pierwszy; bajt równy 0D zastępuje się 0E.
- Nie jest używany obecny w assembly algorytm CRC16 ani cztery bajty poprzedzające READ w lokalnej tablicy. Wykonywane wywołanie WritePort wysyła tylko READ i CR.

Własne ZacDevice i ZacHost implementują wymagane operacje bez kopiowania kodu producenta. Obsługują fragmenty ramek, checksum, potwierdzenia, NAK, ograniczenia pamięci, timeout i anulowanie. Sprawdzenie połączenia `kod_testowy` nie zmienia katalogu, nie zeruje ilości i może odbywać się przy dokumencie gotowym do wysłania. Odbiór katalogu jest zatwierdzany dopiero po jego przyjęciu i zapisaniu sesji. Host zapisuje odebrany dokument przed wysłaniem końcowego Time.

Zebex.SuzBridge to niezależny proces konsolowy z tymi samymi argumentami i komunikatami zakończenia co narzędzia wywoływane przez DLL. Wprost otwiera podany COM i nie szuka USB. Publish self-contained dostarcza runtime; apphost jest kopiowany pod obie nazwy narzędzi. Instalator sprawdza SHA256 DLL i oryginałów, przechowuje kopie w zebex-original-tools, sprawdza manifest paczki i bieżącej instalacji, zapisuje dziennik do rollbacku i na czas zmiany zatrzymuje działającą usługę InsERTDevicesService. Dostępny jest przycisk przywracania. Oryginalna DLL i baza nexo pozostają bez zmian.

Walidacja 0.3: 30 + 19 + 23 + 20 = **92 testy** i kompilacja Windows. Ponadto dwa rzeczywiste porty PTY na Linux, połączone przekaźnikiem bajtów, z uruchomionym procesem mostu: status, dokument, błąd zapisu i ponowny odczyt oraz katalog. Plik faktycznie odczytany przez proces mostu został zaakceptowany przez oryginalny Documents.Load/Validate, z zachowaniem duplikatów i ilości 2 / 3.5. Testy tymczasowe: /tmp/zebex-serial-check.py i /tmp/zebex-driver-oracle; narzędzia producenta nie są dołączone do paczki ani repozytorium.

Końcowy test wymaga SUZ 19.2 i nexo na Windows: instalacja/przywrócenie mostu, odczyt dokumentu do nexo i wysłanie katalogu. Nie deklaruj pełnej zgodności ani testu Windows jako wykonanego przed tym sprawdzeniem.
