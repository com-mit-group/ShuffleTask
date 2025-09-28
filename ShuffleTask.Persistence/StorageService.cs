using System.Globalization;
using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Persistence;

public class StorageService : IStorageService
{
    private const string SettingsKey = "app_settings";
    private const string IntegerSqlType = "INTEGER";

    private readonly TimeProvider _clock;
    private readonly string _dbPath;
    private SQLiteAsyncConnection? _db;

    public StorageService(TimeProvider clock, string databasePath)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        _dbPath = databasePath;
    }

    public async Task InitializeAsync()
    {
        if (_db != null)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

        await _db.CreateTableAsync<TaskItem>();
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
            await AddCol("Paused", IntegerSqlType, "0");
            await AddCol("CreatedAt", "TEXT", "CURRENT_TIMESTAMP");
            await AddCol("Description", "TEXT", "''");
            await AddCol("Status", IntegerSqlType, "0");
            await AddCol("SnoozedUntil", "TEXT", "NULL");
            await AddCol("CompletedAt", "TEXT", "NULL");
            await AddCol("NextEligibleAt", "TEXT", "NULL");
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

        return await Db.Table<TaskItem>()
                       .OrderByDescending(t => t.CreatedAt)
                       .ToListAsync();
    }

    public async Task<TaskItem?> GetTaskAsync(string id)
    {
        await AutoResumeDueTasksAsync();

        return await Db.Table<TaskItem>()
                       .Where(t => t.Id == id)
                       .FirstOrDefaultAsync();
    }

    public async Task AddTaskAsync(TaskItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            item.Id = Guid.NewGuid().ToString("n");
        }
        if (item.CreatedAt == default)
        {
            item.CreatedAt = _clock.GetUtcNow().UtcDateTime;
        }
        if (item.Status != TaskLifecycleStatus.Active &&
            item.Status != TaskLifecycleStatus.Snoozed &&
            item.Status != TaskLifecycleStatus.Completed)
        {
            item.Status = TaskLifecycleStatus.Active;
        }
        await Db.InsertAsync(item);
    }

    public async Task UpdateTaskAsync(TaskItem item)
    {
        await Db.UpdateAsync(item);
    }

    public async Task DeleteTaskAsync(string id)
    {
        var existing = await GetTaskAsync(id);
        if (existing != null)
        {
            await Db.DeleteAsync(existing);
        }
    }

    // Lifecycle helpers
    public async Task<TaskItem?> MarkTaskDoneAsync(string id)
    {
        TaskItem? updated = null;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItem>(id);
            if (existing == null)
            {
                return;
            }

            DateTime doneAt = EnsureUtc(nowUtc);
            existing.LastDoneAt = doneAt;
            existing.CompletedAt = doneAt;
            existing.Status = TaskLifecycleStatus.Completed;
            existing.SnoozedUntil = null;
            existing.NextEligibleAt = ComputeNextEligibleUtc(existing, nowUtc);

            conn.Update(existing);
            updated = existing;
        });

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

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItem>(id);
            if (existing == null)
            {
                return;
            }

            DateTime until = EnsureUtc(nowUtc.Add(duration));
            existing.Status = TaskLifecycleStatus.Snoozed;
            existing.SnoozedUntil = until;
            existing.NextEligibleAt = until;
            existing.CompletedAt = null;

            conn.Update(existing);
            updated = existing;
        });

        return updated;
    }

    public async Task<TaskItem?> ResumeTaskAsync(string id)
    {
        TaskItem? updated = null;

        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<TaskItem>(id);
            if (existing == null)
            {
                return;
            }

            ApplyResume(existing);
            conn.Update(existing);
            updated = existing;
        });

        return updated;
    }

    private async Task AutoResumeDueTasksAsync()
    {
        var pending = await Db.Table<TaskItem>()
                               .Where(t => t.Status != TaskLifecycleStatus.Active && t.NextEligibleAt != null)
                               .ToListAsync();

        if (pending.Count == 0)
        {
            return;
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        List<TaskItem> toUpdate = new();

        foreach (var task in pending)
        {
            DateTime nextUtc = EnsureUtc(task.NextEligibleAt!.Value);
            if (nextUtc <= nowUtc)
            {
                ApplyResume(task);
                toUpdate.Add(task);
            }
        }

        if (toUpdate.Count > 0)
        {
            await Db.UpdateAllAsync(toUpdate);
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
        var kv = new KeyValueEntity
        {
            Key = SettingsKey,
            Value = json
        };

        // Upsert
        KeyValueEntity existing = await Db.FindAsync<KeyValueEntity>(SettingsKey);
        if (existing == null)
        {
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
