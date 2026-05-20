using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence;

namespace ShuffleTask.Tests;

[TestFixture]
public class StorageServicePersistenceTests
{
    private static string CreateDbPath() => Path.Combine(Path.GetTempPath(), $"shuffletask-test-{Guid.NewGuid():N}.db3");

    [Test]
    public async Task Settings_SaveAndReload_PersistsEnvelopeAndData()
    {
        var dbPath = CreateDbPath();
        try
        {
            var storage = new StorageService(TimeProvider.System, dbPath);
            await storage.InitializeAsync();

            await storage.SetSettingsAsync(new AppSettings
            {
                FocusMinutes = 42,
                BreakMinutes = 7,
                PomodoroCycles = 5,
                ImportanceWeight = 33
            });

            var reloaded = new StorageService(TimeProvider.System, dbPath);
            await reloaded.InitializeAsync();

            var loaded = await reloaded.GetSettingsAsync();
            Assert.That(loaded.FocusMinutes, Is.EqualTo(42));
            Assert.That(loaded.BreakMinutes, Is.EqualTo(7));
            Assert.That(loaded.PomodoroCycles, Is.EqualTo(5));
            Assert.That(loaded.ImportanceWeight, Is.EqualTo(33));
        }
        finally
        {
        }
    }

    [Test]
    public async Task Tasks_Restart_PreservesDoneSnoozePriorityDeadlineImportance()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();

        var task = new TaskItem
        {
            Title = "persist me",
            Importance = 5,
            CutInLineMode = CutInLineMode.None,
            Deadline = DateTime.UtcNow.AddDays(1)
        };

        await storage.AddTaskAsync(task);
        task.Importance = 9;
        task.Deadline = DateTime.UtcNow.AddDays(2);
        await storage.UpdateTaskAsync(task);
        await storage.SnoozeTaskAsync(task.Id, TimeSpan.FromMinutes(30));
        await storage.MarkTaskDoneAsync(task.Id);

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var loaded = await reloaded.GetTaskAsync(task.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Status, Is.EqualTo(TaskLifecycleStatus.Completed));
        Assert.That(loaded.Importance, Is.EqualTo(9));
        Assert.That(loaded.Deadline, Is.Not.Null);
        Assert.That(loaded.CutInLineMode, Is.EqualTo(CutInLineMode.None));
    }

    [Test]
    public async Task Tasks_InvalidStatus_IsRecoveredOnLoad()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        var task = new TaskItem { Title = "invalid status" };
        await storage.AddTaskAsync(task);

        dynamic db = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;
        await db.ExecuteAsync($"UPDATE TaskItem SET Status = 999 WHERE Id = '{task.Id}'");

        var loaded = await storage.GetTaskAsync(task.Id);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Status, Is.EqualTo(TaskLifecycleStatus.Active));
    }

    [Test]
    public async Task TaskSchema_Metadata_UpgradesForward()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        dynamic db = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;
        await db.ExecuteAsync("INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('schema_tasks', '0')");

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        dynamic reloadedDb = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reloaded)!;
        var schema = await reloadedDb.ExecuteScalarAsync<string>("SELECT Value FROM KeyValueEntity WHERE Key='schema_tasks'");
        Assert.That(schema, Is.EqualTo("1"));
    }

    [Test]
    public async Task Settings_LoadsLegacyPayload_AndMigrates()
    {
        var dbPath = CreateDbPath();
        try
        {
            var storage = new StorageService(TimeProvider.System, dbPath);
            await storage.InitializeAsync();
            await WriteRawSettingsValueAsync(storage, JsonConvert.SerializeObject(new AppSettings { FocusMinutes = 55 }));

            var loaded = await storage.GetSettingsAsync();
            Assert.That(loaded.FocusMinutes, Is.EqualTo(55));
        }
        finally
        {
        }
    }

    [Test]
    public async Task Settings_Corruption_RecoversToDefaults_AndQuarantines()
    {
        var dbPath = CreateDbPath();
        try
        {
            var storage = new StorageService(TimeProvider.System, dbPath);
            await storage.InitializeAsync();
            await WriteRawSettingsValueAsync(storage, "{bad json");

            var loaded = await storage.GetSettingsAsync();
            Assert.That(loaded.FocusMinutes, Is.EqualTo(new AppSettings().FocusMinutes));

            var hasQuarantine = await HasQuarantineAsync(storage);
            Assert.That(hasQuarantine, Is.True);
        }
        finally
        {
        }
    }

    [Test]
    public async Task Settings_FutureSchemaVersion_ReturnsDefaults_WithoutMutatingStoredValue()
    {
        var dbPath = CreateDbPath();
        try
        {
            var storage = new StorageService(TimeProvider.System, dbPath);
            await storage.InitializeAsync();
            var futurePayload = JsonConvert.SerializeObject(new
            {
                SchemaVersion = 99,
                AppVersion = "future",
                LastSuccessfulSaveUtc = DateTime.UtcNow,
                Data = new AppSettings { FocusMinutes = 88 }
            });
            await WriteRawSettingsValueAsync(storage, futurePayload);

            var loaded = await storage.GetSettingsAsync();
            Assert.That(loaded.FocusMinutes, Is.EqualTo(new AppSettings().FocusMinutes));

            var hasQuarantine = await HasQuarantineAsync(storage);
            Assert.That(hasQuarantine, Is.False);

            var rawAfterLoad = await ReadRawSettingsValueAsync(storage);
            Assert.That(rawAfterLoad, Is.EqualTo(futurePayload));
        }
        finally
        {
        }
    }

    private static async Task WriteRawSettingsValueAsync(StorageService storage, string json)
    {
        dynamic db = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;
        string escaped = json.Replace("'", "''");
        await db.ExecuteAsync($"INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('app_settings', '{escaped}')");
    }

    private static async Task<bool> HasQuarantineAsync(StorageService storage)
    {
        dynamic db = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM KeyValueEntity WHERE Key LIKE 'app_settings_quarantine_%'");
        return count > 0;
    }

    private static async Task<string?> ReadRawSettingsValueAsync(StorageService storage)
    {
        dynamic db = typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;
        return await db.ExecuteScalarAsync<string?>("SELECT Value FROM KeyValueEntity WHERE Key = 'app_settings' LIMIT 1");
    }
}
