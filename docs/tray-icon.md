# Tray icon and context menu (Reactor / WinUI 3)

Closing the main window to the notification area works. The hard part is the
**right-click menu**. That gap is a long-standing Windows App SDK / WinUI 3
limitation (Microsoft has said they are improving tray flyouts). It is not a
Reactor-only bug.

Reactor's `UseTrayIcon` + `ShowFlyout(MenuItems(...))` is **not** a complete
menu host:

- `MenuItems(...)` builds a `MenuFlyoutContentElement`. The reconciler only
  accepts that as a flyout *slot* (`.WithContextFlyout`). It is not
  independently mountable, so a tray `ShowFlyout` throws
  `No handler is registered`.
- Even a custom visual tree in `ShowFlyout` is hosted in a borderless window
  that does not size to content, so the popup becomes a tall empty plate.

Do not invent an extra 1×1 XAML window just to call `MenuFlyout.ShowAt`. Two
approaches below are enough.

## 1. Win32 `TrackPopupMenu` (stable default)

On tray right-click, build an `HMENU` and show it with `TrackPopupMenuEx`
(`TPM_RETURNCMD | TPM_BOTTOMALIGN`). The main window can stay hidden; no
`XamlRoot` is required.

- Look: classic shell context menu.
- Code we validated: `src/Tonarink.App/TrayContextMenu.cs`
  (commit `2d548c2`).
- Use this when you want zero extra packages and maximum reliability.

## 2. WinUIEx `TrayIcon` + `MenuFlyout` (Fluent, current)

[WinUIEx TrayIcon](https://dotmorten.github.io/WinUIEx/concepts/TrayIcon.html)
owns the tray icon and shows whatever you assign to
`TrayIconEventArgs.Flyout`. Assign a `MenuFlyout` there — WinUIEx supplies the
host, so the menu is a real Win11 flyout (acrylic, correct height, follows
light/dark).

```csharp
icon.ContextMenu += (_, args) =>
{
    var flyout = new MenuFlyout();
    flyout.Items.Add(openItem);
    flyout.Items.Add(new MenuFlyoutSeparator());
    flyout.Items.Add(exitItem);
    args.Flyout = flyout;
};
```

- Package: `WinUIEx` (currently 2.9.3).
- Wiring: `LocalizedAppShell` in `AppShell.cs`. Keep a `UseRef<TrayIcon?>` and
  `Dispose()` on unmount / Exit, or the icon stays after the process should
  have left.
- Because this tray is not a Reactor surface, set
  `ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit` while the icon is
  visible, then call `ReactorApp.Exit()` from the menu.

## Shared app rules

These are independent of which menu you pick:

- Intercept `WindowCloseReason.UserClosed` and `Hide()` when "minimize to tray"
  is on. Restore with `Show()` + `Activate()`, and hide from the taskbar while
  in the tray.
- Incoming transfers / share-target activations should restore the window.
- Persist the setting; login-start can pass `--minimized` so the first paint
  goes straight to the tray.

Either option is production-viable. This app currently uses option 2 for the
Fluent menu. Option 1 remains the documented rollback if WinUIEx or a future
Windows tray API is a better fit.
