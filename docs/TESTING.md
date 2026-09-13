# Weryfikacja HealthBreak

## Polecenia

```powershell
./scripts/test.ps1
./scripts/run.ps1 -DataDir ./artifacts/manual-test-data
./scripts/publish.ps1
```

Testy Core sprawdzają logikę bez Windows API. Testy Data korzystają z tymczasowej bazy SQLite. Scenariusze interaktywne należy przeprowadzić na Windows 10 1809+ lub Windows 11 x64. Katalog `manual-test-data` oddziela dane sprawdzania od codziennej historii.

## Status bieżącej weryfikacji

| Sprawdzenie | Status | Uwagi |
| --- | --- | --- |
| Kompilacja pięciu plików usług Native w C# 12 | Wykonane poprawnie | Kompilator z SDK .NET 10, tymczasowy harness i reference pack .NET 10; nie jest to kompilacja całego WinUI |
| Przypisanie 7 kategorii procesów | Wykonane poprawnie | Browser, Development, Communication, Gaming, Entertainment, Work, Other |
| Próbka bez śledzenia aplikacji | Wykonane poprawnie | Pola procesu i kategorii są puste, czas idle nie jest ujemny |
| Utworzenie/aktualizacja/zwolnienie obiektu TrayIcon | Wykonane poprawnie w izolowanym środowisku | Dostępność rzeczywistego pulpitu i ikony była false; widoczna ikona i klikanie nie zostały w ten sposób zweryfikowane |
| Testy automatyczne Core i Data | Wykonane poprawnie | 27 testów Core i 17 testów SQLite; Release, Windows 10 19045 x64 |
| Pełna kompilacja i start WinUI | Wykonane poprawnie | Build bez ostrzeżeń; smoke test uruchomił monitoring, SQLite i wszystkie pięć widoków |
| Publikacja samodzielnego katalogu x64 | Wykonane poprawnie | Sprawdzono komplet XBF/PRI oraz start opublikowanego EXE z kodem wyjścia 0 |
| Interaktywne scenariusze poniżej | Do wykonania na pulpicie Windows | Sukces testów jednostkowych nie potwierdza zachowania GUI |

## Scenariusze funkcjonalne

