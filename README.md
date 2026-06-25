# ShuffleTask

A task prioritization and scheduling system that helps you focus by intelligently selecting what to work on next.

## Documentation

### Architecture & Design
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Clean layered architecture with structured logging and state management

### Ubuntu MAUI Android smoke test

The runnable MAUI host is `ShuffleTask.Presentation`. On Ubuntu, use the Android target:

- Host project: `ShuffleTask.Presentation/ShuffleTask.Presentation.csproj`
- Ubuntu target framework: `net10.0-android`
- Shared presentation target frameworks: `net10.0` and `net10.0-android`

Install the .NET SDK used by the repo, the .NET MAUI workload, and the Android SDK:

```bash
dotnet workload install maui
export ANDROID_HOME="$HOME/Android/Sdk"
```

Then build the Android target from Ubuntu:

```bash
scripts/maui-android-ubuntu.sh build
```

To run on a booted emulator or attached Android device:

```bash
scripts/maui-android-ubuntu.sh run
```

Use `scripts/maui-android-ubuntu.sh run --device SERIAL` when more than one device is attached. The helper fails early with actionable messages when the MAUI workload, Android SDK, platform packages, platform-tools, or a runnable device/emulator are missing.

Windows, iOS, and MacCatalyst targets remain native-host paths. Build Windows on Windows, iOS on macOS with the Apple toolchain, and MacCatalyst on macOS.

### Core Flows

**Task Selection Flow:**
```
Tasks → Filter (Active, Allowed Time) → Score → Weighted Selection → Timer/Notification
```

**Task Lifecycle:**
```
Active ⟷ Snoozed ⟷ Completed
   ↑                    ↓
   └── (Auto-resume) ────┘
```

**Notification Pipeline:**
```
Timer → Platform Notification → Fallback Alert → Logging
```

**Background Notifications:**
Auto-shuffle notifications fire reliably even when the app is backgrounded, using OS-native scheduled notification APIs (AlarmManager on Android, UNUserNotificationCenter on iOS/macOS, ScheduledToastNotification on Windows).

## Allowed periods & presets

Each task can define when it is allowed to be shuffled. The period editor lets you pick a preset (such as a workday or custom window) or create a new preset to reuse across tasks. Presets provide default weekday/time windows; editing a preset updates those defaults for future tasks that choose it.

When using the ad-hoc editor, the **All-day** toggle ignores the start/end time pickers, while alignment modes help snap the window to work or off-work hours. Work/off-work alignment uses the **Work start/end** times from Settings, and off-work covers all remaining hours.


## Prioritization formula

ShuffleTask ranks tasks by combining weighted importance, urgency, and a size-aware multiplier:

- **Importance** contributes up to 60 points based on a 1–5 rating.
- **Urgency** contributes up to 40 points and splits into deadline and repeat components. Deadline urgency now uses a size-aware window:
  - `windowHours = clamp(72 * (storyPoints / 3), 24, 168)`
  - Upcoming deadlines receive `deadlineUrgency = 1 - clamp(hoursUntilDeadline / windowHours, 0, 1)` while overdue work keeps the existing boost.
  - Repeat urgency is unchanged, still weighted at 25% of the urgency share with a streak penalty.
- **Size multiplier** lifts larger efforts while keeping quick wins visible:
  - `sizeMultiplier = clamp(1 + 0.2 * (storyPoints / 3 - 1), 0.8, 1.2)`
  - The final score is `(importancePoints + deadlinePoints + repeatPoints) * sizeMultiplier`.

Story point estimates default to 3 and can be adjusted between 0.5 and 13 in the task editor. Smaller estimates start boosting urgency closer to the deadline, while larger estimates surface earlier in the shuffle.

### Tune the balance

Open **Settings → Weighting** to tailor how the shuffle behaves:

- Adjust the split between **importance** and **urgency** (defaults remain the 60/40 split described above) with a single slider.
- Split the urgency pool between **deadlines** and **repeating work** with a single slider.
- Control the **repeat penalty** to soften or remove the dampening on routine tasks.
- Dial the **size bias strength** down to zero to make scores size-agnostic or up to highlight big pushes.

Changes are saved to your profile and take effect immediately in the next shuffle preview.
