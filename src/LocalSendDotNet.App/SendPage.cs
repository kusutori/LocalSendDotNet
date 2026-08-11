using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

sealed record SendPageProps(AppRuntimeState Runtime, Func<Task> RefreshAsync);

sealed class SendPage : Component<SendPageProps>
{
    public override Element Render()
    {
        var (selection, setSelection) = UseState("尚未选择内容");
        var (text, setText) = UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = UseState(false);

        var selectionGrid = Grid(
            columns:
            [
                GridSize.Star(),
                GridSize.Star(),
                GridSize.Star(),
                GridSize.Star(),
            ],
            rows: [GridSize.Auto],
            SelectionTile("文件", "Document", () => setSelection("文件选择器将在下一步接入"))
                .Grid(column: 0),
            SelectionTile("文件夹", "Folder", () => setSelection("文件夹选择器将在下一步接入"))
                .Grid(column: 1),
            SelectionTile("文本", "Edit", () => setShowTextDialog(true))
                .Grid(column: 2),
            SelectionTile("剪贴板", "Paste", () => setSelection("已选择剪贴板内容"))
                .Grid(column: 3)) with
        {
            ColumnSpacing = 12,
        };

        var devices = Props.Runtime.Devices;
        Element deviceContent = devices.Count == 0
            ? EmptyDevices(Props.Runtime.NodeState)
            : VStack(8,
                devices.Select((device, index) =>
                    DeviceCard(device)
                        .PositionInSet(index + 1, devices.Count)
                        .WithKey(device.Fingerprint))
                .ToArray<Element?>());

        var page = FlexColumn(
            Heading("发送")
                .HeadingLevel(AutomationHeadingLevel.Level1),
            VStack(12,
                Subtitle("选择内容")
                    .HeadingLevel(AutomationHeadingLevel.Level2),
                selectionGrid,
                Caption(selection).Foreground(Theme.SecondaryText)),
            FlexRow(
                Subtitle("附近的设备")
                    .HeadingLevel(AutomationHeadingLevel.Level2)
                    .Flex(grow: 1, basis: 0),
                Button(Icon("Refresh"), () => _ = Props.RefreshAsync())
                    .AutomationName("刷新附近设备")
                    .ToolTip("刷新附近设备")) with
            {
                AlignItems = FlexAlign.Center,
                ColumnGap = 8,
            },
            ScrollView(deviceContent)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .Flex(grow: 1, basis: 0),
            ContentDialog(
                "发送文本",
                TextBox(text, setText, placeholderText: "输入要发送的文本…")
                    .Header("文本内容")
                    .AutomationName("文本内容")
                    .AcceptsReturn()
                    .TextWrapping(TextWrapping.Wrap)
                    .MinHeight(160),
                primaryButtonText: "添加") with
            {
                IsOpen = showTextDialog,
                SecondaryButtonText = "取消",
                OnClosed = result =>
                {
                    if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(text))
                        setSelection($"文本 · {text.Length} 个字符");
                    setShowTextDialog(false);
                },
            });

        return Border(page)
            .Padding(36)
            .Landmark(AutomationLandmarkType.Main);
    }

    private static Element SelectionTile(string label, string icon, Action onClick) =>
        Button(
            VStack(8,
                Icon(icon).AccessibilityHidden(),
                BodyStrong(label)),
            onClick)
        .MinHeight(104)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName($"选择{label}");

    private static Element DeviceCard(LocalSendDevice device) =>
        Button(
            Grid(
                columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Border(Icon(DeviceIcon(device.DeviceType)).AccessibilityHidden())
                    .Size(56, 56)
                    .CornerRadius(28)
                    .Background(Theme.SubtleFill)
                    .Grid(column: 0),
                VStack(4,
                    BodyLarge(device.Alias),
                    Caption(DeviceDescription(device)).Foreground(Theme.SecondaryText))
                    .Margin(left: 16, top: 0, right: 16, bottom: 0)
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 1),
                Icon("Forward").AccessibilityHidden()
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 2)),
            onClick: null)
        .MinHeight(88)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName($"向 {device.Alias} 发送");

    private static Element EmptyDevices(LocalSendNodeState state) =>
        FlexColumn(
            Icon(state == LocalSendNodeState.Faulted ? "Important" : "Find").AccessibilityHidden(),
            Subtitle(state == LocalSendNodeState.Faulted ? "无法启动网络服务" : "正在寻找附近设备"),
            TextBlock(state == LocalSendNodeState.Faulted
                    ? "请检查 53317 端口是否被其他 LocalSend 实例占用。"
                    : "请确保目标设备连接到同一个 Wi-Fi 网络。")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        };

    private static string DeviceIcon(LocalSendDeviceType type) => type switch
    {
        LocalSendDeviceType.Mobile => "Phone",
        LocalSendDeviceType.Web => "World",
        LocalSendDeviceType.Server => "World",
        _ => "Remote",
    };

    private static string DeviceDescription(LocalSendDevice device)
    {
        var model = string.IsNullOrWhiteSpace(device.DeviceModel) ? "未知设备" : device.DeviceModel;
        return $"{model}  ·  v{device.ProtocolVersion}";
    }
}
