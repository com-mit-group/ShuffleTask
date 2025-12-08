using Newtonsoft.Json;
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

    [Ignore]
    public new Dictionary<string, DateTime> FieldUpdatedAt
    {
        get => base.FieldUpdatedAt;
        set => base.FieldUpdatedAt = value;
    }

    [Column("FieldUpdatedAt")]
    public string FieldUpdatedAtJson
    {
        get => JsonConvert.SerializeObject(base.FieldUpdatedAt);
        set => base.FieldUpdatedAt = string.IsNullOrWhiteSpace(value)
            ? new Dictionary<string, DateTime>()
            : JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(value) ?? new Dictionary<string, DateTime>();
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
        // Legacy compatibility: builds prior to the AutoShuffleAllowed/Custom window feature
        // stored OffWork as the integer value 3. After renumbering, 3 now maps to Custom, so
        // older records deserialize with Custom selected but without explicit hours. Reset them
        // to OffWork so their scheduling behavior remains unchanged for upgraded installs.
        if (AllowedPeriod == AllowedPeriod.Custom && CustomStartTime is null && CustomEndTime is null)
        {
            AllowedPeriod = AllowedPeriod.OffWork;
        }

        return TaskItem.FromData(this);
    }
}
