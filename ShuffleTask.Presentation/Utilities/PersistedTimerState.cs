using Microsoft.Maui.Storage;
using System.Globalization;

namespace ShuffleTask.Presentation.Utilities;

internal static class PersistedTimerState
{
    private const string TimerEnvelopeKey = "pref.timerEnvelope";
    private const int CurrentSchemaVersion = 1;
    public static bool TryGetActiveTimer(
        out string taskId,
        out TimeSpan remaining,
        out bool expired,
        out int durationSeconds,
        out DateTimeOffset expiresAt)
    {
        if (TryReadEnvelope(out TimerEnvelope? envelope))
        {
            taskId = envelope.TaskId;
            durationSeconds = envelope.DurationSeconds;
            expiresAt = envelope.ExpiresAt;
            remaining = expiresAt - DateTimeOffset.UtcNow;
            expired = remaining <= TimeSpan.Zero;
            if (expired) remaining = TimeSpan.Zero;
            if (durationSeconds <= 0)
            {
                durationSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            }

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
            return false;
        }

        string expiresIso = Preferences.Default.Get(PreferenceKeys.TimerExpiresAt, string.Empty);
        if (!TryGetExpiration(expiresIso, out expiresAt))
        {
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


    public static void SaveActiveTimer(string taskId, int durationSeconds, DateTimeOffset expiresAt)
    {
        var normalizedTaskId = taskId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTaskId) || durationSeconds <= 0)
        {
            Clear();
            return;
        }

        var envelope = new TimerEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            TaskId = normalizedTaskId,
            DurationSeconds = durationSeconds,
            ExpiresAt = expiresAt,
            SavedAtUtc = DateTimeOffset.UtcNow
        };

        string json = System.Text.Json.JsonSerializer.Serialize(envelope);
        Preferences.Default.Set(TimerEnvelopeKey, json);
        Preferences.Default.Set(PreferenceKeys.CurrentTaskId, normalizedTaskId);
        Preferences.Default.Set(PreferenceKeys.TimerDurationSeconds, durationSeconds);
        Preferences.Default.Set(PreferenceKeys.TimerExpiresAt, expiresAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }

    private static bool TryReadEnvelope(out TimerEnvelope? envelope)
    {
        envelope = null;
        string json = Preferences.Default.Get(TimerEnvelopeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var candidate = System.Text.Json.JsonSerializer.Deserialize<TimerEnvelope>(json);
            if (candidate == null || candidate.SchemaVersion > CurrentSchemaVersion || string.IsNullOrWhiteSpace(candidate.TaskId))
            {
                return false;
            }

            envelope = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Clear()
    {
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
        Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
        Preferences.Default.Remove(TimerEnvelopeKey);
    }

    private sealed class TimerEnvelope
    {
        public int SchemaVersion { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
    }
}
