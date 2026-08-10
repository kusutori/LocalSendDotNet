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
    public int MaxConcurrentFileUploads { get; init; } = 8;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromHours(12);
    public TimeSpan CancelRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan IncomingDecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan IncomingTransferTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan PinLockoutDuration { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan AnnouncementInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DeviceExpiration { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxIncomingItemsPerTransfer { get; init; } = 10_000;
    public long MaxIncomingTransferBytes { get; init; } = long.MaxValue;
    public long MaxPrepareRequestBytes { get; init; } = 4 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentTransfers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentFileUploads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxIncomingItemsPerTransfer, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxIncomingTransferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPrepareRequestBytes);
        ValidateTimeout(RequestTimeout, nameof(RequestTimeout));
        ValidateTimeout(UploadTimeout, nameof(UploadTimeout));
        ValidateTimeout(CancelRequestTimeout, nameof(CancelRequestTimeout));
        ValidateTimeout(IncomingDecisionTimeout, nameof(IncomingDecisionTimeout));
        ValidateTimeout(IncomingTransferTimeout, nameof(IncomingTransferTimeout));
        if (PinLockoutDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PinLockoutDuration));
        if (AnnouncementInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(AnnouncementInterval));
        if (DeviceExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DeviceExpiration));
        if (DeviceExpiration <= AnnouncementInterval)
            throw new ArgumentException("Device expiration must be greater than the announcement interval.", nameof(DeviceExpiration));
        if (MulticastAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("The v2 discovery multicast address must be IPv4.", nameof(MulticastAddress));
        if (ReceivePin is { Length: 0 })
            throw new ArgumentException("A receive PIN cannot be empty.", nameof(ReceivePin));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(name, "The timeout must be positive or infinite.");
    }
}
