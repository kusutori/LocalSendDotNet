using LocalSendDotNet;

enum AppRoute
{
    Receive,
    History,
    Send,
    Settings,
}

enum AutoSaveMode
{
    Off,
    Favorites,
    On,
}

sealed record AppSettings(
    string Alias,
    AutoSaveMode AutoSave,
    int ThemeIndex,
    int LanguageIndex,
    bool MinimizeToTray,
    bool StartWithWindows,
    bool AnimationsEnabled,
    bool FavoritesOnly,
    string DownloadDirectory)
{
    public static readonly AppSettings Default = new(
        Alias: string.IsNullOrWhiteSpace(Environment.UserName) ? Environment.MachineName : Environment.UserName,
        AutoSave: AutoSaveMode.Off,
        ThemeIndex: 0,
        LanguageIndex: 0,
        MinimizeToTray: false,
        StartWithWindows: false,
        AnimationsEnabled: true,
        FavoritesOnly: false,
        DownloadDirectory: AppPlatform.DefaultDownloadDirectory);

    public string ResolvedAlias =>
        string.IsNullOrWhiteSpace(Alias) ? Default.Alias : Alias.Trim();
}

sealed record AppRuntimeState(
    LocalSendNodeState NodeState,
    LocalSendIdentity? Identity,
    IReadOnlyList<LocalSendDevice> Devices,
    IReadOnlyList<IncomingTransferRequest> IncomingTransfers,
    string? Error)
{
    public static readonly AppRuntimeState Initial = new(
        LocalSendNodeState.Created,
        Identity: null,
        Devices: Array.Empty<LocalSendDevice>(),
        IncomingTransfers: Array.Empty<IncomingTransferRequest>(),
        Error: null);
}

sealed record OutgoingTransferViewState(
    LocalSendIdentity? Sender,
    LocalSendDevice Receiver,
    string ContentSummary,
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    bool IsPending,
    bool IsError,
    Action Cancel);

sealed record ShareTargetPayload(
    Guid Id,
    IReadOnlyList<ShareTargetItem> Items);

abstract record ShareTargetItem
{
    public sealed record FileSystem(string Path, bool IsDirectory) : ShareTargetItem;

    public sealed record Text(string Value, string FileName) : ShareTargetItem;
}

sealed record ReceiveHistoryEntry(
    Guid Id,
    string FileName,
    string Path,
    long Size,
    string SenderAlias,
    DateTimeOffset ReceivedAt);
