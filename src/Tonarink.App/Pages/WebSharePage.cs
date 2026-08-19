using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

sealed record WebSharePageProps(
    LocalSendNode? Node,
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<bool?> SetHttpsOverride);

sealed class WebSharePage : Component<WebSharePageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var items = WebShareLaunch.Items;
        var (share, setShare) = UseState(WebShareState.Inactive);
        var (autoAccept, setAutoAccept) = UseState(false);
        var (pin, setPin) = UseState<string?>(null);
        var (pinDraft, setPinDraft) = UseState(RandomPin());
        var (pinDialogOpen, setPinDialogOpen) = UseState(false);
        var (encrypted, setEncrypted) = UseState(
            Props.Runtime.Identity?.Protocol == LocalSendProtocol.Https);
        var (qrPath, setQrPath) = UseState<string?>(null);
        var (qrUrl, setQrUrl) = UseState<string?>(null);
        var (zoomUrl, setZoomUrl) = UseState<string?>(null);
        var node = Props.Node;

        UseNavigationLifecycle(onNavigatedFrom: _ =>
        {
            node?.StopWebShare();
            Props.SetHttpsOverride(null);
        });

        UseEffect(() =>
        {
            if (node is null || Props.Runtime.NodeState != LocalSendNodeState.Running || items.Count == 0)
                return () => { };

            _ = node.StartWebShareAsync(items, new WebShareOptions { AutoAccept = autoAccept, Pin = pin });
            var watch = new CancellationTokenSource();
            _ = WatchAsync(watch.Token);
            return () =>
            {
                watch.Cancel();
                watch.Dispose();
                node.StopWebShare();
            };

            async Task WatchAsync(CancellationToken cancellationToken)
            {
                try
                {
                    setShare(node.GetWebShare());
                    await foreach (var next in node.WatchWebShareAsync(cancellationToken).ConfigureAwait(true))
                        setShare(next);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }, node, Props.Runtime.NodeState);

        var https = encrypted;
        var port = Props.Runtime.Identity?.Port ?? Props.Settings.Port;
        var urls = AppNetworkAddresses.ListIpv4(Props.Settings)
            .Select(address => $"{(https ? "https" : "http")}://{address}:{port}")
            .ToArray();
        if (urls.Length == 0)
            urls = [$"{(https ? "https" : "http")}://127.0.0.1:{port}"];

        Element requestBody = share.Requests.Count == 0
            ? TextBlock(t.Message(new("App", "WebShareNoRequests")))
                .Foreground(Theme.SecondaryText)
            : VStack(8, share.Requests.Select(request =>
                RequestCard(t, request, node).WithKey(request.SessionId)).ToArray<Element?>());

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", "WebShareTitle")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                TextBlock(t.Message(new("App", "WebShareOpenLink")))
                    .SemiBold(),
                VStack(8, urls.Select(url =>
                    LinkBar(t, url, ShowQr, setZoomUrl).WithKey(url)).ToArray<Element?>()),
                VStack(8,
                    BodyStrong(t.Message(new("App", "WebShareRequests"))),
                    requestBody),
                CheckBox(
                    (bool?)encrypted,
                    value =>
                    {
                        setEncrypted(value);
                        Props.SetHttpsOverride(value);
                    },
                    t.Message(new("App", "WebShareEncryption"))),
                encrypted
                    ? TextBlock(t.Message(new("App", "WebShareEncryptionHint")))
                        .Foreground(Theme.SystemCaution)
                        .TextWrapping(TextWrapping.WrapWholeWords)
                    : null,
                CheckBox(
                    (bool?)autoAccept,
                    value =>
                    {
                        setAutoAccept(value);
                        node?.SetWebShareAutoAccept(value);
                    },
                    t.Message(new("App", "WebShareAutoAccept"))),
                CheckBox(
                    (bool?)(pin is not null),
                    value =>
                    {
                        if (value)
                        {
                            setPinDraft(RandomPin());
                            setPinDialogOpen(true);
                        }
                        else
                        {
                            setPin(null);
                            node?.SetWebSharePin(null);
                        }
                    },
                    t.Message(new("App", "WebShareRequirePin"))),
                pin is null
                    ? null
                    : TextBlock(t.Message(new("App", "WebSharePinHint"), ("pin", pin)))
                        .Foreground(Theme.SystemCaution),
                ContentDialog(
                    t.Message(new("App", "WebSharePinTitle")),
                    TextBox(pinDraft, setPinDraft)
                        .AutomationName(t.Message(new("App", "WebSharePinTitle"))),
                    primaryButtonText: t.Message(new("App", "Confirm"))) with
                {
                    IsOpen = pinDialogOpen,
                    SecondaryButtonText = t.Message(new("App", "Cancel")),
                    DefaultButton = ContentDialogButton.Primary,
                    OnClosed = result =>
                    {
                        setPinDialogOpen(false);
                        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(pinDraft))
                            return;
                        var next = pinDraft.Trim();
                        setPin(next);
                        node?.SetWebSharePin(next);
                    },
                },
                ContentDialog(
                    t.Message(new("App", "WebShareQrTitle")),
                    qrPath is null
                        ? ProgressRing()
                        : VStack(12,
                            Image(qrPath)
                                .Size(240, 240)
                                .HAlign(HorizontalAlignment.Center)
                                .AutomationName(t.Message(new("App", "WebShareQrTitle"))),
                            TextBlock(qrUrl ?? "")
                                .TextWrapping(TextWrapping.WrapWholeWords)
                                .IsTextSelectionEnabled(true)),
                    primaryButtonText: t.Message(new("App", "Close"))) with
                {
                    IsOpen = qrUrl is not null,
                    OnClosed = _ =>
                    {
                        setQrUrl(null);
                        setQrPath(null);
                    },
                },
                ContentDialog(
                    t.Message(new("App", "WebShareZoomTitle")),
                    Title(zoomUrl ?? "")
                        .TextWrapping(TextWrapping.WrapWholeWords)
                        .IsTextSelectionEnabled()
                        .AutomationName(t.Message(new("App", "WebShareZoomTitle"))),
                    primaryButtonText: t.Message(new("App", "Close"))) with
                {
                    IsOpen = zoomUrl is not null,
                    OnClosed = _ => setZoomUrl(null),
                })
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        void ShowQr(string url)
        {
            setQrUrl(url);
            setQrPath(null);
            _ = WriteQrAsync(url);
        }

        async Task WriteQrAsync(string url)
        {
            try
            {
                var path = await QrPng.WriteAsync(url).ConfigureAwait(true);
                setQrPath(path);
            }
            catch
            {
            }
        }
    }

    private static Element LinkBar(IntlAccessor t, string url, Action<string> showQr, Action<string?> setZoom) =>
        Border(
            Grid(
                columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto, GridSize.Auto],
                rows: [GridSize.Auto],
                TextBlock(url)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .IsTextSelectionEnabled(true)
                    .VAlign(VerticalAlignment.Center)
                    .ToolTip(url)
                    .Grid(column: 0),
                IconButton("\uE8C8", t.Message(new("App", "WebShareCopy")), () => Copy(url))
                    .Grid(column: 1),
                IconButton("\uED14", t.Message(new("App", "WebShareQr")), () => showQr(url))
                    .Grid(column: 2),
                IconButton("\uE7F4", t.Message(new("App", "WebShareZoom")), () => setZoom(url))
                    .Grid(column: 3)) with
            {
                ColumnSpacing = 4,
            })
            .Padding(horizontal: 16, vertical: 8)
            .CornerRadius(8)
            .Background(Theme.SubtleFill);

    private static Element RequestCard(IntlAccessor t, WebShareRequest request, LocalSendNode? node) =>
        Border(
            Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                VStack(4,
                    TextBlock(request.DeviceInfo)
                        .Foreground(request.Pending ? Theme.SystemCaution : Theme.PrimaryText),
                    Caption(request.Ip).Foreground(Theme.SecondaryText))
                    .Grid(column: 0),
                (request.Pending
                    ? (Element)HStack(4,
                        Button(Icon("Cancel"), () => node?.DeclineWebShareRequest(request.SessionId))
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "Decline")))
                            .MinWidth(40)
                            .MinHeight(40),
                        Button(Icon("Accept"), () => node?.AcceptWebShareRequest(request.SessionId))
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "Accept")))
                            .MinWidth(40)
                            .MinHeight(40))
                    : Caption(t.Message(new("App", "WebShareAccepted")))
                        .Foreground(Theme.SecondaryText)
                        .VAlign(VerticalAlignment.Center))
                    .Grid(column: 1)))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1);

    private static Element IconButton(string glyph, string name, Action onClick) =>
        Button(Icon(glyph), onClick)
            .SubtleButton()
            .AutomationName(name)
            .ToolTip(name)
            .MinWidth(40)
            .MinHeight(40);

    private static void Copy(string url)
    {
        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static string RandomPin()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        Span<char> chars = stackalloc char[6];
        Random.Shared.GetItems(alphabet.AsSpan(), chars);
        return new string(chars);
    }
}
