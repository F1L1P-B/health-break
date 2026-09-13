# HealthBreak

Lokalna aplikacja desktopowa dla Windows, która pomaga regularnie odchodzić od komputera. Mierzy aktywność myszy i klawiatury przez czas ostatniego zdarzenia wejścia, rozpoznaje bezczynność, proponuje przerwy i prowadzi użytkownika przez proste ćwiczenia.

Projekt realizuje wskazany stos **C# 12, .NET 8, WinUI 3 i Windows App SDK**. Wymagania dotyczące Pythona, Qt, QSS i signals/slots zostały potraktowane jako pozostałości alternatywnej specyfikacji: aplikacja używa XAML, MVVM i komunikacji z wątkiem interfejsu przez `DispatcherQueue`. Nie wymaga Pythona.

## Co zawiera

- Dashboard z czasem aktywności, bieżącą sesją, czasem od przerwy, liczbą przerw, średnią i najdłuższą sesją, pominięciami oraz wykonanymi ćwiczeniami.
- Cztery poziomy przypomnień: domyślnie po 30, 45, 60 i 75 minutach, z rozpoczęciem przerwy, odroczeniem o 5 minut i pominięciem.
- Przerwy Quick, Short i Full, katalog ponad 20 ćwiczeń oczu, karku, ramion, pleców i ruchowych oraz ręczne potwierdzanie wykonania.
- Regułowy dobór ćwiczeń z uwzględnieniem długości pracy, ostatnich propozycji, pominięć i różnorodności kategorii.
- Statistics z podsumowaniem dnia i historią 7 dni oraz lokalny zapis SQLite.
- Ustawienia progów, powiadomień, dźwięku, autostartu, śledzenia kategorii aplikacji i Strict Mode.
- Ikonę w zasobniku systemowym z otwieraniem dashboardu, rozpoczęciem przerwy, pauzą/wznowieniem monitoringu, ustawieniami i wyjściem.
- Wyraźnie oznaczony, opcjonalny **Demo Mode ×60** do prezentacji hackathonowej.

## Wymagania

- Windows 10 w wersji 1809 lub nowszej, albo Windows 11; architektura x64.
- Do zbudowania: .NET SDK 8 lub nowszy i PowerShell. Visual Studio nie jest wymagane do uruchomienia skryptów; do pracy w IDE można użyć Visual Studio z obsługą WinUI.
- Dostęp do NuGet podczas pierwszego przywrócenia zależności. Po zbudowaniu aplikacja działa lokalnie bez usług chmurowych, kluczy API, konta i połączenia z internetem.

## Instalacja i uruchomienie ze źródeł

Otwórz PowerShell w katalogu projektu:

```powershell
dotnet --info
./scripts/run.ps1
```

Skrypt przywraca zależności, buduje wersję x64 i uruchamia `HealthBreak.App.exe`. Nie dodaje autostartu i nie instaluje usługi. Pliki roboczej kompilacji trafiają do standardowego katalogu `src/HealthBreak.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64`. Zachowanie standardowej struktury katalogów jest wymagane przez zasoby XAML WinUI.

Przykład uruchomienia z osobnym katalogiem danych do prezentacji:

```powershell
./scripts/run.ps1 -DataDir ./artifacts/demo-data
```

Opcje skryptu: `-Configuration Release`, `-Background`, `-NoRestore`. Opcja `-NoRestore` wymaga wcześniejszego poprawnego pobrania zależności.

