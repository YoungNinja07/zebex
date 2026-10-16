# Windows 0.4.3 — uruchamianie Wi-Fi bez blokowania okna

Wersja 0.4.2 wykonywała blokujące GetAwaiter().GetResult() na wątku okna,
a pętla serwera przechwytywała SynchronizationContext tego samego okna.
Test TLS nie mógł zakończyć się, dopóki okno nie wznowiło pracy; obsługa
błędu przy zamykaniu serwera mogła blokować się bezterminowo.

W 0.4.3 start i test TLS są oczekiwane asynchronicznie. Pętla serwera
uruchamia się w puli wątków. Generowanie i import klucza również odbywa
się poza wątkiem okna. Podczas uruchamiania widoczny jest status, a przycisk
jest wyłączony, by nie uruchomić drugiego serwera równocześnie.

Test regresji uruchamia serwer przy ustawionym SynchronizationContext
imitującym okno i sprawdza, że handshake TLS nie wysyła do niego kontynuacji.

Zamknij poprzednią wersję (jeśli nie reaguje: Menedżer zadań -> ZebexSimulator
-> Zakończ zadanie). Rozpakuj CAŁY nowy ZIP i uruchom EXE z tego folderu.
W razie zmiany folderu ponów regułę zapory przyciskiem Zezwól na Wi-Fi.
Zachowaj aplikację Android 0.4.2, dane i dotychczasowe parowanie.

Testy w środowisku Linux nie zastępują uruchomienia interfejsu na Windows.
