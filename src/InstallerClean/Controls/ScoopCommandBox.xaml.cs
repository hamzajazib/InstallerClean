using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace InstallerClean.Controls;

/// <summary>
/// Scoop's update command in a box, Copy beside it, and the line saying the copy failed.
/// The update card and the notice at the top of the window each hold one, with the
/// <see cref="ViewModels.ChromeViewModel"/> as the DataContext.
/// </summary>
internal sealed partial class ScoopCommandBox : UserControl
{
    /// <summary>Whether the box sits on the update card, which centres it and spaces it as the card does.</summary>
    public static readonly DependencyProperty OnCardProperty = DependencyProperty.Register(
        nameof(OnCard), typeof(bool), typeof(ScoopCommandBox), new PropertyMetadata(false));

    /// <summary>The Copy button's style: the card's pill or the notice's compact pill.</summary>
    public static readonly DependencyProperty CopyButtonStyleProperty = DependencyProperty.Register(
        nameof(CopyButtonStyle), typeof(Style), typeof(ScoopCommandBox), new PropertyMetadata(null));

    /// <summary>
    /// What the box's parts are named in the UI Automation tree, each with its own ending:
    /// ScoopCommand, CopyButton and CopyFailure. Two boxes would otherwise share three ids.
    /// </summary>
    public static readonly DependencyProperty AutomationIdPrefixProperty = DependencyProperty.Register(
        nameof(AutomationIdPrefix), typeof(string), typeof(ScoopCommandBox),
        new PropertyMetadata(string.Empty, (d, e) => ((ScoopCommandBox)d).NameParts((string)e.NewValue)));

    public ScoopCommandBox() => InitializeComponent();

    public bool OnCard
    {
        get => (bool)GetValue(OnCardProperty);
        set => SetValue(OnCardProperty, value);
    }

    public Style? CopyButtonStyle
    {
        get => (Style?)GetValue(CopyButtonStyleProperty);
        set => SetValue(CopyButtonStyleProperty, value);
    }

    public string AutomationIdPrefix
    {
        get => (string)GetValue(AutomationIdPrefixProperty);
        set => SetValue(AutomationIdPrefixProperty, value);
    }

    /// <summary>Puts the keyboard focus on Copy, and answers false where the button is not shown.</summary>
    public bool FocusCopy() => CopyButton.Focus();

    /// <summary>The line saying the copy failed, for the window to read out.</summary>
    public FrameworkElement CopyFailureLine => CopyFailure;

    // The box adds no element of its own for a screen reader to stop on: its parts show in
    // the control view as if they sat directly in the window.
    protected override AutomationPeer OnCreateAutomationPeer() => new RawAutomationPeer(this);

    private void NameParts(string prefix)
    {
        AutomationProperties.SetAutomationId(CommandBox, prefix + "ScoopCommand");
        AutomationProperties.SetAutomationId(CopyButton, prefix + "CopyButton");
        AutomationProperties.SetAutomationId(CopyFailure, prefix + "CopyFailure");
    }
}

/// <summary>
/// A text that takes up room and is never read: it reports itself to UI Automation as
/// neither a control nor content. The Copy button lays one of these over its visible text
/// for each word the button can read, so the button is as wide as the wider of them.
/// </summary>
internal sealed class UnreadText : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RawAutomationPeer(this);
}
