namespace ShuffleTask.Domain.Entities;

public enum RepeatType
{
    None,
    Daily,
    Weekly,
    Interval
}

[Flags]
public enum Weekdays
{
    None = 0,
    Sun = 1,
    Mon = 2,
    Tue = 4,
    Wed = 8,
    Thu = 16,
    Fri = 32,
    Sat = 64
}

public enum AllowedPeriod
{
    Any = 0,
    Work = 1,
    Off = 2,
    OffWork = 3
}

public enum TaskLifecycleStatus
{
    Active = 0,
    Snoozed = 1,
    Completed = 2
}

public enum CutInLineMode
{
    None = 0,
    Once = 1,
    UntilCompletion = 2
}
