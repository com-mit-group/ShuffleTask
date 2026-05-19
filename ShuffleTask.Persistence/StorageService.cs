using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public class StorageService : IStorageService
{
    private const string SettingsKey = "app_settings";
    private const string TaskSchemaVersionKey = "schema_tasks";
    private const string PeriodSchemaVersionKey = "schema_periods";
    private const int CurrentSettingsSchemaVersion = 2;
    private const int CurrentTaskSchemaVersion = 1;
    private const int CurrentPeriodSchemaVersion = 1;
    private const string IntegerSqlType = "INTEGER";
    private readonly record struct SchemaColumn(string Name, string SqlType, string DefaultSql);

    private readonly TimeProvider _clock;
    private readonly string _dbPath;
    private readonly IShuffleLogger? _logger;
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly SemaphoreSlim _taskLock = new(1, 1);

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

    public async Task InitializeAsync()
    {
        if (_db != null)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

        await _db.CreateTableAsync<TaskItemRecord>();
        await _db.CreateTableAsync<PeriodDefinitionRecord>();
        await _db.CreateTableAsync<KeyValueEntity>();

        // Ensure schema has all columns; add columns if missing with sensible defaults.
        await EnsureTaskSchemaAsync();
        await EnsurePeriodDefinitionSchemaAsync();
        await EnsurePersistenceMetadataAsync();
        await EnsurePresetPeriodDefinitionsAsync();
    }

    private async Task EnsurePersistenceMetadataAsync()
    {
        await UpsertSchemaVersionKeyAsync(TaskSchemaVersionKey, CurrentTaskSchemaVersion).ConfigureAwait(false);
        await UpsertSchemaVersionKeyAsync(PeriodSchemaVersionKey, CurrentPeriodSchemaVersion).ConfigureAwait(false);
    }

    private async Task UpsertSchemaVersionKeyAsync(string key, int currentVersion)
    {
        var row = await Db.FindAsync<KeyValueEntity>(key).ConfigureAwait(false);
        if (row == null)
        {
            await Db.InsertAsync(new KeyValueEntity { Key = key, Value = currentVersion.ToString() }).ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(row.Value, out int stored) || stored <= 0)
        {
            stored = 1;
        }

        if (stored > currentVersion)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"{key}={stored}; supported={currentVersion}");
            return;
        }

        if (stored < currentVersion)
        {
            _logger?.LogSyncEvent("PersistenceMigration", $"{key} migrated {stored}->{currentVersion}");
            row.Value = currentVersion.ToString();
            await Db.UpdateAsync(row).ConfigureAwait(false);
        }
    }

    private async Task EnsureTaskSchemaAsync()
    {
        await EnsureSchemaAsync(
            "TaskItem",
            new[]
            {
                new SchemaColumn("Title", "TEXT", "''"),
                new SchemaColumn("Importance", IntegerSqlType, "1"),
                new SchemaColumn("SizePoints", "REAL", "3"),
                new SchemaColumn("Deadline", "TEXT", "NULL"),
                new SchemaColumn("Repeat", IntegerSqlType, "0"),
                new SchemaColumn("Weekdays", IntegerSqlType, "0"),
                new SchemaColumn("IntervalDays", IntegerSqlType, "0"),
                new SchemaColumn("LastDoneAt", "TEXT", "NULL"),
                new SchemaColumn("AllowedPeriod", IntegerSqlType, "0"),
                new SchemaColumn("PeriodDefinitionId", "TEXT", "NULL"),
                new SchemaColumn("AdHocStartTime", "TEXT", "NULL"),
                new SchemaColumn("AdHocEndTime", "TEXT", "NULL"),
                new SchemaColumn("AdHocWeekdays", IntegerSqlType, "NULL"),
                new SchemaColumn("AdHocIsAllDay", IntegerSqlType, "0"),
                new SchemaColumn("AdHocMode", IntegerSqlType, "0"),
                new SchemaColumn("AutoShuffleAllowed", IntegerSqlType, "1"),
                new SchemaColumn("CustomStartTime", "TEXT", "NULL"),
                new SchemaColumn("CustomEndTime", "TEXT", "NULL"),
                new SchemaColumn("CustomWeekdays", IntegerSqlType, "NULL"),
                new SchemaColumn("Paused", IntegerSqlType, "0"),
                new SchemaColumn("CreatedAt", "TEXT", "CURRENT_TIMESTAMP"),
                new SchemaColumn("UpdatedAt", "TEXT", "CURRENT_TIMESTAMP"),
                new SchemaColumn("Description", "TEXT", "''"),
                new SchemaColumn("Status", IntegerSqlType, "0"),
                new SchemaColumn("SnoozedUntil", "TEXT", "NULL"),
                new SchemaColumn("CompletedAt", "TEXT", "NULL"),
                new SchemaColumn("NextEligibleAt", "TEXT", "NULL"),
                new SchemaColumn("CustomTimerMode", IntegerSqlType, "NULL"),
                new SchemaColumn("CustomReminderMinutes", IntegerSqlType, "NULL"),
                new SchemaColumn("CustomFocusMinutes", IntegerSqlType, "NULL"),
                new SchemaColumn("CustomBreakMinutes", IntegerSqlType, "NULL"),
                new SchemaColumn("CustomPomodoroCycles", IntegerSqlType, "NULL"),
                new SchemaColumn("CutInLineMode", IntegerSqlType, "0"),
                new SchemaColumn("EventVersion", IntegerSqlType, "0"),
                new SchemaColumn("DeviceId", "TEXT", "''"),
                new SchemaColumn("UserId", "TEXT", "NULL")
            });
    }

    private async Task EnsurePresetPeriodDefinitionsAsync()
    {
        var presets = PeriodDefinitionCatalog.CreatePresetDefinitions();
        var presetIds = presets.Select(preset => preset.Id).ToList();
        if (presetIds.Count == 0)
        {
            return;
        }

        var existing = await Db.Table<PeriodDefinitionRecord>()
            .Where(record => presetIds.Contains(record.Id))
            .ToListAsync();
        var existingIds = new HashSet<string>(existing.Select(record => record.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets)
        {
            var record = PeriodDefinitionRecord.FromDomain(preset);

            if (existingIds.Contains(preset.Id))
            {
                var existingRecord = existing.First(existingItem =>
                    string.Equals(existingItem.Id, preset.Id, StringComparison.OrdinalIgnoreCase));

                bool needsUpdate = existingRecord.Mode != record.Mode;
                bool shouldClearTimes = record.StartTime is null && record.EndTime is null
                    && (existingRecord.StartTime.HasValue || existingRecord.EndTime.HasValue);

                if (needsUpdate || shouldClearTimes)
                {
                    existingRecord.Mode = record.Mode;
                    existingRecord.StartTime = record.StartTime;
                    existingRecord.EndTime = record.EndTime;
                    await Db.UpdateAsync(existingRecord);
                }

                continue;
            }

            await Db.InsertAsync(record);
        }
    }

    private async Task EnsurePeriodDefinitionSchemaAsync()
    {
        await EnsureSchemaAsync(
            "PeriodDefinition",
            new[]
            {
                new SchemaColumn("Name", "TEXT", "''"),
                new SchemaColumn("Weekdays", IntegerSqlType, "0"),
                new SchemaColumn("StartTime", "TEXT", "NULL"),
                new SchemaColumn("EndTime", "TEXT", "NULL"),
                new SchemaColumn("IsAllDay", IntegerSqlType, "0"),
                new SchemaColumn("Mode", IntegerSqlType, "0")
            });
    }

    private async Task EnsureSchemaAsync(string tableName, IReadOnlyCollection<SchemaColumn> columns)
    {
        try
        {
            var infos = await Db.QueryAsync<TableInfo>($"PRAGMA table_info({tableName});");
            var cols = new HashSet<string>(infos.Select(i => i.name), StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                if (cols.Contains(column.Name))
                {
                    continue;
                }

                string alter = $"ALTER TABLE {tableName} ADD COLUMN {column.Name} {column.SqlType} DEFAULT {column.DefaultSql}";
                await Db.ExecuteAsync(alter);
            }
        }
        catch
        {
            // best-effort; ignore migration errors
        }
    }

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
        return tasks;
    }

    public async Task<TaskItem?> GetTaskAsync(string id)
    {
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
        return task;
    }

    public async Task AddTaskAsync(TaskItem item)
    {
        await _taskLock.WaitAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveStarted", "Adding task");
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
            await Db.RunInTransactionAsync(conn => conn.Insert(record)).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"Task added id={record.Id}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public async Task UpdateTaskAsync(TaskItem item)
    {
        await _taskLock.WaitAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveStarted", $"Updating task id={item.Id}");
        var existing = await Db.FindAsync<TaskItemRecord>(item.Id);

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
        try
        {
            await Db.RunInTransactionAsync(conn =>
            {
                if (existing == null)
                {
                    conn.Insert(record);
                    return;
                }

                conn.Update(record);
            }).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"Task updated id={item.Id}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public async Task DeleteTaskAsync(string id)
    {
        await AutoResumeDueTasksAsync();
        await _taskLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Db.RunInTransactionAsync(conn => conn.Delete<TaskItemRecord>(id)).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"Task deleted id={id}");
        }
        finally
        {
            _taskLock.Release();
        }
    }

    // Period definitions CRUD
    public async Task<List<PeriodDefinition>> GetPeriodDefinitionsAsync()
    {
        var records = await Db.Table<PeriodDefinitionRecord>()
                              .OrderBy(r => r.Name)
                              .ToListAsync();
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<PeriodDefinition?> GetPeriodDefinitionAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var record = await Db.Table<PeriodDefinitionRecord>()
                              .Where(r => r.Id == id)
                              .FirstOrDefaultAsync();
        return record?.ToDomain();
    }

    public async Task AddPeriodDefinitionAsync(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        var record = PeriodDefinitionRecord.FromDomain(definition);
        await Db.InsertAsync(record);
    }

    public async Task UpdatePeriodDefinitionAsync(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        var record = PeriodDefinitionRecord.FromDomain(definition);
        int updated = await Db.UpdateAsync(record);
        if (updated == 0)
        {
            await Db.InsertAsync(record);
        }
    }

    public async Task DeletePeriodDefinitionAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        await Db.DeleteAsync<PeriodDefinitionRecord>(id);
    }

    public async Task<int> MigrateDeviceTasksToUserAsync(string deviceId, string userId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(userId))
        {
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
        });

        return updated;
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
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Completed", "Task marked as done");
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
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Snoozed", $"Snoozed for {duration:mm\\:ss}");
        }

        return updated;
    }

    public async Task<TaskItem?> ResumeTaskAsync(string id)
    {
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
            updated = existing.ToDomain();
        });

        await ApplyPeriodDefinitionAsync(updated);

        if (updated != null && originalStatus != null)
        {
            _logger?.LogStateTransition(id, originalStatus, "Active", "Task resumed");
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
            await Db.UpdateAllAsync(toUpdate);
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

    // Settings
    public async Task<AppSettings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "Loading settings state");

        try
        {
            KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
            if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
            {
                var defaults = new AppSettings();
                await SetSettingsInternalAsync(defaults).ConfigureAwait(false);
                _logger?.LogSyncEvent("PersistenceRecovery", "Settings missing. Persisted defaults.");
                return defaults;
            }

            try
            {
                var payload = DeserializeSettingsPayload(kv.Value!);
                var migrated = MigrateSettingsPayload(payload);
                var normalized = NormalizeSettings(migrated.Data ?? new AppSettings());

                _logger?.LogSyncEvent("PersistenceLoadCompleted", $"schema={migrated.SchemaVersion}; durationMs={stopwatch.ElapsedMilliseconds}");
                return normalized;
            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"schemaVersion={ex.SchemaVersion}; returning defaults read-only; durationMs={stopwatch.ElapsedMilliseconds}");
                return new AppSettings();
            }
            catch (Exception ex)
            {
                await QuarantineSettingsValueAsync(kv.Value!, ex.Message).ConfigureAwait(false);
                var defaults = new AppSettings();
                await SetSettingsInternalAsync(defaults).ConfigureAwait(false);
                _logger?.LogSyncEvent("PersistenceRecovery", $"Corrupted settings recovered with defaults; durationMs={stopwatch.ElapsedMilliseconds}");
                return defaults;
            }
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task SetSettingsAsync(AppSettings settings)
    {
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveStarted", "Saving settings state");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await SetSettingsInternalAsync(settings).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"durationMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task SetSettingsInternalAsync(AppSettings settings)
    {
        settings = NormalizeSettings(settings);
        var existingSettings = await GetExistingSettingsAsync().ConfigureAwait(false);

        if (IsStale(settings, existingSettings))
        {
            return;
        }

        settings.EventVersion = NormalizeVersion(settings.EventVersion, existingSettings?.EventVersion);
        settings.UpdatedAt = NormalizeUpdatedAt(settings.UpdatedAt, existingSettings?.UpdatedAt);

        var payload = new SettingsPayload
        {
            SchemaVersion = CurrentSettingsSchemaVersion,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            LastSuccessfulSaveUtc = _clock.GetUtcNow().UtcDateTime,
            Data = settings
        };

        string json = JsonConvert.SerializeObject(payload);
        KeyValueEntity existing = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
        if (existing == null)
        {
            await Db.InsertAsync(new KeyValueEntity { Key = SettingsKey, Value = json }).ConfigureAwait(false);
            return;
        }

        existing.Value = json;
        await Db.UpdateAsync(existing).ConfigureAwait(false);
    }


    private SettingsPayload DeserializeSettingsPayload(string json)
    {
        var envelope = JsonConvert.DeserializeObject<SettingsPayload>(json);
        if (envelope?.Data != null)
        {
            return envelope;
        }

        var legacy = JsonConvert.DeserializeObject<AppSettings>(json);
        if (legacy == null)
        {
            throw new InvalidOperationException("Settings payload is invalid.");
        }

        return new SettingsPayload
        {
            SchemaVersion = 1,
            AppVersion = "legacy",
            LastSuccessfulSaveUtc = _clock.GetUtcNow().UtcDateTime,
            Data = legacy
        };
    }

    private SettingsPayload MigrateSettingsPayload(SettingsPayload payload)
    {
        if (payload.SchemaVersion > CurrentSettingsSchemaVersion)
        {
            throw new UnsupportedSettingsSchemaException(payload.SchemaVersion);
        }

        if (payload.SchemaVersion <= 0)
        {
            payload.SchemaVersion = 1;
        }

        if (payload.SchemaVersion < CurrentSettingsSchemaVersion)
        {
            _logger?.LogSyncEvent("PersistenceMigration", $"Migrated settings schema {payload.SchemaVersion} -> {CurrentSettingsSchemaVersion}");
            payload.SchemaVersion = CurrentSettingsSchemaVersion;
        }

        return payload;
    }

    private async Task QuarantineSettingsValueAsync(string value, string reason)
    {
        string suffix = _clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmss");
        string key = $"{SettingsKey}_quarantine_{suffix}";
        await Db.InsertOrReplaceAsync(new KeyValueEntity { Key = key, Value = value }).ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceQuarantine", $"Stored corrupt settings as {key}; reason={reason}");
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        settings ??= new AppSettings();
        settings.NormalizeWeights();
        settings.UpdatedAt = EnsureUtc(settings.UpdatedAt == default ? DateTime.UtcNow : settings.UpdatedAt);
        settings.EventVersion = Math.Max(1, settings.EventVersion);
        settings.MinGapMinutes = Math.Clamp(settings.MinGapMinutes, 1, 24 * 60);
        settings.MaxGapMinutes = Math.Max(settings.MinGapMinutes, settings.MaxGapMinutes);
        settings.ReminderMinutes = Math.Clamp(settings.ReminderMinutes, 1, 6 * 60);
        int maxDailyShufflesUpperBound = (24 * 60) / settings.MinGapMinutes;
        settings.MaxDailyShuffles = Math.Clamp(settings.MaxDailyShuffles, 0, maxDailyShufflesUpperBound);
        settings.FocusMinutes = Math.Clamp(settings.FocusMinutes, 5, 120);
        settings.BreakMinutes = Math.Clamp(settings.BreakMinutes, 1, 60);
        settings.PomodoroCycles = Math.Clamp(settings.PomodoroCycles, 1, 8);
        settings.RepeatUrgencyPenalty = Math.Clamp(settings.RepeatUrgencyPenalty, 0.0, 2.0);
        settings.SizeBiasStrength = Math.Clamp(settings.SizeBiasStrength, 0.0, 1.0);
        settings.UrgencyDeadlineShare = Math.Clamp(settings.UrgencyDeadlineShare, 0.0, 100.0);
        settings.Network ??= NetworkOptions.CreateDefault();
        settings.Network.Normalize();
        return settings;
    }

    private async Task<AppSettings?> GetExistingSettingsAsync()
    {
        try
        {
            KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
            if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
            {
                return null;
            }

            var payload = DeserializeSettingsPayload(kv.Value);
            var migrated = MigrateSettingsPayload(payload);
            return migrated.Data is null ? null : NormalizeSettings(migrated.Data);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsStale(AppSettings candidate, AppSettings? existing)
    {
        if (existing is null)
        {
            return false;
        }

        if (candidate.EventVersion > 0 && candidate.EventVersion < existing.EventVersion)
        {
            return true;
        }

        return candidate.EventVersion <= existing.EventVersion && candidate.UpdatedAt <= existing.UpdatedAt;
    }

    private static int NormalizeVersion(int candidate, int? existing)
    {
        if (candidate > 0)
        {
            return candidate;
        }

        if (existing.HasValue && existing.Value > 0)
        {
            return existing.Value + 1;
        }

        return 1;
    }

    private static DateTime NormalizeUpdatedAt(DateTime candidate, DateTime? existing)
    {
        if (candidate != default)
        {
            return candidate;
        }

        if (existing.HasValue)
        {
            return existing.Value;
        }

        return DateTime.UtcNow;
    }

    private sealed class SettingsPayload
    {
        public int SchemaVersion { get; set; }

        public string AppVersion { get; set; } = string.Empty;

        public DateTime LastSuccessfulSaveUtc { get; set; }

        public AppSettings? Data { get; set; }
    }

    private sealed class UnsupportedSettingsSchemaException : InvalidOperationException
    {
        public UnsupportedSettingsSchemaException(int schemaVersion)
            : base($"Unsupported future settings schema version: {schemaVersion}")
        {
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
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
