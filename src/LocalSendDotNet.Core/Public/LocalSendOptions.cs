using System.Net;

namespace LocalSendDotNet;

/// <summary>Configures one LocalSend node.</summary>
public sealed class LocalSendOptions
{
    public const int DefaultPort = 53317;
    public static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("224.0.0.167");

    public required string Alias { get; init; }
    public string? DeviceModel { get; init; } = Environment.OSVersion.Platform.ToString();
    public LocalSendDeviceType DeviceType { get; init; } = LocalSendDeviceType.Desktop;
    public required string DataDirectory { get; init; }
    public required string DownloadDirectory { get; init; }
    public int Port { get; init; } = DefaultPort;
    public IPAddress MulticastAddress { get; init; } = DefaultMulticastAddress;
    public bool EnableHttps { get; init; } = true;
    public string? ReceivePin { get; init; }
    public int MaxConcurrentTransfers { get; init; } = 4;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IncomingDecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentTransfers, 1);
        if (MulticastAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("The v2 discovery multicast address must be IPv4.", nameof(MulticastAddress));
        if (ReceivePin is { Length: 0 })
            throw new ArgumentException("A receive PIN cannot be empty.", nameof(ReceivePin));
    }
}
