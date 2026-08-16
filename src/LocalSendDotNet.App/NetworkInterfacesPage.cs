using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

sealed class NetworkInterfacesPage : Component
{
    public override Element Render()
    {
        var t = UseIntl();

        return Border(
                FlexColumn(
                    Heading(t.Message(new("App", "SettingsNetworkInterfaces")))
                        .HeadingLevel(AutomationHeadingLevel.Level1),
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
