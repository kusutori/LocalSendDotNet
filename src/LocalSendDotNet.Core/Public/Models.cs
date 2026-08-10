using System.Net;

namespace LocalSendDotNet;

public enum LocalSendDeviceType { Mobile, Desktop, Web, Headless, Server }
public enum LocalSendProtocol { Http, Https }
public enum TransferDirection { Send, Receive }
public enum TransferState { Preparing, WaitingForAcceptance, Transferring, Completed, Cancelled, Failed }
public enum DeviceChangeKind { Added, Updated }

public sealed record DeviceEndpoint(IPAddress Address, int Port, LocalSendProtocol Protocol);

public sealed record LocalSendDevice(
    string Alias,
    string ProtocolVersion,
    string? DeviceModel,
    LocalSendDeviceType DeviceType,
    string Fingerprint,
    bool SupportsDownload,
    IReadOnlyList<DeviceEndpoint> Endpoints,
    DateTimeOffset LastSeen);

public sealed record DeviceChange(DeviceChangeKind Kind, LocalSendDevice Device);

public abstract record SendItem(string FileName, string ContentType)
{
    internal abstract ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
    internal abstract long Length { get; }
}

public sealed record SendFileItem : SendItem
{
    public SendFileItem(string path, string? fileName = null, string? contentType = null)
        : base(fileName ?? System.IO.Path.GetFileName(path), contentType ?? "application/octet-stream")
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
    private byte[] Bytes => System.Text.Encoding.UTF8.GetBytes(Text);
    internal override long Length => Bytes.LongLength;
    internal override ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<Stream>(new MemoryStream(Bytes, writable: false));
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
    TransferFailure? Failure = null);
