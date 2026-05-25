using SQLite;

namespace ShuffleTask.Persistence.Models;

internal sealed class KeyValueEntity
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }
}
