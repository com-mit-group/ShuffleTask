using Microsoft.Maui.Storage;
using ShuffleTask.Application.Abstractions;
using System.Globalization;
using System.Text.Json;

namespace ShuffleTask.Presentation.Utilities;

// Shared scheduler state persistence helper backed by host-provided MAUI preferences.
public static class PersistedSchedulerState
{
    public const string SchedulerEnvelopeKey = "pref.schedulerEnvelope";
    public const string SchedulerQuarantinePrefix = "pref.schedulerEnvelope.quarantine.";
    private const int CurrentSchemaVersion = 1;

    public static void SavePendingShuffle(string? taskId, DateTimeOffset scheduledAt, IShuffleLogger? logger = null)
    {
        var envelope = LoadEnvelopeOrDefault(logger);
        envelope.PendingNextAt = scheduledAt;
        envelope.PendingTaskId = string.IsNullOrWhiteSpace(taskId) ? null : taskId.Trim();
        SaveEnvelope(envelope, logger, "pending-save");

        Preferences.Default.Set(PreferenceKeys.NextShuffleAt, scheduledAt.ToString("O", CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(envelope.PendingTaskId))
        {
            Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
        }
        else
        {
            Preferences.Default.Set(PreferenceKeys.PendingShuffleTaskId, envelope.PendingTaskId);
        }
    }

    public static (DateTimeOffset? NextAt, string TaskId) LoadPendingShuffle(IShuffleLogger? logger = null)
    {
        if (TryReadEnvelope(out SchedulerEnvelope? envelope, logger) && envelope!.PendingNextAt.HasValue)
        {
            return (envelope.PendingNextAt.Value, envelope.PendingTaskId ?? string.Empty);
        }

        string iso = Preferences.Default.Get(PreferenceKeys.NextShuffleAt, string.Empty);
        string taskId = Preferences.Default.Get(PreferenceKeys.PendingShuffleTaskId, string.Empty);

        if (!string.IsNullOrWhiteSpace(iso)
            && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextAt))
        {
            return (nextAt, taskId);
        }

        if (!string.IsNullOrWhiteSpace(iso))
        {
            QuarantineLegacySchedulerState("invalid-pending-date", logger);
        }

        return (null, taskId);
    }

    public static void ClearPendingShuffle(IShuffleLogger? logger = null)
    {
        var envelope = LoadEnvelopeOrDefault(logger);
        envelope.PendingNextAt = null;
        envelope.PendingTaskId = null;
        SaveEnvelope(envelope, logger, "pending-clear");
        Preferences.Default.Remove(PreferenceKeys.NextShuffleAt);
        Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
    }

    public static (DateTimeOffset? Date, int Count) LoadDailyCount(IShuffleLogger? logger = null)
    {
        if (TryReadEnvelope(out SchedulerEnvelope? envelope, logger) && envelope!.ShuffleCountDate.HasValue)
        {
            return (envelope.ShuffleCountDate.Value, Math.Max(0, envelope.ShuffleCount));
        }

        string iso = Preferences.Default.Get(PreferenceKeys.ShuffleCountDate, string.Empty);
        int count = Preferences.Default.Get(PreferenceKeys.ShuffleCount, 0);

        if (!string.IsNullOrWhiteSpace(iso)
            && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
        {
            return (date, Math.Max(0, count));
        }

        if (!string.IsNullOrWhiteSpace(iso))
        {
            QuarantineLegacySchedulerState("invalid-daily-count-date", logger);
        }

        return (null, 0);
    }

    public static void SaveDailyCount(DateTimeOffset date, int count, IShuffleLogger? logger = null)
    {
        var envelope = LoadEnvelopeOrDefault(logger);
        envelope.ShuffleCountDate = date;
        envelope.ShuffleCount = Math.Max(0, count);
        SaveEnvelope(envelope, logger, "daily-count-save");

        Preferences.Default.Set(PreferenceKeys.ShuffleCountDate, date.ToString("O", CultureInfo.InvariantCulture));
        Preferences.Default.Set(PreferenceKeys.ShuffleCount, Math.Max(0, count));
    }

    private static SchedulerEnvelope LoadEnvelopeOrDefault(IShuffleLogger? logger)
    {
        return TryReadEnvelope(out SchedulerEnvelope? envelope, logger)
            ? envelope!
            : new SchedulerEnvelope { SchemaVersion = CurrentSchemaVersion };
    }

    private static void SaveEnvelope(SchedulerEnvelope envelope, IShuffleLogger? logger, string operation)
    {
        logger?.LogSyncEvent("PersistenceSaveStarted", $"domain=scheduler-state; operation={operation}");
        envelope.SchemaVersion = CurrentSchemaVersion;
        envelope.SavedAtUtc = DateTimeOffset.UtcNow;
        Preferences.Default.Set(SchedulerEnvelopeKey, JsonSerializer.Serialize(envelope));
        logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=scheduler-state; operation={operation}");
    }

    private static bool TryReadEnvelope(out SchedulerEnvelope? envelope, IShuffleLogger? logger)
    {
        envelope = null;
        string json = Preferences.Default.Get(SchedulerEnvelopeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<SchedulerEnvelope>(json);
            if (candidate == null)
            {
                QuarantineSchedulerEnvelope(json, "invalid-envelope", logger);
                return false;
            }

            if (candidate.SchemaVersion > CurrentSchemaVersion)
            {
                logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=scheduler-state; schemaVersion={candidate.SchemaVersion}");
                return false;
            }

            envelope = candidate;
            return true;
        }
        catch (Exception ex)
        {
            QuarantineSchedulerEnvelope(json, "corrupt-envelope", logger, ex);
            return false;
        }
    }

    private static void QuarantineSchedulerEnvelope(string json, string reason, IShuffleLogger? logger, Exception? exception = null)
    {
        string key = SchedulerQuarantinePrefix + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        Preferences.Default.Set(key, json);
        Preferences.Default.Remove(SchedulerEnvelopeKey);
        logger?.LogSyncEvent("PersistenceQuarantine", $"domain=scheduler-state; reason={reason}; artifact={key}", exception);
    }

    private static void QuarantineLegacySchedulerState(string reason, IShuffleLogger? logger)
    {
        var legacy = new
        {
            NextShuffleAt = Preferences.Default.Get(PreferenceKeys.NextShuffleAt, string.Empty),
            PendingTaskId = Preferences.Default.Get(PreferenceKeys.PendingShuffleTaskId, string.Empty),
            ShuffleCountDate = Preferences.Default.Get(PreferenceKeys.ShuffleCountDate, string.Empty),
            ShuffleCount = Preferences.Default.Get(PreferenceKeys.ShuffleCount, 0)
        };
        string key = SchedulerQuarantinePrefix + "legacy." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        Preferences.Default.Set(key, JsonSerializer.Serialize(legacy));
        logger?.LogSyncEvent("PersistenceQuarantine", $"domain=scheduler-state; reason={reason}; artifact={key}");
    }

    private sealed class SchedulerEnvelope
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset? PendingNextAt { get; set; }
        public string? PendingTaskId { get; set; }
        public DateTimeOffset? ShuffleCountDate { get; set; }
        public int ShuffleCount { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
    }
}
