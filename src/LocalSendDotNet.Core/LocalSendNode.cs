using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using LocalSendDotNet.Protocol;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalSendDotNet;

/// <summary>A UI-independent LocalSend v2.2 peer.</summary>
public sealed class LocalSendNode : IAsyncDisposable
{
    private readonly LocalSendOptions _options;
    private readonly ILogger _logger;
    private readonly ILocalSendProtocolAdapter _protocol = new V2ProtocolAdapter();
    private readonly BroadcastHub<DeviceChange> _deviceChanges = new(128);
    private readonly BroadcastHub<IncomingTransferRequest> _incomingTransfers = new(64);
    private readonly ConcurrentDictionary<Guid, IncomingSession> _pending = new();
    private readonly ConcurrentDictionary<string, IncomingSession> _incomingSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (IPAddress Address, CancellationTokenSource Cancellation)> _outgoingSessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _transferSlots;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DeviceStore _devices;
    private DeviceIdentity? _identity;
    private V2HttpClient? _client;
    private V2Server? _server;
    private V2MulticastDiscovery? _discovery;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    public LocalSendNode(LocalSendOptions options, ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LocalSendNode>();
        _devices = new DeviceStore(_deviceChanges);
        _transferSlots = new SemaphoreSlim(options.MaxConcurrentTransfers, options.MaxConcurrentTransfers);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;
            if (_stopped)
                throw new InvalidOperationException("A stopped LocalSendNode cannot be restarted; create a new node instance.");
            Directory.CreateDirectory(_options.DownloadDirectory);
            _identity = await DeviceIdentityStore.LoadOrCreateAsync(_options.DataDirectory, cancellationToken).ConfigureAwait(false);
            _client = new V2HttpClient(_identity, _options);
            _server = new V2Server(_options, _identity, () => CreateLocalInfo(), OnRegisterAsync, OnPrepareAsync, OnUploadAsync, OnCancelAsync, _logger);
            await _server.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _discovery = new V2MulticastDiscovery(_options, () => CreateLocalInfo(announce: true), OnAnnouncementAsync, _logger);
                await _discovery.StartAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
                await _discovery.AnnounceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _server.DisposeAsync().ConfigureAwait(false);
                _server = null;
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started && _server is null)
                return;
            await _lifetime.CancelAsync().ConfigureAwait(false);
            foreach (var session in _pending.Values)
                session.Decision.TrySetResult(new(false, null));
            foreach (var session in _incomingSessions.Values)
            {
                await session.Cancellation.CancelAsync().ConfigureAwait(false);
                session.Cancel();
            }
            foreach (var outgoing in _outgoingSessions.Values)
                await outgoing.Cancellation.CancelAsync().ConfigureAwait(false);
            if (_discovery is not null)
                await _discovery.DisposeAsync().ConfigureAwait(false);
            if (_server is not null)
                await _server.DisposeAsync().ConfigureAwait(false);
            _discovery = null;
            _server = null;
            _started = false;
            _stopped = true;
            _deviceChanges.Complete();
            _incomingTransfers.Complete();
        }
        finally { _lifecycle.Release(); }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        return _discovery!.AnnounceAsync(cancellationToken);
    }

    public IReadOnlyList<LocalSendDevice> GetDevices() => _devices.Snapshot();

    public IAsyncEnumerable<DeviceChange> WatchDeviceChangesAsync(CancellationToken cancellationToken = default) => _deviceChanges.Subscribe(cancellationToken);

    public IAsyncEnumerable<IncomingTransferRequest> WatchIncomingTransfersAsync(CancellationToken cancellationToken = default) => _incomingTransfers.Subscribe(cancellationToken);

    public async Task<TransferResult> SendAsync(
        LocalSendDevice device,
        IReadOnlyCollection<SendItem> items,
        SendOptions? options = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(items));
        var endpoint = device.Endpoints.OrderByDescending(static x => x.Protocol == LocalSendProtocol.Https).FirstOrDefault()
            ?? throw new LocalSendException("The device has no usable v2 endpoint.");
        var transferId = Guid.NewGuid();
        var itemMap = items.Select(item => (Id: Guid.NewGuid().ToString("N"), Item: item)).ToArray();
        var totalBytes = itemMap.Sum(static x => x.Item.Length);
        progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.Preparing, 0, totalBytes));

        var dto = new PrepareUploadRequestDto
        {
            Info = CreateLocalInfo(),
            Files = itemMap.ToDictionary(static x => x.Id, static x => ToFileDto(x.Id, x.Item), StringComparer.Ordinal)
        };
        PrepareUploadResponseDto prepared;
        try
        {
            progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.WaitingForAcceptance, 0, totalBytes));
            prepared = await _client!.PrepareUploadAsync(endpoint, device.Fingerprint, dto, options?.Pin, cancellationToken).ConfigureAwait(false);
        }
        catch (PinRequiredException) { throw; }
        catch (PinRateLimitedException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(transferId, TransferDirection.Send, TransferState.Failed, [], new("prepare_failed", exception.Message));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _outgoingSessions[prepared.SessionId] = (endpoint.Address, linked);
        var results = new List<TransferredItemResult>();
        long completedBytes = 0;
        try
        {
            foreach (var (id, item) in itemMap)
            {
                if (!prepared.Files.TryGetValue(id, out var token))
                    continue;
                await using var source = await item.OpenReadAsync(linked.Token).ConfigureAwait(false);
                await using var tracked = new ProgressReadStream(source, current => progress?.Report(new(
                    transferId, id, TransferDirection.Send, TransferState.Transferring, completedBytes + current, totalBytes)));
                await _client.UploadAsync(endpoint, device.Fingerprint, prepared.SessionId, id, token, tracked, item.Length, item.ContentType, linked.Token).ConfigureAwait(false);
                completedBytes += item.Length;
                results.Add(new(id, item.FileName, item.Length, null));
            }
            progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.Completed, completedBytes, totalBytes));
            return new(transferId, TransferDirection.Send, TransferState.Completed, results);
        }
        catch (OperationCanceledException)
        {
            try { await _client.CancelAsync(endpoint, device.Fingerprint, prepared.SessionId, CancellationToken.None).ConfigureAwait(false); } catch { }
            return new(transferId, TransferDirection.Send, TransferState.Cancelled, results);
        }
        catch (Exception exception)
        {
            try { await _client.CancelAsync(endpoint, device.Fingerprint, prepared.SessionId, CancellationToken.None).ConfigureAwait(false); } catch { }
            return new(transferId, TransferDirection.Send, TransferState.Failed, results, new("upload_failed", exception.Message));
        }
        finally
        {
            _outgoingSessions.TryRemove(prepared.SessionId, out _);
        }
    }

    public async Task<TransferResult> AcceptAsync(Guid requestId, AcceptTransferOptions? options = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        if (!_pending.TryGetValue(requestId, out var session))
            throw new LocalSendException("The incoming request no longer exists.");
        session.Progress = progress;
        session.Decision.TrySetResult(new(true, options ?? new AcceptTransferOptions()));
        try
        {
            return await session.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await session.Cancellation.CancelAsync().ConfigureAwait(false);
            session.Cancel();
            var endpoint = session.PublicRequest.Sender.Endpoints.FirstOrDefault();
            if (endpoint is not null)
            {
                try
                {
                    await _client!.CancelAsync(endpoint, session.PublicRequest.Sender.Fingerprint, session.SessionId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Could not notify {Peer} that receive session {SessionId} was cancelled", endpoint, session.SessionId);
                }
            }
            return new(session.TransferId, TransferDirection.Receive, TransferState.Cancelled, []);
        }
    }

    public Task DeclineAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pending.TryGetValue(requestId, out var session))
            throw new LocalSendException("The incoming request no longer exists.");
        session.Decision.TrySetResult(new(false, null));
        return Task.CompletedTask;
    }

    private DeviceInfoDto CreateLocalInfo(bool announce = false) => _protocol.CreateDeviceInfo(_identity ?? throw new InvalidOperationException("Identity is not loaded."), _options, announce);

    private async Task OnAnnouncementAsync(DeviceInfoDto announcement, IPAddress source, CancellationToken cancellationToken)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(announcement.Fingerprint, _identity!.Fingerprint) || announcement.Port is < 1 or > 65535)
            return;
        var endpoint = new DeviceEndpoint(source, announcement.Port, StringComparer.OrdinalIgnoreCase.Equals(announcement.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        var response = await _client!.RegisterAsync(endpoint, announcement.Fingerprint, CreateLocalInfo(), cancellationToken).ConfigureAwait(false);
        var candidate = new LocalSendDevice(response.Alias, response.Version, response.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(response.DeviceType), announcement.Fingerprint, response.Download, [endpoint], DateTimeOffset.UtcNow);
        _devices.Upsert(candidate);
    }

    private Task OnRegisterAsync(DeviceInfoDto info, IPAddress source, string? certificateFingerprint)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(info.Fingerprint, _identity!.Fingerprint) || info.Port is < 1 or > 65535)
            return Task.CompletedTask;
        var fingerprint = certificateFingerprint ?? info.Fingerprint;
        var endpoint = new DeviceEndpoint(source, info.Port, StringComparer.OrdinalIgnoreCase.Equals(info.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        _devices.Upsert(new(info.Alias, info.Version, info.DeviceModel, V2ProtocolAdapter.ParseDeviceType(info.DeviceType), fingerprint, info.Download, [endpoint], DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    private async Task<PrepareOutcome> OnPrepareAsync(PrepareUploadRequestDto request, IPAddress remote, string? certificateFingerprint, CancellationToken requestCancellation)
    {
        await _transferSlots.WaitAsync(requestCancellation).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");
        var transferId = Guid.NewGuid();
        var endpoint = new DeviceEndpoint(remote, request.Info.Port, StringComparer.OrdinalIgnoreCase.Equals(request.Info.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        var sender = new LocalSendDevice(request.Info.Alias, request.Info.Version, request.Info.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(request.Info.DeviceType), certificateFingerprint ?? request.Info.Fingerprint, request.Info.Download, [endpoint], DateTimeOffset.UtcNow);
        var items = request.Files.Values.Select(ToIncomingItem).ToArray();
        var publicRequest = new IncomingTransferRequest(requestId, sessionId, sender, items, DateTimeOffset.UtcNow);
        var session = new IncomingSession
        {
            RequestId = requestId,
            TransferId = transferId,
            SessionId = sessionId,
            RemoteAddress = remote,
            Request = request,
            PublicRequest = publicRequest,
            Decision = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        _pending[requestId] = session;
        await _incomingTransfers.PublishAsync(publicRequest, requestCancellation).ConfigureAwait(false);

        IncomingDecision decision;
        using var timeout = new CancellationTokenSource(_options.IncomingDecisionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, timeout.Token, _lifetime.Token);
        try { decision = await session.Decision.Task.WaitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(requestId, out _);
            _transferSlots.Release();
            return new(HttpStatusCode.RequestTimeout, Message: "Incoming transfer decision timed out");
        }
        _pending.TryRemove(requestId, out _);
        if (!decision.Accepted)
        {
            _transferSlots.Release();
            session.Completion.TrySetResult(new(transferId, TransferDirection.Receive, TransferState.Cancelled, []));
            return new(HttpStatusCode.Forbidden, Message: "Transfer declined");
        }

        var selected = decision.Options!.AcceptedItemIds is null
            ? request.Files.Keys.ToHashSet(StringComparer.Ordinal)
            : decision.Options.AcceptedItemIds.Where(request.Files.ContainsKey).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            _transferSlots.Release();
            session.Completion.TrySetResult(new(transferId, TransferDirection.Receive, TransferState.Completed, []));
            return new(HttpStatusCode.NoContent);
        }

        try
        {
            var destinationRoot = decision.Options.DestinationDirectory ?? _options.DownloadDirectory;
            var reserved = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var id in selected)
            {
                var file = request.Files[id];
                var targetName = decision.Options.TargetFileNames?.GetValueOrDefault(id) ?? file.FileName;
                session.Destinations[id] = SafeFileTarget.ResolveUnique(destinationRoot, targetName, reserved);
                session.Tokens[id] = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception exception)
        {
            _transferSlots.Release();
            session.Fail("invalid_destination", exception.Message);
            return new(HttpStatusCode.BadRequest, Message: exception.Message);
        }
        session.InitializeAccepted(selected);
        _incomingSessions[sessionId] = session;
        _ = ReleaseIncomingSlotWhenDoneAsync(session);
        return new(HttpStatusCode.OK, new PrepareUploadResponseDto { SessionId = sessionId, Files = session.TokenSnapshot() });
    }

    private async Task<HttpStatusCode> OnUploadAsync(string sessionId, string fileId, string token, IPAddress remote, Stream body, long? contentLength, CancellationToken requestCancellation)
    {
        if (!_incomingSessions.TryGetValue(sessionId, out var session) || !session.RemoteAddress.Equals(remote))
            return HttpStatusCode.NotFound;
        if (!session.TryConsumeToken(fileId, token) || !session.Request.Files.TryGetValue(fileId, out var file))
            return HttpStatusCode.Forbidden;
        if (contentLength is not null && contentLength != file.Size)
            return HttpStatusCode.BadRequest;

        var destination = session.Destinations[fileId];
        var temporary = destination + $".part-{Guid.NewGuid():N}";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, session.Cancellation.Token, _lifetime.Token);
        long written = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 512 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[512 * 1024];
                while (true)
                {
                    var read = await body.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    written += read;
                    if (written > file.Size)
                        throw new LocalSendException("Uploaded content exceeded the declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                    session.Progress?.Report(new(session.TransferId, fileId, TransferDirection.Receive, TransferState.Transferring, written, file.Size));
                }
                await output.FlushAsync(linked.Token).ConfigureAwait(false);
            }
            if (written != file.Size)
                throw new LocalSendException($"Uploaded content length mismatch: expected {file.Size}, received {written}.");
            File.Move(temporary, destination);
            RestoreTimestamps(destination, file.Metadata);
            session.FileCompleted(fileId, file.FileName, written, destination);
            return HttpStatusCode.OK;
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            session.Cancel();
            return HttpStatusCode.BadRequest;
        }
        catch (Exception exception)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            session.Fail("receive_failed", exception.Message, fileId);
            return HttpStatusCode.BadRequest;
        }
    }

    private async Task<bool> OnCancelAsync(string sessionId, IPAddress remote, CancellationToken cancellationToken)
    {
        if (_incomingSessions.TryGetValue(sessionId, out var incoming) && incoming.RemoteAddress.Equals(remote))
        {
            await incoming.Cancellation.CancelAsync().ConfigureAwait(false);
            incoming.Cancel();
            return true;
        }
        if (_outgoingSessions.TryGetValue(sessionId, out var outgoing) && outgoing.Address.Equals(remote))
        {
            await outgoing.Cancellation.CancelAsync().ConfigureAwait(false);
            return true;
        }
        return false;
    }

    private async Task ReleaseIncomingSlotWhenDoneAsync(IncomingSession session)
    {
        await session.Completion.Task.ConfigureAwait(false);
        _incomingSessions.TryRemove(session.SessionId, out _);
        session.Cancellation.Dispose();
        _transferSlots.Release();
    }

    private static FileDto ToFileDto(string id, SendItem item)
    {
        FileMetadataDto? metadata = null;
        if (item is SendFileItem file)
        {
            var info = new FileInfo(file.Path);
            metadata = new() { Modified = info.LastWriteTimeUtc.ToString("O"), Accessed = info.LastAccessTimeUtc.ToString("O") };
        }
        return new() { Id = id, FileName = item.FileName, Size = item.Length, FileType = item.ContentType, Metadata = metadata };
    }

    private static IncomingItem ToIncomingItem(FileDto file) => new(file.Id, file.FileName, file.Size, file.FileType, file.Sha256, file.Preview,
        ParseTimestamp(file.Metadata?.Modified), ParseTimestamp(file.Metadata?.Accessed));

    private static DateTimeOffset? ParseTimestamp(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static void RestoreTimestamps(string path, FileMetadataDto? metadata)
    {
        if (metadata is null) return;
        if (ParseTimestamp(metadata.Modified) is { } modified) File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        if (ParseTimestamp(metadata.Accessed) is { } accessed) File.SetLastAccessTimeUtc(path, accessed.UtcDateTime);
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("The LocalSend node has not been started.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _identity?.Dispose();
        _lifetime.Dispose();
        _transferSlots.Dispose();
        _lifecycle.Dispose();
    }
}
