using System.Net;

namespace LocalSendDotNet;

public enum LocalSendDeviceType { Mobile, Desktop, Web, Headless, Server }
public enum LocalSendProtocol { Http, Https }
public enum TransferDirection { Send, Receive }
public enum TransferState { Preparing, WaitingForAcceptance, Transferring, Completed, Cancelled, Failed }
public enum DeviceChangeKind { Added, Updated, Removed }
public enum LocalSendNodeState { Created, Starting, Running, Stopping, Stopped, Faulted, Disposed }

public sealed record DeviceEndpoint
{
    public DeviceEndpoint(IPAddress address, int port, LocalSendProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        Address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        Port = port;
        Protocol = protocol;
    }

    public IPAddress Address { get; }
    public int Port { get; }
    public LocalSendProtocol Protocol { get; }
}

public sealed record LocalSendDevice(
    string Alias,
    string ProtocolVersion,
    string? DeviceModel,
    LocalSendDeviceType DeviceType,
    string Fingerprint,
    bool SupportsDownload,
    IReadOnlyList<DeviceEndpoint> Endpoints,
    DateTimeOffset LastSeen)
{
    public DeviceEndpoint? PreferredEndpoint => Endpoints.OrderByDescending(static endpoint => endpoint.Protocol == LocalSendProtocol.Https).FirstOrDefault();
}

public sealed record LocalSendIdentity(
    string Alias,
    string ProtocolVersion,
    string? DeviceModel,
    LocalSendDeviceType DeviceType,
    string Fingerprint,
    int Port,
    LocalSendProtocol Protocol);

public sealed record DeviceChange(DeviceChangeKind Kind, LocalSendDevice Device);
public sealed record DeviceProbeResult(LocalSendDevice Device, bool IdentityVerified);
public sealed record LocalSendNodeStateChange(LocalSendNodeState Previous, LocalSendNodeState Current, Exception? Error = null);

public abstract record SendItem(string FileName, string ContentType)
{
    internal abstract ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
    internal abstract long Length { get; }
}

public sealed record SendFileItem : SendItem
{
    public SendFileItem(string path, string? fileName = null, string? contentType = null)
        : base(fileName ?? System.IO.Path.GetFileName(path), contentType ?? LocalSendContentTypes.GetForFileName(fileName ?? System.IO.Path.GetFileName(path)))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }
    internal override long Length => new FileInfo(Path).Length;
    internal override ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<Stream>(new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
}

public sealed record SendTextItem(string Text, string Name = "message.txt")
    : SendItem(Name, "text/plain")
{
    private byte[]? _bytes;
    private byte[] Bytes => _bytes ??= System.Text.Encoding.UTF8.GetBytes(Text);
    internal override long Length => Bytes.LongLength;
    internal override ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<Stream>(new MemoryStream(Bytes, writable: false));
}

public sealed record SendStreamItem : SendItem
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _openStream;

    public SendStreamItem(string fileName, long length, Func<CancellationToken, ValueTask<Stream>> openStream, string? contentType = null)
        : base(fileName, contentType ?? LocalSendContentTypes.GetForFileName(fileName))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        LengthValue = length;
    }

    public long LengthValue { get; }
    internal override long Length => LengthValue;
    internal override async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        var stream = await _openStream(cancellationToken).ConfigureAwait(false);
        return stream ?? throw new LocalSendException("The stream factory returned null.");
    }
}

public sealed record IncomingItem(
    string Id,
    string FileName,
    long Size,
    string ContentType,
    string? Sha256,
    string? Preview,
    DateTimeOffset? Modified,
    DateTimeOffset? Accessed);

public sealed record IncomingTransferRequest(
    Guid RequestId,
    Guid TransferId,
    string SessionId,
    LocalSendDevice Sender,
    IReadOnlyList<IncomingItem> Items,
    DateTimeOffset ReceivedAt);

public sealed class AcceptTransferOptions
{
    public string? DestinationDirectory { get; init; }
    public IReadOnlyCollection<string>? AcceptedItemIds { get; init; }
    public IReadOnlyDictionary<string, string>? TargetFileNames { get; init; }
}

public sealed class SendOptions
{
    public string? Pin { get; init; }
    public bool ComputeSha256 { get; init; }
}

public sealed record TransferProgress(
    Guid TransferId,
    string? ItemId,
    TransferDirection Direction,
    TransferState State,
    long BytesTransferred,
    long TotalBytes);

public sealed record TransferredItemResult(string ItemId, string FileName, long BytesTransferred, string? SavedPath);
public sealed record TransferFailure(string Code, string Message, string? ItemId = null);

public sealed record TransferResult(
    Guid TransferId,
    TransferDirection Direction,
    TransferState State,
    IReadOnlyList<TransferredItemResult> Items,
    TransferFailure? Failure = null)
{
    public bool IsSuccess => State == TransferState.Completed;
    public long BytesTransferred => Items.Sum(static item => item.BytesTransferred);
}

public static class TransferFailureCodes
{
    public const string PrepareFailed = "prepare_failed";
    public const string UploadFailed = "upload_failed";
    public const string PeerIdentity = "peer_identity";
    public const string PeerBusy = "peer_busy";
    public const string Declined = "declined";
    public const string SourceIo = "source_io";
    public const string InvalidDestination = "invalid_destination";
    public const string LengthMismatch = "length_mismatch";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string ReceiveFailed = "receive_failed";
    public const string TransferTimeout = "transfer_timeout";
}
