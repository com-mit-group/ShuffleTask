using System.Globalization;
using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public class StorageService : IStorageService
{
    private const string SettingsKey = "app_settings";
    private const string IntegerSqlType = "INTEGER";

    private readonly TimeProvider _clock;
    private readonly string _dbPath;
    private readonly IShuffleLogger? _logger;
    private SQLiteAsyncConnection? _db;
    private IRealtimeSyncService? _sync;

    public StorageService(TimeProvider clock, string databasePath, IShuffleLogger? logger = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        _dbPath = databasePath;
        _logger = logger;
    }

    public void AttachSyncService(IRealtimeSyncService syncService)
    {
        ArgumentNullException.ThrowIfNull(syncService);
        _sync = syncService;
    }

    public async Task InitializeAsync()
    {
        if (_db != null)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

        await _db.CreateTableAsync<TaskItemRecord>();
        await _db.CreateTableAsync<KeyValueEntity>();
        await _db.CreateTableAsync<DeletedTaskRecord>();

        // Ensure schema has all columns; add columns if missing with sensible defaults.
        await EnsureTaskSchemaAsync();
    }

    private async Task EnsureTaskSchemaAsync()
    {
        try
        {
            var infos = await Db.QueryAsync<TableInfo>("PRAGMA table_info(TaskItem);");
            var cols = new HashSet<string>(infos.Select(i => i.name), StringComparer.OrdinalIgnoreCase);
            async Task AddCol(string name, string sqlType, string defaultSql)
            {
                if (!cols.Contains(name))
                {
                    string alter = $"ALTER TABLE TaskItem ADD COLUMN {name} {sqlType} DEFAULT {defaultSql}";
                    await Db.ExecuteAsync(alter);
                }
            }

            await AddCol("Title", "TEXT", "''");
            await AddCol("Importance", IntegerSqlType, "1");
            await AddCol("SizePoints", "REAL", "3");
            await AddCol("Deadline", "TEXT", "NULL");
            await AddCol("Repeat", IntegerSqlType, "0");
            await AddCol("Weekdays", IntegerSqlType, "0");
            await AddCol("IntervalDays", IntegerSqlType, "0");
            await AddCol("LastDoneAt", "TEXT", "NULL");
            await AddCol("AllowedPeriod", IntegerSqlType, "0");
            await AddCol("AutoShuffleAllowed", IntegerSqlType, "1");
            await AddCol("CustomStartTime", "TEXT", "NULL");
            await AddCol("CustomEndTime", "TEXT", "NULL");
            await AddCol("Paused", IntegerSqlType, "0");
            await AddCol("CreatedAt", "TEXT", "CURRENT_TIMESTAMP");
            await AddCol("Description", "TEXT", "''");
            await AddCol("Status", IntegerSqlType, "0");
            await AddCol("SnoozedUntil", "TEXT", "NULL");
            await AddCol("CompletedAt", "TEXT", "NULL");
            await AddCol("NextEligibleAt", "TEXT", "NULL");
            await AddCol("CustomTimerMode", IntegerSqlType, "NULL");
            await AddCol("CustomReminderMinutes", IntegerSqlType, "NULL");
            await AddCol("CustomFocusMinutes", IntegerSqlType, "NULL");
            await AddCol("CustomBreakMinutes", IntegerSqlType, "NULL");
            await AddCol("CustomPomodoroCycles", IntegerSqlType, "NULL");
            await AddCol("CutInLineMode", IntegerSqlType, "0");
            await AddCol("UpdatedAt", "TEXT", "CURRENT_TIMESTAMP");
        }
        catch
        {
            // best-effort; ignore migration errors
        }
    }

    private SQLiteAsyncConnection Db => _db ?? throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");

    // Tasks CRUD
    public async Task<List<TaskItem>> GetTasksAsync()
    {
        await AutoResumeDueTasksAsync();

        var records = await Db.Table<TaskItemRecord>()
                              .OrderByDescending(t => t.CreatedAt)
                              .ToListAsync();
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<TaskItem?> GetTaskAsync(string id)
    {
        await AutoResumeDueTasksAsync();

        var record = await Db.Table<TaskItemRecord>()
                             .Where(t => t.Id == id)
                             .FirstOrDefaultAsync();
        return record?.ToDomain();
    }

    public async Task AddTaskAsync(TaskItem item)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            item.Id = Guid.NewGuid().ToString("n");
        }
        if (item.CreatedAt == default)
        {
            item.CreatedAt = nowUtc;
        }
        item.CreatedAt = EnsureUtc(item.CreatedAt);
        item.UpdatedAt = EnsureUtc(nowUtc);
        if (item.Status != TaskLifecycleStatus.Active &&
            item.Status != TaskLifecycleStatus.Snoozed &&
            item.Status != TaskLifecycleStatus.Completed)
        {
            item.Status = TaskLifecycleStatus.Active;
        }

        var record = TaskItemRecord.FromDomain(item);
        await Db.InsertAsync(record);
        await RemoveTombstoneAsync(item.Id).ConfigureAwait(false);
        await BroadcastTaskUpsertAsync(record.ToDomain()).ConfigureAwait(false);
    }

    public async Task UpdateTaskAsync(TaskItem item)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        item.UpdatedAt = EnsureUtc(nowUtc);
        var record = TaskItemRecord.FromDomain(item);
        await Db.UpdateAsync(record);
        await RemoveTombstoneAsync(item.Id).ConfigureAwait(false);
        await BroadcastTaskUpsertAsync(record.ToDomain()).ConfigureAwait(false);
    }

    public async Task DeleteTaskAsync(string id)
    {
        await AutoResumeDueTasksAsync();
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        bool deleted = false;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItemRecord>(id);
            if (existing == null)
            {
                return;
            }

            conn.Delete(existing);
            var tombstone = new DeletedTaskRecord
            {
                Id = id,
                DeletedAt = EnsureUtc(nowUtc)
            };
            conn.InsertOrReplace(tombstone);
            deleted = true;
        });

        if (!deleted)
        {
            return;
        }

        _logger?.LogSyncEvent("TaskDeleted", id);
        await BroadcastTaskDeletedAsync(id, nowUtc).ConfigureAwait(false);
    }

    // Lifecycle helpers
    public async Task<TaskItem?> MarkTaskDoneAsync(string id)
    {
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

            originalStatus = existing.Status.ToString();
            DateTime doneAt = EnsureUtc(nowUtc);
            existing.LastDoneAt = doneAt;
            existing.CompletedAt = doneAt;
            existing.Status = TaskLifecycleStatus.Completed;
            existing.SnoozedUntil = null;
            existing.NextEligibleAt = ComputeNextEligibleUtc(existing, nowUtc);
            existing.CutInLineMode = CutInLineMode.None;
            existing.UpdatedAt = EnsureUtc(nowUtc);

            conn.Update(existing);
            updated = existing.ToDomain();
        });

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Completed", "Task marked as done");
            await RemoveTombstoneAsync(id).ConfigureAwait(false);
            await BroadcastTaskUpsertAsync(updated).ConfigureAwait(false);
        }

        return updated;
    }

    public async Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration)
    {
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

            originalStatus = existing.Status.ToString();
            DateTime until = EnsureUtc(nowUtc.Add(duration));
            existing.Status = TaskLifecycleStatus.Snoozed;
            existing.SnoozedUntil = until;
            existing.NextEligibleAt = until;
            existing.CompletedAt = null;
            existing.UpdatedAt = EnsureUtc(nowUtc);

            conn.Update(existing);
            updated = existing.ToDomain();
        });

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Snoozed", $"Snoozed for {duration:mm\\:ss}");
            await BroadcastTaskUpsertAsync(updated).ConfigureAwait(false);
        }

        return updated;
    }

    public async Task<TaskItem?> ResumeTaskAsync(string id)
    {
        TaskItem? updated = null;
        string? originalStatus = null;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItemRecord>(id);
            if (existing == null)
            {
                return;
            }

            originalStatus = existing.Status.ToString();
            ApplyResume(existing, nowUtc);
            conn.Update(existing);
            updated = existing.ToDomain();
        });

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Active", "Task resumed");
            await BroadcastTaskUpsertAsync(updated).ConfigureAwait(false);
        }

        return updated;
    }

    private async Task AutoResumeDueTasksAsync()
    {
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
                ApplyResume(task, nowUtc);
                toUpdate.Add(task);
                _logger?.LogStateTransition(task.Id, originalStatus, "Active", "Auto-resumed due to schedule");
            }
        }

        if (toUpdate.Count > 0)
        {
            await Db.UpdateAllAsync(toUpdate);
            _logger?.LogSyncEvent("AutoResume", $"Resumed {toUpdate.Count} task(s)");
            foreach (var record in toUpdate)
            {
                await BroadcastTaskUpsertAsync(record.ToDomain()).ConfigureAwait(false);
            }
        }
    }

    private static void ApplyResume(TaskItemRecord task, DateTime nowUtc)
    {
        task.Status = TaskLifecycleStatus.Active;
        task.SnoozedUntil = null;
        task.NextEligibleAt = null;
        task.CompletedAt = null;
        task.UpdatedAt = EnsureUtc(nowUtc);
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

    internal async Task<bool> ApplyRemoteTaskUpsertAsync(TaskItem task, DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(task);
        await InitializeAsync().ConfigureAwait(false);

        bool changed = false;
        DateTime normalizedUpdated = EnsureUtc(updatedAt);

        await Db.RunInTransactionAsync(conn =>
        {
            var tombstone = conn.Find<DeletedTaskRecord>(task.Id);
            if (tombstone != null)
            {
                DateTime deletedAt = EnsureUtc(tombstone.DeletedAt);
                if (deletedAt >= normalizedUpdated)
                {
                    return;
                }
            }

            var record = TaskItemRecord.FromDomain(task);
            record.UpdatedAt = normalizedUpdated;
            record.CreatedAt = EnsureUtc(record.CreatedAt == default ? normalizedUpdated : record.CreatedAt);

            var existing = conn.Find<TaskItemRecord>(task.Id);
            if (existing == null)
            {
                conn.Insert(record);
                changed = true;
            }
            else
            {
                DateTime currentUpdated = EnsureUtc(existing.UpdatedAt);
                if (currentUpdated >= normalizedUpdated)
                {
                    return;
                }

                conn.InsertOrReplace(record);
                changed = true;
            }

            if (changed && tombstone != null)
            {
                conn.Delete(tombstone);
            }
        });

        if (changed)
        {
            _logger?.LogSyncEvent("RemoteTaskUpsert", task.Id);
        }

        return changed;
    }

    internal async Task<bool> ApplyRemoteDeletionAsync(string id, DateTime deletedAt)
    {
        await InitializeAsync().ConfigureAwait(false);

        bool changed = false;
        DateTime normalizedDeleted = EnsureUtc(deletedAt);

        await Db.RunInTransactionAsync(conn =>
        {
            var tombstone = conn.Find<DeletedTaskRecord>(id);
            if (tombstone != null)
            {
                DateTime existingDeleted = EnsureUtc(tombstone.DeletedAt);
                if (existingDeleted >= normalizedDeleted)
                {
                    return;
                }
            }

            var existing = conn.Find<TaskItemRecord>(id);
            if (existing != null)
            {
                DateTime currentUpdated = EnsureUtc(existing.UpdatedAt);
                if (currentUpdated > normalizedDeleted)
                {
                    return;
                }

                conn.Delete(existing);
                changed = true;
            }

            var updatedTombstone = new DeletedTaskRecord
            {
                Id = id,
                DeletedAt = normalizedDeleted
            };
            conn.InsertOrReplace(updatedTombstone);
            changed = true;
        });

        if (changed)
        {
            _logger?.LogSyncEvent("RemoteTaskDeleted", id);
        }

        return changed;
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

    private bool ShouldBroadcast => _sync != null && _sync.ShouldBroadcastLocalChanges;

    private string LocalDeviceId => _sync?.DeviceId ?? "local";

    private async Task BroadcastTaskUpsertAsync(TaskItem task)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var clone = task.Clone();
        var evt = new TaskUpserted(clone, LocalDeviceId, EnsureUtc(task.UpdatedAt));
        await SafePublishAsync(evt, task.Id).ConfigureAwait(false);
    }

    private async Task BroadcastTaskDeletedAsync(string taskId, DateTime deletedAt)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskDeleted(taskId, LocalDeviceId, EnsureUtc(deletedAt));
        await SafePublishAsync(evt, taskId).ConfigureAwait(false);
    }

    private async Task SafePublishAsync<TEvent>(TEvent domainEvent, string? details)
        where TEvent : DomainEventBase
    {
        try
        {
            await _sync!.PublishAsync(domainEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PublishFailed", details, ex);
        }
    }

    private async Task RemoveTombstoneAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || _db == null)
        {
            return;
        }

        try
        {
            await Db.DeleteAsync<DeletedTaskRecord>(id).ConfigureAwait(false);
        }
        catch
        {
            // best effort cleanup
        }
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

    // Settings
    public async Task<AppSettings> GetSettingsAsync()
    {
        KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey);
        if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
        {
            var defaults = new AppSettings();
            await SetSettingsAsync(defaults); // persist defaults once
            return defaults;
        }

        try
        {
            AppSettings? settings = JsonConvert.DeserializeObject<AppSettings>(kv.Value!);
            return NormalizeSettings(settings ?? new AppSettings());
        }
        catch
        {
            // If parsing fails, return defaults
            return new AppSettings();
        }
    }

    public async Task SetSettingsAsync(AppSettings settings)
    {
        settings = NormalizeSettings(settings);
        string json = JsonConvert.SerializeObject(settings);
        // Upsert
        KeyValueEntity existing = await Db.FindAsync<KeyValueEntity>(SettingsKey);
        if (existing == null)
        {
            var kv = new KeyValueEntity
            {
                Key = SettingsKey,
                Value = json
            };
            await Db.InsertAsync(kv);
        }
        else
        {
            existing.Value = json;
            await Db.UpdateAsync(existing);
        }
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        settings ??= new AppSettings();
        settings.NormalizeWeights();
        settings.MinGapMinutes = Math.Clamp(settings.MinGapMinutes, 1, 24 * 60);
        settings.MaxGapMinutes = Math.Max(settings.MinGapMinutes, settings.MaxGapMinutes);
        settings.ReminderMinutes = Math.Clamp(settings.ReminderMinutes, 1, 6 * 60);
        settings.MaxDailyShuffles = Math.Clamp(settings.MaxDailyShuffles, 1, 24);
        settings.FocusMinutes = Math.Clamp(settings.FocusMinutes, 5, 120);
        settings.BreakMinutes = Math.Clamp(settings.BreakMinutes, 1, 60);
        settings.PomodoroCycles = Math.Clamp(settings.PomodoroCycles, 1, 8);
        settings.RepeatUrgencyPenalty = Math.Clamp(settings.RepeatUrgencyPenalty, 0.0, 2.0);
        settings.SizeBiasStrength = Math.Clamp(settings.SizeBiasStrength, 0.0, 1.0);
        settings.UrgencyDeadlineShare = Math.Clamp(settings.UrgencyDeadlineShare, 0.0, 100.0);
        return settings;
    }

    [Table("TaskDeletion")]
    private sealed class DeletedTaskRecord
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        public DateTime DeletedAt { get; set; }
    }

    private sealed class KeyValueEntity
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;

        public string? Value { get; set; }
    }

    private sealed class TableInfo
    {
        public string name { get; set; } = string.Empty;
    }
}
