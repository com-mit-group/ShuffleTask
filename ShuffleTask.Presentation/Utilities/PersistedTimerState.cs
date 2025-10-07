using System;
using System.Globalization;
using Microsoft.Maui.Storage;
using ShuffleTask;

namespace ShuffleTask.Presentation.Utilities;

internal static class PersistedTimerState
{
    public static bool TryGetActiveTimer(
        out string taskId,
        out TimeSpan remaining,
        out bool expired,
        out int durationSeconds)
    {
        taskId = Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty);
        int seconds = Preferences.Default.Get(PreferenceKeys.TimerDurationSeconds, -1);

        durationSeconds = seconds;
        remaining = TimeSpan.Zero;
        expired = false;

        if (string.IsNullOrEmpty(taskId))
        {
            return false;
        }

        string expiresIso = Preferences.Default.Get(PreferenceKeys.TimerExpiresAt, string.Empty);
        if (!TryGetExpiration(expiresIso, out DateTimeOffset expiresAt))
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
            int legacySeconds = Preferences.Default.Get(PreferenceKeys.RemainingSeconds, -1);
            if (legacySeconds > 0)
            {
                durationSeconds = legacySeconds;
                Preferences.Default.Set(PreferenceKeys.TimerDurationSeconds, durationSeconds);
            }
            else
            {
                durationSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            }
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

        int legacySeconds = Preferences.Default.Get(PreferenceKeys.RemainingSeconds, -1);
        string persistedIso = Preferences.Default.Get(PreferenceKeys.RemainingPersistedAt, string.Empty);
        if (legacySeconds > 0
            && !string.IsNullOrWhiteSpace(persistedIso)
            && DateTimeOffset.TryParse(
                persistedIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var persistedAt))
        {
            expiresAt = persistedAt.AddSeconds(legacySeconds);
            Preferences.Default.Set(PreferenceKeys.TimerExpiresAt, expiresAt.ToString("O", CultureInfo.InvariantCulture));
            Preferences.Default.Remove(PreferenceKeys.RemainingPersistedAt);
            Preferences.Default.Remove(PreferenceKeys.RemainingSeconds);
            return true;
        }

        expiresAt = default;
        return false;
    }
}
