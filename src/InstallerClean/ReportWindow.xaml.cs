using System.Windows;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean;

/// <summary>
/// The window the report panel's "See exactly what's sent" link opens: a heading,
/// the text of <c>last-run.json</c>, which is what the send posts, and one Close
/// button, on the same dark card as the app's other dialogs. Opened through
/// <see cref="Services.IWindowService.ShowReport"/>.
/// </summary>
public partial class ReportWindow : Window
{
    /// <param name="report">
    /// The file's text as it was read, shown as it stands, or null where it could not
    /// be read, in which case the window says so in place of the report.
    /// </param>
    public ReportWindow(string? report)
    {
        InitializeComponent();

        // The title is what a screen reader announces when the dialog opens, and
        // ShowInTaskbar is false and the chrome paints no caption, so it is never
        // drawn. The report itself is not put in it, being far too long to hear
        // as a title; the box carrying it is reached with Tab. The sentence shown
        // in its place is short, so it rides on the title as a message does in
        // MessageWindow, joined by the stop the resx carries for the language.
        if (report is null)
        {
            ReportBox.Visibility = Visibility.Collapsed;
            UnreadableText.Visibility = Visibility.Visible;
            Title = Strings.Window_Report_Title + Strings.Display_SentenceSeparator
                + Strings.Window_Report_Unreadable;
        }
        else
        {
            ReportText.Text = report;
            Title = Strings.Window_Report_Title;
        }

        // Sized to content, the card from 480 to 615 scaled. The clamps stop a long
        // report or a very large text scale pushing the card past the work area:
        // the report scrolls inside the card, across and down, and the Close button
        // stays visible.
        this.KeepCardInsideWorkArea(Card, minimumWidth: 480, maximumWidth: 615);

        this.EnableAltSpaceSystemMenu();
        this.SuppressFocusVisualOnDeactivation();
        // Close is the only action, so it takes focus (and Enter and Esc, being
        // IsDefault and IsCancel both). Deferred to Loaded so the visual tree
        // exists when Focus runs.
        Loaded += (_, _) => CloseButton.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
