using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
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
        Assert.That(schema, Is.EqualTo("2"));
    }

    [Test]
    public async Task TaskSchema_VersionOneMigration_PreservesDataCreatesBackupAndRepairsMetadata()
    {
        var dbPath = CreateDbPath();
        var logger = new CapturingShuffleLogger();
        var storage = new StorageService(TimeProvider.System, dbPath, logger);
        await storage.InitializeAsync();
        var task = new TaskItem { Id = "migration-task", Title = "Migrate me", DeviceId = "", UserId = null };
        await storage.AddTaskAsync(task);

        dynamic db = GetDb(storage);
        await db.ExecuteAsync("UPDATE TaskItem SET EventVersion = 0, DeviceId = '', UserId = NULL WHERE Id = 'migration-task'");
        await db.ExecuteAsync("INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('schema_tasks', '1')");

        var reloaded = new StorageService(TimeProvider.System, dbPath, logger);
        await reloaded.InitializeAsync();
        dynamic reloadedDb = GetDb(reloaded);

        var schema = await reloadedDb.ExecuteScalarAsync<string>("SELECT Value FROM KeyValueEntity WHERE Key='schema_tasks'");
        var eventVersion = await reloadedDb.ExecuteScalarAsync<int>("SELECT EventVersion FROM TaskItem WHERE Id='migration-task'");
        var deviceId = await reloadedDb.ExecuteScalarAsync<string>("SELECT DeviceId FROM TaskItem WHERE Id='migration-task'");
        var backupExists = Directory.GetFiles(Path.GetDirectoryName(dbPath)!, $"{Path.GetFileName(dbPath)}.backup.pre-tasks-migration.*.db3").Length > 0;

        Assert.Multiple(() =>
        {
            Assert.That(schema, Is.EqualTo("2"));
            Assert.That(eventVersion, Is.EqualTo(1));
            Assert.That(deviceId, Is.Not.Empty);
            Assert.That(backupExists, Is.True);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceMigrationStarted" && e.Details?.Contains("domain=tasks") == true), Is.True);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceMigrationCompleted" && e.Details?.Contains("domain=tasks") == true), Is.True);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceBackupCreated"), Is.True);
        });
    }

    [Test]
    public async Task TaskSchema_MigrationFailure_RollsBackAndPreservesOriginalDataAndBackup()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "migration-failure-task", Title = "Original" });

        dynamic db = GetDb(storage);
        await db.ExecuteAsync("UPDATE TaskItem SET EventVersion = 0 WHERE Id = 'migration-failure-task'");
        await db.ExecuteAsync("INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('schema_tasks', '1')");

        var fault = new ThrowingFaultInjector { FailOperation = "migration.tasks" };
        var reloaded = new StorageService(TimeProvider.System, dbPath, faultInjector: fault);
        Assert.ThrowsAsync<InvalidOperationException>(() => reloaded.InitializeAsync());

        dynamic failedDb = GetDb(reloaded);
        var schema = await failedDb.ExecuteScalarAsync<string>("SELECT Value FROM KeyValueEntity WHERE Key='schema_tasks'");
        var eventVersion = await failedDb.ExecuteScalarAsync<int>("SELECT EventVersion FROM TaskItem WHERE Id='migration-failure-task'");
        var title = await failedDb.ExecuteScalarAsync<string>("SELECT Title FROM TaskItem WHERE Id='migration-failure-task'");
        var backupExists = Directory.GetFiles(Path.GetDirectoryName(dbPath)!, $"{Path.GetFileName(dbPath)}.backup.pre-tasks-migration.*.db3").Length > 0;

        Assert.Multiple(() =>
        {
            Assert.That(schema, Is.EqualTo("1"));
            Assert.That(eventVersion, Is.EqualTo(0));
            Assert.That(title, Is.EqualTo("Original"));
            Assert.That(backupExists, Is.True);
        });
    }

    [Test]
    public async Task TaskSchema_UnknownFutureSchema_DoesNotExposeOrOverwriteTaskRows()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        var task = new TaskItem { Id = "future-task", Title = "Original" };
        await storage.AddTaskAsync(task);
        dynamic db = GetDb(storage);
        await db.ExecuteAsync("INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('schema_tasks', '99')");

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var loaded = await reloaded.GetTasksAsync();
        await reloaded.UpdateTaskAsync(new TaskItem { Id = "future-task", Title = "Mutated" });

        dynamic reloadedDb = GetDb(reloaded);
        var title = await reloadedDb.ExecuteScalarAsync<string>("SELECT Title FROM TaskItem WHERE Id='future-task'");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Empty);
            Assert.That(title, Is.EqualTo("Original"));
        });
    }

    [Test]
    public async Task StartupValidation_RepairsInvalidTaskInvariantsBeforeExposure()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        var task = new TaskItem { Id = "invalid-task", Title = "Invalid" };
        await storage.AddTaskAsync(task);

        dynamic db = GetDb(storage);
        await db.ExecuteAsync("""
            UPDATE TaskItem
            SET Status = 999,
                Repeat = 3,
                IntervalDays = 0,
                Weekdays = 255,
                Deadline = 'not-a-date',
                UserId = 'user-1',
                DeviceId = 'device-1',
                EventVersion = 0
            WHERE Id = 'invalid-task'
            """);

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var loaded = await reloaded.GetTaskAsync("invalid-task");

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Status, Is.EqualTo(TaskLifecycleStatus.Active));
            Assert.That(loaded.Repeat, Is.EqualTo(RepeatType.Interval));
            Assert.That(loaded.IntervalDays, Is.EqualTo(1));
            Assert.That((int)loaded.Weekdays & ~127, Is.EqualTo(0));
            Assert.That(loaded.Deadline, Is.Null);
            Assert.That(loaded.UserId, Is.EqualTo("user-1"));
            Assert.That(loaded.DeviceId, Is.Null);
            Assert.That(loaded.EventVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartupValidation_RepairsCompletedAndSnoozedLifecycleIncompatibilities()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "done-task", Title = "Done" });
        await storage.AddTaskAsync(new TaskItem { Id = "snoozed-task", Title = "Snoozed" });

        dynamic db = GetDb(storage);
        await db.ExecuteAsync("UPDATE TaskItem SET Status = 2, CompletedAt = NULL, SnoozedUntil = '2999-01-01T00:00:00Z' WHERE Id = 'done-task'");
        await db.ExecuteAsync("UPDATE TaskItem SET Status = 1, SnoozedUntil = NULL, NextEligibleAt = NULL WHERE Id = 'snoozed-task'");

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var done = await reloaded.GetTaskAsync("done-task");
        var snoozed = await reloaded.GetTaskAsync("snoozed-task");

        Assert.Multiple(() =>
        {
            Assert.That(done!.Status, Is.EqualTo(TaskLifecycleStatus.Completed));
            Assert.That(done.CompletedAt, Is.Not.Null);
            Assert.That(done.SnoozedUntil, Is.Null);
            Assert.That(snoozed!.Status, Is.EqualTo(TaskLifecycleStatus.Active));
            Assert.That(snoozed.SnoozedUntil, Is.Null);
            Assert.That(snoozed.NextEligibleAt, Is.Null);
        });
    }

    [Test]
    public async Task StartupValidation_QuarantinesDuplicateTaskIdsCaseInsensitively()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "DUPLICATE", Title = "First" });
        await storage.AddTaskAsync(new TaskItem { Id = "duplicate", Title = "Second" });

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var tasks = await reloaded.GetTasksAsync();
        var quarantineCount = await CountRowsAsync(reloaded, "KeyValueEntity", "Key LIKE 'task_quarantine_duplicate-id_%'");

        Assert.Multiple(() =>
        {
            Assert.That(tasks.Count(t => string.Equals(t.Id, "duplicate", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(quarantineCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DatabaseCorruption_QuarantinesArtifactAndCreatesRecoverableStore()
    {
        var dbPath = CreateDbPath();
        await File.WriteAllTextAsync(dbPath, "not a sqlite database");
        var logger = new CapturingShuffleLogger();

        var storage = new StorageService(TimeProvider.System, dbPath, logger);
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "after-corruption", Title = "Recovered" });
        var loaded = await storage.GetTaskAsync("after-corruption");

        var quarantineExists = Directory.GetFiles(Path.GetDirectoryName(dbPath)!, $"{Path.GetFileName(dbPath)}.quarantine.*.db3").Length > 0;

        Assert.Multiple(() =>
        {
            Assert.That(quarantineExists, Is.True);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceQuarantine" && e.Details?.Contains("Database artifact") == true), Is.True);
        });
    }

    [Test]
    public async Task TaskUpdate_FailedTransaction_RollsBackOriginalTask()
    {
        var dbPath = CreateDbPath();
        var fault = new ThrowingFaultInjector();
        var storage = new StorageService(TimeProvider.System, dbPath, faultInjector: fault);
        await storage.InitializeAsync();
        var task = new TaskItem { Id = "rollback-task", Title = "Original" };
        await storage.AddTaskAsync(task);

        fault.FailOperation = "tasks.update";
        task.Title = "Mutated";
        Assert.ThrowsAsync<InvalidOperationException>(() => storage.UpdateTaskAsync(task));

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var loaded = await reloaded.GetTaskAsync("rollback-task");

        Assert.That(loaded!.Title, Is.EqualTo("Original"));
    }

    [Test]
    public async Task SettingsSave_FailedTransaction_RollsBackOriginalSettings()
    {
        var dbPath = CreateDbPath();
        var fault = new ThrowingFaultInjector();
        var storage = new StorageService(TimeProvider.System, dbPath, faultInjector: fault);
        await storage.InitializeAsync();
        await storage.SetSettingsAsync(new AppSettings { FocusMinutes = 25, BreakMinutes = 5 });

        fault.FailOperation = "settings.save";
        Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetSettingsAsync(new AppSettings { FocusMinutes = 55, BreakMinutes = 9 }));

        var reloaded = new StorageService(TimeProvider.System, dbPath);
        await reloaded.InitializeAsync();
        var loaded = await reloaded.GetSettingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.FocusMinutes, Is.EqualTo(25));
            Assert.That(loaded.BreakMinutes, Is.EqualTo(5));
        });
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

            var raw = await ReadRawSettingsValueAsync(storage);
            Assert.That(raw, Does.Contain("\"SchemaVersion\":2"));
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


    [Test]
    public async Task Settings_FutureSchemaVersion_SetSettings_DoesNotOverwriteStoredValue()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();

        var futurePayload = JsonConvert.SerializeObject(new
        {
            SchemaVersion = 99,
            AppVersion = "future",
            LastSuccessfulSaveUtc = DateTime.UtcNow,
            Data = new AppSettings { FocusMinutes = 88, BreakMinutes = 12 }
        });
        await WriteRawSettingsValueAsync(storage, futurePayload);

        await storage.SetSettingsAsync(new AppSettings { FocusMinutes = 25, BreakMinutes = 5 });

        var rawAfterSave = await ReadRawSettingsValueAsync(storage);
        Assert.That(rawAfterSave, Is.EqualTo(futurePayload));
    }

    [Test]
    public async Task Settings_FutureSchemaVersionWithoutData_SetSettings_DoesNotOverwriteStoredValue()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();

        var futurePayload = JsonConvert.SerializeObject(new
        {
            SchemaVersion = 99,
            AppVersion = "future",
            LastSuccessfulSaveUtc = DateTime.UtcNow,
            Payload = new { FocusMinutes = 88, BreakMinutes = 12 }
        });
        await WriteRawSettingsValueAsync(storage, futurePayload);

        await storage.SetSettingsAsync(new AppSettings { FocusMinutes = 25, BreakMinutes = 5 });

        var rawAfterSave = await ReadRawSettingsValueAsync(storage);
        Assert.That(rawAfterSave, Is.EqualTo(futurePayload));
    }

    [Test]
    public async Task Backup_ExportImport_RestoresTasksSettingsAndPeriodDefinitions()
    {
        var sourcePath = CreateDbPath();
        var targetPath = CreateDbPath();

        var source = new StorageService(TimeProvider.System, sourcePath);
        await source.InitializeAsync();
        await source.SetSettingsAsync(new AppSettings
        {
            FocusMinutes = 42,
            BreakMinutes = 7,
            PomodoroCycles = 5,
            ImportanceWeight = 33
        });
        await source.UpdatePeriodDefinitionAsync(new PeriodDefinition
        {
            Id = "deep-work",
            Name = "Deep work",
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            StartTime = new TimeSpan(8, 30, 0),
            EndTime = new TimeSpan(10, 0, 0),
            Mode = PeriodDefinitionMode.None
        });
        await source.AddTaskAsync(new TaskItem
        {
            Id = "active-task",
            Title = "Active task",
            Description = "keep notes",
            Importance = 5,
            Deadline = DateTime.UtcNow.AddDays(2),
            Repeat = RepeatType.Weekly,
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            PeriodDefinitionId = "deep-work",
            CutInLineMode = CutInLineMode.Once,
            CustomReminderMinutes = 20
        });
        await source.AddTaskAsync(new TaskItem { Id = "snoozed-task", Title = "Snoozed task", Importance = 3 });
        await source.SnoozeTaskAsync("snoozed-task", TimeSpan.FromDays(2));

        string backup = await source.ExportBackupAsync("Android");
        var preview = await source.PreviewBackupImportAsync(backup);

        Assert.Multiple(() =>
        {
            Assert.That(backup, Does.Contain("\"format\": \"ShuffleTaskExport\""));
            Assert.That(backup, Does.Contain("\"formatVersion\": 1"));
            Assert.That(preview.SourcePlatform, Is.EqualTo("Android"));
            Assert.That(preview.TaskCount, Is.EqualTo(2));
        });

        var target = new StorageService(TimeProvider.System, targetPath);
        await target.InitializeAsync();
        await target.AddTaskAsync(new TaskItem { Id = "old-task", Title = "Old task" });
        await target.SetSettingsAsync(new AppSettings { FocusMinutes = 11 });

        await target.ImportBackupAsync(backup);

        var tasks = await target.GetTasksAsync();
        var restoredSettings = await target.GetSettingsAsync();
        var restoredPeriod = await target.GetPeriodDefinitionAsync("deep-work");
        bool safetyBackupExists = Directory.GetFiles(Path.GetDirectoryName(targetPath)!, $"{Path.GetFileName(targetPath)}.backup.pre-import.*.db3").Length > 0;

        Assert.Multiple(() =>
        {
            Assert.That(tasks.Select(t => t.Id), Does.Contain("active-task"));
            Assert.That(tasks.Select(t => t.Id), Does.Contain("snoozed-task"));
            Assert.That(tasks.Select(t => t.Id), Does.Not.Contain("old-task"));
            Assert.That(tasks.Single(t => t.Id == "snoozed-task").Status, Is.EqualTo(TaskLifecycleStatus.Snoozed));
            Assert.That(restoredSettings.FocusMinutes, Is.EqualTo(42));
            Assert.That(restoredSettings.BreakMinutes, Is.EqualTo(7));
            Assert.That(restoredPeriod, Is.Not.Null);
            Assert.That(restoredPeriod!.StartTime, Is.EqualTo(new TimeSpan(8, 30, 0)));
            Assert.That(safetyBackupExists, Is.True);
        });
    }

    [Test]
    public async Task Backup_ImportInvalidJson_DoesNotModifyExistingData()
    {
        var dbPath = CreateDbPath();
        var storage = new StorageService(TimeProvider.System, dbPath);
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "existing-task", Title = "Existing" });
        await storage.SetSettingsAsync(new AppSettings { FocusMinutes = 31 });

        Assert.ThrowsAsync<InvalidDataException>(() => storage.ImportBackupAsync("{bad json"));

        var task = await storage.GetTaskAsync("existing-task");
        var settings = await storage.GetSettingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(task, Is.Not.Null);
            Assert.That(task!.Title, Is.EqualTo("Existing"));
            Assert.That(settings.FocusMinutes, Is.EqualTo(31));
        });
    }

    [Test]
    public async Task Backup_ImportFutureVersion_DoesNotModifyExistingData()
    {
        var source = new StorageService(TimeProvider.System, CreateDbPath());
        await source.InitializeAsync();
        await source.AddTaskAsync(new TaskItem { Id = "source-task", Title = "Source" });
        var json = JObject.Parse(await source.ExportBackupAsync("Android"));
        json["formatVersion"] = 99;

        var target = new StorageService(TimeProvider.System, CreateDbPath());
        await target.InitializeAsync();
        await target.AddTaskAsync(new TaskItem { Id = "existing-task", Title = "Existing" });

        Assert.ThrowsAsync<InvalidDataException>(() => target.ImportBackupAsync(json.ToString(Formatting.None)));

        var tasks = await target.GetTasksAsync();
        Assert.That(tasks.Select(t => t.Id), Is.EquivalentTo(new[] { "existing-task" }));
    }

    [Test]
    public async Task Backup_ImportDuplicateTaskIds_DoesNotModifyExistingData()
    {
        var source = new StorageService(TimeProvider.System, CreateDbPath());
        await source.InitializeAsync();
        await source.AddTaskAsync(new TaskItem { Id = "duplicate-task", Title = "Original" });
        var json = JObject.Parse(await source.ExportBackupAsync("Android"));
        var tasks = (JArray)json["data"]!["tasks"]!;
        tasks.Add(tasks[0]!.DeepClone());

        var target = new StorageService(TimeProvider.System, CreateDbPath());
        await target.InitializeAsync();
        await target.AddTaskAsync(new TaskItem { Id = "existing-task", Title = "Existing" });

        Assert.ThrowsAsync<InvalidDataException>(() => target.ImportBackupAsync(json.ToString(Formatting.None)));

        var existing = await target.GetTaskAsync("existing-task");
        Assert.That(existing, Is.Not.Null);
    }

    [Test]
    public async Task Backup_ImportOlderSchemaVersion_IsAcceptedWhenCurrentModelsCanReadIt()
    {
        var source = new StorageService(TimeProvider.System, CreateDbPath());
        await source.InitializeAsync();
        await source.SetSettingsAsync(new AppSettings { FocusMinutes = 27 });
        await source.AddTaskAsync(new TaskItem { Id = "portable-task", Title = "Portable" });

        var json = JObject.Parse(await source.ExportBackupAsync("Android"));
        json["schemaVersion"] = 1;
        json["taskSchemaVersion"] = 1;
        json["settingsSchemaVersion"] = 1;
        json["periodSchemaVersion"] = 1;

        var target = new StorageService(TimeProvider.System, CreateDbPath());
        await target.InitializeAsync();
        await target.ImportBackupAsync(json.ToString(Formatting.None));

        var task = await target.GetTaskAsync("portable-task");
        var settings = await target.GetSettingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(task, Is.Not.Null);
            Assert.That(settings.FocusMinutes, Is.EqualTo(27));
        });
    }

    private static async Task WriteRawSettingsValueAsync(StorageService storage, string json)
    {
        dynamic db = GetDb(storage);
        string escaped = json.Replace("'", "''");
        await db.ExecuteAsync($"INSERT OR REPLACE INTO KeyValueEntity(Key, Value) VALUES ('app_settings', '{escaped}')");
    }

    private static async Task<bool> HasQuarantineAsync(StorageService storage)
    {
        dynamic db = GetDb(storage);
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM KeyValueEntity WHERE Key LIKE 'app_settings_quarantine_%'");
        return count > 0;
    }

    private static async Task<string?> ReadRawSettingsValueAsync(StorageService storage)
    {
        dynamic db = GetDb(storage);
        return await db.ExecuteScalarAsync<string?>("SELECT Value FROM KeyValueEntity WHERE Key = 'app_settings' LIMIT 1");
    }

    private static dynamic GetDb(StorageService storage)
        => typeof(StorageService).GetProperty("Db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(storage)!;

    private static async Task<int> CountRowsAsync(StorageService storage, string table, string where)
    {
        dynamic db = GetDb(storage);
        return await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {table} WHERE {where}");
    }

    private sealed class ThrowingFaultInjector : IStorageFaultInjector
    {
        public string? FailOperation { get; set; }

        public void BeforeCommit(string operation)
        {
            if (string.Equals(operation, FailOperation, StringComparison.Ordinal))
            {
                FailOperation = null;
                throw new InvalidOperationException($"Injected failure for {operation}");
            }
        }
    }

    private sealed class CapturingShuffleLogger : IShuffleLogger
    {
        public List<(string EventType, string? Details)> Events { get; } = new();

        public void LogTaskSelection(string taskId, string taskTitle, string reason, int candidateCount, TimeSpan nextGap)
        {
        }

        public void LogTimerEvent(string eventType, string? taskId = null, TimeSpan? duration = null, string? reason = null)
        {
        }

        public void LogStateTransition(string taskId, string fromStatus, string toStatus, string? reason = null)
        {
        }

        public void LogSyncEvent(string eventType, string? details = null, Exception? exception = null)
        {
            Events.Add((eventType, details));
        }

        public void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null)
        {
        }

        public void LogOperation(LogLevel level, string operation, string? details = null, Exception? exception = null)
        {
        }
    }
}
