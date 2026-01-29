using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShuffleTask.Tests.TestDoubles;

public class StorageServiceStub : IStorageService
{
    private readonly Dictionary<string, TaskItem> _tasks = new();
    private readonly Dictionary<string, PeriodDefinition> _periodDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private bool _initialized;
    private readonly AppSettings _settings;

    public int InitializeCallCount { get; private set; }
    public int GetTasksCallCount { get; private set; }
    public int UpdateTaskCallCount { get; private set; }
    public int DeleteTaskCallCount { get; private set; }
    public int MarkDoneCallCount { get; private set; }
    public int SnoozeCallCount { get; private set; }
    public int ResumeCallCount { get; private set; }
    public int GetSettingsCallCount { get; private set; }
    public int SetSettingsCallCount { get; private set; }

    public StorageServiceStub(TimeProvider? clock = null, AppSettings? settings = null)
    {
        _clock = clock ?? TimeProvider.System;
        _settings = settings ?? new AppSettings();
    }

    public Task InitializeAsync()
    {
        InitializeCallCount++;
        _initialized = true;
        foreach (var preset in PeriodDefinitionCatalog.CreatePresetDefinitions())
        {
            _periodDefinitions[preset.Id] = Clone(preset);
        }
        return Task.CompletedTask;
    }

