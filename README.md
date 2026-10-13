# Zebex Simulator 0.3 — Windows x64, EKP / SUZ 19.2

Samodzielny Windows EXE do sprawdzenia obsługi kolektora przed zakupem. Ekran, klawiatura i menu odtwarzają dostarczoną instrukcję EKP. Aplikacja i most SUZ zawierają .NET; nie wymagają Pythona ani instalowania .NET. Projekt jest niezależny od ZEBEX, ELZAB i InsERT.

Wersja 0.3 dodaje transmisję dokumentów i katalogu oraz test połączenia SUZ. **Wymaga instalacji dołączonego mostu SUZ.** Oryginalne narzędzia z instalacji użytkownika szukają fizycznego USB VID 24EA / PID 0300 przed otwarciem COM, także po podaniu numeru portu. Sam symulator i para COM nie przejdą tego sprawdzenia. Most zastępuje dwa narzędzia komunikacyjne własnymi programami pracującymi przez wskazany COM; zachowuje bibliotekę modelową SUZ.

## Uruchomienie i połączenie

1. Rozpakuj **cały ZIP** do zwykłego folderu. Zachowaj `suz-bridge` obok `ZebexSimulator.exe`. Uruchom EXE na Windows 10/11 x64.
2. Otwórz **Diagnostyka SUZ / COM → Zainstaluj most SUZ**. Zaakceptuj administratora. Instalator sprawdzi sterownik SUZ 19.2, wykona kopie oryginałów i na czas zmiany zatrzyma oraz ponownie uruchomi działającą usługę SUZ. Zamknij jego okno po zakończeniu.
3. Jeśli masz już działającą parę **COM21 ↔ COM24**, wykorzystaj ją: w symulatorze **COM21**, w SUZ **COM24**. W przeciwnym razie kliknij **Utwórz parę COM**. Dwie aplikacje nie mogą otwierać tego samego portu.
4. W SUZ wybierz **Elzab Kolektor Plus (Zebex)**, właściwy port i **115200**. W symulatorze ustaw **115200 / 8 / None / One**, Handshake **None**, DTR i RTS wyłączone, następnie **Połącz**.
5. Uruchom sprawdzenie połączenia w SUZ. Symulator przyjmie `kod_testowy` i potwierdzi go bez zastępowania katalogu ani zerowania ilości.

## Dokument do Subiekta nexo

1. Na kolektorze wybierz **1 → 1 (Skanuj)**. Wpisz kod po prawej → **SCAN** → ilość → **Enter**. Powtarzający się kod pozostaje osobną pozycją. Skanuj+ oraz Szybki skan są również dostępne.
2. Wróć **ESC** do menu głównego, wybierz **2. Wyślij**, a następnie dokument. Kolektor pokaże oczekiwanie na PC.
3. W nexo uruchom odbiór danych z wybranego kolektora przez SUZ. Symulator wysyła zamrożony dokument i czeka na potwierdzenia rekordów oraz końcowe potwierdzenie odbioru.
4. Domyślnie program zapyta o usunięcie dokumentu dopiero po odbiorze. **Nie** zachowa dane. Automatyczne usuwanie oraz brak usuwania wybiera się w ustawieniach kolektora. Błąd i anulowanie nie kasują dokumentu.

Przy wielu dokumentach przekazywany jest wybrany dokument. Dokumenty Doc.1 / Doc.2 / Doc.3 nie tworzą samoczynnie dokumentów księgowych w nexo; docelową operację wybiera się w Subiekcie.

## Produkty z nexo do kolektora

Przy otwartym porcie uruchom wysyłanie produktów do kolektora w nexo przez SUZ. Odbiór rozpoczyna się automatycznie. Można także wybrać **3. Inne → Ładuj produkty**. Katalog jest przyjmowany po sprawdzeniu wszystkich rekordów i zapisaniu sesji. Jak w instrukcji EKP, wczytanie katalogu zeruje ilości w dokumentach ze sklejaniem; zwykłe skany pozostają.

