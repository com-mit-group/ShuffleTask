using System.Globalization;
using SQLite;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private async Task InitializeDatabaseWithRecoveryAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        _databaseExistedBeforeOpen = File.Exists(_dbPath) && new FileInfo(_dbPath).Length > 0;

        try
        {
            await InitializeDatabaseCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDatabaseOpenFailure(ex))
        {
            await QuarantineDatabaseFileAsync(ex).ConfigureAwait(false);
            await InitializeDatabaseCoreAsync().ConfigureAwait(false);
        }
    }

    private async Task InitializeDatabaseCoreAsync()
    {
        _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

        await ConfigureSqliteDurabilityAsync().ConfigureAwait(false);
        await VerifyDatabaseIntegrityAsync().ConfigureAwait(false);

        await _db.CreateTableAsync<KeyValueEntity>().ConfigureAwait(false);
        await _db.CreateTableAsync<TaskItemRecord>().ConfigureAwait(false);
        await _db.CreateTableAsync<PeriodDefinitionRecord>().ConfigureAwait(false);

        await RunSchemaMigrationsAsync().ConfigureAwait(false);
        await RecoverSettingsAtStartupAsync().ConfigureAwait(false);
        await EnsurePresetPeriodDefinitionsAsync().ConfigureAwait(false);
    }

    private async Task ConfigureSqliteDurabilityAsync()
    {
        await Db.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await Db.ExecuteAsync("PRAGMA synchronous=FULL;").ConfigureAwait(false);
        await Db.ExecuteAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);
    }

    private async Task VerifyDatabaseIntegrityAsync()
    {
        try
        {
            string result = await Db.ExecuteScalarAsync<string>("PRAGMA integrity_check;").ConfigureAwait(false);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SQLite integrity check failed: {result}");
            }
        }
        catch (Exception ex) when (IsDatabaseOpenFailure(ex))
        {
            throw;
        }
    }

    private static bool IsDatabaseOpenFailure(Exception ex)
    {
        if (ex is SQLiteException)
        {
            return true;
        }

        string message = ex.Message;
        return message.Contains("database disk image is malformed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("file is not a database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SQLite", StringComparison.OrdinalIgnoreCase);
    }

    private async Task QuarantineDatabaseFileAsync(Exception reason)
    {
        if (_db != null)
        {
            await _db.CloseAsync().ConfigureAwait(false);
            _db = null;
        }

        if (!File.Exists(_dbPath))
        {
            _logger?.LogSyncEvent("PersistenceRecovery", "Database open failed and no artifact existed to quarantine.", reason);
            return;
        }

        string quarantinePath = CreateSidecarPath("quarantine");
        File.Move(_dbPath, quarantinePath, overwrite: false);
        MoveSidecarIfExists("-wal", quarantinePath + "-wal");
        MoveSidecarIfExists("-shm", quarantinePath + "-shm");
        _databaseExistedBeforeOpen = false;
        _logger?.LogSyncEvent("PersistenceQuarantine", $"Database artifact quarantined as {Path.GetFileName(quarantinePath)}", reason);
    }

    private void MoveSidecarIfExists(string sourceSuffix, string targetPath)
    {
        string source = _dbPath + sourceSuffix;
        if (File.Exists(source))
        {
            File.Move(source, targetPath, overwrite: false);
        }
    }

    private string CreateSidecarPath(string reason)
    {
        string suffix = _clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        return $"{_dbPath}.{reason}.{suffix}.db3";
    }

}
