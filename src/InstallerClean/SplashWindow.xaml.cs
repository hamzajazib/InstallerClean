using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;

namespace InstallerClean;

public partial class SplashWindow : Window
{
    // Where the bar stands is worked out by ScanProgressFill, which holds the
    // band arithmetic and no drawing; this window turns what it returns into
    // motion. See that class for how the scan's milestones and ticker divide the
    // range between the floor and the ceiling.
    private readonly ScanProgressFill _fill = new();

    // How long a step of the fill takes when the caller does not say. The closing
    // step does say, so that the last of the bar arrives as the window goes
    // rather than being cut off by it.
    private static readonly TimeSpan StepEase = TimeSpan.FromMilliseconds(250);

    // Set when the user asks to stop. The scan observes cancellation at its next
    // checkpoint, so updates already in flight arrive after this and would
    // otherwise write a phase message over the line saying the app is stopping.
    private bool _cancelling;

    public event EventHandler? CancelRequested;

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = DisplayHelpers.GetVersionString();

        // The 480 x 320 box is the 100% design. The card sizes its height to
        // content (SizeToContent="Height"); MinHeight holds the designed box,
        // the star spacer rows absorbing the slack while the content is
        // shorter, and the card grows when the content is taller, e.g. a
        // larger OS text scale or a longer-language hero name / step text /
        // Cancel outgrowing 320. A fixed Height clips the bottom-pinned
        // version against the card's edge. MaxHeight caps growth at the work
        // area. The splash opens before any other window exists, so the clamp
        // resolves against the primary monitor's work area (the helper's null
        // fallback). Width is assigned rather than sized to content: the
        // content fits 480 in every language.
        var factor = AccessibilitySettings.Current.TextScaleFactor;
        Width = Math.Min(480 * factor, DetailWindowSizing.WorkAreaWidthLimit(null));
        MinHeight = Math.Min(320 * factor, DetailWindowSizing.WorkAreaHeightLimit(null));
        MaxHeight = DetailWindowSizing.WorkAreaHeightLimit(null);

        this.SuppressFocusVisualOnDeactivation();
        // Loaded fires after the visual tree is realised, so Focus()
        // on the only focusable element lands on first paint and the
        // keyboard-only user sees a visible focus ring immediately.
        Loaded += (_, _) => CancelButton.Focus();
    }

    public void OnScanProgress(ScanProgressUpdate update)
    {
        if (_cancelling) return;

        if (update.IsMilestone)
        {
            UpdateStep(update.Message, _fill.AtMilestone());
            return;
        }
        // Ticker: per-item, display-only. The step text (the live
        // region) is left alone so the splash does not queue one
        // announcement per installed product.
        ProductTicker.Text = update.Message;
        AnimateProgress(_fill.AtTicker(update.Position, update.Total));
    }

    public void UpdateStep(string message, double progressPercent, TimeSpan? ease = null)
    {
        StepText.Text = message;
        // A milestone closes the phase the ticker was narrating; clear
        // it so the last product name does not sit stale beside the next
        // phase's message.
        ProductTicker.Text = string.Empty;
        AnimateProgress(progressPercent, ease);
    }

    private void AnimateProgress(double progressPercent, TimeSpan? ease = null)
    {
        progressPercent = _fill.At(progressPercent);
        if (AccessibilitySettings.Current.ReduceMotion)
        {
            // Reduced motion: set the value with no easing. Clearing the
            // animation clock first is required because an animation left
            // holding its end value otherwise overrides a plain Value
            // assignment.
            SplashProgress.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
            SplashProgress.Value = progressPercent;
            return;
        }
        // ProgressBar.Value isn't implicitly animated; on a fast scan
        // the bar would jump 0 -> ~95 -> 100 in two frames. Ease each
        // step so the splash feels like a deliberate motion rather than
        // a sequence of instantaneous frames.
        var animation = new DoubleAnimation
        {
            To = progressPercent,
            Duration = ease ?? StepEase,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        SplashProgress.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, animation);
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        _cancelling = true;
        CancelButton.IsEnabled = false;
        CancelButton.Content = Strings.Status_Cancelling;
        // SR-announced name tracks the visible Content swap; a
        // declared-once XAML name drifts from the post-click label and
        // from IsEnabled=false.
        AutomationProperties.SetName(CancelButton, Strings.Status_Cancelling);
        // Tooltip explains the disabled state: cancellation is observed
        // at the next CancellationToken checkpoint, which during a
        // mid-MSI-API call can be several seconds. Without this hint a
        // user sees a frozen "Cancelling..." button and can't tell the
        // app from hung.
        ToolTipService.SetShowOnDisabled(CancelButton, true);
        CancelButton.ToolTip = Strings.Tooltip_CancellingPending;
        // The same sentence as HelpText, from the same value, because a WPF
        // ToolTip opens on hover and never on keyboard focus: without this the
        // one explanation for a button that has just gone dead reaches a mouse
        // user only, and the wait here sits inside an MSI API call and can be
        // the longest in the app.
        AutomationProperties.SetHelpText(CancelButton, Strings.Tooltip_CancellingPending);
        StepText.Text = Strings.Status_Cancelling;
        ProductTicker.Text = string.Empty;

        // The length of this wait is set by where the scan happens to be, so the
        // bar stops claiming a position and shows only that the app is working,
        // which is the bar the main window shows for a scan of its own. The
        // animation clock is cleared first because a determinate animation left
        // running holds its end value against the bar it no longer drives.
        SplashProgress.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
        SplashProgress.IsIndeterminate = true;

        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
