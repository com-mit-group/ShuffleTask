using ShuffleTask.Application.Models;

namespace ShuffleTask.Persistence.Models;

internal sealed class SettingsPayload
{
    public int SchemaVersion { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public DateTime LastSuccessfulSaveUtc { get; set; }

    public AppSettings? Data { get; set; }
}
