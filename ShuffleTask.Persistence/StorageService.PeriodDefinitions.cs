using System.Diagnostics;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private async Task EnsurePresetPeriodDefinitionsAsync()
    {
        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=periods; operation=ensure-presets; reason=future-schema");
            return;
        }

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

        await Db.RunInTransactionAsync(conn =>
        {
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
                        conn.Update(existingRecord);
                    }

                    continue;
                }

                conn.Insert(record);
            }

            _faultInjector?.BeforeCommit("periods.ensure-presets");
        }).ConfigureAwait(false);
    }

    // Period definitions CRUD
    public async Task<List<PeriodDefinition>> GetPeriodDefinitionsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=periods; operation=list");
        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=periods; operation=list; durationMs={stopwatch.ElapsedMilliseconds}");
            return new List<PeriodDefinition>();
        }

        await ValidateAndRecoverPeriodTableAsync().ConfigureAwait(false);
        var records = await Db.Table<PeriodDefinitionRecord>()
                              .OrderBy(r => r.Name)
                              .ToListAsync();
        var periods = records.Select(r => r.ToDomain()).ToList();
        _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=periods; operation=list; count={periods.Count}; durationMs={stopwatch.ElapsedMilliseconds}");
        return periods;
    }

    public async Task<PeriodDefinition?> GetPeriodDefinitionAsync(string id)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=periods; operation=get");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=periods; operation=get; durationMs={stopwatch.ElapsedMilliseconds}");
            return null;
        }

        await ValidateAndRecoverPeriodTableAsync().ConfigureAwait(false);
        var record = await Db.Table<PeriodDefinitionRecord>()
                              .Where(r => r.Id == id)
                              .FirstOrDefaultAsync();
        var period = record?.ToDomain();
        _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=periods; operation=get; found={period != null}; durationMs={stopwatch.ElapsedMilliseconds}");
        return period;
    }

    public async Task AddPeriodDefinitionAsync(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=periods; operation=add");

        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=periods; operation=add; reason=future-schema");
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        var record = PeriodDefinitionRecord.FromDomain(definition);
        await Db.RunInTransactionAsync(conn =>
        {
            conn.Insert(record);
            _faultInjector?.BeforeCommit("periods.add");
        }).ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=periods; operation=add; durationMs={stopwatch.ElapsedMilliseconds}");
    }

    public async Task UpdatePeriodDefinitionAsync(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=periods; operation=update");

        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=periods; operation=update; reason=future-schema");
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        var record = PeriodDefinitionRecord.FromDomain(definition);
        await Db.RunInTransactionAsync(conn =>
        {
            int updated = conn.Update(record);
            if (updated == 0)
            {
                conn.Insert(record);
            }

            _faultInjector?.BeforeCommit("periods.update");
        }).ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=periods; operation=update; durationMs={stopwatch.ElapsedMilliseconds}");
    }

    public async Task DeletePeriodDefinitionAsync(string id)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=periods; operation=delete");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        if (_periodSchemaIsFuture)
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "domain=periods; operation=delete; reason=future-schema");
            return;
        }

        await Db.RunInTransactionAsync(conn =>
        {
            conn.Delete<PeriodDefinitionRecord>(id);
            _faultInjector?.BeforeCommit("periods.delete");
        }).ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=periods; operation=delete; durationMs={stopwatch.ElapsedMilliseconds}");
    }

}
