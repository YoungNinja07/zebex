# Zebex Simulator 0.2 — Windows x64

Samodzielny EXE do wypróbowania obsługi kolektora przed zakupem. Nie wymaga Pythona ani instalacji .NET. Wygląd i lokalne zachowanie oparto na dostarczonej instrukcji uproszczona_instrukcja_obslugi_z-9000_ekp.pdf: ekran LCD, żółty SCAN, klawiatura, menu EKP, tryby zbierania danych, produkty i dokumenty.

**Protokół kolektora dla SUZ nie jest zaimplementowany. Program nie przekazuje danych do SUZ ani Subiekta nexo i nie zastępuje urządzenia w ich sterowniku.** Instrukcja opisuje EKP i m.in. Subiekta GT + Sfera; nie zawiera formatu ramek, komend ani odpowiedzi do emulacji komunikacji. Projekt jest niezależny od ZEBEX, ELZAB i InsERT.

## Pierwsze uruchomienie

1. Rozpakuj ZIP i uruchom **ZebexSimulator.exe** na Windows 10/11 x64.
2. Na klawiaturze kolektora kliknij **1 → 1**, aby wejść do **Skanuj**.
3. Wpisz kod w polu po prawej i naciśnij **SCAN** lub Enter. Wpisz ilość i zatwierdź Enterem. Zera na początku kodu są zachowane.
4. **ESC** wraca do menu. **▲/▼** zmieniają wybór, **ENTER/SCAN** zatwierdzają. **ON/OFF** wyłącza ekran i włącza go ponownie bez kasowania danych.

Przyciski kolektora są aktywne. Cyfry wybierają opcje w menu albo wpisują dane. **BS** usuwa znak. **FN**, następnie 1–5, uruchamia skróty do skanowania, wysyłania, ładowania produktów, przeglądu i usuwania. Odpowiadają im F1–F5 komputera. **α** przełącza litery i znaki; powtarzanie klawisza cyklicznie wybiera znak. Ujemną ilość można wpisać klawiaturą komputera lub α+1 (znak „-”). Ctrl+R albo **Reset systemu** przywraca ustawienia domyślne bez kasowania dokumentów i produktów.

## Tryby z instrukcji EKP

| Opcja | Zachowanie lokalnego symulatora |
| --- | --- |
| Skanuj | Każdy odczyt to osobna pozycja, również gdy kod się powtarza. |
| Skanuj+ | Powtórzony kod sumuje ilości. Ilość ujemna koryguje sumę. Wiersz z sumą 0 pozostaje, zgodnie z opisem obsługi korekt po stronie PC. |
| Szybki skan | Odczyt zapisuje ilość 1 bez osobnego zatwierdzania ilości. |
| Dokumenty | Własna nazwa i wybór sklejania albo szybkiego skanowania. Istniejący dokument można kontynuować. |
| Dok.1 (PZ), Dok.2, Dok.3 | Dokumenty ze stałymi nazwami i własnymi pozycjami. Nie tworzą dokumentów księgowych w Subiekcie. |
| Ładuj produkty | Lokalny katalog CSV; nazwa, stan i cena są widoczne przy skanowaniu. Wczytanie/usunięcie produktów zeruje sumy, zachowując zwykłe odczyty. |
| Wyślij | Wybór dokumentu i stan oczekiwania na PC. Jedyny dokument jest wybierany od razu. Potwierdzenie odbioru jest wyłącznie lokalną symulacją. |
| Usuwanie | Usunięcie wybranego dokumentu, ostatniej pozycji albo produktów. |

Ustawienia: ręczne (domyślne), automatyczne lub wyłączone usuwanie po **symulowanym** potwierdzeniu odbioru oraz rodzaj informacji po skanie. **Prędkość EKP z instrukcji wynosi 115200** i jest domyślną wartością diagnostyki COM. Pozostałe parametry dopasuj do sterownika SUZ.

## Dane, katalog i eksport

Sesja zapisuje się po zmianach automatycznie w %LocalAppData%\\ZebexSimulator\\session.json i odtwarza przy uruchomieniu. **Zapisz sesję / Wczytaj sesję** służą do przenoszenia kopii JSON; obsługiwane są też pliki z wersji 0.1. Uszkodzony plik automatycznej sesji jest zachowywany i nie jest automatycznie nadpisywany.

Katalog CSV wymaga nagłówka **Kod;Nazwa;Stan;Cena**, kod jest tekstem, liczby mają kropkę dziesiętną. Dołączony plik **produkty.csv** zawiera:
 
    Kod;Nazwa;Stan;Cena
    5901234123457;Towar demonstracyjny;12;9.99
    000123;Kod z zerami;5;24.50

