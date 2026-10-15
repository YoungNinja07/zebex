# Zebex Phone 0.4 — telefon zamiast kolektora

Potrzebujesz Androida 7.0 lub nowszego, komputera Windows z SUZ 19.2 i Subiektem nexo oraz wspólnego Wi-Fi. Telefon skanuje aparatem lub przyjmuje kod z klawiatury. Dane są przekazywane przez program Windows i most SUZ. Komputer musi pozostać włączony podczas przekazywania odczytów do nexo.

## Komputer — pierwsza konfiguracja

1. Zamknij stary symulator, rozpakuj cały ZIP wersji 0.4 i uruchom ZebexSimulator.exe. Zachowaj folder suz-bridge obok EXE.
2. W **Diagnostyka SUZ / COM** zainstaluj most SUZ, jeśli nie był jeszcze zainstalowany. W razie aktualizacji mostu program sprawdzi dotychczasowe pliki i zachowa oryginały.
3. Wykorzystaj istniejącą parę: **symulator COM21**, **SUZ COM24**, prędkość **115200**, 8 / None / One, Handshake None. Kliknij **Połącz**. W SUZ pozostaje model **Elzab Kolektor Plus (Zebex)**.
4. Otwórz **Telefon Android / Wi-Fi → Zezwól na Wi-Fi w Windows**, zaakceptuj administratora i zamknij okno po zakończeniu. Reguła zapory dopuszcza tylko lokalną podsieć, port TCP 18790 i ten EXE, w sieci Prywatnej lub Domenowej. Jeśli sieć Windows ma profil Publiczny, ustaw profil Prywatny dla swojej zaufanej sieci Wi-Fi.
5. Kliknij **Włącz telefony przez Wi-Fi**. Wybierz adres komputera należący do wspólnego Wi-Fi, jeśli komputer ma kilka adresów. Pojawi się QR.

## Telefon — instalacja i parowanie

1. Pobierz i zainstaluj **Zebex-Phone.apk**. Android może poprosić o zezwolenie na instalowanie aplikacji z przeglądarki lub aplikacji Pliki.
2. Otwórz **Zebex Phone → Połącz** i zezwól na aparat.
3. Zeskanuj QR z okna programu Windows. Kod jest ważny pięć minut; **Nowy kod QR** odświeża go. Sparowania nie trzeba powtarzać przy każdym uruchomieniu.
4. Połączenie jest szyfrowane. Telefon sprawdza odcisk certyfikatu z QR. Nie trzeba instalować certyfikatu w systemie telefonu ani udostępniać hasła do bazy nexo.

## Skanowanie i wysyłanie

- **SKANUJ APARATEM** odczytuje kod; w zwykłym trybie podaj ilość i kliknij Zapisz. Możesz również wpisać kod ręcznie i użyć Dodaj. Zera na początku kodu są zachowane.
- Domyślny tryb sumuje powtarzające się kody. **Nowy** pozwala wybrać osobne pozycje lub szybkie skanowanie z ilością 1. Korekty ujemne są dostępne przy sumowaniu.
- Dane zapisują się na telefonie po każdym skanie, również bez Wi-Fi. **Cofnij odczyt** usuwa ostatni odczyt z niezakończonego dokumentu.
- **Wyślij / ponów** zamyka dokument do edycji i przekazuje go komputerowi. Jeśli połączenie się urwie, dokument zostaje na telefonie. Ponów wysyłanie tego samego dokumentu — identyfikator i zawartość są stałe, a komputer nie tworzy drugiej kopii.
- Kolejne skany zapisuj przez **Nowy**. **Dokumenty** otwiera wcześniejsze dane. Telefon nie kasuje odczytów po wysłaniu.
- W nexo uruchom odbiór danych z kolektora przez SUZ. Program Windows automatycznie udostępnia najstarszy oczekujący dokument telefonu. Przy kilku telefonach dokumenty są kolejkowane i odbierane kolejno.

## Znaczenie statusów

| Status | Znaczenie |
| --- | --- |
| Szkic / skanowanie offline | Odczyty są zapisane lokalnie na telefonie. |
| Dokument zamknięty do wysyłki | Wysyłka mogła się nie zakończyć; możesz ją ponowić. |
| Przyjęte na komputerze | Komputer trwale zapisał dokument. W nexo uruchom odbiór z kolektora. |
| Odebrane przez SUZ | Most potwierdził odebranie odczytów. Sprawdź wynik importu i docelowy dokument w nexo. |
| Usunięte na komputerze | Dokument usunięto z kolejki Windows. Kopia odczytów pozostaje na telefonie. |

Status „odebrane przez SUZ” nie oznacza automatycznego zatwierdzenia PZ/WZ ani dokumentu księgowego. Docelową operację wybiera się w nexo, tak jak przy kolektorze.

## Nazwy, katalog i historia

Format oryginalnego sterownika wymaga kodów i nazw dokumentów w ASCII, bez cudzysłowów. Domyślna nazwa zawiera datę; własna może mieć do 40 znaków. Dokument mieści do 10000 odczytów. Widok pokazuje ostatnie 200 pozycji, ale przechowuje i wysyła wszystkie.

Opcja **Produkty** pobiera katalog z programu Windows. Najpierw wyślij produkty z nexo przez SUZ do programu Windows, następnie pobierz je na telefon. Nazwa, stan i cena pomagają rozpoznać zeskanowany kod. Niepełny lub przerwany katalog nie zastępuje dotychczasowego. Pobranie katalogu nie kasuje ani nie zeruje odczytów z telefonu.

Telefony są parowane osobno, maksymalnie 32. Każdy może sprawdzać własne dokumenty. **Rozłącz sparowane telefony** unieważnia ich dostęp; ponowne parowanie wymaga QR. Przycisk **Zatrzymaj połączenie telefonu** zamyka serwer Wi-Fi.

Aktualizuj APK podpisanym wydaniem bez odinstalowywania aplikacji — aktualizacja zachowuje bazę. Odinstalowanie lub wyczyszczenie danych Androida usuwa lokalne skany i parowanie. Aplikacja nie wysyła kopii do usług chmurowych. Sesja Windows jest zapisywana w %LocalAppData%\ZebexSimulator\session.json; wersja 0.4 zachowuje w niej również historię przyjętych dokumentów telefonu.

## Sprawdzenie wersji

Wykonano 118 testów .NET oraz 10 testów Androida w Robolectric, w tym uruchomienie ekranu, SQLite, dokładne ilości i cały przepływ: kod aplikacji Android → szyfrowane HTTPS → serwer połączenia telefonu (.NET) na Linux → rzeczywiste porty PTY → proces mostu SUZ → plik dokumentu i potwierdzenie w aplikacji. Zbudowano podpisany APK oraz Windows EXE; Android lint nie wykazał błędów.

Nie uruchomiono aparatu na fizycznym telefonie ani SUZ/nexo oraz instalatora na Windows w środowisku chmurowym. Te elementy wymagają końcowego sprawdzenia na Twoich urządzeniach. Przed przyjęciem danych do nexo sprawdź kody i ilości, jak przy pierwszej konfiguracji kolektora.
