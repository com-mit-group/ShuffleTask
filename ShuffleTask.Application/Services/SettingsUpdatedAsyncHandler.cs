using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Abstractions;
using System;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Services;

internal class SettingsUpdatedAsyncHandler : IAsyncEventHandler<SettingsUpdatedEvent>
{
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly IStorageService _storage;
    private readonly AppSettings _appSettings;

    public SettingsUpdatedAsyncHandler(ILogger<NetworkSyncService>? logger, IStorageService storage, AppSettings appSettings)
    {
        _logger = logger;
        _storage = storage;
        _appSettings = appSettings;
    }

    public async Task OnNextAsync(SettingsUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(domainEvent.Settings);

        if (IsAnonymousSession(_appSettings))
        {
            return;
        }

        if (!IsSameUser(domainEvent.UserId, _appSettings.Network?.UserId))
        {
            return;
        }

        try
        {
            var current = await _storage.GetSettingsAsync().ConfigureAwait(false);
            var incoming = NormalizeIncoming(domainEvent.Settings, current);

            if (IsStale(incoming, current))
            {
                _logger?.LogInformation("Ignoring stale settings update with version {Version}", incoming.EventVersion);
                return;
            }

            await _storage.SetSettingsAsync(incoming).ConfigureAwait(false);
            _appSettings.CopyFrom(incoming);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound settings update");
        }
    }

    private static bool IsAnonymousSession(AppSettings settings)
    {
        return settings.Network?.AnonymousSession is true || string.IsNullOrWhiteSpace(settings.Network?.UserId);
    }

    private static bool IsSameUser(string? eventUserId, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(eventUserId) || string.IsNullOrWhiteSpace(currentUserId))
        {
            return false;
        }

        return string.Equals(eventUserId, currentUserId, StringComparison.Ordinal);
    }

    private static bool IsStale(AppSettings incoming, AppSettings? existing)
    {
        if (existing is null)
        {
            return false;
        }

        if (incoming.EventVersion > 0 || existing.EventVersion > 0)
        {
            if (incoming.EventVersion > existing.EventVersion)
            {
                return false;
            }

            if (incoming.EventVersion < existing.EventVersion)
            {
                return true;
            }

            if (incoming.UpdatedAt != default && existing.UpdatedAt != default)
            {
                return incoming.UpdatedAt <= existing.UpdatedAt;
            }
        }

        if (incoming.UpdatedAt != default && existing.UpdatedAt != default)
        {
            return incoming.UpdatedAt <= existing.UpdatedAt;
        }

        return false;
    }

    private static AppSettings NormalizeIncoming(AppSettings source, AppSettings? existing)
    {
        var normalized = new AppSettings();
        normalized.CopyFrom(source);

        normalized.UpdatedAt = normalized.UpdatedAt == default ? DateTime.UtcNow : normalized.UpdatedAt;
        normalized.EventVersion = Math.Max(1, normalized.EventVersion);

        if (!string.IsNullOrWhiteSpace(normalized.Network?.UserId))
        {
            normalized.Network.DeviceId = null;
        }
        else
        {
            normalized.Network.UserId = existing?.Network?.UserId;
        }

        if (existing is null)
        {
            return normalized;
        }

        if (normalized.UpdatedAt == default)
        {
            normalized.UpdatedAt = existing.UpdatedAt;
        }

        if (normalized.EventVersion <= 0)
        {
            normalized.EventVersion = existing.EventVersion + 1;
        }

        return normalized;
    }
}
