# Hackathon Demo: 3–5 Minutes

## Preparation

1. On Windows x64, run `./scripts/run.ps1 -DataDir ./artifacts/demo-data`, or launch the published EXE with `--data-dir` pointing to a separate presentation data directory.
2. Open the dashboard and settings. Show that application tracking, Strict Mode, and Demo Mode are optional. Keep the default thresholds of 30/45/60/75 minutes and idle time of 180 seconds.
3. Enable Demo Mode in the application. Confirm that the **DEMO ×60** indicator is visible. This mode applies only to the current application session and does not remain enabled after restarting.

Demo Mode scales real time by 60; it does not generate artificial input events. While the session timer is increasing, move the mouse or use the keyboard at least once every 1–2 seconds. Three seconds without input already reaches the idle threshold. If a natural break occurs, the timing of the next reminder may change because the session counter receives relief.

## Scenario

| Stage | What to do | What to show |
| --- | --- | --- |
| Start | Keep providing input activity | Increasing Current session and Active time today values; clearly visible demo mode |
| Around 30 s | Wait for the first active-session threshold | Gentle reminder and a Quick break suggestion |
| Postpone | Select Remind me in 5 minutes; keep moving the mouse | Reminder returns after about 5 real seconds; score decreases because of postponement |
| Break | Select Start break | Exercise list, categories, description, and a dedicated confirmation button |
| Confirmation | Manually mark the exercise as completed and finish the break after the timer ends | Increase in Completed exercises and Breaks today; relief applied to the current session |
| Natural rest | Do not touch the mouse or keyboard for 7–8 s, then resume activity | Idle time without work being counted; after returning, a full break and session reset |
| Statistics | Open Statistics | Daily totals and a 7-day view for demo data |
| Tray | Close the main window and reopen the application from the system tray | Continuous operation, pause/resume, Start break, and Exit |

Manual breaks are also accelerated: Quick 45 s lasts about 0.75 s, Short 150 s about 2.5 s, and Full 300 s about 5 s. The exercise window remains open until the user interacts with it. A very short timer does not mean the exercises are completed automatically; they must be confirmed manually.

## Demonstrating a Longer Session and Strict Mode

By maintaining activity, you can see the next thresholds after approximately 45, 60, and 75 seconds of continuous work. Earlier breaks reduce the session counter, so these times are calculated from the current session value. To demonstrate only the threshold progression, use Skip on earlier reminders.

Enable Strict Mode in the settings and save the changes. At the final threshold, the full-screen **Health break required** screen will appear. Show the exercises and **Emergency skip**, followed by the increase in the skip counter. Access to Alt+Tab, Windows shortcuts, and Task Manager remains fully available.

## Ending the Demo

Disable Demo Mode, show the normal statistics, and close the application using tray → Exit. Presentation data is marked with `is_demo=1` and is excluded from normal usage statistics. Every new application launch starts with Demo Mode disabled.

To demonstrate the real exercise duration, disable acceleration and start a break manually. There is no need to wait 30 minutes for a reminder in order to show the exercises.