Jeżeli lokalna polityka PowerShell blokuje uruchamianie skryptu, można wykonać pojedynczy skrypt bez trwałej zmiany polityki systemu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/run.ps1
```

## Wersja do przekazania użytkownikowi

```powershell
./scripts/publish.ps1
```

Gotowy katalog to `artifacts/HealthBreak-win-x64`. Zawiera aplikację, runtime .NET i Windows App SDK. Przekaż **cały katalog**, a następnie uruchom `HealthBreak.App.exe`. Sam plik EXE nie wystarcza. Na docelowym komputerze nie jest potrzebny SDK. To wersja przenośna, bez instalatora i bez podpisu komercyjnego.

Argumenty aplikacji:

```powershell
./HealthBreak.App.exe --data-dir "D:/HealthBreakData"
./HealthBreak.App.exe --background
```

`--background` uruchamia aplikację w zasobniku. `--data-dir` wskazuje katalog lokalnej bazy zamiast domyślnego. W zwykłym trybie zamknięcie głównego okna pozostawia monitoring w zasobniku; **Exit** kończy aplikację. Jeżeli ikona zasobnika nie jest dostępna, aplikacja powinna pozostać dostępna przez okno.

## Jak liczony jest czas

`GetLastInputInfo` informuje, kiedy wystąpiło ostatnie zdarzenie myszy lub klawiatury. Aplikacja nie przechwytuje poszczególnych klawiszy. To pomiar aktywności wejścia: samo oglądanie filmu lub czytanie bez dotykania urządzeń może przejść w bezczynność.

Domyślny próg idle wynosi 180 sekund. Czas bez ruchu przed rozstrzygnięciem jest tymczasowy; po wykryciu dłuższej bezczynności zostaje skorygowany do czasu idle. Dzięki temu trzy minuty oczekiwania na próg nie pozostają sztucznie zaliczone do pracy. Blokada pulpitu, odłączona sesja lub przerwa w próbkowaniu nie dodają czasu pracy.

Naturalna przerwa jest zapisywana po wznowieniu aktywności:

| Bezczynność | Domyślna interpretacja |
| --- | --- |
| Mniej niż 2 minuty | Nie jest przerwą |
| Od 2 do 5 minut włącznie | Krótka przerwa |
| Ponad 5 minut | Pełna przerwa |

Minimalna długość przerwy i próg idle są niezależnymi ustawieniami. Każda ukończona przerwa prowadzona (Quick, Short albo Full) zeruje licznik bieżącej sesji i rozpoczyna nowy cykl przypomnień. Naturalna krótka przerwa wykryta przez idle domyślnie zmniejsza licznik ciągłej pracy o 15 minut; można zmienić ulgę lub wybrać pełne zerowanie. Korekta nie odejmuje rzeczywiście przepracowanego czasu od sumy dziennej.

| Przerwa prowadzona | Czas | Domyślna propozycja |
| --- | --- | --- |
| Quick | 45 sekund | Od pierwszego progu do drugiego, domyślnie 30–44 min |
| Short | 2 minuty 30 sekund | Od drugiego do mocnego progu, domyślnie 45–74 min |
| Full | 5 minut | Od mocnego progu, domyślnie od 75 min |

Przerwa nie jest zaliczana jako ukończona przed upływem jej czasu. Potwierdzenie ćwiczenia jest dobrowolnym oświadczeniem użytkownika, niezależnym od timera.

## Ćwiczenia i Health Score

Katalog znajduje się w `src/HealthBreak.Core/Data/exercises.json`. Każdy wpis zawiera identyfikator, nazwę, kategorię, opis, sugerowany czas oraz opcjonalne powtórzenia. Plik jest osadzony w aplikacji.

Selektor jest deterministyczny. Zaczyna od 100 punktów na ćwiczenie, premiuje niewykorzystane lub dawno proponowane pozycje i kategorię dopasowaną do sesji, a odejmuje punkty za niedawne propozycje, pomijanie, duplikowanie kategorii oraz ruch w ostatnich trzech przerwach. Po 45 minutach preferuje oczy; od 60 minut uwzględnia oczy i ruch; od 90 minut dodaje kark albo ramiona. Remisy rozstrzyga identyfikator ćwiczenia. Nie używa ML ani AI.

Health Score jest **wskaźnikiem nawyków**, obliczanym dla wybranego dnia:

```text
L = max(najdłuższa zapisana sesja, aktualna sesja), w minutach
score = 100
        - min(35, 0,4 × max(0, L - 45))
        - min(40, 3 × pominięte przypomnienia)
        - min(12, 1,5 × odroczenia)
        - min(16, 4 × emergency skip)
        + min(20, 4 × ukończone przerwy)
        + min(20, 2 × wykonane ćwiczenia)
