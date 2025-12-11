using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;

namespace ShuffleTask.Application.Services;

public class PeerSyncCoordinator
{
    private readonly IStorageService _storageService;

    public PeerSyncCoordinator(IStorageService storageService)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<ManifestComparisonResult> CompareManifestAsync(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId = "",
        string deviceId = "")
    {
        ArgumentNullException.ThrowIfNull(remoteManifest);

        var remoteEntries = remoteManifest
            .GroupBy(entry => entry.TaskId)
            .ToDictionary(group => group.Key, group => group.First());

        var localManifest = await LoadLocalManifestAsync(userId, deviceId).ConfigureAwait(false);

        var missing = new List<TaskManifestEntry>();
        var remoteNewer = new List<TaskManifestEntry>();
        var localNewer = new List<TaskManifestEntry>();
        var equal = new List<TaskManifestEntry>();

        foreach (var localEntry in localManifest)
        {
            if (!remoteEntries.TryGetValue(localEntry.TaskId, out var remoteEntry))
            {
                localNewer.Add(localEntry);
                continue;
            }

            var comparison = CompareEntries(localEntry, remoteEntry);
            switch (comparison)
            {
                case ManifestComparison.LocalNewer:
                    localNewer.Add(localEntry);
                    break;
                case ManifestComparison.RemoteNewer:
                    remoteNewer.Add(remoteEntry);
                    break;
                case ManifestComparison.Equal:
                    equal.Add(localEntry);
                    break;
            }

            remoteEntries.Remove(localEntry.TaskId);
        }

        missing.AddRange(remoteEntries.Values);

        return new ManifestComparisonResult(missing, remoteNewer, localNewer, equal);
    }

    public async Task<IReadOnlyCollection<string>> GetTasksToRequestAsync(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId = "",
        string deviceId = "")
    {
        var comparison = await CompareManifestAsync(remoteManifest, userId, deviceId).ConfigureAwait(false);
        return comparison.TasksToRequest;
    }

    public async Task<IReadOnlyCollection<string>> GetTasksToAdvertiseAsync(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId = "",
        string deviceId = "")
    {
        var comparison = await CompareManifestAsync(remoteManifest, userId, deviceId).ConfigureAwait(false);
        return comparison.TasksToAdvertise;
    }

    private static ManifestComparison CompareEntries(TaskManifestEntry local, TaskManifestEntry remote)
    {
        if (local.EventVersion > remote.EventVersion)
        {
            return ManifestComparison.LocalNewer;
        }

        if (local.EventVersion < remote.EventVersion)
        {
            return ManifestComparison.RemoteNewer;
        }

        if (local.UpdatedAt > remote.UpdatedAt)
        {
            return ManifestComparison.LocalNewer;
        }

        if (local.UpdatedAt < remote.UpdatedAt)
        {
            return ManifestComparison.RemoteNewer;
        }

        return ManifestComparison.Equal;
    }

    private async Task<IReadOnlyCollection<TaskManifestEntry>> LoadLocalManifestAsync(string? userId, string deviceId)
    {
        var tasks = await _storageService.GetTasksAsync(userId, deviceId).ConfigureAwait(false);
        return tasks.Select(ToManifestEntry).ToList();
    }

    private static TaskManifestEntry ToManifestEntry(ShuffleTask.Domain.Entities.TaskItem task)
    {
        return new TaskManifestEntry
        {
            TaskId = task.Id,
            EventVersion = task.EventVersion,
            UpdatedAt = task.UpdatedAt,
            DeviceId = task.DeviceId,
            UserId = task.UserId,
        };
    }
}

public enum ManifestComparison
{
    Equal,
    LocalNewer,
    RemoteNewer,
}

public class ManifestComparisonResult
{
    public ManifestComparisonResult(
        IReadOnlyCollection<TaskManifestEntry> missing,
        IReadOnlyCollection<TaskManifestEntry> remoteNewer,
        IReadOnlyCollection<TaskManifestEntry> localNewer,
        IReadOnlyCollection<TaskManifestEntry> equal)
    {
        Missing = missing ?? throw new ArgumentNullException(nameof(missing));
        RemoteNewer = remoteNewer ?? throw new ArgumentNullException(nameof(remoteNewer));
        LocalNewer = localNewer ?? throw new ArgumentNullException(nameof(localNewer));
        Equal = equal ?? throw new ArgumentNullException(nameof(equal));
    }

    public IReadOnlyCollection<TaskManifestEntry> Missing { get; }

    public IReadOnlyCollection<TaskManifestEntry> RemoteNewer { get; }

    public IReadOnlyCollection<TaskManifestEntry> LocalNewer { get; }

    public IReadOnlyCollection<TaskManifestEntry> Equal { get; }

    public IReadOnlyCollection<string> TasksToRequest => Missing
        .Concat(RemoteNewer)
        .Select(entry => entry.TaskId)
        .Distinct()
        .ToList();

    public IReadOnlyCollection<string> TasksToAdvertise => LocalNewer
        .Select(entry => entry.TaskId)
        .Distinct()
        .ToList();
}
