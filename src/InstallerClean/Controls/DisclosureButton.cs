using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace InstallerClean.Controls;

/// <summary>
/// A button that opens and closes a panel, <see cref="ToggleButton.IsChecked"/> being
/// whether the panel is open. A click, Enter or Space toggles it, as it does any
/// ToggleButton. A screen reader is told it is a button and whether it is expanded or
/// collapsed, through the ExpandCollapse pattern, where a ToggleButton's own peer reports
/// a toggle that is on or off, which reads as a setting.
/// </summary>
internal sealed class DisclosureButton : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DisclosureButtonAutomationPeer(this);

    // Raised for every change of IsChecked, the click and a change made through its
    // binding alike, so the state a screen reader holds follows the panel.
    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        RaiseStateChanged(ExpandCollapseState.Collapsed, ExpandCollapseState.Expanded);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        RaiseStateChanged(ExpandCollapseState.Expanded, ExpandCollapseState.Collapsed);
    }

    private void RaiseStateChanged(ExpandCollapseState from, ExpandCollapseState to) =>
        (UIElementAutomationPeer.FromElement(this) as DisclosureButtonAutomationPeer)?
            .RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, from, to);
}

/// <summary>
/// The peer of a <see cref="DisclosureButton"/>: a Button that expands and collapses.
/// Expand and Collapse set IsChecked the way a click does, keeping its binding.
/// </summary>
internal sealed class DisclosureButtonAutomationPeer(DisclosureButton owner)
    : ButtonBaseAutomationPeer(owner), IExpandCollapseProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetClassNameCore() => "Button";

    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState =>
        owner.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => SetOpen(true);

    public void Collapse() => SetOpen(false);

    private void SetOpen(bool open)
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        owner.SetCurrentValue(ToggleButton.IsCheckedProperty, open);
    }
}
