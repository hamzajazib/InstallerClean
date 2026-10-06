using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace InstallerClean.Helpers;

/// <summary>
/// Moves the keyboard focus on from a scrolling region that stops taking it while it
/// has it.
///
/// <para>
/// The BodyScrollRegion style makes a region a Tab stop only while it can scroll. A
/// region that has the focus and stops overflowing, as a taller window or a smaller
/// text size makes it, stops taking the focus, and WPF moves focus it can no longer
/// hold up to the nearest parent that can take it, which is the window. Where
/// <see cref="PassesFocusOnProperty"/> is set, the focus goes instead to the stop Tab
/// would go to next: the region's first stop inside it, otherwise the first one after
/// it.
/// </para>
///
/// <para>
/// The move is posted at Normal priority rather than made where the region stops taking
/// the focus. That happens inside the region's own layout update, partway through
/// setting its scroll sizes, and focus arriving inside the region asks it to scroll
/// (<see cref="FocusRing.KeepsInViewProperty"/>). WPF answers the focus it can no longer
/// hold with a check posted at Input priority, which runs after anything posted at
/// Normal, so the move lands first and the check finds it and leaves it.
/// </para>
/// </summary>
public static class ScrollRegionFocus
{
    /// <summary>
    /// Set on a ScrollViewer, by the BodyScrollRegion style on every one the app draws.
    /// </summary>
    public static readonly DependencyProperty PassesFocusOnProperty =
        DependencyProperty.RegisterAttached(
            "PassesFocusOn", typeof(bool), typeof(ScrollRegionFocus),
            new PropertyMetadata(false, OnPassesFocusOnChanged));

    public static bool GetPassesFocusOn(DependencyObject element) =>
        (bool)element.GetValue(PassesFocusOnProperty);

    public static void SetPassesFocusOn(DependencyObject element, bool value) =>
        element.SetValue(PassesFocusOnProperty, value);

    private static void OnPassesFocusOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;

        viewer.FocusableChanged -= OnFocusableChanged;
        if (e.NewValue is true)
            viewer.FocusableChanged += OnFocusableChanged;
    }

    private static void OnFocusableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not false || sender is not ScrollViewer viewer || !viewer.IsKeyboardFocused) return;

        // Checked again when the move runs, because anything run before it may have moved
        // the focus on, hidden the region or let it take the focus again. A move from a
        // region that no longer has the focus would take it from wherever it now is.
        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (viewer.IsKeyboardFocused && !viewer.Focusable && viewer.IsVisible)
                viewer.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        });
    }
}
