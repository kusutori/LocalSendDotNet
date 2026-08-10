using System.Net;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet;

internal sealed record IncomingDecision(bool Accepted, AcceptTransferOptions? Options);

internal sealed class IncomingSession
{
    private int _remaining;
    private readonly List<TransferredItemResult> _results = [];
    private readonly object _gate = new();
    private readonly object _tokenGate = new();

    public required Guid RequestId { get; init; }
    public required Guid TransferId { get; init; }
    public required string SessionId { get; init; }
    public required IPAddress RemoteAddress { get; init; }
    public required PrepareUploadRequestDto Request { get; init; }
    public required IncomingTransferRequest PublicRequest { get; init; }
    public required TaskCompletionSource<IncomingDecision> Decision { get; init; }
    public required TaskCompletionSource<TransferResult> Completion { get; init; }
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Destinations { get; } = new(StringComparer.Ordinal);
    public CancellationTokenSource Cancellation { get; } = new();
    public IProgress<TransferProgress>? Progress { get; set; }

    public void InitializeAccepted(IEnumerable<string> ids) => _remaining = ids.Count();

    public bool TryConsumeToken(string itemId, string token)
    {
        lock (_tokenGate)
        {
            if (!Tokens.TryGetValue(itemId, out var expected) || !StringComparer.Ordinal.Equals(expected, token))
                return false;
            Tokens.Remove(itemId);
            return true;
        }
    }

    public Dictionary<string, string> TokenSnapshot()
    {
        lock (_tokenGate) return new(Tokens, StringComparer.Ordinal);
    }

    public void FileCompleted(string itemId, string fileName, long bytes, string path)
    {
        lock (_gate)
        {
            _results.Add(new(itemId, fileName, bytes, path));
            if (Interlocked.Decrement(ref _remaining) == 0)
                Completion.TrySetResult(new(TransferId, TransferDirection.Receive, TransferState.Completed, _results.ToArray()));
        }
    }

    public void Fail(string code, string message, string? itemId = null)
    {
        Cancellation.Cancel();
        Completion.TrySetResult(new(TransferId, TransferDirection.Receive, TransferState.Failed, _results.ToArray(), new(code, message, itemId)));
    }

    public void Cancel() => Completion.TrySetResult(new(TransferId, TransferDirection.Receive, TransferState.Cancelled, _results.ToArray()));
}