Próbny katalog sprawdzania połączenia nie zmienia danych. Nowy katalog nie jest przyjmowany w trakcie wysyłania dokumentu. Jeśli zmienisz odczyty podczas odbioru lub wystąpi błąd, stare dane zostaną zachowane.

## Obsługa lokalna

**Skanuj** zachowuje osobne pozycje. **Skanuj+** sumuje kody, pozwala na ujemne korekty i pozostawia sumę zero. **Szybki skan** ma ilość 1. **Dokumenty** pozwalają nazwać i kontynuować odczyty. **FN**, następnie 1–5, uruchamia skróty; na komputerze F1–F5. **α** przełącza litery i znaki. **BS** kasuje znak, **ESC** wraca, **ON/OFF** zachowuje dane po wyłączeniu ekranu.

Sesja zapisuje się w `%LocalAppData%\ZebexSimulator\session.json`; JSON można także zapisać i wczytać ręcznie. Sesje wersji 0.1/0.2 pozostają obsługiwane. Uszkodzona automatyczna sesja nie jest nadpisywana. Lokalny katalog CSV używa nagłówka `Kod;Nazwa;Stan;Cena` i kropki dziesiętnej; przykład `produkty.csv` jest w paczce. Eksport CSV/TSV jest pomocniczy i nie stanowi transmisji SUZ.

Format odczytu biblioteki SUZ używa ASCII i interpretuje cudzysłowy. W nazwach wysyłanych dokumentów i kodach używaj ASCII bez cudzysłowów; program zgłosi niedozwolone dane przed transmisją. Sterownik SUZ zamienia polskie znaki w nazwach wysyłanych produktów przed przekazaniem katalogu.

## Most SUZ i przywracanie

Most instaluje się tylko dla dokładnie rozpoznanej DLL i narzędzi SUZ 19.2. Nie modyfikuje DLL InsERT, bazy Subiekta ani podpisów oryginalnych plików. Zastępuje `zac_dataReader.exe` i `zac_dlookup.exe` własnymi programami i dodaje runtime do `Binaries\Elzab\KolektorPlusZebex`. Kopie oryginałów są w `zebex-original-tools`. Manifest SHA-256 chroni przed nadpisywaniem plików zmienionych przez aktualizację SUZ.

**Przywróć oryginalne narzędzia SUZ** odtwarza kopie i usuwa pliki mostu. Użyj tego przed podłączeniem fizycznego kolektora. Nie uruchamiaj instalacji mostu i aktualizacji SUZ równocześnie. Inna wersja plików albo niestandardowy katalog sterowników powoduje zatrzymanie instalatora bez zmiany narzędzi.

Konfigurator COM pobiera podpisany com0com z [SourceForge](https://sourceforge.net/projects/com0com/files/com0com/3.0.0.0/) i sprawdza przypięty SHA-256. Reużywa własną parę, testuje oba kierunki i nie usuwa par innych programów. Nie wyłącza podpisów, Secure Boot ani innych zabezpieczeń. Pierwsza instalacja portów wymaga Internetu i administratora; nie jest potrzebna, jeśli para już działa.

## Budowanie i walidacja

Windows: `build-windows.ps1` z SDK .NET 8. Linux: `bash setup-cloud.sh`, który przygotowuje SDK 8.0.425, wykonuje cztery zestawy testów i buduje EXE oraz `suz-bridge` z manifestem.

Walidacja 0.3: **92 testy** logiki, bezpieczeństwa, formatów i protokołu; kompilacja Windows; rzeczywisty test dwóch portów szeregowych PTY na Linux z uruchomionym procesem mostu. Sprawdzono status bez zmiany danych, odczyt dokumentu, przyjęcie katalogu, błąd zapisu pliku i zachowanie odczytów. Plik odczytany przez most oraz format katalogu sprawdzono z oryginalną DLL użytkownika.

**SUZ 19.2 i Subiekt nexo nie zostały uruchomione na Windows w tym środowisku.** Test PTY sprawdza transport i procesy, ale nie zastępuje końcowego testu w nexo. Instalacja mostu, restart usługi i GUI wymagają sprawdzenia na komputerze użytkownika. Szczegóły: [docs/SUZ-integration.md](docs/SUZ-integration.md).
