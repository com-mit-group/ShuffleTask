namespace ShuffleTask.Domain.Entities;

public class TaskItem : TaskItemData
{
    public static TaskItem Clone(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return FromData(task);
    }

    public TaskItem Clone()
    {
        return FromData(this);
    }

    public static TaskItem FromData(TaskItemData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var task = new TaskItem();
        task.CopyFrom(data);
        return task;
    }
}
