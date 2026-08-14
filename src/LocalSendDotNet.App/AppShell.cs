using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using static Microsoft.UI.Reactor.Factories;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System.UserProfile;
using Windows.Storage;

sealed class AppShell : Component
{
    private static readonly ReswResourceProvider Resources = new(defaultLocale: "en-US");

    public override Element Render()
    {
        var (settings, updateSettings) = UseReducer(AppSettings.Default);
        var window = UseWindow();

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
        return LocaleProvider(
            locale,
            Component<LocalizedAppShell, LocalizedAppShellProps>(new(settings, updateSettings, locale)),
            Resources,
            defaultLocale: "en-US")
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

        UseEffect(() =>
        {
            EventHandler activationReceived = (_, _) => ScheduleActivationDrain();
            ShareTargetActivationBroker.ActivationReceived += activationReceived;
            ScheduleActivationDrain();
            return () => ShareTargetActivationBroker.ActivationReceived -= activationReceived;
        });

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
                _ = DrainActivationsAsync();
            else
                dispatcher.TryEnqueue(() => _ = DrainActivationsAsync());
        }

        async Task DrainActivationsAsync()
        {
            if (drainingActivationsRef.Current)
                return;

            drainingActivationsRef.Current = true;
            try
            {
                while (ShareTargetActivationBroker.TryDequeue(out var activation))
                {
                    window?.Activate();
                    if (activation?.Kind != ExtendedActivationKind.ShareTarget
                        || activation.Data is not ShareTargetActivatedEventArgs shareArgs)
                    {
                        continue;
                    }

                    await ReceiveSharedContentAsync(shareArgs);
                }
            }
            finally
            {
                drainingActivationsRef.Current = false;
                if (ShareTargetActivationBroker.HasPendingActivations)
                    ScheduleActivationDrain();
            }
        }

        async Task ReceiveSharedContentAsync(ShareTargetActivatedEventArgs shareArgs)
        {
            var operation = shareArgs.ShareOperation;
            operation.ReportStarted();
            try
            {
                var payload = await ReadShareTargetPayloadAsync(operation.Data);
                operation.ReportDataRetrieved();

                setShareTargetPayload(payload);
                if (navigation.CurrentRoute != AppRoute.Send)
                    navigation.Navigate(AppRoute.Send);
                window?.Activate();
                operation.ReportCompleted();
            }
            catch (Exception exception)
            {
                try
                {
                    operation.ReportError(t.Message(
                        new("App", "ShareTargetFailed"),
                        ("error", exception.Message)));
                }
                catch
                {
                }
                updateRuntime(current => current with { Error = exception.Message });
            }
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

    private static async Task<ShareTargetPayload> ReadShareTargetPayloadAsync(DataPackageView data)
    {
        var items = new List<ShareTargetItem>();
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await data.GetStorageItemsAsync();
            foreach (var storageItem in storageItems)
            {
                if (string.IsNullOrWhiteSpace(storageItem.Path))
                    continue;

                items.Add(new ShareTargetItem.FileSystem(
                    storageItem.Path,
                    storageItem is StorageFolder));
            }
        }
        else if (data.Contains(StandardDataFormats.WebLink))
        {
            var link = await data.GetWebLinkAsync();
            items.Add(new ShareTargetItem.Text(link.ToString(), "shared-link.txt"));
        }
        else if (data.Contains(StandardDataFormats.Text))
        {
            var text = await data.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(new ShareTargetItem.Text(text, "shared-text.txt"));
        }

        if (items.Count == 0)
            throw new InvalidDataException("The share did not contain accessible files or text.");

        return new ShareTargetPayload(Guid.NewGuid(), items);
    }
}