Przycisk **Przykładowy katalog** również wczytuje dane demonstracyjne. Katalog CSV jest lokalną symulacją, nie importem z SUZ. Zerowanie istniejących sum wymaga potwierdzenia.

Eksport CSV (średnik) / TSV (tabulator) zapisuje wybrany dokument jako UTF-8 z BOM, nagłówkiem Kod / Ilosc i ilościami z kropką dziesiętną. To ogólne formaty wymiany, **nie potwierdzone formaty importu SUZ/nexo**. Eksport sam nie kasuje odczytów i nie udaje transmisji.

## Automatyczne porty COM

1. Otwórz **Diagnostyka SUZ / COM** i kliknij **Utwórz parę COM**.
2. Zaakceptuj monit administratora. Konfigurator pobierze oficjalny podpisany pakiet com0com, sprawdzi SHA-256, w razie potrzeby zainstaluje sterownik i utworzy parę z wolnymi numerami przydzielonymi przez Windows.
3. Konfigurator testuje przesłanie bajtów w obie strony. Kliknij **Zamknij** w jego oknie.
4. Program pokaże port dla **SUZ** i wybierze drugi dla **symulatora**. W konfiguracji kolektora SUZ ustaw wskazany port. Dwie aplikacje nie mogą otwierać tego samego portu.
5. Dopasuj parametry COM (EKP: **115200**) i kliknij **Połącz** w symulatorze. Wywołaj komunikację w SUZ. **RX** pokazuje odebrane bajty; **TX** pozwala ręcznie wysłać HEX lub ASCII.

Pierwsza instalacja wymaga Internetu i administratora. Zgodność starego sterownika com0com z konkretną konfiguracją Windows 10/11 nie jest gwarantowana. Jeśli Windows odrzuci sterownik, aplikacja zatrzyma konfigurację i **nie wyłączy podpisów, Secure Boot ani innych zabezpieczeń**. Nie instaluje unsigned/test-signed wariantu i nie tworzy portów przez modyfikację samego rejestru.

Aplikacja zapamiętuje własną parę i sprawdza jej rzeczywistą konfigurację; przy ponowieniu testuje ją zamiast dodawać duplikat. Nie usuwa par innych programów. Przed ponownym testem zamknij SUZ lub inne programy używające własnej pary. Stan i raport konfiguracji: %ProgramData%\\ZebexSimulator. com0com można odinstalować w ustawieniach aplikacji Windows.

Źródło: [oryginalny projekt com0com](https://sourceforge.net/projects/com0com/files/com0com/3.0.0.0/). Pakiet: com0com-3.0.0.0-i386-and-x64-signed.zip. SHA-256: 6e5d4359865277430d4ae88c73fb7e648a0ed8e81aea5002478179cfcb0bb0e1. Sterownik jest projektem GPL; EXE pobiera oryginalny pakiet przez HTTPS. Sterownik nie jest dołączony do ZIP symulatora.

**Test pary potwierdza połączenie COM, nie zgodność z SUZ.** SUZ może zgłosić brak odpowiedzi, bo protokół kolektora nie jest obsługiwany. RX pokazuje porcje odczytu, nie ustalone ramki. Do dalszej integracji potrzeba specyfikacji protokołu albo wiarygodnego zapisu odpowiedzi urządzenia oraz wersji SUZ i nazwy sterownika. Same żądania SUZ mogą nie wystarczyć. Testuj na środowisku testowym Subiekta.

## Budowanie i walidacja

Na Windows z .NET SDK 8 uruchom build-windows.ps1. Wynik: artifacts/windows-x64/ZebexSimulator.exe, samodzielny EXE z dołączonym .NET. Sam symulator nie jest podpisany certyfikatem wydawcy.

Na Linux uruchom bash setup-cloud.sh. Skrypt instaluje SDK 8.0.425 z weryfikacją oficjalnego SHA-512, uruchamia testy i buduje Windows EXE. Osobno: dotnet run --project tests/Zebex.Checks -c Release.

Zweryfikowano 30 testów logiki danych, formatów, trybów EKP, korekt, dokumentów, trwałości sesji, katalogu, parsowania par COM i sumy kontrolnej pakietu. Kompilacja i integralność ZIP są sprawdzane osobno. **Interfejs Windows, instalacja sterownika, podwyższenie uprawnień i komunikacja z SUZ nie zostały wykonane w środowisku Linux.** Test transmisji pary uruchamia się dopiero na Windows.
