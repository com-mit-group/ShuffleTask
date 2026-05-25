using System.Diagnostics;
using System.Globalization;
using SQLite;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private async Task EnsureTaskSchemaAsync()
    {
        await EnsureSchemaAsync("TaskItem", GetTaskSchemaColumns()).ConfigureAwait(false);
    }

    private async Task EnsurePeriodDefinitionSchemaAsync()
    {
        await EnsureSchemaAsync("PeriodDefinition", GetPeriodSchemaColumns()).ConfigureAwait(false);
    }

    private static IReadOnlyCollection<SchemaColumn> GetTaskSchemaColumns()
        => new[]
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
        };

    private static IReadOnlyCollection<SchemaColumn> GetPeriodSchemaColumns()
        => new[]
        {
            new SchemaColumn("Name", "TEXT", "''"),
            new SchemaColumn("Weekdays", IntegerSqlType, "0"),
            new SchemaColumn("StartTime", "TEXT", "NULL"),
            new SchemaColumn("EndTime", "TEXT", "NULL"),
            new SchemaColumn("IsAllDay", IntegerSqlType, "0"),
            new SchemaColumn("Mode", IntegerSqlType, "0")
        };

    private async Task EnsureSchemaAsync(string tableName, IReadOnlyCollection<SchemaColumn> columns)
    {
        var infos = await Db.QueryAsync<TableInfo>($"PRAGMA table_info({tableName});").ConfigureAwait(false);
        await EnsureSchemaColumns(tableName, columns, infos.Select(i => i.name), sql => Db.ExecuteAsync(sql)).ConfigureAwait(false);
    }

    private static void EnsureSchemaColumns(SQLiteConnection conn, string tableName, IReadOnlyCollection<SchemaColumn> columns)
    {
        var infos = conn.Query<TableInfo>($"PRAGMA table_info({tableName});");
        EnsureSchemaColumns(tableName, columns, infos.Select(i => i.name), sql =>
        {
            conn.Execute(sql);
            return Task.FromResult(0);
        }).GetAwaiter().GetResult();
    }

    private static async Task EnsureSchemaColumns(
        string tableName,
        IReadOnlyCollection<SchemaColumn> columns,
        IEnumerable<string> existingColumnNames,
        Func<string, Task<int>> executeAsync)
    {
        var cols = new HashSet<string>(existingColumnNames, StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (cols.Contains(column.Name))
            {
                continue;
            }

            string alter = $"ALTER TABLE {tableName} ADD COLUMN {column.Name} {column.SqlType} DEFAULT {column.DefaultSql}";
            await executeAsync(alter).ConfigureAwait(false);
        }
    }

    private async Task RunSchemaMigrationsAsync()
    {
        _taskSchemaIsFuture = await RunSchemaMigrationPipelineAsync(
            "tasks",
            TaskSchemaVersionKey,
            CurrentTaskSchemaVersion,
            CreateTaskMigrations()).ConfigureAwait(false);

        _periodSchemaIsFuture = await RunSchemaMigrationPipelineAsync(
            "periods",
            PeriodSchemaVersionKey,
            CurrentPeriodSchemaVersion,
            CreatePeriodMigrations()).ConfigureAwait(false);
    }

    private async Task<bool> RunSchemaMigrationPipelineAsync(
        string domain,
        string key,
        int currentVersion,
        IReadOnlyList<SchemaMigration> migrations)
    {
        int storedVersion = await ReadSchemaVersionAsync(key).ConfigureAwait(false);

        if (storedVersion > currentVersion)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain={domain}; stored={storedVersion}; supported={currentVersion}");
            return true;
        }

        if (storedVersion == currentVersion)
        {
            return false;
        }

        string? backupPath = await CreateDatabaseBackupAsync($"pre-{domain}-migration").ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceMigrationStarted", $"domain={domain}; from={storedVersion}; to={currentVersion}; backup={Path.GetFileName(backupPath)}");

        try
        {
            await Db.RunInTransactionAsync(conn =>
            {
                int version = storedVersion;
                while (version < currentVersion)
                {
                    SchemaMigration? migration = migrations.FirstOrDefault(m => m.FromVersion == version);
                    if (migration is null || migration.ToVersion <= version)
                    {
                        throw new InvalidOperationException($"Missing {domain} migration step from schema {version}.");
                    }

                    migration.Apply(conn);
                    version = migration.ToVersion;
                    UpsertSchemaVersion(conn, key, version);
                }

                _faultInjector?.BeforeCommit($"migration.{domain}");
            }).ConfigureAwait(false);

            _logger?.LogSyncEvent("PersistenceMigrationCompleted", $"domain={domain}; from={storedVersion}; to={currentVersion}; durationMs={stopwatch.ElapsedMilliseconds}");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PersistenceMigrationFailed", $"domain={domain}; from={storedVersion}; to={currentVersion}; backup={Path.GetFileName(backupPath)}; durationMs={stopwatch.ElapsedMilliseconds}", ex);
            throw;
        }
    }

    private async Task<int> ReadSchemaVersionAsync(string key)
    {
        KeyValueEntity? row = await Db.FindAsync<KeyValueEntity>(key).ConfigureAwait(false);
        if (row == null || string.IsNullOrWhiteSpace(row.Value))
        {
            return 0;
        }

        return int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 0
            ? parsed
            : 0;
    }

    private static void UpsertSchemaVersion(SQLiteConnection conn, string key, int version)
    {
        var existing = conn.Find<KeyValueEntity>(key);
        if (existing == null)
        {
            conn.Insert(new KeyValueEntity { Key = key, Value = version.ToString(CultureInfo.InvariantCulture) });
            return;
        }

        existing.Value = version.ToString(CultureInfo.InvariantCulture);
        conn.Update(existing);
    }

    private async Task<string?> CreateDatabaseBackupAsync(string reason)
    {
        if (!File.Exists(_dbPath))
        {
            return null;
        }

        if (!_databaseExistedBeforeOpen)
        {
            return null;
        }

        string backupPath = CreateSidecarPath($"backup.{reason}");
        await Db.CloseAsync().ConfigureAwait(false);
        File.Copy(_dbPath, backupPath, overwrite: false);
        CopySidecarIfExists("-wal", backupPath + "-wal");
        CopySidecarIfExists("-shm", backupPath + "-shm");
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        await ConfigureSqliteDurabilityAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceBackupCreated", $"artifact={Path.GetFileName(backupPath)}; reason={reason}");
        return backupPath;
    }

    private void CopySidecarIfExists(string sourceSuffix, string targetPath)
    {
        string source = _dbPath + sourceSuffix;
        if (File.Exists(source))
        {
            File.Copy(source, targetPath, overwrite: false);
        }
    }

    private static IReadOnlyList<SchemaMigration> CreateTaskMigrations()
        => new[]
        {
            new SchemaMigration(0, 1, conn => EnsureSchemaColumns(conn, "TaskItem", GetTaskSchemaColumns())),
            new SchemaMigration(1, 2, NormalizeTaskMetadataForSchemaV2)
        };

    private static IReadOnlyList<SchemaMigration> CreatePeriodMigrations()
        => new[]
        {
            new SchemaMigration(0, 1, conn => EnsureSchemaColumns(conn, "PeriodDefinition", GetPeriodSchemaColumns())),
            new SchemaMigration(1, 2, NormalizePeriodDefinitionsForSchemaV2)
        };

    private static void NormalizeTaskMetadataForSchemaV2(SQLiteConnection conn)
    {
        EnsureSchemaColumns(conn, "TaskItem", GetTaskSchemaColumns());
        string machineName = Environment.MachineName;
        conn.Execute("UPDATE TaskItem SET EventVersion = 1 WHERE EventVersion IS NULL OR EventVersion <= 0");
        conn.Execute("UPDATE TaskItem SET CreatedAt = CURRENT_TIMESTAMP WHERE CreatedAt IS NULL OR TRIM(CAST(CreatedAt AS TEXT)) = ''");
        conn.Execute("UPDATE TaskItem SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL OR TRIM(CAST(UpdatedAt AS TEXT)) = ''");
        conn.Execute("UPDATE TaskItem SET DeviceId = ? WHERE (UserId IS NULL OR TRIM(UserId) = '') AND (DeviceId IS NULL OR TRIM(DeviceId) = '')", machineName);
        conn.Execute("UPDATE TaskItem SET DeviceId = NULL WHERE UserId IS NOT NULL AND TRIM(UserId) <> ''");
        conn.Execute("UPDATE TaskItem SET Status = 0, SnoozedUntil = NULL, NextEligibleAt = NULL WHERE Status NOT IN (0, 1, 2)");
        conn.Execute("UPDATE TaskItem SET Repeat = 0, Weekdays = 0, IntervalDays = 0 WHERE Repeat NOT IN (0, 1, 2, 3)");
        conn.Execute("UPDATE TaskItem SET IntervalDays = 1 WHERE Repeat = 3 AND (IntervalDays IS NULL OR IntervalDays < 1)");
        conn.Execute("UPDATE TaskItem SET CutInLineMode = 0 WHERE CutInLineMode NOT IN (0, 1, 2)");
    }

    private static void NormalizePeriodDefinitionsForSchemaV2(SQLiteConnection conn)
    {
        EnsureSchemaColumns(conn, "PeriodDefinition", GetPeriodSchemaColumns());
        conn.Execute("UPDATE PeriodDefinition SET Name = 'Untitled period' WHERE Name IS NULL OR TRIM(Name) = ''");
        conn.Execute("UPDATE PeriodDefinition SET Weekdays = ? WHERE Weekdays IS NULL OR Weekdays = 0", ValidWeekdayMask);
        conn.Execute("UPDATE PeriodDefinition SET Weekdays = (Weekdays & ?) WHERE (Weekdays & ~?) <> 0", ValidWeekdayMask, ValidWeekdayMask);
        conn.Execute("UPDATE PeriodDefinition SET Mode = 0 WHERE Mode IS NULL OR (Mode & ~?) <> 0", ValidPeriodModeMask);
        conn.Execute("UPDATE PeriodDefinition SET StartTime = NULL, EndTime = NULL WHERE IsAllDay <> 0");
    }

}