```

Wynik jest ograniczony do 0–100 i zaokrąglony. Kary mają limity, dzięki czemu pojedynczy zły dzień nie zamyka drogi do poprawy wyniku. Każda ukończona przerwa i ćwiczenie daje widoczny bonus; ręczne otwarcie i zamknięcie podglądu przerwy nie jest liczone jako odrzucone przypomnienie. Emergency skip jest także pominięciem i otrzymuje dodatkową karę. Statusy: **Excellent** 80–100, **Good** 60–79, **Needs attention** 40–59 i **Poor** 0–39. Długie sesje nie są diagnozą; wskaźnik nie ocenia stanu zdrowia.

## Strict Mode i autostart

Strict Mode jest domyślnie wyłączony. Po ostatnim progu przypomnienia pokazuje pełnoekranową propozycję wymaganej przerwy z ćwiczeniami. Przycisk **Emergency skip** pozostaje dostępny i zapisuje zdarzenie w statystykach. Program nie blokuje skrótów Windows, Alt+Tab ani Menedżera zadań i nie instaluje hooków klawiatury. Można go bezpiecznie zamknąć.

Autostart jest domyślnie wyłączony. Zapisanie odpowiedniej opcji dodaje wyłącznie wpis bieżącego użytkownika `HKCU/Software/Microsoft/Windows/CurrentVersion/Run/HealthBreak`, wskazujący bieżący EXE z argumentem `--background`. Wyłączenie usuwa ten wpis. Po przeniesieniu aplikacji należy ponownie zapisać autostart z nowej lokalizacji.

## Demo Mode

Włącz **Demo Mode** w aplikacji. Jedna rzeczywista sekunda odpowiada minucie czasu aplikacji. Pierwsze przypomnienie pojawia się po około 30 sekundach aktywnej pracy, a próg 60 minut po około minucie.

Demo skaluje także idle i czas przerw. Przy domyślnych ustawieniach 3 sekundy bez wejścia oznaczają 3 minuty idle, więc podczas prezentowania narastającej sesji poruszaj myszą lub korzystaj z klawiatury co najwyżej co 1–2 sekundy. Demo jest zawsze widocznie oznaczone, nie włącza się domyślnie i nie jest zapisywane jako preferencja na kolejne uruchomienie. Dane demo mają `is_demo=1` i są oddzielone w statystykach od normalnej pracy. Szczegółowy scenariusz: [docs/DEMO.md](docs/DEMO.md).

## Architektura

```text
HealthBreak.sln
src/
  HealthBreak.Core/
    Models/                  modele i walidacja ustawień
    Services/                SessionManager, BreakManager, ExerciseSelector, HealthScore
    Data/exercises.json      osadzony katalog ćwiczeń
  HealthBreak.Data/
    DatabaseSchema.cs        schemat i wersjonowanie SQLite
    HealthRepository.cs      transakcje, statystyki, ustawienia i historia
  HealthBreak.App/
    Services/Native/          Win32 input, proces, tray, autostart, okno i dźwięk
    Assets/HealthBreak.ico   ikona okna, paska zadań, tray i pliku EXE
    Resources/Theme.xaml     kolory, typografia i style
    MainWindow.xaml          powłoka interfejsu
    App.xaml                 zasoby i uruchomienie WinUI
tests/
  HealthBreak.Core.Tests/     deterministyczne scenariusze czasu i reguł
  HealthBreak.Data.Tests/     baza, agregacje i trwałość danych
