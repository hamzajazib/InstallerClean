using System.Windows;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean;

public partial class ConfirmMoveWindow : Window
{
    /// <param name="sameDrive">
    /// True when the destination sits on the drive the files are already on. The
    /// caller has classified it in its pre-flight, so the dialog is told rather
    /// than working it out: resolving a drive is a Win32 hop, and this runs on
    /// the dispatcher.
    /// </param>
    public ConfirmMoveWindow(int fileCount, string sizeDisplay, string destination, bool sameDrive)
    {
        InitializeComponent();
        var label = DisplayHelpers.PluraliseFile(fileCount);
        MessageText.Text = string.Format(
            Strings.Confirm_MoveTitle, DisplayHelpers.FormatCount(fileCount), label, sizeDisplay);
        DestinationLabel.Text = DisplayHelpers.Pluralise(fileCount,
            Strings.Confirm_MoveDestination_Singular,
            Strings.Confirm_MoveDestination_Plural,
            "Confirm.MoveDestination");
        // A long path breaks after a backslash inside it, at a folder boundary
        // rather than inside a folder name, and not between the two backslashes a
        // share's name opens with (InstallerPathText.AllowFolderBreaksInAnyPath).
        DestinationText.Text = InstallerPathText.AllowFolderBreaksInAnyPath(destination);

        // A same-drive move is a rename: it frees nothing until the user deletes
        // the parked copies themselves. This is the only moment the app knows
        // that and the user is still deciding.
        if (sameDrive)
            SameDriveNote.Visibility = Visibility.Visible;

        // The window title is what a screen reader announces when a dialog
        // opens, and ShowInTaskbar is false under custom chrome, so it serves
        // the announcement and nothing else. It carries the question itself: a
        // title naming only the category leaves the count and the size
        // unspoken. The same-drive note rides
        // along with it: on open, only the title and the focused button are
        // spoken, so a note left in the body alone would never be heard by the
        // user deciding whether to press Move.
        //
        // The destination rides along for the same reason, and it is the fact
        // this dialog exists to confirm. The body sits in a scroll region that
        // is not a tab stop until it overflows, so the title is the route a
        // screen reader has to it. Label then value, the order the card reads,
        // so the join holds in every language. The raw path, not the wrapped
        // DestinationText, whose break characters are for the line breaker.
        //
        // The question and the label each carry their own punctuation, so the
        // space after each is the one the language puts after a sentence. The
        // path carries none, so the note after it takes the language's own stop
        // and space.
        var space = DisplayHelpers.SentenceSpace;
        var destinationLine = DestinationLabel.Text + space + destination;
        Title = sameDrive
            ? MessageText.Text + space + destinationLine
                + Strings.Display_SentenceSeparator + Strings.Confirm_MoveSameDrive
            : MessageText.Text + space + destinationLine;

        // Sized to content, the card from 440 to 520 scaled. The clamps stop a
        // very large text scale pushing the card past the work area: across, its
        // text wraps into the narrower card, and down, the destination row
        // scrolls and the action buttons stay visible.
        this.KeepCardInsideWorkArea(Card, minimumWidth: 440, maximumWidth: 520);

        this.EnableAltSpaceSystemMenu();
        this.SuppressFocusVisualOnDeactivation();
        // Open with focus on Cancel (IsDefault/IsCancel) so a keyboard
        // user gets a visible focus ring at once, mirroring the Confirm
        // Delete dialog. Deferred to Loaded so the visual tree exists
        // when Focus runs.
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnMove(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
