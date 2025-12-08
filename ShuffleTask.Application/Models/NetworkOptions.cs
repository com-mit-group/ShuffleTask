using CommunityToolkit.Mvvm.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ShuffleTask.Application.Models;

public partial class NetworkOptions : ObservableObject
{
    private const int MinPort = 15000;
    private const int MaxPort = 45000;
    private const int PortRange = MaxPort - MinPort;
    private const string LocalHostString = "127.0.0.1";
    [ObservableProperty]
    private string host;

    [ObservableProperty]
    private int listeningPort;

    [ObservableProperty]
    private string deviceId = Environment.MachineName;

    [ObservableProperty]
    private string? userId = Environment.UserName;

    [ObservableProperty]
    private string peerHost = LocalHostString;

    [ObservableProperty]
    private int peerPort;

    public static NetworkOptions CreateDefault()
    {
        var options = new NetworkOptions();
        options.Normalize();
        return options;
    }

    private void ResolveLocalHost()
    {

        try
        {
            string hostName = Dns.GetHostName();
            var entry = Dns.GetHostEntry(hostName);
            var address = entry.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            Host = address?.ToString() ?? hostName;
        }
        catch
        {
            Host = LocalHostString;
        }
    }

    public void Normalize()
    {
        ResolveLocalHost();
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId.Trim();
        UserId = string.IsNullOrWhiteSpace(UserId) ? null : UserId.Trim();
        PeerHost = string.IsNullOrWhiteSpace(PeerHost) ? LocalHostString : PeerHost.Trim();
        ListeningPort = NormalizePort(ListeningPort, DeviceId);
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
