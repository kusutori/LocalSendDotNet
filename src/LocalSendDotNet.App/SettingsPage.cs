using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static LocalSendDotNet.App.Controls.Toolkit.SettingsCardElement;

sealed record SettingsPageProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class SettingsPage : Component<SettingsPageProps>
{
    private static readonly string[] ThemeOptions = ["跟随系统", "浅色", "深色"];
    private static readonly string[] LanguageOptions = ["跟随系统", "简体中文", "English"];

    public override Element Render()
    {
        var generalCards = SettingsGroup(
            "通用",
            SettingsCard(
                header: "主题",
                description: "选择应用的明暗外观。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(ThemeOptions, Props.Settings.ThemeIndex, index =>
                    Props.UpdateSettings(settings => settings with { ThemeIndex = index }))
                    .MinWidth(180)),
            SettingsCard(
                header: "语言",
                description: "更改界面显示语言。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(LanguageOptions, Props.Settings.LanguageIndex, index =>
                    Props.UpdateSettings(settings => settings with { LanguageIndex = index }))
                    .MinWidth(180)),
            SettingsCard(
                header: "关闭时最小化到系统托盘",
                description: "关闭主窗口时继续在后台接收。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.MinimizeToTray, value =>
                    Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))),
            SettingsCard(
                header: "登录系统后自动启动",
                description: "登录 Windows 后自动运行 LocalSend。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                    Props.UpdateSettings(settings => settings with { StartWithWindows = value }))),
            SettingsCard(
                header: "动画效果",
                description: "启用页面切换和状态过渡动画。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.AnimationsEnabled, value =>
                    Props.UpdateSettings(settings => settings with { AnimationsEnabled = value }))));

        var receiveCards = SettingsGroup(
            "接收",
            SettingsCard(
                header: "设备名称",
                description: "其他设备将看到这个名称；更改后需要重启应用。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                TextBox(Props.Settings.Alias, value =>
                    Props.UpdateSettings(settings => settings with { Alias = value }))
                    .Header("设备名称")
                    .AutomationName("设备名称")
                    .MinWidth(240)),
            SettingsCard(
                header: "自动保存",
                description: "自动接受并保存收到的内容。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.AutoSave == AutoSaveMode.On, value =>
                    Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = value ? AutoSaveMode.On : AutoSaveMode.Off,
                    }))),
            SettingsCard(
                header: "仅自动接收收藏设备",
                description: "自动保存开启时，只信任收藏列表中的设备。",
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.FavoritesOnly, value =>
                    Props.UpdateSettings(settings => settings with { FavoritesOnly = value }))),
            SettingsCard(
                header: "保存位置",
                description: Props.Settings.DownloadDirectory,
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                Button("更改…", () => { })
                    .AutomationName("更改接收文件保存位置")));

        return ScrollView(
            VStack(24,
                Heading("设置")
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                generalCards,
                receiveCards)
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);
    }

    private static Element SettingsGroup(string title, params Element[] cards) =>
        VStack(4,
        [
            Subtitle(title)
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            .. cards.Select(card => card.HAlign(HorizontalAlignment.Stretch)),
        ]);
}
