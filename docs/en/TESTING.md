# HealthBreak Verification

## Commands

```powershell
./scripts/test.ps1
./scripts/run.ps1 -DataDir ./artifacts/manual-test-data
./scripts/publish.ps1
```

Core tests verify the application logic without using Windows APIs. Data tests use a temporary SQLite database. Interactive scenarios should be performed on Windows 10 1809+ or Windows 11 x64. The `manual-test-data` directory keeps test data separate from everyday usage history.

## Current Verification Status

| Check | Status | Notes |
| --- | --- | --- |
| Compilation of five Native service files in C# 12 | Completed successfully | Compiler from .NET 10 SDK, temporary harness, and .NET 10 reference pack; this is not a full WinUI build |
| Assignment of 7 process categories | Completed successfully | Browser, Development, Communication, Gaming, Entertainment, Work, Other |
| Sample without application tracking | Completed successfully | Process and category fields are empty; idle time is not negative |
| Creation/update/disposal of the TrayIcon object | Completed successfully in an isolated environment | Actual desktop and icon availability were false; the visible icon and clicking behavior were therefore not verified |
| Automated Core and Data tests | Completed successfully | 27 Core tests and 17 SQLite tests; Release, Windows 10 19045 x64 |
| Full WinUI build and startup | Completed successfully | Build without warnings; smoke test started monitoring, SQLite, and all five views |
| Publishing of the standalone x64 directory | Completed successfully | Verified complete XBF/PRI files and startup of the published EXE with exit code 0 |
| Interactive scenarios below | To be performed on a Windows desktop | Successful unit tests do not confirm GUI behavior |

## Functional Scenarios

| Scenario | Steps | Expected Result |
| --- | --- | --- |
| First launch | Start with a new data directory | Dashboard loads without exceptions; score 100; demo, autostart, strict mode, and application tracking disabled |
| Mouse and keyboard | Use both devices | Session and active time increase without recording keystrokes |
| Idle | Leave the computer for more than 180 s, then return | Inactive time is not counted as active work; the appropriate break is detected after returning |
| Short absence | Leave the workstation for less than 120 s | No break is counted |
| Short natural break | Leave the workstation for 2–5 min and return | One break; default 15 min session relief without subtracting from actual total work time |
| Full natural break | Leave the workstation for more than 5 min and return | One full break and reset of the current session |
| Time correction | Observe the counter before and after the idle threshold | Temporarily counted inactivity time is reclassified as idle |
| Pause | Tray → Pause monitoring; wait; Resume monitoring | No work time is added while paused; measurement resumes correctly afterward |
| Windows lock | Win+L, wait, then sign in | Locked time is not counted as activity |
| Sleep | Put the computer to sleep and resume | The sampling gap is not added to active work time |
| Four reminders | Use demo mode, remain active, skip earlier thresholds | Thresholds 30/45/60/75; no popup every tick at the same threshold |
| Postpone | Remind me in 5 minutes | Reminder returns after 5 min, or about 5 s in demo mode; one postponement stored in the database |
| Skip | Skip | Popup disappears, skip is counted, and the score reacts |
| Manual break | Start break, check the timer and exercises | Work time is not counted; completion resets the session and does not immediately trigger another popup; ending early does not pretend that a break was completed or that a reminder was dismissed |
| Recommendation duration | In Demo Mode, pass the second and strong thresholds in sequence | By default, 30 min suggests Quick 45 s, 45 min suggests Short 2:30, and 75 min suggests Full 5:00 |
| Recoverable Health Score | Add many skips, then complete a break and exercises | Penalties are capped; completing breaks and exercises raises the score even after a poor result |
| Exercise confirmation | Mark the same exercise as completed twice | Only one completion per exercise is counted during a given break |
| Exercise variety | Complete several breaks | Exercises vary according to history and categories; no automatic movement detection |
| Strict Mode | Enable it and pass the final threshold | Full-screen view with exercises and an available Emergency skip |
| Emergency exit | Use Emergency skip; also test Alt+Tab and closing the window | Windows is not blocked; emergency skip is stored; the application can be closed |
| Settings validation | Enter decreasing thresholds or an invalid break duration | Clear error message; invalid state is not saved |
| Settings persistence | Save settings, close the app, and relaunch | Settings are restored; Demo Mode remains disabled |
| Demo separation | Create both normal and demo activity | Statistics are filtered using `is_demo` |
| Process categories | Enable tracking, activate an editor and browser, then disable it | When enabled, the correct category is shown; when disabled, no new application data is collected |
| Tray | Close the main window, open the menu and dashboard | Monitoring continues; tooltip and all menu items work correctly |
| Help and icon | Open Help; check the window, taskbar, tray, and EXE | Help describes the main features; the HealthBreak icon is visible everywhere |
| Explorer restart | Restart Explorer on the test computer | The icon returns after the TaskbarCreated message |
| Autostart | Deliberately enable/save it, then disable/save it | Only the HKCU Run HealthBreak entry is created and removed; scripts do not modify it themselves |
| Application restart | Exit, then launch again | History is preserved, the SQLite connection is closed, and no abandoned tray icon remains |
| Process crash | Using test data, terminate the process and relaunch | Database opens correctly; the interrupted session ends at the last saved measurement |
| Corrupted database | Using separate test data, replace the database with an invalid file and launch | Application starts, preserves the file as `.corrupt-*`, and restores a verified `.backup` or creates a new database |
| Day change | Run the Core clock test; interactively observe the transition through midnight | Daily totals and 7-day history do not assign the entire session to the wrong day |
| No internet | Launch a previously published directory while offline | Dashboard, monitoring, exercises, and local data storage continue to work |

## Privacy and File Checks

After closing the application, open the test database using an SQLite tool. The tables should contain time values, numeric values, the exercise catalog, flags, and optional category totals. They should not contain typed text, window titles, or URLs. Do not modify the database while the application using it is still running.

When testing errors, use a separate data directory. Include the Windows version, launch command, normal/demo mode, and the exception text, if any, in the test result. Do not mark a scenario as passed solely based on code review.