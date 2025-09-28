namespace ShuffleTask.Domain.Entities;

public record struct ScoredTask(TaskItem Task, double Score)
{
    public static implicit operator (TaskItem Task, double Score)(ScoredTask value)
    {
        return (value.Task, value.Score);
    }

    public static implicit operator ScoredTask((TaskItem Task, double Score) value)
    {
        return new ScoredTask(value.Task, value.Score);
    }
}
