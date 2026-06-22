using Microsoft.Maui.Storage;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;
using System.Globalization;
using System.Text.Json;

namespace ShuffleTask.Presentation.Utilities;

// Shared timer state persistence helper backed by host-provided MAUI preferences.
public static class PersistedTimerState
{
    public const string TimerEnvelopeKey = "pref.timerEnvelope";
    public const string TimerQuarantinePrefix = "pref.timerEnvelope.quarantine.";
    private const int CurrentSchemaVersion = 1;

#if TEST
    public static Action<string>? FaultInjector { get; set; }
#endif

    public static bool TryGetActiveTimer(
        out string taskId,
        out TimeSpan remaining,
        out bool expired,
        out int durationSeconds,
        out DateTimeOffset expiresAt,
        IShuffleLogger? logger = null)
        => TryGetActiveTimer(
            out taskId,
            out remaining,
            out expired,
            out durationSeconds,
            out expiresAt,
            out _,
            logger);

    public static bool TryGetActiveTimer(
        out string taskId,
        out TimeSpan remaining,
        out bool expired,
        out int durationSeconds,
        out DateTimeOffset expiresAt,
        out TimerDetails? details,
        IShuffleLogger? logger = null)
    {
        details = null;
        var started = DateTimeOffset.UtcNow;
        logger?.LogSyncEvent("PersistenceLoadStarted", "domain=timer; operation=get-active");

        if (TryReadEnvelope(out TimerEnvelope? envelope, logger))
        {
            var activeEnvelope = envelope!;
            taskId = activeEnvelope.TaskId;
            durationSeconds = activeEnvelope.DurationSeconds;
            expiresAt = activeEnvelope.ExpiresAt;
            remaining = expiresAt - DateTimeOffset.UtcNow;
            expired = remaining <= TimeSpan.Zero;
            if (expired) remaining = TimeSpan.Zero;
            if (durationSeconds <= 0)
            {
                durationSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            }

            details = activeEnvelope.ToDetails();
            logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=timer; source=canonical; expired={expired}; durationMs={(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}");
            return !string.IsNullOrWhiteSpace(taskId);
        }

        taskId = Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty);
        int seconds = Preferences.Default.Get(PreferenceKeys.TimerDurationSeconds, -1);

        durationSeconds = seconds;
        remaining = TimeSpan.Zero;
        expired = false;
        expiresAt = default;

        if (string.IsNullOrEmpty(taskId))
        {
            logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=timer; source=empty; durationMs={(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}");
            return false;
        }

        string expiresIso = Preferences.Default.Get(PreferenceKeys.TimerExpiresAt, string.Empty);
        if (!TryGetExpiration(expiresIso, out expiresAt))
        {
            QuarantineLegacyTimerState("invalid-legacy-expiration", logger);
            logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=timer; source=invalid-legacy; durationMs={(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}");
            return false;
        }

        remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
            expired = true;
        }

        if (durationSeconds <= 0)
        {
            durationSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        }

        logger?.LogSyncEvent("PersistenceLoadCompleted", $"domain=timer; source=legacy; expired={expired}; durationMs={(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}");
        return true;
    }

    private static bool TryGetExpiration(string expiresIso, out DateTimeOffset expiresAt)
    {
        if (!string.IsNullOrWhiteSpace(expiresIso)
            && DateTimeOffset.TryParse(
                expiresIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out expiresAt))
        {
            return true;
        }

        expiresAt = default;
        return false;
    }


    public static void SaveActiveTimer(
        string taskId,
        int durationSeconds,
        DateTimeOffset expiresAt,
        TimerDetails? details = null,
        IShuffleLogger? logger = null)
    {
        var started = DateTimeOffset.UtcNow;
        logger?.LogSyncEvent("PersistenceSaveStarted", "domain=timer; operation=save-active");
        var normalizedTaskId = taskId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTaskId) || durationSeconds <= 0)
        {
            Clear(logger);
            return;
        }

        var envelope = new TimerEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            TaskId = normalizedTaskId,
            DurationSeconds = durationSeconds,
            ExpiresAt = expiresAt,
            SavedAtUtc = DateTimeOffset.UtcNow,
            TimerMode = details?.TimerMode,
            PomodoroPhase = details?.PomodoroPhase,
            CycleIndex = details?.CycleIndex,
            CycleCount = details?.CycleCount,
            FocusMinutes = details?.FocusMinutes,
            BreakMinutes = details?.BreakMinutes
        };

        string json = JsonSerializer.Serialize(envelope);
        Preferences.Default.Set(TimerEnvelopeKey, json);
#if TEST
        FaultInjector?.Invoke("timer.after-canonical-write");
