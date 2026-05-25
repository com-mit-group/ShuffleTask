using System.Diagnostics;
using System.Threading;
using SQLite;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Persistence;

public partial class StorageService : IStorageService
{
    private const string SettingsKey = "app_settings";
    private const string TaskSchemaVersionKey = "schema_tasks";
    private const string PeriodSchemaVersionKey = "schema_periods";
    private const int CurrentSettingsSchemaVersion = 2;
    private const int CurrentTaskSchemaVersion = 2;
    private const int CurrentPeriodSchemaVersion = 2;
    private const string IntegerSqlType = "INTEGER";
    private const int ValidWeekdayMask = (int)(Weekdays.Sun | Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri | Weekdays.Sat);
    private const int ValidPeriodModeMask = (int)(PeriodDefinitionMode.AlignWithWorkHours
        | PeriodDefinitionMode.OffWorkRelativeToWorkHours
        | PeriodDefinitionMode.Morning
        | PeriodDefinitionMode.Lunch
        | PeriodDefinitionMode.Evening);
    private readonly TimeProvider _clock;
    private readonly string _dbPath;
    private readonly IShuffleLogger? _logger;
    private readonly IStorageFaultInjector? _faultInjector;
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly SemaphoreSlim _taskLock = new(1, 1);
    private bool _taskSchemaIsFuture;
    private bool _periodSchemaIsFuture;
    private bool _databaseExistedBeforeOpen;

    public StorageService(
        TimeProvider clock,
        string databasePath,
        IShuffleLogger? logger = null,
        IStorageFaultInjector? faultInjector = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        _dbPath = databasePath;
        _logger = logger;
        _faultInjector = faultInjector;
    }

    public async Task InitializeAsync()
    {
        if (_db != null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=startup");

        try
        {
            await InitializeDatabaseWithRecoveryAsync().ConfigureAwait(false);
            await ValidateAndRecoverStartupStateAsync().ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=startup; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PersistenceLoadFailed", $"domain=startup; durationMs={stopwatch.ElapsedMilliseconds}", ex);
            throw;
        }
    }
}
