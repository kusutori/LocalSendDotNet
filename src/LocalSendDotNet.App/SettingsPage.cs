using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

sealed record SettingsPageProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class SettingsPage : Component<SettingsPageProps>
{
    private static readonly string[] ThemeOptions = ["跟随系统", "浅色", "深色"];
    private static readonly string[] LanguageOptions = ["跟随系统", "简体中文", "English"];

    public override Element Render()
    {
        var generalCard = SettingsCard(
            "通用",
            SettingRow(
                "主题",
                "选择应用的明暗外观。",
                ComboBox(ThemeOptions, Props.Settings.ThemeIndex, index =>
                    Props.UpdateSettings(settings => settings with { ThemeIndex = index }))
                    .MinWidth(180)),
            SettingRow(
                "语言",
                "更改界面显示语言。",
                ComboBox(LanguageOptions, Props.Settings.LanguageIndex, index =>
                    Props.UpdateSettings(settings => settings with { LanguageIndex = index }))
                    .MinWidth(180)),
            SettingRow(
                "关闭时最小化到系统托盘",
                "关闭主窗口时继续在后台接收。",
                ToggleSwitch(Props.Settings.MinimizeToTray, value =>
                    Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))),
            SettingRow(
                "登录系统后自动启动",
                "登录 Windows 后自动运行 LocalSend。",
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                    Props.UpdateSettings(settings => settings with { StartWithWindows = value }))),
            SettingRow(
                "动画效果",
                "启用页面切换和状态过渡动画。",
                ToggleSwitch(Props.Settings.AnimationsEnabled, value =>
                    Props.UpdateSettings(settings => settings with { AnimationsEnabled = value }))));

        var receiveCard = SettingsCard(
            "接收",
            SettingRow(
                "设备名称",
                "其他设备将看到这个名称；更改后需要重启应用。",
                TextBox(Props.Settings.Alias, value =>
                    Props.UpdateSettings(settings => settings with { Alias = value }))
                    .Header("设备名称")
                    .AutomationName("设备名称")
                    .MinWidth(240)),
            SettingRow(
                "自动保存",
                "自动接受并保存收到的内容。",
                ToggleSwitch(Props.Settings.AutoSave == AutoSaveMode.On, value =>
                    Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = value ? AutoSaveMode.On : AutoSaveMode.Off,
                    }))),
            SettingRow(
                "仅自动接收收藏设备",
                "自动保存开启时，只信任收藏列表中的设备。",
                ToggleSwitch(Props.Settings.FavoritesOnly, value =>
                    Props.UpdateSettings(settings => settings with { FavoritesOnly = value }))),
            SettingRow(
                "保存位置",
                Props.Settings.DownloadDirectory,
                Button("更改…", () => { })
                    .AutomationName("更改接收文件保存位置")));

        return ScrollView(
            VStack(24,
                Heading("设置")
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                generalCard,
                receiveCard)
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);
    }

    private static Element SettingsCard(string title, params Element[] rows) =>
        Card(
            VStack(4,
            [
                Subtitle(title)
                    .HeadingLevel(AutomationHeadingLevel.Level2)
                    .Margin(bottom: 12),
                .. rows,
            ]));

    private static Element SettingRow(string title, string description, Element control) =>
        Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(4,
                BodyStrong(title),
                TextBlock(description)
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(Microsoft.UI.Xaml.TextWrapping.WrapWholeWords))
                .Margin(right: 24)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 0),
            control
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 1))
        .Padding(12);
}
