using SQLite;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Persistence.Models;

[Table("TaskItem")]
internal sealed class TaskItemRecord : TaskItemData
{
    [PrimaryKey]
    public new string Id
    {
        get => base.Id;
        set => base.Id = value;
    }

    [Indexed]
    public new string Title
    {
        get => base.Title;
        set => base.Title = value;
    }

    public static TaskItemRecord FromDomain(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var record = new TaskItemRecord();
        record.CopyFrom(task);
        return record;
    }

    public TaskItem ToDomain()
    {
        if (AllowedPeriod == AllowedPeriod.Custom && CustomStartTime is null && CustomEndTime is null)
        {
            AllowedPeriod = AllowedPeriod.OffWork;
        }

        return TaskItem.FromData(this);
    }
}
