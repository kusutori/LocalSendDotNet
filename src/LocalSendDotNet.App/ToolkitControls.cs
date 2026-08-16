using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Wrappers;

namespace LocalSendDotNet.App.Controls.Toolkit;

[GenerateReactorWrapper(typeof(SettingsCard))]
public partial record SettingsCardElement;

[GenerateReactorWrapper(typeof(SettingsExpander))]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsExpanderElement;

[GenerateReactorWrapper(typeof(Segmented))]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;
