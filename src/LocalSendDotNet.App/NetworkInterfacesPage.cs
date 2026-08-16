using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

sealed class NetworkInterfacesPage : Component
{
    public override Element Render()
    {
        var t = UseIntl();
        var navigation = UseNavigation<AppRoute>();

        return Border(
                FlexColumn(
                    FlexRow(
                            navigation.CanGoBack
                                ? Button(Icon(FontIcon("\uE72B")), () => navigation.GoBack())
                                    .SubtleButton()
                                    .AutomationName(t.Message(new("App", "HistoryBack")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                : null,
                            Heading(t.Message(new("App", "SettingsNetworkInterfaces")))
                                .HeadingLevel(AutomationHeadingLevel.Level1)
                                .Flex(grow: 1, basis: 0))
                        with { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                    TextBlock(t.Message(new("App", "SettingsNetworkInterfacesPlaceholder")))
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.WrapWholeWords)) with
                {
                    RowGap = 20,
                })
            .Padding(36)
            .Landmark(AutomationLandmarkType.Main);
    }
}
