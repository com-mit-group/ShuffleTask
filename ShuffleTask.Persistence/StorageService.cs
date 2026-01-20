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
    private const string IntegerSqlType = "INTEGER";

    private readonly TimeProvider _clock;
    private readonly string _dbPath;
    private readonly IShuffleLogger? _logger;
    private SQLiteAsyncConnection? _db;

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
            await AddCol("PeriodDefinitionId", "TEXT", "NULL");
            await AddCol("AdHocStartTime", "TEXT", "NULL");
            await AddCol("AdHocEndTime", "TEXT", "NULL");
            await AddCol("AdHocWeekdays", IntegerSqlType, "NULL");
            await AddCol("AdHocIsAllDay", IntegerSqlType, "0");
            await AddCol("AdHocMode", IntegerSqlType, "0");
            await AddCol("AutoShuffleAllowed", IntegerSqlType, "1");
            await AddCol("CustomStartTime", "TEXT", "NULL");
            await AddCol("CustomEndTime", "TEXT", "NULL");
            await AddCol("CustomWeekdays", IntegerSqlType, "NULL");
            await AddCol("Paused", IntegerSqlType, "0");
            await AddCol("CreatedAt", "TEXT", "CURRENT_TIMESTAMP");
            await AddCol("UpdatedAt", "TEXT", "CURRENT_TIMESTAMP");
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
            await AddCol("EventVersion", IntegerSqlType, "0");
            await AddCol("DeviceId", "TEXT", "''");
            await AddCol("UserId", "TEXT", "NULL");
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
        var task = record?.ToDomain();
        await ApplyPeriodDefinitionAsync(task);
        return task;
    }

    public async Task AddTaskAsync(TaskItem item)
    {
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
        await Db.InsertAsync(record);
    }

    public async Task UpdateTaskAsync(TaskItem item)
    {
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
        if (existing == null)
        {
            await Db.InsertAsync(record);
            return;
        }

        await Db.UpdateAsync(record);
    }

    public async Task DeleteTaskAsync(string id)
    {
        await AutoResumeDueTasksAsync();
        await Db.DeleteAsync<TaskItemRecord>(id);
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
        var existingSettings = await GetExistingSettingsAsync().ConfigureAwait(false);

        if (IsStale(settings, existingSettings))
        {
            return;
        }

        settings.EventVersion = NormalizeVersion(settings.EventVersion, existingSettings?.EventVersion);
        settings.UpdatedAt = NormalizeUpdatedAt(settings.UpdatedAt, existingSettings?.UpdatedAt);
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
        settings.UpdatedAt = EnsureUtc(settings.UpdatedAt == default ? DateTime.UtcNow : settings.UpdatedAt);
        settings.EventVersion = Math.Max(1, settings.EventVersion);
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

            AppSettings? parsed = JsonConvert.DeserializeObject<AppSettings>(kv.Value);
            return parsed is null ? null : NormalizeSettings(parsed);
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
