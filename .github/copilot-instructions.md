# GitHub Copilot Instructions for ShuffleTask

## Project Overview

ShuffleTask is a task prioritization and scheduling system built with .NET MAUI that helps users focus by intelligently selecting what to work on next. It uses a sophisticated scoring algorithm combining importance, urgency, and size-aware multipliers to rank tasks.

### Technology Stack

- **.NET 8.0** with C# 12
- **MAUI** for cross-platform UI (Android, iOS, Windows, macOS)
- **SQLite** for local data persistence
- **NUnit** for testing
- **CommunityToolkit.Mvvm** for MVVM pattern

## Architecture

ShuffleTask follows a **clean layered architecture** with clear separation of concerns:

### Layers

1. **Presentation Layer** (`ShuffleTask.Presentation`)
   - MAUI views and view models
   - Platform-specific services (notifications, storage)
   - UI-specific services (ShuffleCoordinatorService)

2. **Application Layer** (`ShuffleTask.Application`)
   - Business logic services (SchedulerService, ImportanceUrgencyCalculator)
   - Service abstractions (IStorageService, ISchedulerService, INotificationService)
   - Application models (AppSettings, ScoredTask)

3. **Domain Layer** (`ShuffleTask.Domain`)
   - Core entities (TaskItem, TaskItemData)
   - Enums (TaskLifecycleStatus, RepeatType, AllowedPeriod)
   - Domain logic

4. **Persistence Layer** (`ShuffleTask.Persistence`)
   - SQLite database implementation
   - Data access logic

### Key Design Patterns

- **Dependency Injection**: All services are registered in MauiProgram.cs
- **MVVM**: ViewModels use `ObservableObject` from CommunityToolkit.Mvvm
- **Repository Pattern**: StorageService abstracts data access
- **Strategy Pattern**: Pluggable scoring and notification strategies

## Coding Standards

### General Guidelines

- Follow C# naming conventions (PascalCase for public members, _camelCase for private fields)
- Use nullable reference types (`?`) appropriately
- Prefer expression-bodied members for simple methods
- Prefer explicit type declarations over `var` for clarity (limit `var` usage to cases where the type is immediately obvious)
- Keep methods focused and single-purpose

### Namespace Organization

- Use file-scoped namespaces: `namespace ShuffleTask.Application.Services;`
- Match folder structure to namespace hierarchy
- One class per file, named to match the class name

### Comments and Documentation

- Add XML documentation comments for public APIs
- Use inline comments sparingly - prefer self-documenting code
- Document complex algorithms (e.g., scoring formula in ImportanceUrgencyCalculator)
- Keep comments current with code changes

### Error Handling

- Use nullable returns (`TaskItem?`) instead of exceptions for expected "not found" scenarios
- Log errors using IShuffleLogger with structured data
- Handle platform-specific exceptions in platform services

## Key Domain Concepts

### Task Lifecycle States

Tasks progress through three states:
- **Active**: Available for selection in the shuffle
- **Snoozed**: Hidden until a specific time
- **Completed**: Marked done; may auto-resume if repeating

State transitions are logged for debugging.

### Task Selection Algorithm

The `SchedulerService.PickNextTask()` method:

1. **Filters** tasks by:
   - Lifecycle status (Active only)
   - Time windows (AllowedPeriod: Any/Work/OffWork/Custom)
   - AutoShuffleAllowed flag (for automatic selection)

2. **Scores** tasks using `ImportanceUrgencyCalculator`:
   - Importance: Up to 60 points (1-5 rating)
   - Deadline urgency: Size-aware window calculation
   - Repeat urgency: Streak penalty for routine tasks
   - Size multiplier: 0.8-1.2x based on story points

3. **Selects** via weighted random or deterministic (testing)

**Important**: Manual shuffle bypasses `AutoShuffleAllowed` but may respect `AllowedPeriod` based on settings.

### Prioritization Formula

```csharp
windowHours = clamp(72 * (storyPoints / 3), 24, 168)
deadlineUrgency = 1 - clamp(hoursUntilDeadline / windowHours, 0, 1)
sizeMultiplier = clamp(1 + 0.2 * (storyPoints / 3 - 1), 0.8, 1.2)
finalScore = (importancePoints + deadlinePoints + repeatPoints) * sizeMultiplier
```

This formula is implemented in `ImportanceUrgencyCalculator.cs`.

### Repeat Tasks

Tasks can repeat on schedules:
- **Daily**: Every N days
- **Weekly**: On specific weekdays
- **Interval**: Fixed time intervals

Next occurrence is calculated in `ComputeNextEligibleUtc()`.

## Development Workflow

### Building

```bash
# Build all projects
dotnet build ShuffleTask.sln

# Build specific framework
dotnet build ShuffleTask.Presentation/ShuffleTask.Presentation.csproj -f net8.0-android

# Build without GUI (for tests/services)
dotnet build ShuffleTask.NoGUI.slnf
```

### Testing

```bash
# Run all tests
dotnet test ShuffleTask.sln

# Run specific test project
dotnet test ShuffleTask.Tests/ShuffleTask.Tests.csproj

# Run with verbosity
dotnet test --verbosity normal
```

### Test Guidelines

