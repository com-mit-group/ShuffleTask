using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Models;

namespace ShuffleTask.Services;

public class StorageService
{
    private const string DatabaseFileName = "shuffletask.db3";
    private const string SettingsKey = "app_settings";

    private readonly string _dbPath;
    private SQLiteAsyncConnection? _db;

    public StorageService()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
    }

    public async Task InitializeAsync()
    {
        if (_db != null)
            return;

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
            await AddCol("Importance", "INTEGER", "1");
            await AddCol("SizePoints", "REAL", "3");
            await AddCol("Deadline", "TEXT", "NULL");
            await AddCol("Repeat", "INTEGER", "0");
            await AddCol("Weekdays", "INTEGER", "0");
            await AddCol("IntervalDays", "INTEGER", "0");
            await AddCol("LastDoneAt", "TEXT", "NULL");
            await AddCol("AllowedPeriod", "INTEGER", "0");
            await AddCol("Paused", "INTEGER", "0");
            await AddCol("CreatedAt", "TEXT", "CURRENT_TIMESTAMP");
            await AddCol("Description", "TEXT", "''");
        }
        catch
        {
            // best-effort; ignore migration errors
        }
    }

    private SQLiteAsyncConnection Db => _db ?? throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");

    // Tasks CRUD
    public Task<List<TaskItem>> GetTasksAsync()
    {
        return Db.Table<TaskItem>()
                 .OrderByDescending(t => t.CreatedAt)
                 .ToListAsync();
    }

    public Task<TaskItem> GetTaskAsync(string id)
    {
        return Db.Table<TaskItem>().Where(t => t.Id == id).FirstOrDefaultAsync();
    }

    public async Task AddTaskAsync(TaskItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            item.Id = Guid.NewGuid().ToString("n");
        }
        if (item.CreatedAt == default)
        {
            item.CreatedAt = DateTime.UtcNow;
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

    // Mark done helper
    public async Task MarkTaskDoneAsync(string id)
    {
        TaskItem existing = await GetTaskAsync(id);
        if (existing != null)
        {
            existing.LastDoneAt = DateTime.UtcNow;
            await Db.UpdateAsync(existing);
        }
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
            await Db.UpdateAsync(kv);
        }
    }

    // Local key-value table for settings JSON
    [Table("KeyValue")]
    internal class KeyValueEntity
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    private class TableInfo { public int cid { get; set; } public string name { get; set; } = ""; public string type { get; set; } = ""; public int notnull { get; set; } public string? dflt_value { get; set; } public int pk { get; set; } }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        var defaults = new AppSettings();

        if (settings.ReminderMinutes <= 0)
        {
            settings.ReminderMinutes = defaults.ReminderMinutes;
        }

        if (settings.FocusMinutes <= 0)
        {
            settings.FocusMinutes = defaults.FocusMinutes;
        }

        if (settings.BreakMinutes <= 0)
        {
            settings.BreakMinutes = defaults.BreakMinutes;
        }

        if (settings.PomodoroCycles <= 0)
        {
            settings.PomodoroCycles = defaults.PomodoroCycles;
        }

        return settings;
    }
}
