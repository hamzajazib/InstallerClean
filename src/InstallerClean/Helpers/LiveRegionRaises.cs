using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace InstallerClean.Helpers;

/// <summary>
/// Tells a screen reader to read a line marked with
/// <see cref="System.Windows.Automation.AutomationProperties.LiveSettingProperty"/>, by
/// raising <see cref="AutomationEvents.LiveRegionChanged"/> on its automation peer. WPF
/// raises that event for no element on its own, whether the line's text changes or the line
/// is revealed, so a line marked live is read only where a window queues a raise here.
///
/// THE RAISES ARE QUEUED AT BACKGROUND PRIORITY, AND THE PRIORITY IS THE CONTRACT.
/// Dispatcher priorities are serviced highest value first, and Loaded (6) outranks Input
/// (5), so a raise queued at Loaded lands BEFORE a focus move queued at Input, and the
/// focus announcement then cancels the queued polite speech (NVDA documents
/// cancel-on-focus; Narrator behaves the same in practice). Background (4) sits below Input, so a
/// raise follows every focus move queued in the same drain and a polite line is read once
/// the focus announcement finishes. Background still runs after data binding and render.
///
/// A screen reader reads the line's name when the event reaches it, and the peer answers
/// that from the element as it stands, so the line read is the one showing when the raise
/// runs, not when it was queued.
/// </summary>
internal sealed class LiveRegionRaises(Dispatcher dispatcher)
{
    /// <summary>
    /// The raises queued and not yet run, each posted to the dispatcher at Background priority
    /// (<see cref="PendingRaises{TElement}"/>).
    /// </summary>
    private readonly PendingRaises<UIElement> _pending =
        new(run => dispatcher.BeginInvoke(DispatcherPriority.Background, run), Raise);

    /// <summary>
    /// Queues a raise for <paramref name="element"/>. Where one is already queued for it and
    /// has not run, this adds no second raise, the one raise reading the line as it then
    /// stands. <paramref name="stillShown"/>, where given, joins the tests that raise is made
    /// under, which are asked when it runs, and one answering false drops it: a line whose card
    /// has gone by then is not read.
    /// </summary>
    internal void Queue(UIElement element, Func<bool>? stillShown = null) =>
        _pending.Queue(element, stillShown);

    /// <summary>Raises the event for <paramref name="element"/> now.</summary>
    internal static void Raise(UIElement element)
    {
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
