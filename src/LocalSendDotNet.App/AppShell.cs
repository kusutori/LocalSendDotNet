using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Windows.System.UserProfile;

sealed class AppShell : Component
{
    private static readonly ReswResourceProvider Resources = new(defaultLocale: "en-US");

    public override Element Render()
    {
        var (settings, updateSettings) = UseReducer(AppSettings.Default);

        var locale = settings.LanguageIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => SystemLocale(),
        };

        return LocaleProvider(
            locale,
            Component<LocalizedAppShell, LocalizedAppShellProps>(new(settings, updateSettings)),
            Resources,
            defaultLocale: "en-US");
    }

    private static string SystemLocale() => GlobalizationPreferences.Languages.Any(
        static language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ? "zh-CN"
            : "en-US";
}

sealed record LocalizedAppShellProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class LocalizedAppShell : Component<LocalizedAppShellProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var settings = Props.Settings;
        var updateSettings = Props.UpdateSettings;
        var navigation = UseNavigation(AppRoute.Receive);
        var (runtime, updateRuntime) = UseReducer(AppRuntimeState.Initial);
        var (outgoingTransfer, setOutgoingTransfer) = UseState<OutgoingTransferViewState?>(null);
        var nodeRef = UseRef<LocalSendNode?>(null);

        UseEffect(() =>
        {
            var cancellation = new CancellationTokenSource();
            _ = RunNodeAsync(cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                _ = DisposeNodeAsync(cancellation);
            };
        });

        var titleBar = (TitleBar("LocalSend") with
        {
            Subtitle = t.Message(new("App", "Tagline")),
            RightHeader = Caption(NodeStatusText(t, runtime.NodeState))
                .Foreground(runtime.Error is null ? Theme.SecondaryText : Theme.SystemCritical),
        }).Flex(shrink: 0);

        var content = NavigationHost(navigation, route => route switch
        {
            AppRoute.Receive => Component<ReceivePage, ReceivePageProps>(new(
                runtime,
                settings,
                updateSettings)),
            AppRoute.Send => Component<SendPage, SendPageProps>(new(
                runtime,
                nodeRef.Current,
                RefreshAsync,
                setOutgoingTransfer)),
            AppRoute.Settings => Component<SettingsPage, SettingsPageProps>(new(
                settings,
                updateSettings)),
            _ => TextBlock(t.Message(new("App", "PageNotFound"))),
        }) with
        {
            CacheMode = NavigationCacheMode.Enabled,
            CacheSize = 3,
            Transition = settings.AnimationsEnabled
                ? NavigationTransition.Fade(TimeSpan.FromMilliseconds(160))
                : NavigationTransition.None,
        };

        var navigationView = (NavigationView(
            [
                NavItem(t.Message(new("App", "NavReceive")), icon: "\uE701", tag: RouteTag(AppRoute.Receive)),
                NavItem(t.Message(new("App", "NavSend")), icon: "Send", tag: RouteTag(AppRoute.Send)),
                NavItem(t.Message(new("App", "NavSettings")), icon: "Setting", tag: RouteTag(AppRoute.Settings)),
            ],
            content)
            .WithNavigation(navigation, RouteTag, ParseRoute)
            .PaneDisplayMode(NavigationViewPaneDisplayMode.Left)
            .OpenPaneLength(248)
            .CompactPaneLength(56)
            .PaneToggleButtonVisible(true)
            .AlwaysShowHeader(false)
            .BackButtonVisible(false)
            .TitleBarAutoPadding(false)
            .Flex(grow: 1, basis: 0)) with
        {
            IsSettingsVisible = false,
        };

        var pendingIncoming = runtime.IncomingTransfers.FirstOrDefault();
        Element? transferOverlay = pendingIncoming is not null && nodeRef.Current is { } node
            ? Component<IncomingTransferOverlay, IncomingTransferOverlayProps>(new(
                    node,
                    pendingIncoming,
                    settings.DownloadDirectory,
                    DismissIncoming))
                .WithKey(pendingIncoming.RequestId.ToString("N"))
            : outgoingTransfer is not null
                ? Component<OutgoingTransferOverlay, OutgoingTransferOverlayProps>(new(
                    outgoingTransfer,
                    () => setOutgoingTransfer(null)))
                : null;

        var contentLayer = Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                navigationView.Grid(row: 0, column: 0),
                transferOverlay?.Grid(row: 0, column: 0))
            .Flex(grow: 1, basis: 0);

        var root = FlexColumn(titleBar, contentLayer)
            .RequestedTheme(settings.ThemeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            })
            .Backdrop(BackdropKind.Mica);

        return root;

        void DismissIncoming(Guid requestId)
        {
            updateRuntime(current => current with
            {
                IncomingTransfers = current.IncomingTransfers
                    .Where(request => request.RequestId != requestId)
                    .ToArray(),
            });
        }

        async Task RunNodeAsync(CancellationToken cancellationToken)
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LocalSendDotNet");
            var node = new LocalSendNode(new LocalSendOptions
            {
                Alias = settings.Alias,
                DeviceModel = Environment.MachineName,
                DeviceType = LocalSendDeviceType.Desktop,
                DataDirectory = dataDirectory,
                DownloadDirectory = settings.DownloadDirectory,
            });
            nodeRef.Current = node;
            updateRuntime(current => current with
            {
                NodeState = LocalSendNodeState.Starting,
                Error = null,
            });

            try
            {
                await node.StartAsync(cancellationToken).ConfigureAwait(false);
                updateRuntime(current => current with
                {
                    NodeState = node.State,
                    Identity = node.Identity,
                    Devices = node.GetDevices(),
                    Error = null,
                });

                await Task.WhenAll(
                    WatchDevicesAsync(node, cancellationToken),
                    WatchIncomingTransfersAsync(node, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with
                {
                    NodeState = node.State,
                    Error = exception.Message,
                });
            }
        }

        async Task WatchDevicesAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var _ in node.WatchDeviceChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                });
            }
        }

        async Task WatchIncomingTransfersAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken).ConfigureAwait(false))
            {
                updateRuntime(current => current with
                {
                    IncomingTransfers = [.. current.IncomingTransfers, request],
                });
            }
        }

        async Task RefreshAsync()
        {
            var node = nodeRef.Current;
            if (node?.State != LocalSendNodeState.Running)
                return;

            try
            {
                await node.RefreshAsync().ConfigureAwait(false);
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                    Error = null,
                });
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with { Error = exception.Message });
            }
        }

        async Task DisposeNodeAsync(CancellationTokenSource cancellation)
        {
            try
            {
                if (nodeRef.Current is { } node)
                    await node.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    private static string RouteTag(AppRoute route) => route switch
    {
        AppRoute.Receive => "receive",
        AppRoute.Send => "send",
        AppRoute.Settings => "settings",
        _ => "receive",
    };

    private static AppRoute ParseRoute(string tag) => tag switch
    {
        "send" => AppRoute.Send,
        "settings" => AppRoute.Settings,
        _ => AppRoute.Receive,
    };

    private static string NodeStatusText(IntlAccessor t, LocalSendNodeState state) => state switch
    {
        LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
        LocalSendNodeState.Running => t.Message(new("App", "NodeRunning")),
        LocalSendNodeState.Faulted => t.Message(new("App", "NodeFaulted")),
        LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
        _ => t.Message(new("App", "NodeDisconnected")),
    };
}
