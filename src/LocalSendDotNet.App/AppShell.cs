using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Windows.System.UserProfile;

sealed class AppShell : Component
{
    private static readonly ReswResourceProvider Resources = new(defaultLocale: "en-US");

    public override Element Render()
    {
        var (settings, updateSettings) = UseReducer(AppSettingsStore.Load());
        var window = UseWindow();

        UseEffect(() => AppSettingsStore.Save(settings), settings);
        UseEffect(() =>
        {
            if (settings.StartWithWindows)
                WindowsStartup.UpdateLaunchCommand(settings.MinimizeToTray);
        }, settings.StartWithWindows, settings.MinimizeToTray);
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = SyncStartupAsync(cts.Token);
            return () => cts.Cancel();

            async Task SyncStartupAsync(CancellationToken cancellationToken)
            {
                try
                {
                    var enabled = await WindowsStartup.IsEnabledAsync().ConfigureAwait(true);
                    if (cancellationToken.IsCancellationRequested || enabled == settings.StartWithWindows)
                        return;
                    updateSettings(current => current with { StartWithWindows = enabled });
                }
                catch
                {
                }
            }
        });

        UseEffect(() =>
        {
            if (window is not null)
            {
                window.AppWindow.TitleBar.PreferredTheme = settings.ThemeIndex switch
                {
                    1 => TitleBarTheme.Light,
                    2 => TitleBarTheme.Dark,
                    _ => TitleBarTheme.UseDefaultAppMode,
                };
            }
        }, settings.ThemeIndex);

        var locale = settings.LanguageIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => SystemLocale(),
        };
        var theme = settings.ThemeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        var startHidden = AppPlatform.StartHidden && settings.MinimizeToTray;
        var (splashVisible, _) = UseState(!startHidden && settings.AnimationsEnabled);

        var shell = LocaleProvider(
            locale,
            Component<LocalizedAppShell, LocalizedAppShellProps>(new(settings, updateSettings, locale)),
            Resources,
            defaultLocale: "en-US")
            .RequestedTheme(theme);

        if (!splashVisible)
            return shell.Backdrop(BackdropKind.Mica);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                shell.Grid(row: 0, column: 0),
                Component<StartupSplashOverlay>().Grid(row: 0, column: 0))
            .RequestedTheme(theme)
            .Backdrop(BackdropKind.Mica);
    }

    private static string SystemLocale() => GlobalizationPreferences.Languages.Any(
        static language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ? "zh-CN"
            : "en-US";
}

sealed record LocalizedAppShellProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    string Locale);

