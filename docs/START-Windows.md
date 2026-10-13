# Zebex Simulator 0.3 — uruchomienie

1. Rozpakuj cały ZIP. Uruchom ZebexSimulator.exe. Folder suz-bridge musi być obok EXE.
2. W Diagnostyka SUZ / COM kliknij **Zainstaluj most SUZ** i zaakceptuj administratora. Zamknij okno po komunikacie o zakończeniu. Most zachowuje kopie oryginalnych narzędzi SUZ.
3. Masz już parę COM21 ↔ COM24: **symulator COM21, SUZ COM24**. Nie twórz następnej pary. Jeśli nie masz portów, użyj Utwórz parę COM i kieruj się pokazanymi numerami.
4. SUZ: **Elzab Kolektor Plus (Zebex)**, COM24, **115200**. Symulator: COM21, **115200 / 8 / None / One**, Handshake None, DTR/RTS wyłączone → **Połącz**.
5. W SUZ uruchom sprawdzenie połączenia. W logu pojawią się RX/TX, a na kolektorze „Test połączenia SUZ OK”. Test nie zmienia katalogu ani odczytów.

## Odczyty do nexo

Na kolektorze **1 → 1**, wpisz kod → SCAN → ilość → Enter. Wróć ESC do menu głównego → **2. Wyślij** → dokument. W nexo uruchom odbiór danych z kolektora przez SUZ. Po potwierdzeniu program zapyta o usunięcie dokumentu; Nie zachowa odczyty.

## Produkty z nexo

Przy otwartym porcie uruchom w nexo wysyłanie produktów do kolektora przez SUZ. Odbiór jest automatyczny. Pełny katalog jest zapisywany po sprawdzeniu; ilości dokumentów ze sklejaniem zostaną wyzerowane zgodnie z EKP. Zwykłe skany pozostają.

## Przywrócenie oryginałów

Przycisk **Przywróć oryginalne narzędzia SUZ** odtwarza kopie z zebex-original-tools i usuwa most. Użyj go przed podłączeniem prawdziwego Zebexa. Instalator wspiera dokładnie rozpoznany sterownik SUZ 19.2 i nie zmienia bazy nexo ani DLL modelowej.

## Co sprawdzono

92 testy, kompilację Windows, transmisję między portami szeregowymi z uruchomionym procesem mostu i zgodność pliku dokumentu z oryginalną DLL SUZ. SUZ/nexo i instalatora na Twoim Windows nie uruchomiono w środowisku chmurowym. Jeśli po instalacji wystąpi błąd, zapisz log przyciskiem Zapisz log oraz skopiuj komunikat SUZ.
