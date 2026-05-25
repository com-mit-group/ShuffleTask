using System.Diagnostics;
using SQLite;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private SQLiteAsyncConnection Db => _db ?? throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");

    private void EnsureMetadata(TaskItemData item, TaskItemRecord? existing, bool bumpVersion)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        EnsureOwnership(item, existing);
        item.CreatedAt = item.CreatedAt == default ? nowUtc : EnsureUtc(item.CreatedAt);
        item.UpdatedAt = item.UpdatedAt == default ? nowUtc : EnsureUtc(item.UpdatedAt);

        if (existing == null)
        {
            item.EventVersion = Math.Max(1, item.EventVersion);
            return;
        }

        int baseVersion = Math.Max(item.EventVersion, existing.EventVersion);
        item.EventVersion = bumpVersion ? Math.Max(baseVersion, existing.EventVersion + 1) : baseVersion;
    }

    private static void EnsureOwnership(TaskItemData item, TaskItemRecord? existing)
    {
        string? preferredUser = string.IsNullOrWhiteSpace(item.UserId) ? existing?.UserId : item.UserId;
        string? preferredDevice = string.IsNullOrWhiteSpace(item.DeviceId) ? existing?.DeviceId : item.DeviceId;

        if (!string.IsNullOrWhiteSpace(preferredUser))
        {
            item.UserId = preferredUser;
            item.DeviceId = null;
            return;
        }

        item.UserId = null;
        item.DeviceId = string.IsNullOrWhiteSpace(preferredDevice)
            ? Environment.MachineName
            : preferredDevice.Trim();
    }

    // Tasks CRUD
    public async Task<List<TaskItem>> GetTasksAsync(string? userId = "", string deviceId = "")
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=tasks; operation=list");

        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=tasks; operation=list; durationMs={stopwatch.ElapsedMilliseconds}");
            return new List<TaskItem>();
        }

        await ValidateAndRecoverTaskTableAsync().ConfigureAwait(false);
        await AutoResumeDueTasksAsync();

        AsyncTableQuery<TaskItemRecord> query = Db.Table<TaskItemRecord>();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(t => t.UserId == userId);
        }
        else if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(t => (t.UserId == null || t.UserId == "") && t.DeviceId == deviceId);
        }

        var records = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        await ValidateAndRepairTaskRecordsAsync(records).ConfigureAwait(false);
        var tasks = records.Select(r => r.ToDomain()).ToList();
        await ApplyPeriodDefinitionsAsync(tasks);
        _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=tasks; operation=list; count={tasks.Count}; durationMs={stopwatch.ElapsedMilliseconds}");
        return tasks;
    }

    public async Task<TaskItem?> GetTaskAsync(string id)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=tasks; operation=get");

        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=tasks; operation=get; durationMs={stopwatch.ElapsedMilliseconds}");
            return null;
        }

        await ValidateAndRecoverTaskTableAsync().ConfigureAwait(false);
        await AutoResumeDueTasksAsync();

        var record = await Db.Table<TaskItemRecord>()
                             .Where(t => t.Id == id)
                             .FirstOrDefaultAsync();
        if (record != null)
        {
            await ValidateAndRepairTaskRecordsAsync(new[] { record }).ConfigureAwait(false);
        }
        var task = record?.ToDomain();
        await ApplyPeriodDefinitionAsync(task);
        _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=tasks; operation=get; found={task != null}; durationMs={stopwatch.ElapsedMilliseconds}");
        return task;
    }

    public async Task AddTaskAsync(TaskItem item)
    {
        await _taskLock.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=add");
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            item.Id = Guid.NewGuid().ToString("n");
        }
        if (item.Status != TaskLifecycleStatus.Active &&
            item.Status != TaskLifecycleStatus.Snoozed &&
            item.Status != TaskLifecycleStatus.Completed)
        {
            item.Status = TaskLifecycleStatus.Active;
        }

        EnsureMetadata(item, null, bumpVersion: true);

        var record = BuildTaskRecord(item);
        try
        {
            if (_taskSchemaIsFuture)
            {
                _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=add; reason=future-schema");
                return;
            }

            await Db.RunInTransactionAsync(conn =>
            {
                conn.Insert(record);
                _faultInjector?.BeforeCommit("tasks.add");
            }).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=add; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public async Task UpdateTaskAsync(TaskItem item)
    {
        await _taskLock.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=update");
            if (_taskSchemaIsFuture)
            {
                _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=update; reason=future-schema");
                return;
            }

            var existing = await Db.FindAsync<TaskItemRecord>(item.Id).ConfigureAwait(false);

            if (existing != null)
            {
                item.CreatedAt = existing.CreatedAt;
            }

            if (item.UpdatedAt == default)
            {
                item.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            }

            EnsureMetadata(item, existing, bumpVersion: true);

            var record = BuildTaskRecord(item);
            await Db.RunInTransactionAsync(conn =>
            {
                if (existing == null)
                {
                    conn.Insert(record);
                    _faultInjector?.BeforeCommit("tasks.update");
                    return;
                }

                conn.Update(record);
                _faultInjector?.BeforeCommit("tasks.update");
            }).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=update; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public async Task DeleteTaskAsync(string id)
    {
        await _taskLock.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=delete");
            if (_taskSchemaIsFuture)
            {
                _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=delete; reason=future-schema");
                return;
            }

            await AutoResumeDueTasksAsync();
            await Db.RunInTransactionAsync(conn =>
            {
                conn.Delete<TaskItemRecord>(id);
                _faultInjector?.BeforeCommit("tasks.delete");
            }).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=delete; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public async Task<int> MigrateDeviceTasksToUserAsync(string deviceId, string userId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=migrate-device-owner");
        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=migrate-device-owner; reason=future-schema");
            return 0;
        }

        int updated = 0;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        await Db.RunInTransactionAsync(conn =>
        {
            var matches = conn.Table<TaskItemRecord>()
                .Where(t => (t.UserId == null || t.UserId == "") && t.DeviceId == deviceId)
                .ToList();

            foreach (var task in matches)
            {
                task.UserId = userId;
                task.DeviceId = null;
                task.UpdatedAt = EnsureUtc(nowUtc);
                task.EventVersion = Math.Max(task.EventVersion + 1, 1);
                conn.Update(task);
                updated++;
            }

            _faultInjector?.BeforeCommit("tasks.migrate-owner");
        });

        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=migrate-device-owner; updated={updated}; durationMs={stopwatch.ElapsedMilliseconds}");
        return updated;
    }

    // Lifecycle helpers
    public async Task<TaskItem?> MarkTaskDoneAsync(string id)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=mark-done");
        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=mark-done; reason=future-schema");
            return null;
        }

        TaskItem? updated = null;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        string? originalStatus = null;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItemRecord>(id);
            if (existing == null)
            {
                return;
            }

            var baseline = new TaskItemRecord();
            baseline.CopyFrom(existing);

            originalStatus = existing.Status.ToString();
            DateTime doneAt = EnsureUtc(nowUtc);
            existing.LastDoneAt = doneAt;
            existing.CompletedAt = doneAt;
            existing.Status = TaskLifecycleStatus.Completed;
            existing.SnoozedUntil = null;
            existing.NextEligibleAt = ComputeNextEligibleUtc(existing, nowUtc);
            existing.CutInLineMode = CutInLineMode.None;

            existing.UpdatedAt = EnsureUtc(nowUtc);
            EnsureMetadata(existing, baseline, bumpVersion: true);

            conn.Update(existing);
            _faultInjector?.BeforeCommit("tasks.mark-done");
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Completed", "Task marked as done");
        }

        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=mark-done; changed={updated != null}; durationMs={stopwatch.ElapsedMilliseconds}");
        return updated;
    }

    public async Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=snooze");
        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=snooze; reason=future-schema");
            return null;
        }

        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(15);
        }

        TaskItem? updated = null;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        string? originalStatus = null;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItemRecord>(id);
            if (existing == null)
            {
                return;
            }

            var baseline = new TaskItemRecord();
            baseline.CopyFrom(existing);

            originalStatus = existing.Status.ToString();
            DateTime until = EnsureUtc(nowUtc.Add(duration));
            existing.Status = TaskLifecycleStatus.Snoozed;
            existing.SnoozedUntil = until;
            existing.NextEligibleAt = until;
            existing.CompletedAt = null;

            existing.UpdatedAt = EnsureUtc(nowUtc);
            EnsureMetadata(existing, baseline, bumpVersion: true);

            conn.Update(existing);
            _faultInjector?.BeforeCommit("tasks.snooze");
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Snoozed", $"Snoozed for {duration:mm\\:ss}");
        }

        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=snooze; changed={updated != null}; durationMs={stopwatch.ElapsedMilliseconds}");
        return updated;
    }

    public async Task<TaskItem?> ResumeTaskAsync(string id)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=tasks; operation=resume");
        if (_taskSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=tasks; operation=resume; reason=future-schema");
            return null;
        }

        TaskItem? updated = null;
        string? originalStatus = null;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItemRecord>(id);
            if (existing == null)
            {
                return;
            }

            var baseline = new TaskItemRecord();
            baseline.CopyFrom(existing);

            originalStatus = existing.Status.ToString();
            ApplyResume(existing);
            existing.UpdatedAt = EnsureUtc(_clock.GetUtcNow().UtcDateTime);
            EnsureMetadata(existing, baseline, bumpVersion: true);
            conn.Update(existing);
            _faultInjector?.BeforeCommit("tasks.resume");
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Active", "Task resumed");
        }

        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=tasks; operation=resume; changed={updated != null}; durationMs={stopwatch.ElapsedMilliseconds}");
        return updated;
    }

    private async Task AutoResumeDueTasksAsync()
    {
        if (_taskSchemaIsFuture)
        {
            return;
        }

        var pending = await Db.Table<TaskItemRecord>()
                               .Where(t => t.Status != TaskLifecycleStatus.Active && t.NextEligibleAt != null)
                               .ToListAsync();

        if (pending.Count == 0)
        {
            return;
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        List<TaskItemRecord> toUpdate = new();

        foreach (var task in pending)
        {
            DateTime nextUtc = EnsureUtc(task.NextEligibleAt!.Value);
            if (nextUtc <= nowUtc)
            {
                string originalStatus = task.Status.ToString();
                var baseline = new TaskItemRecord();
                baseline.CopyFrom(task);
                ApplyResume(task);
                task.UpdatedAt = EnsureUtc(nowUtc);
                EnsureMetadata(task, baseline, bumpVersion: true);
                toUpdate.Add(task);
                _logger?.LogStateTransition(task.Id, originalStatus, "Active", "Auto-resumed due to schedule");
            }
        }

        if (toUpdate.Count > 0)
        {
            await Db.RunInTransactionAsync(conn =>
            {
                foreach (var task in toUpdate)
                {
                    conn.Update(task);
                }

                _faultInjector?.BeforeCommit("tasks.auto-resume");
            }).ConfigureAwait(false);
            _logger?.LogSyncEvent("AutoResume", $"Resumed {toUpdate.Count} task(s)");
        }
    }

    private async Task ValidateAndRepairTaskRecordsAsync(IEnumerable<TaskItemRecord> records)
    {
        foreach (var record in records)
        {
            bool dirty = false;
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Title))
            {
                record.Title = "Untitled";
                dirty = true;
            }

            if (record.Status != TaskLifecycleStatus.Active &&
                record.Status != TaskLifecycleStatus.Snoozed &&
                record.Status != TaskLifecycleStatus.Completed)
            {
                record.Status = TaskLifecycleStatus.Active;
                record.SnoozedUntil = null;
                record.NextEligibleAt = null;
                dirty = true;
            }

            if (record.Status == TaskLifecycleStatus.Completed && record.CompletedAt == null)
            {
                record.CompletedAt = record.UpdatedAt == default ? _clock.GetUtcNow().UtcDateTime : record.UpdatedAt;
                dirty = true;
            }

            if (record.Status == TaskLifecycleStatus.Snoozed && record.SnoozedUntil == null)
            {
                record.Status = TaskLifecycleStatus.Active;
                dirty = true;
            }

            EnsureOwnership(record, null);
            if (dirty)
            {
                _logger?.LogSyncEvent("PersistenceRecovery", $"Repaired invalid task id={record.Id}");
                await Db.UpdateAsync(record).ConfigureAwait(false);
            }
        }
    }

    private static void ApplyResume(TaskItemRecord task)
    {
        task.Status = TaskLifecycleStatus.Active;
        task.SnoozedUntil = null;
        task.NextEligibleAt = null;
        task.CompletedAt = null;
    }

    private static DateTime? ComputeNextEligibleUtc(TaskItemRecord task, DateTime nowUtc)
    {
        return task.Repeat switch
        {
            RepeatType.None => null,
            RepeatType.Daily => EnsureUtc(TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), TimeZoneInfo.Local).AddDays(1).UtcDateTime),
            RepeatType.Weekly => ComputeWeeklyNext(task.Weekdays, nowUtc),
            RepeatType.Interval => EnsureUtc(TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), TimeZoneInfo.Local).AddDays(Math.Max(1, task.IntervalDays)).UtcDateTime),
            _ => null,
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

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static TaskItemRecord BuildTaskRecord(TaskItem item)
    {
        var record = TaskItemRecord.FromDomain(item);
        NormalizePeriodDefinition(record);
        return record;
    }

    private static void NormalizePeriodDefinition(TaskItemData item)
    {
        if (string.IsNullOrWhiteSpace(item.PeriodDefinitionId))
        {
            return;
        }

        if (IsCustomPeriodDefinitionId(item.PeriodDefinitionId))
        {
            return;
        }

        item.AdHocStartTime = null;
        item.AdHocEndTime = null;
        item.AdHocWeekdays = null;
        item.AdHocIsAllDay = false;
        item.AdHocMode = PeriodDefinitionMode.None;
    }

    private async Task ApplyPeriodDefinitionsAsync(IReadOnlyList<TaskItem> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        var ids = tasks.Select(t => t.PeriodDefinitionId)
            .Where(IsCustomPeriodDefinitionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        var records = await Db.Table<PeriodDefinitionRecord>()
                              .Where(r => ids.Contains(r.Id))
                              .ToListAsync();

        if (records.Count == 0)
        {
            return;
        }

        var map = records.ToDictionary(r => r.Id, r => r.ToDomain(), StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            if (task.PeriodDefinitionId != null && map.TryGetValue(task.PeriodDefinitionId, out PeriodDefinition? definition))
            {
                ApplyPeriodDefinition(task, definition);
            }
        }
    }

    private async Task ApplyPeriodDefinitionAsync(TaskItem? task)
    {
        if (task == null)
        {
            return;
        }

        await ApplyPeriodDefinitionsAsync(new[] { task });
    }

    private static void ApplyPeriodDefinition(TaskItem task, PeriodDefinition definition)
    {
        task.AdHocStartTime = definition.StartTime;
        task.AdHocEndTime = definition.EndTime;
        task.AdHocWeekdays = definition.Weekdays;
        task.AdHocIsAllDay = definition.IsAllDay;
        task.AdHocMode = definition.Mode;
    }

    private static bool IsCustomPeriodDefinitionId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && !PeriodDefinitionCatalog.TryGet(id, out _);
    }

}
