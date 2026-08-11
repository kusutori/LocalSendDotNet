using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

sealed class AppShell : Component
{
    public override Element Render()
    {
        var navigation = UseNavigation(AppRoute.Receive);
        var (settings, updateSettings) = UseReducer(AppSettings.Default);
        var (runtime, updateRuntime) = UseReducer(AppRuntimeState.Initial);
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
            Subtitle = "安全、快速的局域网传输",
            RightHeader = Caption(NodeStatusText(runtime.NodeState))
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
                RefreshAsync)),
            AppRoute.Settings => Component<SettingsPage, SettingsPageProps>(new(
                settings,
                updateSettings)),
            _ => TextBlock("找不到此页面。"),
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
                NavItem("接收", icon: "Download", tag: RouteTag(AppRoute.Receive)),
                NavItem("发送", icon: "Send", tag: RouteTag(AppRoute.Send)),
                NavItem("设置", icon: "Setting", tag: RouteTag(AppRoute.Settings)),
            ],
            content)
            .WithNavigation(navigation, RouteTag, ParseRoute)
            .PaneHeader(
                VStack(4,
                    Title("LocalSend"),
                    Caption("LocalSendDotNet").Foreground(Theme.SecondaryText))
                .Padding(left: 8, top: 24, right: 8, bottom: 20))
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

        var root = FlexColumn(titleBar, navigationView)
            .RequestedTheme(settings.ThemeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            })
            .Backdrop(BackdropKind.Mica);

        return root;

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

    private static string NodeStatusText(LocalSendNodeState state) => state switch
    {
        LocalSendNodeState.Starting => "正在启动…",
        LocalSendNodeState.Running => "已连接到本地网络",
        LocalSendNodeState.Faulted => "网络服务不可用",
        LocalSendNodeState.Stopping => "正在停止…",
        _ => "未连接",
    };
}
