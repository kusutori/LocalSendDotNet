using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static LocalSendDotNet.App.Controls.Toolkit.SegmentedElement;

sealed record ReceivePageProps(
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class ReceivePage : Component<ReceivePageProps>
{
    public override Element Render()
    {
        var autoSaveItems = UseMemo(() => new object[]
        {
            new SegmentedItem { Content = "关闭" },
            new SegmentedItem { Content = "收藏设备" },
            new SegmentedItem { Content = "开启" },
        }, []);
        var identity = Props.Runtime.Identity;
        var alias = identity?.Alias ?? Props.Settings.Alias;
        var fingerprint = identity?.Fingerprint;
        var shortId = fingerprint is null
            ? "正在载入身份…"
            : $"#{Convert.ToInt32(fingerprint[..4], 16) % 1000:D3}  #1";

        var identityPanel = FlexColumn(
            Border(Icon(FontIcon("\uE701", fontSize: 48)).AccessibilityHidden())
                .Size(112, 112)
                .CornerRadius(56)
                .Background(Theme.SubtleFill)
                .HAlign(HorizontalAlignment.Center),
            Title(alias).HAlign(HorizontalAlignment.Center),
            BodyLarge(shortId)
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center),
            fingerprint is null
                ? null
                : Caption($"Fingerprint  {fingerprint[..12]}…")
                    .Foreground(Theme.TertiaryText)
                    .HAlign(HorizontalAlignment.Center)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
        };

        var autoSave = Card(
            FlexColumn(
                FlexRow(
                    VStack(4,
                        Subtitle("自动保存"),
                        TextBlock("选择收到内容时的处理方式。")
                            .Foreground(Theme.SecondaryText))
                        .Flex(grow: 1, basis: 0),
                    Props.Runtime.IncomingTransfers.Count > 0
                        ? InfoBadge(Props.Runtime.IncomingTransfers.Count)
                            .AutomationName($"{Props.Runtime.IncomingTransfers.Count} 个待处理请求")
                        : null) with
                {
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                },
                Segmented(
                    selectedIndex: (int)Props.Settings.AutoSave,
                    onSelectedIndexChanged: index => Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = (AutoSaveMode)index,
                    }),
                    items: autoSaveItems)
                    .HAlign(HorizontalAlignment.Stretch)) with
            { RowGap = 20 })
            .MaxWidth(560)
            .HAlign(HorizontalAlignment.Stretch);

        var page = ScrollView(
            FlexColumn(
                Heading("接收")
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                identityPanel.Flex(grow: 1, basis: 0),
                autoSave) with
            {
                RowGap = 32,
                AlignItems = FlexAlign.Stretch,
            })
            .Padding(36)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        return page;
    }
}
