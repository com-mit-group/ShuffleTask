# ShuffleTask Architecture Overview

## System Architecture

ShuffleTask follows a clean layered architecture with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                       │
│  ┌─────────────────┐  ┌─────────────────────────────────────┐ │
│  │   MAUI UI       │  │     Services                       │ │
│  │   Views         │  │ ┌─────────────────────────────────┐ │ │
│  │   ViewModels    │  │ │   ShuffleCoordinatorService    │ │ │
│  │                 │  │ │   NotificationService          │ │ │
│  └─────────────────┘  │ └─────────────────────────────────┘ │ │
│                       │                                     │ │
└───────────────────────┼─────────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────────┐
│                Application Layer                            │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                  Abstractions                           │ │
│  │  IStorageService │ ISchedulerService │ INotificationService │ │
│  │  IShuffleLogger                                         │ │
│  └─────────────────────────────────────────────────────────┘ │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                   Services                              │ │
│  │  SchedulerService │ DefaultShuffleLogger                │ │
│  │  ImportanceUrgencyCalculator │ TimeWindowService        │ │
│  └─────────────────────────────────────────────────────────┘ │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                   Models                                │ │
│  │  AppSettings │ ScoredTask                               │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────┼─────────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────────┐
│                 Domain Layer                                │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                  Entities                               │ │
│  │  TaskItem │ TaskItemData                                │ │
│  └─────────────────────────────────────────────────────────┘ │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                   Enums                                 │ │
│  │  TaskLifecycleStatus │ RepeatType │ AllowedPeriod        │ │
│  │  Weekdays                                               │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────┼─────────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────────┐
│              Persistence Layer                              │
│                       │                                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                StorageService                           │ │
│  │  SQLite Database │ TaskItemRecord │ Settings Storage    │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. Task Lifecycle Management

Tasks progress through well-defined states with structured logging:

```
     ┌─────────┐
     │ Active  │◄──────────┐
     └────┬────┘           │
          │                │
          ▼                │
     ┌─────────┐      ┌─────────┐
     │Snoozed  │      │Completed│
     └────┬────┘      └────┬────┘
          │                │
          └────────────────┘
         (Auto-resume)   (Repeating)
```

**State Transitions:**
- `Active → Snoozed`: User snoozes task for specified duration
- `Active → Completed`: User marks task as done
- `Snoozed → Active`: Auto-resume when snooze period expires  
- `Completed → Active`: For repeating tasks based on schedule
- `* → Active`: Manual resume via ResumeTaskAsync

### 2. Task Selection & Scheduling

The `SchedulerService` implements intelligent task selection:

1. **Filtering**: Only eligible, unpaused tasks in allowed time windows
   - Respects `AutoShuffleAllowed` flag to prevent auto-selection of certain tasks
   - Checks `AllowedPeriod` (Any/Work/OffWork/Custom) with time windows
   - For Custom periods, validates against task's `CustomStartTime` and `CustomEndTime`
   - **Note**: Manual shuffle (via UI) always bypasses the `AutoShuffleAllowed` flag and can optionally respect
     `AllowedPeriod` based on the "Manual shuffle respects allowed hours" setting
2. **Scoring**: Multi-factor scoring based on:
   - Importance weight (user-defined priority)
   - Urgency weight (deadline proximity, overdue status)
   - Size bias (larger tasks get slight boost)
   - Streak bias (tasks not done recently get preference)
   - Repeat penalty (reduces urgency for frequently repeated tasks)
3. **Selection**: Weighted random selection or deterministic (for testing)

### 3. Notification Pipeline

Cross-platform notifications with fallback mechanisms:

```
NotificationService
│
├── Platform-specific implementation (iOS/Android/Windows)
│   ├── Native notifications
│   ├── Background notifications  
│   └── Sound/vibration
│
└── Fallback: XAML DisplayAlert
    └── Main thread alerts
```

### 4. Sync & State Management

Centralized state management in `StorageService`:

- **SQLite** for local persistence
- **Transactional updates** for data consistency
- **Auto-resume logic** for time-based state transitions
- **Structured logging** for debugging distributed sync

## Data Model

### TaskLifecycleStatus Enum
```csharp
public enum TaskLifecycleStatus
{
    Active = 0,     // Available for selection
    Snoozed = 1,    // Hidden until SnoozedUntil time
    Completed = 2   // Done, may auto-resume if repeating
}
```

### Key Properties
- **NextEligibleAt**: When snoozed/completed tasks become active again
- **AllowedPeriod**: Time window constraints (Any/Work/OffWork/Custom)
- **AutoShuffleAllowed**: Flag to control whether auto-shuffle can select this task
- **CustomStartTime/CustomEndTime**: Custom time ranges when task can be auto-shuffled (used with Custom period)
- **Repeat**: None/Daily/Weekly/Interval with proper next occurrence calculation

## Logging Structure

Structured logging provides comprehensive debugging information:

```
[HH:mm:ss.fff] TASK_SELECTION | TaskId=abc123 | Title="Review PR" | Reason=Task selected by scoring | Candidates=5 | NextGap=01:30
[HH:mm:ss.fff] STATE_TRANSITION | TaskId=abc123 | From=Active | To=Snoozed | Reason=Snoozed for 15:00
[HH:mm:ss.fff] TIMER_EVENT | Event=Started | TaskId=abc123 | Duration=25:00 | Reason=Pomodoro session
[HH:mm:ss.fff] SYNC_EVENT | Event=AutoResume | Details=Resumed 3 task(s)
[HH:mm:ss.fff] NOTIFICATION | Type=TaskReminder | Title="Time for Review PR" | Status=SUCCESS
```

## Key Flows

### Task Flow
1. User creates task → `StorageService.AddTaskAsync()`
2. Task enters `Active` state, eligible for selection
3. `SchedulerService.PickNextTask()` selects based on scoring (respects `AutoShuffleAllowed` and time windows)
4. `ShuffleCoordinatorService` manages timer and notifications
5. User can manually shuffle to pick a different task. Manual shuffle always ignores `AutoShuffleAllowed` and respects
   `AllowedPeriod` when the corresponding setting is enabled.
6. User completes → `StorageService.MarkTaskDoneAsync()` → `Completed` state
7. If repeating → Auto-transition to `Active` based on schedule

### Notification Flow  
1. Timer expires → `ShuffleCoordinatorService.NotifyAsync()`
2. `NotificationService` attempts platform-specific notification
3. Fallback to XAML alert if platform notification fails
4. All notification attempts logged for debugging

### Sync Flow
1. `StorageService.AutoResumeDueTasksAsync()` checks for eligible tasks
2. Tasks past their `NextEligibleAt` time transition to `Active`
3. State transitions logged for sync debugging
4. UI automatically refreshes to show newly available tasks

## Extension Points

- **IShuffleLogger**: Custom logging implementations (file, remote, etc.)
- **INotificationService**: Platform-specific notification strategies
- **Scoring Algorithm**: Modify `ImportanceUrgencyCalculator` for different prioritization
- **Repeat Logic**: Extend `ComputeNextEligibleUtc` for new repeat patterns