namespace ShuffleTask;

internal static class PreferenceKeys
{
    public const string CurrentTaskId = "pref.currentTaskId";
    public const string TimerDurationSeconds = "pref.timerDurationSecs";
    public const string TimerExpiresAt = "pref.timerExpiresAt";
    public const string RemainingSeconds = "pref.remainingSecs"; // legacy
    public const string RemainingPersistedAt = "pref.remainingPersistedAt"; // legacy
    public const string NextShuffleAt = "pref.nextShuffleAt";
    public const string PendingShuffleTaskId = "pref.pendingTaskId";
    public const string ShuffleCountDate = "pref.shuffleCountDate";
    public const string ShuffleCount = "pref.shuffleCount";
    public const string CutInLineTaskId = "pref.cutInLineTaskId";
}
