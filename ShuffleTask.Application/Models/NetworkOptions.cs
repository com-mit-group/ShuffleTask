using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShuffleTask.Application.Models;

public partial class NetworkOptions : ObservableObject
{
    private const int MinPort = 15000;
    private const int MaxPort = 45000;
    private const int PortRange = MaxPort - MinPort;

    [ObservableProperty]
    private string host = "127.0.0.1";

    [ObservableProperty]
    private int listeningPort;

    [ObservableProperty]
    private string deviceId = Environment.MachineName;

    [ObservableProperty]
    private string? userId = Environment.UserName;

    [ObservableProperty]
    private string peerHost = "127.0.0.1";

    [ObservableProperty]
    private int peerPort;

    public static NetworkOptions CreateDefault()
    {
        var options = new NetworkOptions();
        options.EnsureListeningPort();
        return options;
    }

    public NetworkOptions Clone()
    {
        return new NetworkOptions
        {
            Host = Host,
            ListeningPort = ListeningPort,
            DeviceId = DeviceId,
            UserId = UserId,
            PeerHost = PeerHost,
            PeerPort = PeerPort,
        };
    }

    public void Normalize()
    {
        Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim();
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId.Trim();
        UserId = string.IsNullOrWhiteSpace(UserId) ? null : UserId.Trim();
        PeerHost = string.IsNullOrWhiteSpace(PeerHost) ? "127.0.0.1" : PeerHost.Trim();
        ListeningPort = NormalizePort(ListeningPort, DeviceId);
        PeerPort = NormalizePort(PeerPort, DeviceId, allowZero: true);
    }

    public string ResolveAuthenticationSecret()
    {
        return string.IsNullOrWhiteSpace(UserId) ? DeviceId : UserId;
    }

    public Guid ResolveSessionUserId()
    {
        if (!string.IsNullOrWhiteSpace(UserId) && Guid.TryParse(UserId, out Guid parsed))
        {
            return parsed;
        }

        return CreateGuidFromString(UserId ?? DeviceId);
    }

    public string BuildAuthToken()
    {
        string secret = ResolveAuthenticationSecret();
        Guid sessionUserId = ResolveSessionUserId();
        return $"{sessionUserId}@{Host}:{ListeningPort}||{secret}";
    }

    public void EnsureListeningPort()
    {
        ListeningPort = NormalizePort(ListeningPort, DeviceId);
    }

    private static int NormalizePort(int port, string seed, bool allowZero = false)
    {
        if (port > 0 && port <= MaxPort)
        {
            return port;
        }

        if (allowZero && port == 0)
        {
            return 0;
        }

        int hash = ComputeStableHash(seed);
        int offset = hash % PortRange;
        return MinPort + offset;
    }

    private static int ComputeStableHash(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        int value = BitConverter.ToInt32(hash, 0);
        return Math.Abs(value);
    }

    private static Guid CreateGuidFromString(string value)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
