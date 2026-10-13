# Zebex Simulator 0.1 — Windows x64

Pobierz plik **ZebexSimulator-Windows-x64.zip** z tej gałęzi (przycisk pobierania pliku / Download raw file na stronie pliku). Rozpakuj ZIP i uruchom **ZebexSimulator.exe** na Windows 10/11 x64.

Program zawiera środowisko .NET; nie wymaga Pythona ani instalacji .NET. Paczka zawiera EXE oraz instrukcję. Sumę kontrolną ZIP znajdziesz w SHA256SUMS.txt.

Funkcje: wprowadzanie kodów i ilości, sumowanie powtórzonych kodów, zapis i odczyt sesji JSON, eksport CSV/TSV, diagnostyka komunikacji przez COM.

**To prototyp: nie implementuje jeszcze protokołu Zebex Z-9000 i nie ma potwierdzonej zgodności z SUZ ani Subiektem nexo.** Eksport CSV/TSV jest ogólnym formatem wymiany. Diagnostyka COM pozwala odbierać bajty oraz ręcznie wysyłać dane; program nie odpowiada automatycznie jak urządzenie.

Logika danych: 8 testów zakończonych powodzeniem. Pakiet Windows został skompilowany i archiwum ZIP sprawdzone. Interfejs Windows oraz komunikacja z SUZ wymagają testu na Windows. EXE nie jest podpisany certyfikatem wydawcy. Niezależny projekt, nie produkt ZEBEX ani InsERT.
