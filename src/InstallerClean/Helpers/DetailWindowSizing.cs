using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using InstallerClean.Interop.Native;

namespace InstallerClean.Helpers;

/// <summary>
/// Window sizing against the work area (the monitor minus the taskbar)
/// of the screen the app is on: startup heights for the detail windows,
/// MaxWidth / MaxHeight limits for the SizeToContent windows, and the
/// card widths of the dialogs that size to their card, so large OS text
/// scales grow them up to the screen and no further. A
/// fixed default cannot cover the range: a 1080p laptop at 150% scale
/// has roughly 672 device-independent units of work-area height, while
/// a 100% desktop has roughly 1030.
/// </summary>
internal static class DetailWindowSizing
{
    /// <summary>
    /// Gap kept between the window and the work-area edge so a clamped
    /// window never opens flush against the taskbar.
    /// </summary>
    private const double EdgeMargin = 24;

    /// <summary>
    /// <paramref name="preferred"/>, raised to <paramref name="minimum"/>
    /// and then held to the work-area height of the monitor hosting
    /// <paramref name="reference"/> (the primary monitor when the
    /// reference is null or not yet shown). See <see cref="Within"/>.
    /// All values in device-independent units.
    /// </summary>
    public static double ClampHeightToWorkArea(Window? reference, double preferred, double minimum)
        => Within(preferred, minimum, WorkAreaHeightLimit(reference));

    /// <summary>
    /// Horizontal counterpart of
    /// <see cref="ClampHeightToWorkArea(Window?, double, double)"/>.
    /// </summary>
    public static double ClampWidthToWorkArea(Window? reference, double preferred, double minimum)
        => Within(preferred, minimum, WorkAreaWidthLimit(reference));

    /// <summary>
    /// <paramref name="preferred"/>, never below <paramref name="minimum"/>
    /// and never above <paramref name="limit"/>. Where the limit is under the
    /// minimum the limit wins, so a window sized through this is never larger
    /// than its work area, whatever minimum it asks for.
    /// </summary>
    public static double Within(double preferred, double minimum, double limit)
        => Math.Min(Math.Max(minimum, preferred), limit);

    /// <summary>
    /// Sizes a resizable window, before it is shown, to
    /// <paramref name="preferredWidth"/> by <paramref name="preferredHeight"/>
    /// held inside the work area of the monitor the main window is on. The window's MinWidth and MinHeight are
    /// lowered to that work area first where they exceed it, because WPF holds
    /// a window's Width and Height at its MinWidth and MinHeight; on a larger
    /// work area they keep the values the XAML gives them.
    /// </summary>
    public static void SizeWithinWorkArea(this Window window, double preferredWidth, double preferredHeight)
    {
        var reference = Application.Current?.MainWindow;
        window.MinWidth = Math.Min(window.MinWidth, WorkAreaWidthLimit(reference));
        window.MinHeight = Math.Min(window.MinHeight, WorkAreaHeightLimit(reference));
        window.Width = ClampWidthToWorkArea(reference, preferredWidth, window.MinWidth);
        window.Height = ClampHeightToWorkArea(reference, preferredHeight, window.MinHeight);
    }

    /// <summary>
    /// The tallest a window may sensibly open on the monitor hosting
    /// <paramref name="reference"/>: the work-area height less the edge
    /// margin. Suited to a MaxHeight on a SizeToContent window.
    /// </summary>
    public static double WorkAreaHeightLimit(Window? reference)
        => WorkArea(reference).Height - EdgeMargin;

    /// <summary>
    /// The widest a window may sensibly open on the monitor hosting
    /// <paramref name="reference"/>: the work-area width less the edge
    /// margin. Suited to a MaxWidth on a SizeToContent window.
    /// </summary>
    public static double WorkAreaWidthLimit(Window? reference)
        => WorkArea(reference).Width - EdgeMargin;