#endif
        Preferences.Default.Set(PreferenceKeys.CurrentTaskId, normalizedTaskId);
        Preferences.Default.Set(PreferenceKeys.TimerDurationSeconds, durationSeconds);
        Preferences.Default.Set(PreferenceKeys.TimerExpiresAt, expiresAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        logger?.LogSyncEvent("PersistenceSaveCompleted", $"domain=timer; operation=save-active; durationMs={(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}");
    }

    public static async Task<bool> RecoverAgainstStorageAsync(IStorageService storage, IShuffleLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (!TryGetActiveTimer(
                out string taskId,
                out _,
                out _,
                out _,
                out _,
                logger))
        {
            return true;
        }

        TaskItem? task = await storage.GetTaskAsync(taskId).ConfigureAwait(false);
        if (task == null)
        {
            QuarantineActiveTimer("missing-task", logger);
            Clear(logger);
            return false;
        }

        if (task.Status == TaskLifecycleStatus.Completed)
        {
            QuarantineActiveTimer("completed-task", logger);
            Clear(logger);
            return false;
        }

        if (task.Status == TaskLifecycleStatus.Snoozed)
        {
            QuarantineActiveTimer("snoozed-task", logger);
            Clear(logger);
            return false;
        }

        return true;
    }

    private static bool TryReadEnvelope(out TimerEnvelope? envelope, IShuffleLogger? logger)
    {
        envelope = null;
        string json = Preferences.Default.Get(TimerEnvelopeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<TimerEnvelope>(json);
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.TaskId))
            {
                QuarantineTimerEnvelope(json, "invalid-envelope", logger);
                return false;
            }

            if (candidate.SchemaVersion > CurrentSchemaVersion)
            {
                logger?.LogSyncEvent("PersistenceLoadUnsupportedSchema", $"domain=timer; schemaVersion={candidate.SchemaVersion}");
                return false;
            }

            if (candidate.DurationSeconds <= 0 || candidate.ExpiresAt == default)
            {
                QuarantineTimerEnvelope(json, "invalid-envelope-values", logger);
                return false;
            }

            envelope = candidate;
            return true;
        }
        catch (Exception ex)
        {
            QuarantineTimerEnvelope(json, "corrupt-envelope", logger, ex);
            return false;
        }
    }

    public static void Clear(IShuffleLogger? logger = null)
    {
        logger?.LogSyncEvent("PersistenceSaveStarted", "domain=timer; operation=clear");
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
        Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
        Preferences.Default.Remove(TimerEnvelopeKey);
        logger?.LogSyncEvent("PersistenceSaveCompleted", "domain=timer; operation=clear");
    }

    private static void QuarantineActiveTimer(string reason, IShuffleLogger? logger)
    {
        string json = Preferences.Default.Get(TimerEnvelopeKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(json))
        {
            QuarantineTimerEnvelope(json, reason, logger);
            return;
        }

        QuarantineLegacyTimerState(reason, logger);
    }

    private static void QuarantineTimerEnvelope(string json, string reason, IShuffleLogger? logger, Exception? exception = null)
    {
        string key = TimerQuarantinePrefix + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        Preferences.Default.Set(key, json);
        Preferences.Default.Remove(TimerEnvelopeKey);
        logger?.LogSyncEvent("PersistenceQuarantine", $"domain=timer; reason={reason}; artifact={key}", exception);
    }

    private static void QuarantineLegacyTimerState(string reason, IShuffleLogger? logger)
    {
        string taskId = Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty);
        string expiresAt = Preferences.Default.Get(PreferenceKeys.TimerExpiresAt, string.Empty);
        int durationSeconds = Preferences.Default.Get(PreferenceKeys.TimerDurationSeconds, -1);
        if (string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(expiresAt) && durationSeconds <= 0)
        {
            return;
        }

        var legacy = new
        {
            TaskId = taskId,
            ExpiresAt = expiresAt,
            DurationSeconds = durationSeconds
        };
        string key = TimerQuarantinePrefix + "legacy." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        Preferences.Default.Set(key, JsonSerializer.Serialize(legacy));
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
        Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
        logger?.LogSyncEvent("PersistenceQuarantine", $"domain=timer; reason={reason}; artifact={key}");
    }

    public sealed record TimerDetails(
        int TimerMode,
        int? PomodoroPhase,
        int CycleIndex,
        int CycleCount,
        int FocusMinutes,
        int BreakMinutes);

    private sealed class TimerEnvelope
    {
        public int SchemaVersion { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public int? TimerMode { get; set; }
        public int? PomodoroPhase { get; set; }
        public int? CycleIndex { get; set; }
        public int? CycleCount { get; set; }
        public int? FocusMinutes { get; set; }
        public int? BreakMinutes { get; set; }

        public TimerDetails? ToDetails()
        {
            if (!TimerMode.HasValue)
            {
                return null;
            }

            return new TimerDetails(
                TimerMode.Value,
                PomodoroPhase,
                Math.Max(1, CycleIndex ?? 1),
                Math.Max(1, CycleCount ?? 1),
                Math.Max(1, FocusMinutes ?? 15),
                Math.Max(1, BreakMinutes ?? 5));
        }
    }
}