scripts/                     uruchomienie, publikacja, testy
docs/                        demonstracja i lista weryfikacji Windows
```

Warstwa Core nie zależy od WinUI ani Windows API. Monitoring i operacje bazy wykonuje koordynator w tle; interfejs otrzymuje stan przez kolejkę UI. Zamykanie kończy pracę w tle, zapisuje zmiany, zwalnia SQLite oraz usuwa ikonę i subclass okna. Native tray korzysta z `Shell_NotifyIcon` i odtwarza ikonę po ponownym uruchomieniu Explorera. Ekran **Help** w bocznej nawigacji opisuje pomiar czasu, przypomnienia, rodzaje przerw, Health Score, Demo Mode i zasady prywatności.

Technologie: C# 12, .NET 8, WinUI 3 / Windows App SDK, XAML, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, SQLite i Win32 P/Invoke. Wykres tygodniowy jest rysowany przez interfejs XAML; QtCharts i matplotlib nie są potrzebne.

## Dane i prywatność

Domyślna baza: `%LOCALAPPDATA%/HealthBreak/healthbreak.db`. SQLite przechowuje sesje, przerwy, ćwiczenia, potwierdzenia, działania wobec przypomnień i ustawienia. Baza używa transakcji, parametrów SQL, ograniczeń, kluczy obcych, trybu WAL i pełnej synchronizacji zapisu. Po nieoczekiwanym zamknięciu otwarte sesje kończą się na ostatnim zapisanym punkcie pomiarowym.

Program regularnie tworzy zweryfikowaną kopię `healthbreak.db.backup`. Przy każdym starcie sprawdza integralność bazy i relacje kluczy obcych. Jeżeli baza jest uszkodzona, HealthBreak zachowuje ją jako `healthbreak.db.corrupt-DATA`, przywraca ostatnią prawidłową kopię, a gdy kopii nie da się użyć — tworzy nową, sprawną bazę i pokazuje komunikat w aplikacji. Kopia powstaje najpierw jako plik tymczasowy i zastępuje poprzednią dopiero po pomyślnym sprawdzeniu, więc przerwany zapis kopii nie niszczy ostatniej wersji ratunkowej.

Śledzenie aktywnej aplikacji jest **wyłączone domyślnie**. Po włączeniu odczytywana jest wyłącznie nazwa procesu aktywnego okna, a w bazie zapisywane są sumy kategorii: Work, Browser, Gaming, Communication, Development, Entertainment i Other. Nazwa procesu służy bieżącemu widokowi i klasyfikacji. Nie są pobierane tytuły okien, adresy stron, treść dokumentów, schowek, zrzuty ekranu ani naciśnięte klawisze. Program nie używa kamery, analizy obrazu, zewnętrznego API ani chmury.

Pliki SQLite i ich kopie nie są szyfrowane; chronią je uprawnienia konta Windows. Aby wykonać dodatkową kopię ręczną, zakończ aplikację przez **Exit**, a następnie skopiuj katalog danych. Usunięcie katalogu danych po zakończeniu aplikacji resetuje historię i ustawienia.

## Zrzuty ekranu

- Dashboard: ciemny interfejs, karty czasu i Health Score ![Dashboard](.\screenshots\dashboard.png)
- Popup przypomnienia oraz okno z ćwiczeniami <img src=".\screenshots\popup.png" width="50%" alt="Popup"> <img src=".\screenshots\popup2.png" width="50%" alt="Popup">
- Statistics: dzienne wartości i 7 dni historii ![Statistics](.\screenshots\stats.png)
- Biblioteka ćwiczeń ![Excercise library](.\screenshots\excercises.png)
- Settings oraz opcja Demo Mode ![Settings](.\screenshots\settings.png) ![Settings](.\screenshots\settings2.png)
- Strict Mode z przyciskiem Emergency skip. ![Strict Mode](.\screenshots\strict.png) ![Strict Mode](.\screenshots\strict2.png)

## Testowanie

```powershell
./scripts/test.ps1
```

Skrypt uruchamia testy Core i SQLite. `RollForward=Major` pozwala uruchomić host testów także przy zainstalowanym nowszym runtime .NET. Wyniki TRX trafiają do `artifacts/TestResults`. Status wykonanych sprawdzeń i scenariusze wymagające interaktywnego Windows są opisane w [docs/TESTING.md](docs/TESTING.md). Samo powodzenie testów jednostkowych nie zastępuje sprawdzenia okna, tray i blokady Windows na rzeczywistym pulpicie.

## Informacja medyczna

HealthBreak nie jest urządzeniem medycznym ani poradą medyczną. Proponuje ogólne przypomnienia i łagodne ćwiczenia; użytkownik sam decyduje o ich wykonaniu. Nie wykonuj ruchu powodującego ból. Dobierz ćwiczenia do własnych możliwości i zaleceń osoby prowadzącej leczenie, jeżeli takie masz.
