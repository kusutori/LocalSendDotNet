using Microsoft.UI.Reactor;

if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync())
    return;

ReactorApp.Run<AppShell>("LocalSend", width: 1120, height: 760);
