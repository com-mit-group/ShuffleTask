using Microsoft.Maui.Storage;
using System.Globalization;

namespace ShuffleTask.Presentation.Utilities;

internal static class PersistedTimerState
{
    public static bool TryGetActiveTimer(
        out string taskId,
        out TimeSpan remaining,
        out bool expired,
        out int durationSeconds,
        out DateTimeOffset expiresAt)
    {
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

    public static void Clear()
    {
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
        Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
    }
}
