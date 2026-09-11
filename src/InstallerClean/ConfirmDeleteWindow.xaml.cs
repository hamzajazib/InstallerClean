using System.Windows;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean;

public partial class ConfirmDeleteWindow : Window
{
    public ConfirmDeleteWindow(int fileCount, string sizeDisplay)
    {
        InitializeComponent();
        var label = DisplayHelpers.PluraliseFile(fileCount);
        MessageText.Text = string.Format(
            Strings.Confirm_DeleteTitle, DisplayHelpers.FormatCount(fileCount), label, sizeDisplay);
        var body = DisplayHelpers.Pluralise(fileCount,
            Strings.Confirm_DeletePermanently_Singular,
            Strings.Confirm_DeletePermanently_Plural,
            "Confirm.DeletePermanently");
        BodyText.Text = body;
        // The window title is what a screen reader announces when a dialog
        // opens, and ShowInTaskbar is false under custom chrome, so it serves
        // the announcement and nothing else. It carries the question itself: a
        // title naming only the category leaves the count and the size unspoken.
        //
        // The body rides along with it, as it does on the sibling modals: on
        // open, only the title and the focused button are spoken, so a line left
        // in the body alone would go unheard. Here that line is the one being
        // consented to. It says the deletion is permanent and offers Move as the
        // way to keep a backup, which is the choice still open to the person
        // the dialog is asking.
        Title = MessageText.Text + " " + body;

        // Sized to content; the clamp stops a very large text scale
        // pushing the card past the work area, at which point the body
        // row scrolls and the action buttons stay visible.
        MaxHeight = DetailWindowSizing.WorkAreaHeightLimit(Application.Current?.MainWindow);

        this.EnableAltSpaceSystemMenu();
        this.SuppressFocusVisualOnDeactivation();
        // Open with focus on Cancel (IsDefault/IsCancel, the safe
        // default) so a keyboard user gets a visible focus ring at once
        // and a reflexive Space cannot delete. Deferred to Loaded so the
        // visual tree exists when Focus runs.
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
