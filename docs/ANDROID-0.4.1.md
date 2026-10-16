# Zebex Phone 0.4.1 — poprawka skanera

Poprzedni APK nie zawierał AndroidX Core. ZXing 4.3.0 wywołuje
ContextCompat.checkSelfPermission i ActivityCompat.requestPermissions przy
uruchamianiu aparatu, ale jego opublikowany POM nie deklaruje tej zależności.
Skutkiem był NoClassDefFoundError przy onResume ekranu CaptureActivity.

Dodano jawną zależność androidx.core:core:1.13.1. Test regresji uruchamia cały
ekran skanera do onResume i sprawdza prośbę o CAMERA na Androidzie 9 i 11.
Wcześniejszy test jedynie podawał wynik skanowania do aplikacji.

Zainstaluj nowy APK jako aktualizację istniejącej aplikacji — nie odinstalowuj
starej. Pakiet i podpis są te same; dane i parowanie pozostają na telefonie.
Na pytanie o dostęp do aparatu wybierz zezwolenie podczas używania aplikacji.
Program Windows 0.4.0 pozostaje zgodny. Wymagany Android 7 lub nowszy.

Testy JVM nie zastępują sprawdzenia fizycznego aparatu i uprawnień na urządzeniu.