sealed class LocalizedAppShell : Component<LocalizedAppShellProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var settings = Props.Settings;
        var updateSettings = Props.UpdateSettings;
        var navigation = UseNavigation(AppRoute.Receive);
        var (runtime, updateRuntime) = UseReducer(AppRuntimeState.Initial);
        var (outgoingTransfer, setOutgoingTransfer) = UseState<OutgoingTransferViewState?>(null);
        var (shareTargetPayload, setShareTargetPayload) = UseState<ShareTargetPayload?>(null);
        var nodeRef = UseRef<LocalSendNode?>(null);
        var drainingActivationsRef = UseRef(false);
        var trayIcon = UseRef<WinUIEx.TrayIcon?>(null);

        UseEffect(() =>
        {
            EventHandler activationReceived = (_, _) => ScheduleActivationDrain();
            ShareTargetActivationBroker.ActivationReceived += activationReceived;
            ScheduleActivationDrain();
            return () => ShareTargetActivationBroker.ActivationReceived -= activationReceived;
        });

        UseEffect(() =>
        {
            if (window is null)
                return () => { };

            void OnClosing(object? sender, WindowClosingEventArgs args)
            {
                if (args.Reason != WindowCloseReason.UserClosed || !settings.MinimizeToTray)
                    return;

                args.Cancel = true;
                HideToTray();
            }

            window.Closing += OnClosing;
            return () => window.Closing -= OnClosing;
        }, window, settings.MinimizeToTray);

        UseEffect(() =>
        {
            ReactorApp.ShutdownPolicy = settings.MinimizeToTray
                ? ShutdownPolicy.Explicit
                : ShutdownPolicy.OnLastSurfaceClosed;

            if (!settings.MinimizeToTray)
            {
                trayIcon.Current?.Dispose();
                trayIcon.Current = null;
                return () => { };
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath))
                iconPath = AppPlatform.ExecutablePath;

            var icon = new WinUIEx.TrayIcon(1, iconPath, t.Message(new("App", "TrayTooltip")));
            icon.Selected += (_, _) => RestoreWindow();
            icon.LeftDoubleClick += (_, _) => RestoreWindow();
            icon.ContextMenu += (_, args) =>
            {
                var flyout = new MenuFlyout();
                var open = new MenuFlyoutItem { Text = t.Message(new("App", "TrayOpen")) };
                open.Click += (_, _) => RestoreWindow();
                var exit = new MenuFlyoutItem { Text = t.Message(new("App", "TrayExit")) };
                exit.Click += (_, _) =>
                {
                    icon.Dispose();
                    trayIcon.Current = null;
                    ReactorApp.Exit();
                };
                flyout.Items.Add(open);
                flyout.Items.Add(new MenuFlyoutSeparator());
                flyout.Items.Add(exit);
                args.Flyout = flyout;
            };
            icon.IsVisible = true;
            trayIcon.Current = icon;

            return () =>
            {
                icon.Dispose();
                if (ReferenceEquals(trayIcon.Current, icon))
                    trayIcon.Current = null;
            };
        }, settings.MinimizeToTray, t.Locale);

        UseEffect(() =>
        {
            if (runtime.IncomingTransfers.Count > 0)
                RestoreWindow();
        }, runtime.IncomingTransfers.Count);

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
        })
        .Tall()
        .Icon(ImageIcon(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), UriKind.Absolute)))
        .Flex(shrink: 0);

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
                setOutgoingTransfer,
                shareTargetPayload,
                ConsumeShareTargetPayload)),
            AppRoute.Settings => Component<SettingsPage, SettingsPageProps>(new(
                settings,
                updateSettings))
                .WithKey($"settings:{Props.Locale}"),
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
            .PaneDisplayMode(NavigationViewPaneDisplayMode.Auto)
            .CompactModeThresholdWidth(640)
            .ExpandedModeThresholdWidth(1008)
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

        var root = FlexColumn(titleBar, contentLayer);

        return root;

        void RestoreWindow()
        {
            if (window is null)
                return;

            if (!window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = true });
            window.Show();
            window.Activate();
        }

        void HideToTray()
        {
            if (window is null)
                return;

            window.Hide();
            if (window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = false });
        }

        void DismissIncoming(Guid requestId)
        {
            updateRuntime(current => current with
            {
                IncomingTransfers = current.IncomingTransfers
                    .Where(request => request.RequestId != requestId)
                    .ToArray(),
            });
        }

        void ConsumeShareTargetPayload(Guid payloadId)
        {
            if (shareTargetPayload?.Id == payloadId)
                setShareTargetPayload(null);
        }

        void ScheduleActivationDrain()
        {
            var dispatcher = ReactorApp.UIDispatcher;
            if (dispatcher is null)
                return;

            if (dispatcher.HasThreadAccess)
                DrainActivations();
            else
                dispatcher.TryEnqueue(DrainActivations);
        }

        void DrainActivations()
        {
            if (drainingActivationsRef.Current)
                return;

            drainingActivationsRef.Current = true;
            try
            {
                RestoreWindow();
                while (ShareTargetActivationBroker.TryDequeue(out var payload))
                {
                    if (payload is null)
                        continue;

                    setShareTargetPayload(payload);
                    if (navigation.CurrentRoute != AppRoute.Send)
                        navigation.Navigate(AppRoute.Send);
                    RestoreWindow();
                }
            }
            finally
            {
                drainingActivationsRef.Current = false;
                if (ShareTargetActivationBroker.HasPendingActivations)
                    ScheduleActivationDrain();
            }
        }

        async Task RunNodeAsync(CancellationToken cancellationToken)
        {
            var dataDirectory = AppPlatform.DataDirectory;
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