    public Task<List<TaskItem>> GetTasksAsync(string? userId = "", string deviceId = "")
    {
        EnsureInitialized();
        AutoResumeDueTasks();
        GetTasksCallCount++;
        var query = _tasks.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(t => t.UserId == userId);
        }
        else if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(t => (t.UserId == null || t.UserId == "") && t.DeviceId == deviceId);
        }

        var snapshot = query
            .OrderByDescending(t => t.CreatedAt)
            .Select(Clone)
            .ToList();
        ApplyPeriodDefinitions(snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<TaskItem?> GetTaskAsync(string id)
    {
        EnsureInitialized();
        AutoResumeDueTasks();
        if (!_tasks.TryGetValue(id, out var item))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        var clone = Clone(item);
        ApplyPeriodDefinition(clone);
        return Task.FromResult<TaskItem?>(clone);
    }

    public Task AddTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        EnsureMetadata(item, null, bumpVersion: true);
        _tasks[item.Id] = Clone(NormalizePeriodDefinition(item));
        return Task.CompletedTask;
    }

    public Task UpdateTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        UpdateTaskCallCount++;
        item.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        EnsureMetadata(item, _tasks.TryGetValue(item.Id, out var existing) ? existing : null, bumpVersion: true);
        _tasks[item.Id] = Clone(NormalizePeriodDefinition(item));
        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string id)
    {
        EnsureInitialized();
        DeleteTaskCallCount++;
        _tasks.Remove(id);
        return Task.CompletedTask;
    }

    public Task<List<PeriodDefinition>> GetPeriodDefinitionsAsync()
    {
        EnsureInitialized();
        var definitions = _periodDefinitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();
        return Task.FromResult(definitions);
    }

    public Task<PeriodDefinition?> GetPeriodDefinitionAsync(string id)
    {
        EnsureInitialized();
        return Task.FromResult(_periodDefinitions.TryGetValue(id, out var definition) ? Clone(definition) : null);
    }

    public Task AddPeriodDefinitionAsync(PeriodDefinition definition)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        _periodDefinitions[definition.Id] = Clone(definition);
        return Task.CompletedTask;
    }

    public Task UpdatePeriodDefinitionAsync(PeriodDefinition definition)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        _periodDefinitions[definition.Id] = Clone(definition);
        return Task.CompletedTask;
    }

    public Task DeletePeriodDefinitionAsync(string id)
    {
        EnsureInitialized();
        _periodDefinitions.Remove(id);
        return Task.CompletedTask;
    }

    public Task<int> MigrateDeviceTasksToUserAsync(string deviceId, string userId)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(0);
        }

        int updated = 0;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        foreach (var task in _tasks.Values.Where(t => string.IsNullOrWhiteSpace(t.UserId) && t.DeviceId == deviceId))
        {
            task.UserId = userId;
            task.DeviceId = null;
            EnsureMetadata(task, task, bumpVersion: true, updatedAtOverride: nowUtc);
            updated++;
        }

        return Task.FromResult(updated);
    }

    public Task<TaskItem?> MarkTaskDoneAsync(string id)
    {
        EnsureInitialized();
        MarkDoneCallCount++;

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        DateTime doneAt = EnsureUtc(nowUtc);

        existing.LastDoneAt = doneAt;
        existing.CompletedAt = doneAt;
        existing.Status = TaskLifecycleStatus.Completed;
        existing.SnoozedUntil = null;
        existing.NextEligibleAt = ComputeNextEligibleUtc(existing, nowUtc);

        EnsureMetadata(existing, existing, bumpVersion: true, updatedAtOverride: doneAt);

        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration)
    {
        EnsureInitialized();
        SnoozeCallCount++;

        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(15);
        }

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        DateTime until = EnsureUtc(nowUtc.Add(duration));

        existing.Status = TaskLifecycleStatus.Snoozed;
        existing.SnoozedUntil = until;
        existing.NextEligibleAt = until;
        existing.CompletedAt = null;

        EnsureMetadata(existing, existing, bumpVersion: true, updatedAtOverride: nowUtc);

        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<TaskItem?> ResumeTaskAsync(string id)
    {
        EnsureInitialized();
        ResumeCallCount++;

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        ApplyResume(existing);
        existing.UpdatedAt = EnsureUtc(_clock.GetUtcNow().UtcDateTime);
        EnsureMetadata(existing, existing, bumpVersion: true);
        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<AppSettings> GetSettingsAsync()
    {
        EnsureInitialized();
        GetSettingsCallCount++;
        return Task.FromResult(_settings);
    }

    public Task SetSettingsAsync(AppSettings settings)
    {
        EnsureInitialized();
        SetSettingsCallCount++;
        StampSettings(settings);
        _settings.CopyFrom(settings);
        _settings.NormalizeWeights();
        return Task.CompletedTask;
    }

    private void AutoResumeDueTasks()
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        foreach (var task in _tasks.Values)
        {
            if (task.Status == TaskLifecycleStatus.Active)
            {
                continue;
            }

            if (task.NextEligibleAt == null)
            {
                continue;
            }

            DateTime nextUtc = EnsureUtc(task.NextEligibleAt.Value);
            if (nextUtc <= nowUtc)
            {
                ApplyResume(task);
                task.UpdatedAt = EnsureUtc(nowUtc);
                EnsureMetadata(task, task, bumpVersion: true, updatedAtOverride: nowUtc);
            }
        }
    }

    private static void ApplyResume(TaskItem task)
    {
        task.Status = TaskLifecycleStatus.Active;
        task.SnoozedUntil = null;
        task.NextEligibleAt = null;
        task.CompletedAt = null;
    }

    private static DateTime? ComputeNextEligibleUtc(TaskItem task, DateTime nowUtc)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), TimeZoneInfo.Local);
        return task.Repeat switch
        {
            RepeatType.None => null,
            RepeatType.Daily => EnsureUtc(nowLocal.AddDays(1).UtcDateTime),
            RepeatType.Weekly => ComputeWeeklyNext(task.Weekdays, nowUtc),
            RepeatType.Interval => EnsureUtc(nowLocal.AddDays(Math.Max(1, task.IntervalDays)).UtcDateTime),
            _ => null
        };
    }

    private static DateTime? ComputeWeeklyNext(Weekdays weekdays, DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), TimeZoneInfo.Local);
        if (weekdays == Weekdays.None)
        {
            weekdays = DayToWeekdayFlag(local.DayOfWeek);
        }

        for (int offset = 1; offset <= 7; offset++)
        {
            DateTimeOffset candidateLocal = new(local.Date.AddDays(offset).Add(local.TimeOfDay), local.Offset);
            Weekdays flag = DayToWeekdayFlag(candidateLocal.DayOfWeek);
            if ((weekdays & flag) != 0)
            {
                return EnsureUtc(candidateLocal.UtcDateTime);
            }
        }

        DateTimeOffset fallbackLocal = new(local.Date.AddDays(7).Add(local.TimeOfDay), local.Offset);
        return EnsureUtc(fallbackLocal.UtcDateTime);
    }

    private static Weekdays DayToWeekdayFlag(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Sunday => Weekdays.Sun,
            DayOfWeek.Monday => Weekdays.Mon,
            DayOfWeek.Tuesday => Weekdays.Tue,
            DayOfWeek.Wednesday => Weekdays.Wed,
            DayOfWeek.Thursday => Weekdays.Thu,
            DayOfWeek.Friday => Weekdays.Fri,
            DayOfWeek.Saturday => Weekdays.Sat,
            _ => Weekdays.None
        };
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");
        }
    }

    private void EnsureMetadata(TaskItem task, TaskItem? existing, bool bumpVersion, DateTime? updatedAtOverride = null)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        EnsureOwnership(task, existing);
        task.CreatedAt = task.CreatedAt == default
            ? nowUtc
            : EnsureUtc(task.CreatedAt);

        task.UpdatedAt = updatedAtOverride.HasValue
            ? EnsureUtc(updatedAtOverride.Value)
            : task.UpdatedAt == default
                ? nowUtc
                : EnsureUtc(task.UpdatedAt);

        int existingVersion = existing?.EventVersion ?? 0;
        int baseVersion = Math.Max(task.EventVersion, existingVersion);

        if (existing == null)
        {
            task.EventVersion = Math.Max(1, baseVersion);
            return;
        }

        task.EventVersion = bumpVersion
            ? Math.Max(baseVersion, existingVersion + 1)
            : Math.Max(1, baseVersion);
    }

    private static void EnsureOwnership(TaskItemData task, TaskItem? existing)
    {
        string? preferredUser = string.IsNullOrWhiteSpace(task.UserId) ? existing?.UserId : task.UserId;
        string? preferredDevice = string.IsNullOrWhiteSpace(task.DeviceId) ? existing?.DeviceId : task.DeviceId;

        if (!string.IsNullOrWhiteSpace(preferredUser))
        {
            task.UserId = preferredUser;
            task.DeviceId = null;
            return;
        }

        task.UserId = null;
        task.DeviceId = string.IsNullOrWhiteSpace(preferredDevice)
            ? Environment.MachineName
            : preferredDevice.Trim();
    }

    private void StampSettings(AppSettings settings)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        settings.UpdatedAt = settings.UpdatedAt == default ? nowUtc : settings.UpdatedAt;
        settings.EventVersion = settings.EventVersion <= 0 ? _settings.EventVersion + 1 : settings.EventVersion;
    }

    private static TaskItem Clone(TaskItem task) => TaskItem.Clone(task);

    private static PeriodDefinition Clone(PeriodDefinition definition)
    {
        return new PeriodDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Weekdays = definition.Weekdays,
            StartTime = definition.StartTime,
            EndTime = definition.EndTime,
            IsAllDay = definition.IsAllDay,
            Mode = definition.Mode
        };
    }

    private TaskItem NormalizePeriodDefinition(TaskItem task)
    {
        var clone = Clone(task);
        if (string.IsNullOrWhiteSpace(clone.PeriodDefinitionId))
        {
            return clone;
        }

        clone.AdHocStartTime = null;
        clone.AdHocEndTime = null;
        clone.AdHocWeekdays = null;
        clone.AdHocIsAllDay = false;
        clone.AdHocMode = PeriodDefinitionMode.None;
        return clone;
    }

    private void ApplyPeriodDefinitions(IReadOnlyList<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            ApplyPeriodDefinition(task);
        }
    }

    private void ApplyPeriodDefinition(TaskItem task)
    {
        NormalizeLegacyPeriodDefinition(task);
        if (string.IsNullOrWhiteSpace(task.PeriodDefinitionId))
        {
            return;
        }

        if (PeriodDefinitionCatalog.TryGet(task.PeriodDefinitionId, out _))
        {
            return;
        }

        if (!_periodDefinitions.TryGetValue(task.PeriodDefinitionId, out var definition))
        {
            return;
        }

        task.AdHocStartTime = definition.StartTime;
        task.AdHocEndTime = definition.EndTime;
        task.AdHocWeekdays = definition.Weekdays;
        task.AdHocIsAllDay = definition.IsAllDay;
        task.AdHocMode = definition.Mode;
    }

    private static void NormalizeLegacyPeriodDefinition(TaskItem task)
    {
        if (!string.IsNullOrWhiteSpace(task.PeriodDefinitionId) || HasAdHocDefinition(task))
        {
            return;
        }

        if (task.AllowedPeriod == AllowedPeriod.Custom)
        {
            if (task.CustomStartTime.HasValue || task.CustomEndTime.HasValue || task.CustomWeekdays.HasValue)
            {
                task.AdHocStartTime = task.CustomStartTime;
                task.AdHocEndTime = task.CustomEndTime;
                task.AdHocWeekdays = task.CustomWeekdays;
                task.AdHocIsAllDay = !task.CustomStartTime.HasValue || !task.CustomEndTime.HasValue;
                task.AdHocMode = PeriodDefinitionMode.None;
            }

            return;
        }

        task.PeriodDefinitionId = task.AllowedPeriod switch
        {
            AllowedPeriod.Work => PeriodDefinitionCatalog.WorkId,
            AllowedPeriod.OffWork => PeriodDefinitionCatalog.OffWorkId,
            _ => PeriodDefinitionCatalog.AnyId
        };
    }

    private static bool HasAdHocDefinition(TaskItem task)
    {
        return task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