    /// <summary>
    /// The least and greatest width of a dialog's card: <paramref name="minimum"/>
    /// and <paramref name="maximum"/>, its widths at 100% text scale, multiplied by
    /// <paramref name="factor"/>, and neither wider than <paramref name="limit"/>.
    /// The minimum is held to the limit as well as the maximum, because WPF lets
    /// MinWidth win over both MaxWidth and the room an element is given.
    /// </summary>
    public static (double Minimum, double Maximum) CardWidths(
        double minimum, double maximum, double factor, double limit)
        => (Math.Min(minimum * factor, limit), Math.Min(maximum * factor, limit));

    /// <summary>
    /// Holds a dialog that sizes to its card inside the work area of the monitor
    /// the main window is on: the card's MinWidth and MaxWidth come from
    /// <see cref="CardWidths"/> with <see cref="WorkAreaWidthLimit"/> as the limit,
    /// and the window's MaxHeight is <see cref="WorkAreaHeightLimit"/>. Both are
    /// taken again when the text size changes, until the window closes.
    /// <paramref name="minimumWidth"/> and <paramref name="maximumWidth"/> are the
    /// card's widths at 100% text scale, and the card carries no width of its own
    /// in XAML, so these are the only ones it has.
    /// </summary>
    public static void KeepCardInsideWorkArea(
        this Window window, FrameworkElement card, double minimumWidth, double maximumWidth)
    {
        void Apply()
        {
            var reference = Application.Current?.MainWindow;
            (card.MinWidth, card.MaxWidth) = CardWidths(minimumWidth, maximumWidth,
                AccessibilitySettings.Current.TextScaleFactor, WorkAreaWidthLimit(reference));
            window.MaxHeight = WorkAreaHeightLimit(reference);
        }

        void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(AccessibilitySettings.TextScaleFactor))
                Apply();
        }

        Apply();
        AccessibilitySettings.Current.PropertyChanged += OnSettingsChanged;
        window.Closed += (_, _) => AccessibilitySettings.Current.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Moves <paramref name="window"/> back inside its monitor's work
    /// area if its bottom or right edge has crossed out. A shown
    /// SizeToContent window grows down and right from a fixed top-left,
    /// so a live OS text-scale increase can push its action rows under
    /// the taskbar even though the window itself still fits the screen;
    /// repositioning restores them. Best effort: a window with no
    /// handle yet, or an unresolvable monitor, is left where it is.
    /// </summary>
    public static void NudgeIntoWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || double.IsNaN(window.Left) || double.IsNaN(window.Top))
            return;

        var monitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        var info = new User32.MONITORINFO { cbSize = Unsafe.SizeOf<User32.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !User32.GetMonitorInfo(monitor, ref info))
            return;

        // rcWork is device pixels; the window's DPI scale converts to
        // the units Window.Left/Top are expressed in.
        var dpi = VisualTreeHelper.GetDpi(window);
        var workLeft = info.rcWork.Left / dpi.DpiScaleX;
        var workTop = info.rcWork.Top / dpi.DpiScaleY;
        var workRight = info.rcWork.Right / dpi.DpiScaleX;
        var workBottom = info.rcWork.Bottom / dpi.DpiScaleY;

        if (window.Left + window.ActualWidth > workRight)
            window.Left = Math.Max(workLeft, workRight - window.ActualWidth);
        if (window.Top + window.ActualHeight > workBottom)
            window.Top = Math.Max(workTop, workBottom - window.ActualHeight);
    }

    private static (double Width, double Height) WorkArea(Window? reference)
    {
        if (reference is not null
            && new WindowInteropHelper(reference).Handle is var hwnd
            && hwnd != IntPtr.Zero)
        {
            var monitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
            var info = new User32.MONITORINFO { cbSize = Unsafe.SizeOf<User32.MONITORINFO>() };
            if (monitor != IntPtr.Zero && User32.GetMonitorInfo(monitor, ref info))
            {
                // rcWork is device pixels; the reference window's DPI
                // scale converts to the units WPF sizes windows in.
                var dpi = VisualTreeHelper.GetDpi(reference);
                return ((info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX,
                        (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY);
            }
        }

        // Primary monitor's work area, already in device-independent units.
        return (SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
    }
}
