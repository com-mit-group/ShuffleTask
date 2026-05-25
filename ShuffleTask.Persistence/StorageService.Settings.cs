using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShuffleTask.Application.Models;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    // Settings
    private async Task RecoverSettingsAtStartupAsync()
    {
        KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
        if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=settings; operation=startup-validate");

        try
        {
            var payload = DeserializeSettingsPayload(kv.Value);
            int originalSchema = payload.SchemaVersion;
            var migrated = MigrateSettingsPayload(payload);
            var normalized = NormalizeSettings(migrated.Data ?? new AppSettings());

            if (originalSchema != migrated.SchemaVersion)
            {
                await SaveSettingsPayloadAsync(normalized).ConfigureAwait(false);
                _logger?.LogSyncEvent("PersistenceRecovery", $"domain=settings; operation=startup-migration-persisted; schema={migrated.SchemaVersion}");
            }

            _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=settings; operation=startup-validate; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=settings; schemaVersion={ex.SchemaVersion}; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            await QuarantineSettingsValueAsync(kv.Value!, ex.Message).ConfigureAwait(false);
            await SaveSettingsPayloadAsync(new AppSettings()).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceRecovery", $"domain=settings; operation=startup-corruption-defaulted; durationMs={stopwatch.ElapsedMilliseconds}", ex);
        }
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceLoadStarted", "domain=settings; operation=get");

        try
        {
            KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
            if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
            {
                var defaults = new AppSettings();
                await SetSettingsInternalAsync(defaults).ConfigureAwait(false);
                _logger?.LogSyncEvent("PersistenceRecovery", "domain=settings; recovery=missing-defaulted");
                return defaults;
            }

            try
            {
                var payload = DeserializeSettingsPayload(kv.Value!);
                int originalSchema = payload.SchemaVersion;
                var migrated = MigrateSettingsPayload(payload);
                var normalized = NormalizeSettings(migrated.Data ?? new AppSettings());
                if (originalSchema != migrated.SchemaVersion)
                {
                    await SaveSettingsPayloadAsync(normalized).ConfigureAwait(false);
                }

                _logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=settings; schema={migrated.SchemaVersion}; durationMs={stopwatch.ElapsedMilliseconds}");
                return normalized;
            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                _logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=settings; schemaVersion={ex.SchemaVersion}; returning defaults read-only; durationMs={stopwatch.ElapsedMilliseconds}");
                return new AppSettings();
            }
            catch (Exception ex)
            {
                await QuarantineSettingsValueAsync(kv.Value!, ex.Message).ConfigureAwait(false);
                var defaults = new AppSettings();
                await SetSettingsInternalAsync(defaults).ConfigureAwait(false);
                _logger?.LogSyncEvent("PersistenceRecovery", $"domain=settings; recovery=corrupt-defaulted; durationMs={stopwatch.ElapsedMilliseconds}");
                return defaults;
            }
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task SetSettingsAsync(AppSettings settings)
    {
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceSaveStarted", "domain=settings; operation=set");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await SetSettingsInternalAsync(settings).ConfigureAwait(false);
            _logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=settings; operation=set; durationMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task SetSettingsInternalAsync(AppSettings settings)
    {
        settings = NormalizeSettings(settings);

        if (await HasFutureSettingsSchemaAsync().ConfigureAwait(false))
        {
            _logger?.LogSyncEvent("PersistenceSaveSkipped", "Skipped settings save because stored schema version is newer than supported.");
            return;
        }

        var existingSettings = await GetExistingSettingsAsync().ConfigureAwait(false);

        if (IsStale(settings, existingSettings))
        {
            return;
        }

        settings.EventVersion = NormalizeVersion(settings.EventVersion, existingSettings?.EventVersion);
        settings.UpdatedAt = NormalizeUpdatedAt(settings.UpdatedAt, existingSettings?.UpdatedAt);

        await SaveSettingsPayloadAsync(settings).ConfigureAwait(false);
    }

    private async Task SaveSettingsPayloadAsync(AppSettings settings)
    {
        var payload = CreateSettingsPayload(settings);
        string json = JsonConvert.SerializeObject(payload);
        await Db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Find<KeyValueEntity>(SettingsKey);
            if (existing == null)
            {
                conn.Insert(new KeyValueEntity { Key = SettingsKey, Value = json });
                _faultInjector?.BeforeCommit("settings.save");
                return;
            }

            existing.Value = json;
            conn.Update(existing);
            _faultInjector?.BeforeCommit("settings.save");
        }).ConfigureAwait(false);
    }

    private SettingsPayload CreateSettingsPayload(AppSettings settings)
        => new()
        {
            SchemaVersion = CurrentSettingsSchemaVersion,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            LastSuccessfulSaveUtc = _clock.GetUtcNow().UtcDateTime,
            Data = settings
        };



    private async Task<bool> HasFutureSettingsSchemaAsync()
    {
        KeyValueEntity kv = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
        if (kv == null || string.IsNullOrWhiteSpace(kv.Value))
        {
            return false;
        }

        try
        {
            if (TryReadSchemaVersion(kv.Value, out int schemaVersion) && schemaVersion > CurrentSettingsSchemaVersion)
            {
                return true;
            }

            var payload = DeserializeSettingsPayload(kv.Value);
            if (payload.SchemaVersion > CurrentSettingsSchemaVersion)
            {
                return true;
            }
        }
        catch (UnsupportedSettingsSchemaException)
        {
            return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryReadSchemaVersion(string json, out int schemaVersion)
    {
        schemaVersion = 0;

        try
        {
            var token = JToken.Parse(json);
            if (token is not JObject obj)
            {
                return false;
            }

            var schemaToken = obj["SchemaVersion"];
            if (schemaToken == null || schemaToken.Type == JTokenType.Null)
            {
                return false;
            }

            if (schemaToken.Type == JTokenType.Integer)
            {
                schemaVersion = schemaToken.Value<int>();
                return true;
            }

            if (schemaToken.Type == JTokenType.String && int.TryParse(schemaToken.Value<string>(), out int parsed))
            {
                schemaVersion = parsed;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private SettingsPayload DeserializeSettingsPayload(string json)
    {
        var envelope = JsonConvert.DeserializeObject<SettingsPayload>(json);
        if (envelope?.Data != null)
        {
            return envelope;
        }

        var legacy = JsonConvert.DeserializeObject<AppSettings>(json);
        if (legacy == null)
        {
            throw new InvalidOperationException("Settings payload is invalid.");
        }

        return new SettingsPayload
        {
            SchemaVersion = 1,
            AppVersion = "legacy",
            LastSuccessfulSaveUtc = _clock.GetUtcNow().UtcDateTime,
            Data = legacy
        };
    }

    private SettingsPayload MigrateSettingsPayload(SettingsPayload payload)
    {
        if (payload.SchemaVersion > CurrentSettingsSchemaVersion)
        {
            throw new UnsupportedSettingsSchemaException(payload.SchemaVersion);
        }

        if (payload.SchemaVersion <= 0)
        {
            payload.SchemaVersion = 1;
        }

        int fromVersion = payload.SchemaVersion;
        if (fromVersion < CurrentSettingsSchemaVersion)
        {
            _logger?.LogSyncEvent("PersistenceMigrationStarted", $"domain=settings; from={fromVersion}; to={CurrentSettingsSchemaVersion}");
        }

        while (payload.SchemaVersion < CurrentSettingsSchemaVersion)
        {
            payload = payload.SchemaVersion switch
            {
                1 => MigrateSettingsV1ToV2(payload),
                _ => throw new InvalidOperationException($"Missing settings migration step from schema {payload.SchemaVersion}.")
            };
        }

        if (fromVersion < CurrentSettingsSchemaVersion)
        {
            _logger?.LogSyncEvent("PersistenceMigrationCompleted", $"domain=settings; from={fromVersion}; to={CurrentSettingsSchemaVersion}");
        }

        return payload;
    }

    private static SettingsPayload MigrateSettingsV1ToV2(SettingsPayload payload)
    {
        payload.Data ??= new AppSettings();
        payload.Data.Network ??= NetworkOptions.CreateDefault();
        payload.Data.Network.Normalize();
        payload.SchemaVersion = 2;
        return payload;
    }

    private async Task QuarantineSettingsValueAsync(string value, string reason)
    {
        string suffix = _clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        string key = $"{SettingsKey}_quarantine_{suffix}";
        await Db.RunInTransactionAsync(conn =>
        {
            conn.InsertOrReplace(new KeyValueEntity { Key = key, Value = value });
        }).ConfigureAwait(false);
        _logger?.LogSyncEvent("PersistenceQuarantine", $"domain=settings; artifact={key}; reason={reason}");
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
        int maxDailyShufflesUpperBound = (24 * 60) / settings.MinGapMinutes;
        settings.MaxDailyShuffles = Math.Clamp(settings.MaxDailyShuffles, 0, maxDailyShufflesUpperBound);
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

            var payload = DeserializeSettingsPayload(kv.Value);
            var migrated = MigrateSettingsPayload(payload);
            return migrated.Data is null ? null : NormalizeSettings(migrated.Data);
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

}
