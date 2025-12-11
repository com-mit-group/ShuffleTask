using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ShuffleTask.Application.Services;

public class PeerSyncCoordinator
{
    private readonly IStorageService _storageService;
    private readonly SemaphoreSlim _comparisonLock = new(1, 1);

    private ManifestComparisonResult? _comparison;
    private ManifestComparisonCacheKey? _comparisonKey;

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

        var remoteEntries = remoteManifest.ToList();
        var cacheKey = ManifestComparisonCacheKey.From(remoteEntries, userId, deviceId);

        await _comparisonLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_comparison is not null && cacheKey.Equals(_comparisonKey))
            {
                return _comparison;
            }

            var comparison = await CompareManifestInternalAsync(remoteEntries, userId, deviceId)
                .ConfigureAwait(false);

            _comparisonKey = cacheKey;
            _comparison = comparison;

            return comparison;
        }
        finally
        {
            _comparisonLock.Release();
        }
    }

    public async Task<IReadOnlyCollection<string>> GetTasksToRequestAsync(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId = "",
        string deviceId = "")
    {
        var comparison = await CompareManifestAsync(remoteManifest, userId, deviceId).ConfigureAwait(false);
        return comparison.GetTasksToRequest();
    }

    public async Task<IReadOnlyCollection<string>> GetTasksToAdvertiseAsync(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId = "",
        string deviceId = "")
    {
        var comparison = await CompareManifestAsync(remoteManifest, userId, deviceId).ConfigureAwait(false);
        return comparison.GetTasksToAdvertise();
    }

    private async Task<ManifestComparisonResult> CompareManifestInternalAsync(
        IReadOnlyCollection<TaskManifestEntry> remoteManifest,
        string? userId,
        string deviceId)
    {
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

        _tasksToRequest = BuildDistinctTaskIds(Missing.Concat(RemoteNewer));
        _tasksToAdvertise = BuildDistinctTaskIds(LocalNewer);
    }

    public IReadOnlyCollection<TaskManifestEntry> Missing { get; }

    public IReadOnlyCollection<TaskManifestEntry> RemoteNewer { get; }

    public IReadOnlyCollection<TaskManifestEntry> LocalNewer { get; }

    public IReadOnlyCollection<TaskManifestEntry> Equal { get; }

    public IReadOnlyCollection<string> GetTasksToRequest() => _tasksToRequest;

    public IReadOnlyCollection<string> GetTasksToAdvertise() => _tasksToAdvertise;

    private static IReadOnlyCollection<string> BuildDistinctTaskIds(IEnumerable<TaskManifestEntry> entries)
    {
        return entries
            .Select(entry => entry.TaskId)
            .Distinct()
            .ToArray();
    }

    private readonly IReadOnlyCollection<string> _tasksToRequest;
    private readonly IReadOnlyCollection<string> _tasksToAdvertise;
}

internal sealed class ManifestComparisonCacheKey : IEquatable<ManifestComparisonCacheKey>
{
    public ManifestComparisonCacheKey(string? userId, string deviceId, IReadOnlyList<ManifestEntryKey> remoteEntries)
    {
        UserId = userId;
        DeviceId = deviceId;
        RemoteEntries = remoteEntries;
    }

    public string? UserId { get; }
    public string DeviceId { get; }
    public IReadOnlyList<ManifestEntryKey> RemoteEntries { get; }

    public bool Equals(ManifestComparisonCacheKey? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return string.Equals(UserId, other.UserId, StringComparison.Ordinal)
            && string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal)
            && RemoteEntries.SequenceEqual(other.RemoteEntries);
    }

    public override bool Equals(object? obj) => Equals(obj as ManifestComparisonCacheKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(UserId, StringComparer.Ordinal);
        hash.Add(DeviceId, StringComparer.Ordinal);

        foreach (var entry in RemoteEntries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    public static ManifestComparisonCacheKey From(
        IEnumerable<TaskManifestEntry> remoteManifest,
        string? userId,
        string deviceId)
    {
        var entries = remoteManifest
            .Select(entry => new ManifestEntryKey(entry.TaskId, entry.EventVersion, entry.UpdatedAt))
            .OrderBy(entry => entry.TaskId)
            .ThenBy(entry => entry.EventVersion)
            .ThenBy(entry => entry.UpdatedAt)
            .ToArray();

        return new ManifestComparisonCacheKey(userId, deviceId, entries);
    }
}

internal sealed record ManifestEntryKey(string TaskId, int EventVersion, DateTime UpdatedAt);