| Scenariusz | Kroki | Oczekiwany wynik |
| --- | --- | --- |
| Pierwszy start | Uruchom z nowym katalogiem danych | Dashboard bez wyjątków; score 100; demo, autostart, strict i śledzenie aplikacji wyłączone |
| Mysz i klawiatura | Używaj obu urządzeń | Sesja i czas aktywny rosną bez zapisu klawiszy |
| Idle | Pozostaw komputer przez ponad 180 s, następnie wróć | Okres bezczynności nie pozostaje aktywną pracą; po powrocie zostaje rozpoznana odpowiednia przerwa |
| Krótkie odejście | Opuść stanowisko na mniej niż 120 s | Brak zaliczonej przerwy |
| Krótka przerwa naturalna | Opuść stanowisko na 2–5 min i wróć | Jedna przerwa; domyślna ulga 15 min w sesji, bez odejmowania od rzeczywistej sumy pracy |
| Pełna przerwa naturalna | Opuść stanowisko na ponad 5 min i wróć | Jedna pełna przerwa i zerowanie bieżącej sesji |
| Korekta czasu | Obserwuj licznik przed i po progu idle | Tymczasowo naliczony czas ciszy jest przeklasyfikowany do idle |
| Pauza | Tray → Pause monitoring; odczekaj; Resume monitoring | W pauzie nie przybywa czasu pracy; po wznowieniu pomiar działa |
| Blokada Windows | Win+L, odczekaj, zaloguj się | Zablokowany czas nie jest aktywnością |
| Uśpienie | Uśpij i wznow komputer | Luka w próbkowaniu nie jest dodana do aktywnej pracy |
| Cztery przypomnienia | Użyj demo, utrzymuj aktywność, pomijaj poprzednie progi | Progi 30/45/60/75, brak popupu co tick na tym samym progu |
| Odroczenie | Remind me in 5 minutes | Powrót po 5 min, w demo po około 5 s; jedno odroczenie w bazie |
| Pominięcie | Skip | Popup znika, pominięcie jest liczone, wynik score reaguje |
| Przerwa ręczna | Start break, sprawdź timer i ćwiczenia | Praca nie jest naliczana; ukończenie zeruje sesję i nie wywołuje od razu kolejnego popupu; zakończenie przed czasem nie udaje ukończonej przerwy ani odrzucenia przypomnienia |
| Długość rekomendacji | W Demo Mode przekrocz kolejno drugi i mocny próg | Domyślnie 30 min proponuje Quick 45 s, 45 min Short 2:30, a 75 min Full 5:00 |
| Odzyskiwalny Health Score | Dodaj wiele pominięć, następnie ukończ przerwę i ćwiczenia | Kary są ograniczone; ukończenie przerwy i ćwiczeń podnosi wynik także po słabym wyniku |
| Potwierdzenie ćwiczenia | Oznacz to samo ćwiczenie dwukrotnie | Jedno wykonanie na ćwiczenie w danej przerwie |
| Różnorodność ćwiczeń | Wykonaj kilka przerw | Zmiany ćwiczeń zgodne z historią i kategoriami; żadnego automatycznego wykrywania ruchu |
| Strict Mode | Włącz i przekrocz ostatni próg | Pełny ekran z ćwiczeniami i dostępnym Emergency skip |
| Wyjście awaryjne | Emergency skip; także sprawdź Alt+Tab i zamknięcie okna | Brak blokady Windows; emergency skip zapisany; aplikację można zakończyć |
| Walidacja ustawień | Wpisz malejące progi lub niepoprawną długość przerwy | Czytelny komunikat, brak zapisania niepoprawnego stanu |
| Trwałość ustawień | Zapisz ustawienia, zakończ i uruchom | Ustawienia wracają, Demo Mode pozostaje wyłączony |
| Oddzielenie demo | Utwórz aktywność normalną i demo | Statystyki są filtrowane przez `is_demo` |
| Kategorie procesów | Włącz tracking, aktywuj edytor i przeglądarkę, potem wyłącz | Włączona opcja pokazuje poprawną kategorię; wyłączona nie zbiera nowych danych aplikacji |
| Tray | Zamknij główne okno, otwórz menu i dashboard | Monitoring działa, tooltip i wszystkie pozycje menu są użyteczne |
| Pomoc i ikona | Otwórz Help; sprawdź okno, pasek zadań, tray oraz EXE | Help opisuje główne funkcje; wszędzie widoczna jest ikona HealthBreak |
| Restart Explorera | Na komputerze testowym uruchom ponownie Explorer | Ikona wraca po wiadomości TaskbarCreated |
| Autostart | Świadomie włącz/zapisz, potem wyłącz/zapisz | Powstaje i znika tylko wpis HKCU Run HealthBreak; skrypty same go nie zmieniają |
| Restart aplikacji | Exit, uruchom ponownie | Zachowana historia, zamknięte połączenie SQLite i brak porzuconej ikony |
| Awaria procesu | Na danych testowych zakończ proces, uruchom ponownie | Baza otwiera się, przerwana sesja kończy się na ostatnim zapisanym pomiarze |
| Uszkodzona baza | Na osobnych danych zastąp bazę nieprawidłowym plikiem i uruchom | Aplikacja startuje, zachowuje plik `.corrupt-*` i przywraca zweryfikowany `.backup` albo tworzy nową bazę |
| Zmiana dnia | Test zegara w Core; interaktywnie obserwuj przejście przez północ | Dzienne sumy i historia 7 dni nie przenoszą całej sesji na niewłaściwy dzień |
| Brak internetu | Uruchom wcześniej opublikowany katalog bez sieci | Dashboard, monitoring, ćwiczenia i zapis danych działają lokalnie |

## Kontrola prywatności i plików

Po zakończeniu aplikacji otwórz testową bazę narzędziem SQLite. Tabele mają zawierać czas, liczby, katalog ćwiczeń, flagi i opcjonalne sumy kategorii. Nie powinno być treści wpisanego tekstu, tytułów okien ani adresów URL. Nie modyfikuj bazy używanej właśnie przez działającą aplikację.

Przy testowaniu błędów używaj osobnego katalogu danych. Do wyniku testu dołącz wersję Windows, polecenie uruchomienia, tryb normalny/demo oraz treść wyjątku, jeśli wystąpił. Nie oznaczaj scenariusza jako zaliczonego wyłącznie na podstawie przeglądu kodu.
