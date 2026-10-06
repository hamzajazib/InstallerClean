using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InstallerClean.Helpers;

/// <summary>
/// Reports whether the focus ring is actually on an element, so a style can
/// stand a second cue down while the ring is drawn and keep it while the ring
/// is not.
///
/// <para>
/// <c>IsKeyboardFocused</c> is the wrong question and the difference is not
/// academic. WPF draws the FocusVisualStyle adorner from
/// <c>FrameworkElement.OnGotKeyboardFocus</c>, which calls
/// <c>KeyboardNavigation.ShowFocusVisual()</c>, and that gates on
/// <c>AlwaysShowFocusVisual || IsKeyboardMostRecentInputDevice()</c>, where
/// <c>AlwaysShowFocusVisual</c> is seeded from
/// <see cref="SystemParameters.KeyboardCues"/> (off unless the user has asked
/// Windows to underline access keys always) and
/// <c>IsKeyboardMostRecentInputDevice</c> asks the input manager which device
/// was used last (dotnet/wpf, PresentationFramework,
/// <c>System/Windows/Input/KeyboardNavigation.cs</c>). A mouse click takes
/// keyboard focus and draws no ring. A control that dropped its own edge on
/// <c>IsKeyboardFocused</c> would therefore show neither ring nor edge in the
/// commonest case there is, which is somebody clicking the thing.
/// </para>
///
/// <para>
/// The same expression is evaluated here, on the same element, at the same
/// moment: focus arrival. The third term is the element's own
/// <see cref="FrameworkElement.FocusVisualStyle"/>, because a null one adorns
/// nothing however the focus arrived, which is the state
/// <c>SuppressFocusVisualOnDeactivation</c> leaves behind while another
/// application is in front.
/// </para>
///
/// <para>
/// It also keeps the ring in view inside a scrolling region:
/// <see cref="KeepsInViewProperty"/>.
/// </para>
/// </summary>
public static class FocusRing
{
    private static readonly DependencyPropertyKey IsShowingKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsShowing", typeof(bool), typeof(FocusRing),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsShowingProperty = IsShowingKey.DependencyProperty;

    public static bool GetIsShowing(DependencyObject element) =>
        (bool)element.GetValue(IsShowingProperty);

    /// <summary>
    /// How far a scrolling region keeps a focused element from its edges, in
    /// device-independent pixels.
    ///
    /// <para>
    /// Inside a ScrollViewer the ring is drawn in the adorner layer of its
    /// ScrollContentPresenter, which clips everything it holds to the viewport, and
    /// the ring round a focused element is drawn 2 outside it. So a ring round an
    /// element at an edge of the region is cut on that side unless there is room
    /// between the element and the edge. The room is twice that 2, so a ring snapped
    /// outward to whole device pixels at a fractional display scale still clears the
    /// edge.
    /// </para>
    ///
    /// <para>
    /// The scrolling regions holding a link or a control at an edge keep the same room
    /// in their XAML, as margins of 4 in MainWindow.xaml and RegisteredFilesWindow.xaml.
    /// A change here is a change to those as well.
    /// </para>
    /// </summary>
    public const double Room = 4;

    /// <summary>
    /// Set on a ScrollViewer, by the BodyScrollRegion style on every one the app draws.
    /// Where focus moves to an element inside it, the region scrolls far enough to show
    /// the element with <see cref="Room"/> to spare on every side rather than just the
    /// element, so the ring round it is in view as well. At an end of the content, the
    /// room has to be in the content itself, as its margin.
    /// </summary>
    public static readonly DependencyProperty KeepsInViewProperty =
        DependencyProperty.RegisterAttached(
            "KeepsInView", typeof(bool), typeof(FocusRing),
            new PropertyMetadata(false));

    public static bool GetKeepsInView(DependencyObject element) =>
        (bool)element.GetValue(KeepsInViewProperty);

    public static void SetKeepsInView(DependencyObject element, bool value) =>
        element.SetValue(KeepsInViewProperty, value);

    /// <summary>
    /// Starts tracking, once, before any window is shown. Class handlers rather
    /// than per-control subscriptions: <see cref="IsShowingProperty"/> has to be
    /// truthful on every focusable element, <see cref="KeepsInViewProperty"/> has
    /// to act on every scrolling region that carries it, and a style cannot attach
    /// a handler to the control it is styling.
    /// </summary>
    internal static void Track()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus));
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnLostKeyboardFocus));
        EventManager.RegisterClassHandler(typeof(ScrollContentPresenter), FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
    }

    // Both events bubble, so every ancestor of the focused element sees them.
    // The OriginalSource guard is the one WPF's own handlers use to pick out
    // the element the focus actually landed on.
    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not FrameworkElement element) return;

        element.SetValue(IsShowingKey,
            element.FocusVisualStyle is not null &&
            (SystemParameters.KeyboardCues || InputManager.Current.MostRecentInputDevice is KeyboardDevice));
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not FrameworkElement element) return;

        // Includes losing focus to nothing, which is what a window deactivating
        // does, and which is also when WPF hides the ring.
        element.SetValue(IsShowingKey, false);
    }

    // A request to scroll something into view bubbles from that element through the
    // ScrollContentPresenter to its ScrollViewer, which scrolls just far enough to show
    // the rectangle the request names. Focus arriving raises one naming no rectangle,
    // which the ScrollViewer takes as the element's bounds, or, for a link inside a
    // TextBlock, as the first line the link is drawn on. Here at the presenter, one
    // step before the ScrollViewer, the request is marked handled and raised again on
    // the scrolled content, naming every line of the element grown by Room. The
    // ScrollViewer then acts on that one alone. The second request names the content
    // itself and is let through.
    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not ScrollContentPresenter presenter ||
            presenter.TemplatedParent is not ScrollViewer viewer || !GetKeepsInView(viewer) ||
            presenter.Content is not FrameworkElement content ||
            ReferenceEquals(e.TargetObject, content) ||
            BoundsWithin(content, e) is not { } bounds) return;

        e.Handled = true;
        bounds.Inflate(Room, Room);
        content.BringIntoView(bounds);
    }

    // The rectangle the request names, in the content's coordinates. An element names
    // its own bounds where the request names none. A link names nothing, and its
    // TextBlock gives the rectangle of each line it is drawn on. Null where the target
    // is outside the content or has nothing drawn, and the request then goes on as it
    // was raised.
    private static Rect? BoundsWithin(FrameworkElement content, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is UIElement element && content.IsAncestorOf(element))
        {
            var rect = e.TargetRect.IsEmpty ? new Rect(element.RenderSize) : e.TargetRect;
            return element.TransformToAncestor(content).TransformBounds(rect);
        }

        if (e.TargetObject is ContentElement inline && HostOf(inline) is { } host && content.IsAncestorOf(host))
        {
            var lines = Rect.Empty;
            foreach (var line in ((IContentHost)host).GetRectangles(inline))
                lines.Union(line);
            if (!lines.IsEmpty)
                return host.TransformToAncestor(content).TransformBounds(lines);
        }

        return null;
    }

    // The TextBlock, or other element hosting text, that draws a link.
    private static UIElement? HostOf(ContentElement inline)
    {
        for (DependencyObject? node = inline; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is UIElement host and IContentHost)
                return host;
        }
        return null;
    }
}
