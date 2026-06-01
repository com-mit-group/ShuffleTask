using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private const string ExportFormat = "ShuffleTaskExport";
    private const int CurrentExportFormatVersion = 1;

    public async Task<string> ExportBackupAsync(string? sourcePlatform = null)
    {
        await InitializeAsync().ConfigureAwait(false);

        if (_taskSchemaIsFuture || _periodSchemaIsFuture || await HasFutureSettingsSchemaAsync().ConfigureAwait(false))
        {
            throw new InvalidDataException("This backup was created by a newer version of ShuffleTask and cannot be exported by this app.");
        }

        await ValidateAndRecoverTaskTableAsync().ConfigureAwait(false);
        await ValidateAndRecoverPeriodTableAsync().ConfigureAwait(false);

        var tasks = await Db.Table<TaskItemRecord>()
            .OrderBy(record => record.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        var periodDefinitions = await Db.Table<PeriodDefinitionRecord>()
            .OrderBy(record => record.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        var settings = await GetSettingsAsync().ConfigureAwait(false);

        var envelope = new ShuffleTaskExportEnvelope
        {
            Format = ExportFormat,
            FormatVersion = CurrentExportFormatVersion,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
            SourcePlatform = string.IsNullOrWhiteSpace(sourcePlatform) ? "Unknown" : sourcePlatform.Trim(),
            SchemaVersion = CurrentBackupSchemaVersion,
            TaskSchemaVersion = CurrentTaskSchemaVersion,
            SettingsSchemaVersion = CurrentSettingsSchemaVersion,
            PeriodSchemaVersion = CurrentPeriodSchemaVersion,
            Data = new ShuffleTaskExportData
            {
                Tasks = tasks,
                Settings = settings,
                PeriodDefinitions = periodDefinitions,
                Metadata = new Dictionary<string, string>
                {
                    ["exportedAtUtc"] = _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                }
            }
        };

        return JsonConvert.SerializeObject(envelope, Formatting.Indented);
    }

    public Task<BackupImportPreview> PreviewBackupImportAsync(string backupJson)
    {
        var envelope = ParseAndValidateBackup(backupJson);
        return Task.FromResult(new BackupImportPreview(
            EnsureUtc(envelope.CreatedAtUtc),
            string.IsNullOrWhiteSpace(envelope.SourcePlatform) ? "Unknown" : envelope.SourcePlatform,
            string.IsNullOrWhiteSpace(envelope.AppVersion) ? "unknown" : envelope.AppVersion,
            envelope.FormatVersion,
            envelope.SchemaVersion,
            envelope.Data!.Tasks.Count));
    }

    public async Task ImportBackupAsync(string backupJson)
    {
        await InitializeAsync().ConfigureAwait(false);

        var envelope = ParseAndValidateBackup(backupJson);
        var data = envelope.Data!;
        var settings = NormalizeSettings(data.Settings ?? new AppSettings());
        string settingsJson = JsonConvert.SerializeObject(CreateSettingsPayload(settings));

        await _settingsLock.WaitAsync().ConfigureAwait(false);
        await _taskLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await CreateImportSafetyBackupAsync().ConfigureAwait(false);

            await Db.RunInTransactionAsync(conn =>
            {
                conn.DeleteAll<TaskItemRecord>();
                conn.DeleteAll<PeriodDefinitionRecord>();
                conn.DeleteAll<KeyValueEntity>();

                foreach (var task in data.Tasks)
                {
                    conn.Insert(task);
                }

                foreach (var definition in data.PeriodDefinitions)
                {
                    conn.Insert(definition);
                }

                conn.Insert(new KeyValueEntity { Key = SettingsKey, Value = settingsJson });
                UpsertSchemaVersion(conn, TaskSchemaVersionKey, CurrentTaskSchemaVersion);
                UpsertSchemaVersion(conn, PeriodSchemaVersionKey, CurrentPeriodSchemaVersion);
                _faultInjector?.BeforeCommit("backup.import");
            }).ConfigureAwait(false);

            _taskSchemaIsFuture = false;
            _periodSchemaIsFuture = false;
            await EnsurePresetPeriodDefinitionsAsync().ConfigureAwait(false);
            _logger?.LogSyncEvent("BackupImportCompleted", $"tasks={data.Tasks.Count}; periods={data.PeriodDefinitions.Count}; schema={envelope.SchemaVersion}");
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("BackupImportFailed", "Import failed. Existing data was not intentionally changed.", ex);
            throw;
        }
        finally
        {
            _taskLock.Release();
            _settingsLock.Release();
        }
    }

    private static int CurrentBackupSchemaVersion => Math.Max(CurrentTaskSchemaVersion, Math.Max(CurrentSettingsSchemaVersion, CurrentPeriodSchemaVersion));

    private ShuffleTaskExportEnvelope ParseAndValidateBackup(string backupJson)
    {
        if (string.IsNullOrWhiteSpace(backupJson))
        {
            throw BackupFormatException();
        }

        ShuffleTaskExportEnvelope? envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<ShuffleTaskExportEnvelope>(backupJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This backup is damaged or incomplete.", ex);
        }

        if (envelope == null || !string.Equals(envelope.Format, ExportFormat, StringComparison.Ordinal))
        {
            throw BackupFormatException();
        }

        if (envelope.TaskSchemaVersion <= 0)
        {
            envelope.TaskSchemaVersion = envelope.SchemaVersion;
        }

        if (envelope.SettingsSchemaVersion <= 0)
        {
            envelope.SettingsSchemaVersion = envelope.SchemaVersion;
        }

        if (envelope.PeriodSchemaVersion <= 0)
        {
            envelope.PeriodSchemaVersion = envelope.SchemaVersion;
        }

        if (envelope.FormatVersion > CurrentExportFormatVersion
            || envelope.SchemaVersion > CurrentBackupSchemaVersion
            || envelope.TaskSchemaVersion > CurrentTaskSchemaVersion
            || envelope.SettingsSchemaVersion > CurrentSettingsSchemaVersion
            || envelope.PeriodSchemaVersion > CurrentPeriodSchemaVersion)
        {
            throw new InvalidDataException("This backup was created by a newer version of ShuffleTask and cannot be imported.");
        }

        if (envelope.FormatVersion <= 0
            || envelope.SchemaVersion <= 0
            || envelope.Data == null
            || envelope.Data.Tasks == null
            || envelope.Data.Settings == null
            || envelope.Data.PeriodDefinitions == null)
        {
            throw new InvalidDataException("This backup is damaged or incomplete.");
        }

        NormalizeImportedData(envelope);
        ValidateTasks(envelope.Data.Tasks);
        ValidatePeriodDefinitions(envelope.Data.PeriodDefinitions);
        NormalizeSettings(envelope.Data.Settings);
        return envelope;
    }

    private void NormalizeImportedData(ShuffleTaskExportEnvelope envelope)
    {
        if (envelope.TaskSchemaVersion < CurrentTaskSchemaVersion)
        {
            DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

            foreach (var task in envelope.Data!.Tasks)
            {
                task.CreatedAt = task.CreatedAt == default ? nowUtc : EnsureUtc(task.CreatedAt);
                task.UpdatedAt = task.UpdatedAt == default ? task.CreatedAt : EnsureUtc(task.UpdatedAt);
                task.EventVersion = Math.Max(1, task.EventVersion);

                if (task.Repeat == RepeatType.Interval && task.IntervalDays < 1)
                {
                    task.IntervalDays = 1;
                }

                if (!string.IsNullOrWhiteSpace(task.UserId))
                {
                    task.DeviceId = null;
                }
                else if (string.IsNullOrWhiteSpace(task.DeviceId))
                {
                    task.DeviceId = Environment.MachineName;
                }
            }
        }

        if (envelope.PeriodSchemaVersion < CurrentPeriodSchemaVersion)
        {
            foreach (var definition in envelope.Data!.PeriodDefinitions)
            {
                if (definition.Weekdays == Weekdays.None)
                {
                    definition.Weekdays = PeriodDefinitionCatalog.AllWeekdays;
                }

                definition.Weekdays = (Weekdays)((int)definition.Weekdays & ValidWeekdayMask);
                definition.Mode = (PeriodDefinitionMode)((int)definition.Mode & ValidPeriodModeMask);
            }
        }
    }

    private static void ValidateTasks(IEnumerable<TaskItemRecord> tasks)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id) || !ids.Add(task.Id))
            {
                throw new InvalidDataException("This backup is damaged or incomplete: task ids must be valid and unique.");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                throw new InvalidDataException("This backup is damaged or incomplete: each task needs a title.");
            }

            if (task.CreatedAt == default || task.UpdatedAt == default)
            {
                throw new InvalidDataException("This backup is damaged or incomplete: task timestamps are missing.");
            }

            if (!Enum.IsDefined(task.Repeat)
                || !Enum.IsDefined(task.AllowedPeriod)
                || !Enum.IsDefined(task.Status)
                || !Enum.IsDefined(task.CutInLineMode)
                || ((int)task.Weekdays & ~ValidWeekdayMask) != 0
                || (task.CustomWeekdays.HasValue && ((int)task.CustomWeekdays.Value & ~ValidWeekdayMask) != 0)
                || (task.AdHocWeekdays.HasValue && ((int)task.AdHocWeekdays.Value & ~ValidWeekdayMask) != 0)
                || ((int)task.AdHocMode & ~ValidPeriodModeMask) != 0)
            {
                throw new InvalidDataException("This backup is damaged or incomplete: a task has invalid rules.");
            }

            if (task.Repeat == RepeatType.Interval && task.IntervalDays < 1)
            {
                throw new InvalidDataException("This backup is damaged or incomplete: a repeating task has an invalid interval.");
            }

            if (task.Status == TaskLifecycleStatus.Snoozed && task.SnoozedUntil == null)
            {
                throw new InvalidDataException("This backup is damaged or incomplete: a snoozed task is missing its snooze time.");
            }
        }
    }

    private static void ValidatePeriodDefinitions(IEnumerable<PeriodDefinitionRecord> definitions)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
            {
                throw new InvalidDataException("This backup is damaged or incomplete: period definition ids must be valid and unique.");
            }

            if (string.IsNullOrWhiteSpace(definition.Name)
                || ((int)definition.Weekdays & ~ValidWeekdayMask) != 0
                || ((int)definition.Mode & ~ValidPeriodModeMask) != 0)
            {
                throw new InvalidDataException("This backup is damaged or incomplete: an allowed-time rule is invalid.");
            }
        }
    }

    private async Task<string?> CreateImportSafetyBackupAsync()
    {
        if (!File.Exists(_dbPath))
        {
            return null;
        }

        string backupPath = CreateSidecarPath("backup.pre-import");
        await Db.ExecuteAsync("PRAGMA wal_checkpoint(FULL);").ConfigureAwait(false);
        await Db.CloseAsync().ConfigureAwait(false);
        File.Copy(_dbPath, backupPath, overwrite: false);
        CopySidecarIfExists("-wal", backupPath + "-wal");
        CopySidecarIfExists("-shm", backupPath + "-shm");
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        await ConfigureSqliteDurabilityAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceBackupCreated", $"artifact={Path.GetFileName(backupPath)}; reason=pre-import");
        return backupPath;
    }

    private static InvalidDataException BackupFormatException()
        => new("This does not look like a ShuffleTask backup.");

    private sealed class ShuffleTaskExportEnvelope
    {
        [JsonProperty("format")]
        public string? Format { get; set; }

        [JsonProperty("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonProperty("appVersion")]
        public string? AppVersion { get; set; }

        [JsonProperty("createdAtUtc")]
        public DateTime CreatedAtUtc { get; set; }

        [JsonProperty("sourcePlatform")]
        public string? SourcePlatform { get; set; }

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("taskSchemaVersion")]
        public int TaskSchemaVersion { get; set; }

        [JsonProperty("settingsSchemaVersion")]
        public int SettingsSchemaVersion { get; set; }

        [JsonProperty("periodSchemaVersion")]
        public int PeriodSchemaVersion { get; set; }

        [JsonProperty("data")]
        public ShuffleTaskExportData? Data { get; set; }
    }

    private sealed class ShuffleTaskExportData
    {
        [JsonProperty("tasks")]
        public List<TaskItemRecord> Tasks { get; set; } = new();

        [JsonProperty("settings")]
        public AppSettings? Settings { get; set; }

        [JsonProperty("periodDefinitions")]
        public List<PeriodDefinitionRecord> PeriodDefinitions { get; set; } = new();

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
