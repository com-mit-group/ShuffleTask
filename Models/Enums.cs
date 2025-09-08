namespace ShuffleTask.Models;

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
    Any,
    Work,
    Off
}
