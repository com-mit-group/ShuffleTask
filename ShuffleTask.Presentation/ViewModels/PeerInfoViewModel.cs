using ShuffleTask.Application.Models;
using System;

namespace ShuffleTask.ViewModels;

public sealed class PeerInfoViewModel
{
    public PeerInfoViewModel(PeerInfo peer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(peer);

        DeviceId = string.IsNullOrWhiteSpace(peer.DeviceId) ? "Unknown device" : peer.DeviceId;
        UserIdLabel = string.IsNullOrWhiteSpace(peer.UserId) ? "User: Unknown" : $"User: {peer.UserId}";
        SessionIdLabel = string.IsNullOrWhiteSpace(peer.SessionId) ? "Session: Unknown" : $"Session: {peer.SessionId}";
        ConnectionStateLabel = string.IsNullOrWhiteSpace(peer.ConnectionState) ? "State: Unknown" : $"State: {peer.ConnectionState}";
        LastSeenLabel = FormatLastSeen(peer.LastSeen, now);
    }

    public string DeviceId { get; }

    public string UserIdLabel { get; }

    public string SessionIdLabel { get; }

    public string ConnectionStateLabel { get; }

    public string LastSeenLabel { get; }

    private static string FormatLastSeen(DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        if (!lastSeen.HasValue)
        {
            return "Last seen: Unknown";
        }

        var lastSeenUtc = lastSeen.Value.ToUniversalTime();
        var delta = now.ToUniversalTime() - lastSeenUtc;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "Last seen: just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"Last seen: {Math.Floor(delta.TotalMinutes)}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"Last seen: {Math.Floor(delta.TotalHours)}h ago";
        }

        return $"Last seen: {lastSeen.Value.LocalDateTime:g}";
    }
}
