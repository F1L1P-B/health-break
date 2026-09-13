# Demo hackathonowe: 3–5 minut

## Przygotowanie

1. Na Windows x64 wykonaj `./scripts/run.ps1 -DataDir ./artifacts/demo-data` albo uruchom opublikowany EXE z `--data-dir` wskazującym osobny katalog prezentacyjny.
2. Otwórz dashboard i ustawienia. Pokaż, że śledzenie aplikacji, Strict Mode i Demo Mode są opcjonalne. Pozostaw standardowe progi 30/45/60/75 minut i idle 180 sekund.
3. Włącz Demo Mode w aplikacji. Potwierdź widoczne oznaczenie **DEMO ×60**. Tryb dotyczy aktualnego uruchomienia; nie pozostaje włączony po ponownym starcie.

Demo skaluje rzeczywisty czas przez 60; nie generuje sztucznych zdarzeń wejścia. Przy narastaniu sesji poruszaj myszą lub korzystaj z klawiatury nie rzadziej niż co 1–2 sekundy. Trzy sekundy bez wejścia oznaczają już próg idle. Jeżeli wystąpi naturalna przerwa, moment kolejnego przypomnienia może się zmienić przez ulgę w liczniku sesji.

## Scenariusz

| Etap | Co zrobić | Co pokazać |
| --- | --- | --- |
| Start | Utrzymuj aktywność wejścia | Rosnące Current session i Active time today; wyraźny tryb demo |
| Około 30 s | Poczekaj na pierwszy próg aktywnej sesji | Delikatne przypomnienie i propozycja Quick break |
| Odroczenie | Wybierz Remind me in 5 minutes; nadal ruszaj myszą | Powrót przypomnienia po około 5 rzeczywistych sekundach; spadek score za odroczenie |
| Przerwa | Wybierz Start break | Lista ćwiczeń, kategorie, opis i własny przycisk potwierdzenia |
| Potwierdzenie | Ręcznie oznacz wykonane ćwiczenie i zakończ przerwę po upływie timera | Zwiększenie Completed exercises i Breaks today; ulga w bieżącej sesji |
| Naturalny odpoczynek | Nie dotykaj myszy i klawiatury przez 7–8 s, następnie wróć | Idle bez naliczania pracy; po powrocie pełna przerwa i reset sesji |
| Statystyki | Otwórz Statistics | Dzienne sumy i widok 7 dni dla danych demo |
| Zasobnik | Zamknij główne okno i otwórz aplikację z tray | Ciągłe działanie, pauza/wznowienie, Start break i Exit |

Ręczne przerwy również są przyspieszone: Quick 45 s trwa około 0,75 s, Short 150 s około 2,5 s, Full 300 s około 5 s. Okno ćwiczeń pozostaje do obsłużenia przez użytkownika. Bardzo krótki timer nie oznacza automatycznego zaliczenia ćwiczeń; należy je potwierdzić ręcznie.

## Prezentacja dłuższej sesji i Strict Mode

Utrzymując aktywność można zobaczyć kolejne progi po około 45, 60 i 75 sekundach ciągłej pracy. Wcześniejsze przerwy zmniejszają licznik, więc te czasy liczy się od aktualnej wartości sesji. Aby pokazać samo przechodzenie progów, użyj Skip na wcześniejszych przypomnieniach.

Włącz Strict Mode w ustawieniach i zapisz. Przy ostatnim progu pojawi się pełnoekranowy ekran **Health break required**. Pokaż ćwiczenia i **Emergency skip**, a następnie wzrost licznika pominięć. Dostęp do Alt+Tab, skrótów Windows i Menedżera zadań pozostaje normalny.

## Zakończenie

Wyłącz Demo Mode, pokaż normalne statystyki i zakończ aplikację przez tray → Exit. Dane prezentacyjne są oznaczone `is_demo=1` i nie wchodzą do statystyk normalnego użytkowania. Następne uruchomienie zawsze zaczyna z wyłączonym demo.

Do prezentacji prawdziwej długości ćwiczeń wyłącz przyspieszenie i uruchom przerwę ręcznie. Nie trzeba czekać 30 minut na przypomnienie, aby pokazać ćwiczenia.