- Write tests in NUnit with `[Test]` attribute and `[TestFixture]` for test classes
- Use custom test doubles from `ShuffleTask.Tests/TestDoubles/` for mocking (e.g., StorageServiceStub)
- Test both happy paths and edge cases
- Name tests descriptively: `MethodName_Scenario_ExpectedBehavior`
- Keep tests focused on one concern
- Use explicit type declarations in tests for clarity

Example:
```csharp
[TestFixture]
public class SchedulerServiceTests
{
    [Test]
    public void PickNextTask_WithNoActiveTasks_ReturnsNull()
    {
        // Arrange
        List<TaskItem> tasks = new List<TaskItem>();
        AppSettings settings = new AppSettings();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        
        // Act
        TaskItem? result = scheduler.PickNextTask(tasks, settings, now);
        
        // Assert
        Assert.That(result, Is.Null);
    }
}
```

## Structured Logging

Use `IShuffleLogger` for consistent, parseable logs:

```csharp
logger.LogTaskSelection(taskId, title, "Reason text", candidateCount, nextGap);
logger.LogStateTransition(taskId, fromState, toState, "Reason text");
logger.LogTimerEvent("Started", taskId, duration, "Reason text");
logger.LogSyncEvent("AutoResume", $"Resumed {count} task(s)");
logger.LogNotification("TaskReminder", title, success);
```

Log format: `[HH:mm:ss.fff] CATEGORY | Key=Value | Key=Value ...`

## Common Patterns

### Async/Await

- Use `async Task` for void-returning async methods
- Use `async Task<T>` for value-returning async methods
- Suffix async method names with `Async`
- Always await async calls; don't use `.Result` or `.Wait()`

### Observable Properties

ViewModels use `[ObservableProperty]` from CommunityToolkit.Mvvm:

```csharp
[ObservableProperty]
private TaskItem? _activeTask;  // Generates ActiveTask property
```

### Relay Commands

Use `[RelayCommand]` for command handlers:

```csharp
[RelayCommand]
private async Task ShuffleAsync()
{
    // Command implementation
}
// Generates ShuffleCommand property
```

## Platform-Specific Code

Use conditional compilation for platform-specific features:

```csharp
#if ANDROID
// Android-specific code
#elif IOS
// iOS-specific code
#elif WINDOWS
// Windows-specific code
#endif
```

## Time and Date Handling

- Use `DateTimeOffset` for all timestamps (timezone-aware)
- Use `TimeSpan` for durations
- Store times in UTC; convert to local for display
- Time windows use `CustomStartTime` and `CustomEndTime` (TimeOnly)

## Settings and Configuration

- `AppSettings` contains all user preferences
- Settings are persisted via `IStorageService`
- Changes take effect immediately (no restart required)
- Weights and multipliers are tunable in Settings → Weighting

## Common Gotchas

1. **AutoShuffleAllowed vs Manual Shuffle**: Auto-shuffle respects `AutoShuffleAllowed`, manual shuffle does not
2. **Time Windows**: Custom periods require both `CustomStartTime` and `CustomEndTime`
3. **Repeat Tasks**: Completed repeating tasks auto-resume based on `NextEligibleAt`
4. **Scoring**: Size bias affects both deadline urgency window and final score multiplier
5. **MAUI Lifecycle**: ViewModels may initialize multiple times; use `_isInitialized` flag

## File Organization

- **Services**: Implement business logic, are stateless or have minimal state
- **ViewModels**: Manage UI state, coordinate services, handle user interactions
- **Views**: XAML UI with code-behind for platform integration
- **Models**: DTOs and data structures (AppSettings, ScoredTask)
- **Entities**: Domain objects with behavior (TaskItem)

## Extension Points

When adding new features, consider:

- **IShuffleLogger**: Custom logging (file, remote, analytics)
- **INotificationService**: Platform notification strategies
- **Scoring Algorithm**: Modify `ImportanceUrgencyCalculator` for new factors
- **Repeat Patterns**: Extend `ComputeNextEligibleUtc` for new repeat types

## Resources

- **[README.md](../README.md)**: User-facing documentation and prioritization formula
- **[ARCHITECTURE.md](../ARCHITECTURE.md)**: Detailed system architecture and flows
- **Tests**: See existing tests for patterns and examples
- **.NET MAUI Docs**: https://learn.microsoft.com/dotnet/maui/
- **CommunityToolkit.Mvvm**: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/

## Best Practices for This Repository

1. **Minimal Changes**: Prefer targeted fixes over refactoring
2. **Test Coverage**: Add tests for new logic, especially scoring changes
3. **Logging**: Add structured logs for debugging complex flows
4. **State Management**: Be mindful of async state changes in ViewModels
5. **Performance**: Tasks list may grow large; optimize filtering and scoring
6. **Cross-Platform**: Test on multiple platforms when touching UI or notifications
7. **Documentation**: Update README.md or ARCHITECTURE.md for significant changes

## When Working on This Repository

- **Understand the scoring algorithm** before modifying task selection logic
- **Check both auto and manual shuffle** when changing task filtering
- **Consider time zones** when working with dates and times
- **Test state transitions** thoroughly (Active ↔ Snoozed ↔ Completed)
- **Respect layering**: Don't reference Presentation from Application/Domain
- **Use dependency injection**: Don't create service instances directly
- **Follow MVVM**: UI logic in ViewModels, not code-behind
