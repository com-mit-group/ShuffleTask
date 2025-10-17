using System;
using System.Collections.Generic;

namespace ShuffleTask.Application.Sync;

public sealed class SyncOptions
{
    public bool Enabled { get; set; } = true;

    public int ListenPort { get; set; } = 27850;

    public List<SyncPeer> Peers { get; } = new();

    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(10);

    public static SyncOptions LoadFromEnvironment()
    {
        var options = new SyncOptions();
        string? disabled = Environment.GetEnvironmentVariable("SHUFFLETASK_SYNC_DISABLED");
        if (!string.IsNullOrWhiteSpace(disabled) && bool.TryParse(disabled, out bool disabledValue) && disabledValue)
        {
            options.Enabled = false;
        }

        string? listenPort = Environment.GetEnvironmentVariable("SHUFFLETASK_SYNC_LISTEN_PORT");
        if (int.TryParse(listenPort, out int parsedPort) && parsedPort > 0)
        {
            options.ListenPort = parsedPort;
        }

        string? host = Environment.GetEnvironmentVariable("SHUFFLETASK_SYNC_HOST");
        string? portValue = Environment.GetEnvironmentVariable("SHUFFLETASK_SYNC_PORT");
        if (!string.IsNullOrWhiteSpace(host) && int.TryParse(portValue, out int peerPort) && peerPort > 0)
        {
            options.Peers.Add(new SyncPeer(host, peerPort));
        }

        return options;
    }
}

public sealed record SyncPeer(string Host, int Port);
