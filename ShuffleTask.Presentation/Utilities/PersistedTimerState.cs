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
        out int persistedSeconds)
    {
        taskId = Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty);
        int seconds = Preferences.Default.Get(PreferenceKeys.RemainingSeconds, -1);

        persistedSeconds = seconds;
        remaining = TimeSpan.Zero;
        expired = false;

        if (string.IsNullOrEmpty(taskId) || seconds <= 0)
        {
            return false;
        }

        remaining = TimeSpan.FromSeconds(seconds);

        string storedAtIso = Preferences.Default.Get(PreferenceKeys.RemainingPersistedAt, string.Empty);
        if (!string.IsNullOrWhiteSpace(storedAtIso)
            && DateTimeOffset.TryParse(
                storedAtIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var persistedAt))
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - persistedAt;
            if (elapsed > TimeSpan.Zero)
            {
                if (elapsed >= remaining)
                {
                    remaining = TimeSpan.Zero;
                    expired = true;
                }
                else
                {
                    remaining -= elapsed;
                }
            }
        }

        return true;
    }
}
