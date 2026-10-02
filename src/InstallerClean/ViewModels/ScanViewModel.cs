using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.ViewModels;

/// <summary>
/// Scanning slice of the main window's state. Owns the scan command,
/// the displayed registered/orphaned counts, the pending-reboot
/// warning, the missing-from-disk warning and a reference to the last
/// scan result.
///
/// Other slices (CleanupViewModel, ChromeViewModel) read
/// <see cref="LastScanResult"/> rather than calling the scan service
/// themselves so the cached result stays the single source of truth.
/// </summary>
public partial class ScanViewModel : ObservableObject
{
    private readonly IFileSystemScanService _scanService;
    private readonly IPendingRebootService _rebootService;
    private readonly IDialogService _dialogService;
    private readonly Func<bool> _isExternallyBlocked;

    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Reveals the scanning overlay. Deliberately not the gate on any command:
    /// see <see cref="IsScanInFlight"/>.
    /// </summary>
    [ObservableProperty] private bool _isScanning;

    /// <summary>
    /// True from the first line of every scan (the Scan command, the splash
    /// startup scan and the silent post-operation refresh) until it ends.
    ///
    /// <see cref="IsScanning"/> cannot serve as this gate: it is an
    /// overlay-reveal flag, set only once a scan outlives the 200 ms delay,
    /// and never at all by the silent refresh. It would leave the Move and
    /// Delete buttons live through the first 200 ms of a scan, which is long
    /// enough to start a destructive batch against the previous scan's result
    /// while a fresh scan walks the same folder, and to leave two scans writing
    /// <see cref="LastScanResult"/> with no ordering between them.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanInFlight;

    [ObservableProperty] private string _scanProgress = string.Empty;

    /// <summary>
    /// Ticker line under the scan overlay's milestone text: a count of the
    /// folder's files as the scan lists them, the name of each installed
    /// program as it asks Windows about it, then a count of the files as
    /// it matches them. Display-only: the bound TextBlock carries no
    /// LiveSetting because the ticker updates once per program and many
    /// times while the files are counted, which can run to hundreds of
    /// updates in a few seconds, and a live region would queue an
    /// announcement for every one of them.
    /// </summary>
    [ObservableProperty] private string _scanTicker = string.Empty;

    /// <summary>
    /// True while the view model holds the result of a scan that completed. False
    /// before the first one, which is a state the user reaches by cancelling the
    /// startup scan, and false again after a scan that stops or fails, and after
    /// the user cancels the scan that follows a Move or Delete. The main window has
    /// to say so rather than paint a zeroed scan result, or an earlier one.
    ///
    /// A Re-scan the user cancels leaves it as it was. An earlier result stays on
    /// screen: that scan completed, and nothing has acted on its result since.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrphans))]
    private bool _hasScanned;

    [ObservableProperty] private int _registeredFileCount;
    [ObservableProperty] private string _registeredSizeDisplay = string.Empty;
    [ObservableProperty] private int _orphanedFileCount;
    [ObservableProperty] private string _orphanedSizeDisplay = string.Empty;

    /// <summary>
    /// Last pending-reboot probe result. Null until a scan completes, and null
    /// again at each ending <see cref="HasScanned"/> lists, the banner going with
    /// the list it sits over.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingReboot))]
    [NotifyPropertyChangedFor(nameof(PendingRebootBannerText))]
    private PendingRebootResult? _pendingRebootResult;

    /// <summary>True when the last probe returned Block.</summary>
    public bool HasPendingReboot => PendingRebootResult?.IsBlocked == true;

    // A non-localised PendingRebootLabel lived here for the result-log payload
    // and went with that field in schema 4. Nothing else ever read it: the state
    // reaches the screen through PendingRebootBannerText below and reaches both
    // action paths through their own fresh probe, neither of which wants a label.

    /// <summary>
    /// Localised banner text for the current Block reason; empty otherwise.
    ///
    /// The last arm is what a reason with no line of its own gets, and it is a
    /// string rather than a throw because of where this is read from: WPF calls
    /// it through a binding and swallows anything it throws as a binding error,
    /// so the user would meet a blank banner over greyed buttons with nothing on
    /// screen saying why. A generic sentence that is true of the whole family
    /// beats that. It is unreachable while every member of the enum is handled
    /// above, and a member added without a banner is a failing test rather than a
    /// silent gap because Every_reason_has_a_banner_of_its_own walks the enum,
    /// and A_reason_with_no_banner_of_its_own_still_says_something covers this
    /// arm (ScanViewModelPendingRebootTests).
    ///
    /// THE SAME SENTENCE IS ALSO THE IN-PROGRESS FILE'S OWN, named in its arm
    /// above: it says Windows Installer has something in progress, which is what
    /// that file records. A member added later without a banner falls through to
    /// the same text and meets the test's distinctness check, which the file's arm
    /// keeps armed.
    /// </summary>
    public string PendingRebootBannerText => PendingRebootResult?.Reason switch
    {
        PendingRebootReason.MsiExecuteMutexHeld => Strings.Body_PendingReboot_MsiExecuteMutex,
        PendingRebootReason.MsiExecuteMutexAccessRefused => Strings.Body_PendingReboot_MsiExecuteMutexAccessRefused,
        PendingRebootReason.InstallerInProgress => Strings.Body_PendingReboot_InstallerInProgress,
        PendingRebootReason.PendingRenameInCache => Strings.Body_PendingReboot_PendingRenameInCache,
        PendingRebootReason.PendingRenameUnresolved => Strings.Body_PendingReboot_PendingRenameUnresolved,
        PendingRebootReason.RegistryCheckUnreadable => Strings.Body_PendingReboot_RegistryCheckUnreadable,
        PendingRebootReason.InstallerInProgressMarker => Strings.Body_PendingReboot_Other,
        // Windows refused the read of the in-progress file: the refused-permission
        // sentence is as true of that read as of the mutex's.
        PendingRebootReason.InstallerInProgressMarkerAccessRefused => Strings.Body_PendingReboot_MsiExecuteMutexAccessRefused,
        null => string.Empty,
        _ => Strings.Body_PendingReboot_Other,
    };

    /// <summary>
    /// The registrations this scan found naming a file that is not on disk AND whose
    /// absence it could not establish to be harmless. Drives the missing-files line, and
    /// it is the count the line prints as well as the condition that fires it, so the two
    /// cannot disagree.
    ///
    /// IT IS A HALF, AND THE AXIS IS A CONJUNCTION. A missing registration is left out
    /// only where its patch state is superseded or obsoleted AND every product sharing the
    /// patch was shown to hold no patch that could be uninstalled and roll back onto the
    /// file. The state alone does not make an absence harmless, Windows opening every
    /// registered patch's cached file whichever state it carries. Counting every missing
    /// registration would warn about files this app removed after establishing exactly
    /// that condition.
    ///
    /// <see cref="MissingFilesReport.Affected"/> is that expression, named once, and the
    /// programs this line names come off the same predicate. The full total still travels
    /// in the scan result and in the report payload, where a public chart reads it with no
    /// version gate.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingFromDisk))]
    [NotifyPropertyChangedFor(nameof(MissingFromDiskSummaryText))]
    private int _missingFromDiskCount;

    /// <summary>
    /// The programs those files belong to, as the one phrase the line names them
    /// in, already capped and joined by <see cref="MissingFilesReport"/> so the
    /// window and the command line say the same thing. Empty when there are none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MissingFromDiskSummaryText))]
    private string _missingFromDiskPrograms = string.Empty;

    /// <summary>
    /// The result the main window is showing, from the most recent scan that
    /// completed. Null until the first scan completes, and null again after a scan
    /// that stops or fails and after the user cancels the scan that follows a Move
    /// or Delete (see <see cref="HasScanned"/>), so Move and Delete have no files
    /// to act on then and the Details windows no list to open. Otherwise the same
    /// instance until the next scan replaces it.
    /// </summary>
    public ScanResult? LastScanResult { get; private set; }

    /// <summary>
    /// Wall-clock duration of the most recent user-visible scan, in
    /// milliseconds. Set by <c>ScanAsync</c> and <c>ScanWithProgressAsync</c>;
    /// not overwritten by <c>RefreshAsync</c> so the result-log entry
    /// built after a Move or Delete reports the duration of the scan
    /// that surfaced the orphans, not the silent post-operation refresh.
    /// </summary>
    public long LastScanDurationMs { get; private set; }

    /// <summary>
    /// Raised after every successful scan completes, including the
    /// initial startup scan. Subscribers can read
    /// <see cref="LastScanResult"/> at this point.
    /// </summary>
    public event EventHandler? ScanCompleted;

    public ScanViewModel(
        IFileSystemScanService scanService,
        IPendingRebootService rebootService,
        IDialogService dialogService,
        Func<bool>? isExternallyBlocked = null)
    {
        _scanService = scanService;
        _rebootService = rebootService;
        _dialogService = dialogService;
        _isExternallyBlocked = isExternallyBlocked ?? (() => false);
        _statusWait = new WaitLine(() => ScanProgress, line => ScanProgress = line);
    }

    /// <summary>
    /// Tells the Scan command to re-evaluate its CanExecute. MainViewModel
    /// calls this when the externally-blocked predicate's inputs change
    /// (Cleanup.IsOperationInFlight or Completion.IsComplete).
    /// </summary>
    public void NotifyExternallyBlockedChanged() =>
        ScanCommand.NotifyCanExecuteChanged();

    public string RegisteredSummaryText =>
        string.Format(
            DisplayHelpers.Pluralise(RegisteredFileCount,
                Strings.Summary_RegisteredStillUsed_Singular,
                Strings.Summary_RegisteredStillUsed_Plural,
                "Summary.RegisteredStillUsed"),
            DisplayHelpers.FormatCount(RegisteredFileCount));

    public string OrphanedSummaryText =>
        string.Format(
            DisplayHelpers.Pluralise(OrphanedFileCount,
                Strings.Summary_OrphanedToCleanUp_Singular,
                Strings.Summary_OrphanedToCleanUp_Plural,
                "Summary.OrphanedToCleanUp"),
            DisplayHelpers.FormatCount(OrphanedFileCount));

    /// <summary>
    /// True when the last scan found files to clean up. Move, Delete and the
    /// Details button beside the unneeded count are enabled only while this
    /// holds, and the window's pending-reboot lead
    /// (<see cref="Strings.Body_PendingReboot_Lead"/>) shows only while it holds.
    /// </summary>
    public bool HasOrphans => HasScanned && OrphanedFileCount > 0;

    public bool HasMissingFromDisk => MissingFromDiskCount > 0;

    public string MissingFromDiskSummaryText =>
        string.Format(
            DisplayHelpers.Pluralise(MissingFromDiskCount,
                Strings.Summary_MissingFromDisk_Singular,
                Strings.Summary_MissingFromDisk_Plural,
                "Summary.MissingFromDisk"),
            DisplayHelpers.FormatCount(MissingFromDiskCount), MissingFromDiskPrograms);

    partial void OnRegisteredFileCountChanged(int value) =>
        OnPropertyChanged(nameof(RegisteredSummaryText));

    partial void OnOrphanedFileCountChanged(int value)
    {
        OnPropertyChanged(nameof(OrphanedSummaryText));
        OnPropertyChanged(nameof(HasOrphans));
    }

    /// <summary>
    /// Runs the scan service and updates this VM's display fields.
    /// Used by the user-driven Scan command and by the splash startup
    /// scan. Does not raise <see cref="ScanCompleted"/>; that fires
    /// from <see cref="ScanAsync"/> and
    /// <see cref="ScanWithProgressAsync"/> after their respective
    /// success paths.
    /// </summary>
    private async Task RunScanCoreAsync(IProgress<ScanProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        // Set before the first await, so it is already true when the caller's
        // command returns to the dispatcher: every scan entry point routes
        // through here, so this is the one place the gate can be complete.
        IsScanInFlight = true;
        try
        {
            // Compute everything off the call results before touching any
            // observable property, so a throw or a cancel leaves every property
            // as this method found it. Which endings keep that earlier result on
            // screen is the callers' decision, each in its own catch.
            var result = await _scanService.ScanAsync(progress, cancellationToken);
            // Sample reboot after the scan; ordering matters. An MSI install
            // starting mid-scan could flip the _MSIExecute mutex, and
            // probing first would miss it.
            var pendingRebootResult = await Task.Run(() => _rebootService.Check(), cancellationToken);

            // THE LEFT-ALONE LINE COUNTS THE WITHHELD FILES TOO, so that it and the
            // offer between them take in every file a registration names and every
            // file on the scan's candidate list. THE PROPERTY IS THE ACCOUNTING, NOT
            // THE NUMBER OF TERMS: a withheld file is in neither population
            // otherwise, being neither offered nor a registered row, because no
            // registration names it. The two lines would then leave every withheld
            // file out, with the difference shown nowhere, which is exactly what this
            // term exists to prevent. Anything added here later has to close it
            // again.
            //
            // NO CAUSE TRAVELS WITH IT to this line, and none may be added. What the
            // user sees is that the app left these alone, which is true of every file
            // in both populations; why any particular one was left is not a sentence
            // this line can carry.
            //
            // The Details window's own header counts the same two populations, off
            // the same lists, because the two are one click apart and a reader
            // comparing them must not find them disagreeing.
            //
            // A REGISTRATION WHOSE FILE IS GONE IS NOT ONE OF THESE. The line says files
            // were left alone; a file that is not in the folder was not left alone, and
            // there is nothing on the disk for the size beside it to measure.
            // RegisteredTotalBytes is summed under the same existence test the scan
            // settles the split on, so the count reads it the same way and the two
            // describe one population. The Details window's footer works off the same
            // set and says how many are missing, which is where that figure belongs:
            // this line carries no cause and the missing rows are not on this screen.
            var withheld = result.WithheldFiles ?? Array.Empty<OrphanedFile>();
            var registeredCount =
                result.RegisteredPackages.Count(p => !p.IsMissingFromDisk) + withheld.Count;
            var registeredSize = DisplayHelpers.FormatSize(
                result.RegisteredTotalBytes + withheld.Sum(f => f.SizeBytes));
            var orphanedCount = result.RemovableFiles.Count;
            var orphanedSize = DisplayHelpers.FormatSize(result.RemovableFiles.Sum(f => f.SizeBytes));
            // Built here with the rest of the display state rather than in the
            // property, which is read on every binding refresh: it walks the whole
            // registered set and groups it, and the answer only changes when a
            // scan does.
            var missingPrograms = MissingFilesReport.Inline(
                MissingFilesReport.Products(result.RegisteredPackages));

            PendingRebootResult = pendingRebootResult;
            LastScanResult = result;
            RegisteredFileCount = registeredCount;
            RegisteredSizeDisplay = registeredSize;
            OrphanedFileCount = orphanedCount;
            OrphanedSizeDisplay = orphanedSize;
            // THE AFFECTED HALF, NOT THE SUM. The banner fires where something could
            // still reach for a file that is gone, so a registration whose absence the
            // app positively established to be harmless is not in the count and its
            // program is not named. Both come off the same predicate in
            // MissingFilesReport, and the sum still travels in the report payload, where
            // a public chart reads it with no version gate.
            MissingFromDiskCount = result.MissingAffectedCount;
            MissingFromDiskPrograms = missingPrograms;
            HasScanned = true;
        }
        finally
        {
            IsScanInFlight = false;
        }
    }

    /// <summary>
    /// User-driven scan command. Shows the scan overlay if the scan
    /// takes longer than 200ms, surfaces admin / DB / unknown errors
    /// to the dialog service, and updates <see cref="ScanProgress"/>
    /// throughout.
    /// </summary>
    private bool CanScan() => !IsScanInFlight && !_isExternallyBlocked();

    /// <summary>
    /// True when the most recent scan ended because the user cancelled it
    /// (rather than completing or failing). The view reads this when the
    /// scanning overlay collapses to re-announce "Scan cancelled." past the
    /// focus move that would otherwise swallow it, and the main window's
    /// states with no result read it to say why there is nothing on screen.
    /// Reset at the start of every scan.
    ///
    /// Observable, not a plain property: the startup scan is the one that gets
    /// cancelled in practice, and it sets this without ever setting
    /// <see cref="HasScanned"/>, so nothing else raises for the window to
    /// re-read it.
    /// </summary>
    public bool LastScanWasCancelled
    {
        get => _lastScanWasCancelled;
        private set => SetProperty(ref _lastScanWasCancelled, value);
    }

    private bool _lastScanWasCancelled;

    /// <summary>
    /// Tailored, safe-to-show message for the most recent scan that FAILED. Empty
    /// until the first failure and cleared by a scan that completes. A Re-scan the
    /// user cancels leaves it where it was, along with everything else on screen,
    /// and the scan after a Move or Delete clears it as it starts, carrying a
    /// message of its own (<see cref="UnfinishedRefreshMessage"/>). Both the
    /// user-driven Scan command and the startup scan set it
    /// through the one error ladder (<see cref="DescribeScanFailure"/>); the main
    /// window shows it in place of the not-yet-scanned copy, with Re-scan focused,
    /// so a failed startup scan opens the window with the diagnosis rather than
    /// exiting.
    /// </summary>
    public string LastScanError
    {
        get => _lastScanError;
        private set
        {
            if (SetProperty(ref _lastScanError, value))
                OnPropertyChanged(nameof(HasScanError));
        }
    }

    private string _lastScanError = string.Empty;

    /// <summary>True when the last scan failed and its message is on screen.</summary>
    public bool HasScanError => LastScanError.Length > 0;

    /// <summary>
    /// The heading for <see cref="LastScanError"/>, the one its dialog carries on
    /// a Re-scan, so the main window and the dialog name the failure alike.
    /// Meaningful only while <see cref="HasScanError"/> is true. The window
    /// re-reads its heading when this changes as well as when the message does,
    /// so the two can be set in either order.
    /// </summary>
    public string LastScanErrorTitle
    {
        get => _lastScanErrorTitle;
        private set => SetProperty(ref _lastScanErrorTitle, value);
    }

    private string _lastScanErrorTitle = string.Empty;

    /// <summary>
    /// What the main window says in place of a list when the scan after a Move or
    /// Delete ended without a result: stopped, failed or cancelled. Empty
    /// otherwise. That scan clears it as it starts and sets it if it ends without
    /// a result, a Re-scan that completes or fails clears it, and a Re-scan the
    /// user cancels leaves it where it was, with the rest of the window.
    ///
    /// It is its own message rather than the failure's, which is what
    /// <see cref="LastScanError"/> carries after the other two scans. The account a
    /// scan gives when it stops, or when Windows refuses it, says that nothing has
    /// been removed. That is true of the scan, and it reads as false straight after
    /// a Move or Delete that has moved or deleted files. So the window says only
    /// that this scan did not finish, whatever ended it, and the account goes to
    /// the crash log, whose path the message names where the write succeeded. A
    /// Re-scan gives the account in full if the cause is still there.
    /// </summary>
    public string UnfinishedRefreshMessage
    {
        get => _unfinishedRefreshMessage;
        private set
        {
            if (SetProperty(ref _unfinishedRefreshMessage, value))
                OnPropertyChanged(nameof(HasUnfinishedRefresh));
        }
    }

    private string _unfinishedRefreshMessage = string.Empty;

    /// <summary>True when the scan after a Move or Delete ended without a result.</summary>
    public bool HasUnfinishedRefresh => UnfinishedRefreshMessage.Length > 0;

    /// <summary>
    /// Takes the last result off the view model, at each ending
    /// <see cref="HasScanned"/> lists. Everything the main window draws from a
    /// result goes with it: both counts, the missing-files line and the
    /// pending-reboot banner. So do the commands that act on one, Move and Delete
    /// having no files to act on and the Details windows no list to open.
    ///
    /// <see cref="LastScanResult"/> goes first and <see cref="HasScanned"/> last.
    /// The result raises nothing when it changes, and the Details commands re-ask
    /// whether they can run when HasScanned does, so HasScanned going false while
    /// the result was still here would leave both buttons live over it.
    /// </summary>
    private void DropResult()
    {
        LastScanResult = null;
        RegisteredFileCount = 0;
        RegisteredSizeDisplay = string.Empty;
        OrphanedFileCount = 0;
        OrphanedSizeDisplay = string.Empty;
        MissingFromDiskCount = 0;
        MissingFromDiskPrograms = string.Empty;
        PendingRebootResult = null;
        HasScanned = false;
    }

    /// <summary>
    /// The scan's one error ladder: maps a scan (or act-time re-verify) failure to
    /// the message, dialog title and overlay status line the user should see, so
    /// the user-driven Scan command, the startup scan and the re-verify all
    /// diagnose a failure the same way instead of each inventing its own. The
    /// two arms that write the crash log name its path in the message they return,
    /// and this is called once per failure, so neither writes twice.
    /// <see cref="OperationCanceledException"/> is handled by its own catch and
    /// never reaches here.
    /// </summary>
    internal ScanFailure DescribeScanFailure(Exception ex) => ex switch
    {
        // LocalisedAccessException before UnauthorizedAccessException: it derives
        // from it and carries a precise, safe-to-echo resx message naming what
        // Windows refused, where the BCL type only earns the general message that
        // Windows refused access.
        LocalisedAccessException =>
            new(ex.Message, Strings.Error_AdminRequiredTitle, IsError: false, Strings.Status_ScanAccessDenied),
        UnauthorizedAccessException =>
            new(Strings.Error_AdminRequiredBody, Strings.Error_AdminRequiredTitle, IsError: false, Strings.Status_ScanAccessDenied),
        LocalisedInvalidOperationException => DescribeDeliberateStop(ex),
        _ => DescribeUnexpectedScanFailure(ex),
    };

    /// <summary>
    /// A stop the app decided on for itself, at a scan or at the check made just
    /// before a Move or Delete. The stops have different causes, and the message is a
    /// full account of this one, built from the app's own strings, so it is safe to
    /// show. The heading and the status line are the same for every stop and name no
    /// cause.
    ///
    /// IT GOES TO THE CRASH LOG AS WELL, WHICH IS THE WHOLE REASON THIS ARM IS A
    /// METHOD. The account is shown and then gone: on the overlay it lasts as long
    /// as the window, and at the two act-time callers it is a dialog the reader
    /// dismisses. Writing it leaves a file that can be attached to a report and read
    /// back afterwards. The command line reaches the same condition through its own
    /// catch and writes the reason to the Windows event log.
    ///
    /// The closing sentence is added only where the write succeeded, following the
    /// generic branch's two forms: a message naming a file that was never written
    /// sends somebody looking for it.
    /// </summary>
    private static ScanFailure DescribeDeliberateStop(Exception ex)
    {
        var crash = CrashLog.TryWrite(ex);
        var message = crash.Written
            ? ex.Message + Environment.NewLine + Environment.NewLine
                + string.Format(Strings.Error_ScanStoppedDetails, crash.Path)
            : ex.Message;
        return new ScanFailure(message, Strings.Error_StoppedTitle,
            IsError: true, Strings.Status_ScanStopped);
    }

    private static ScanFailure DescribeUnexpectedScanFailure(Exception ex)
    {
        // ex.Message never reaches UI: type name plus log path only, because a
        // framework message from an elevated process can carry a path out of
        // another user's profile.
        var crash = CrashLog.TryWrite(ex);
        var typeName = ex.GetType().Name;
        // THE PATH GOES IN RAW AND THE BREAK OPPORTUNITIES ARE ADDED WHERE IT IS
        // DRAWN, by InstallerPathTextConverter. This string reaches two surfaces: the
        // intro line, which is laid out, and the scan announcer, which is never drawn
        // and takes it unbound. Inserting them here would hand a speech engine
        // invisible format characters for a layout that does not exist.
        var message = crash.Written
            ? string.Format(Strings.Status_ScanFailedDetails, typeName, crash.Path)
            : string.Format(Strings.Status_ScanFailedDetails_NoLog, typeName);
        return new ScanFailure(message, Strings.Error_ScanFailedTitle, IsError: true, message);
    }

    /// <summary>One rung of the scan error ladder: what to show and how.</summary>
    internal readonly record struct ScanFailure(string Message, string Title, bool IsError, string StatusLine);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        // The two messages stay until this scan has an outcome of its own. A
        // cancel leaves the window as it was before the click, whichever state
        // that was.
        LastScanWasCancelled = false;
        _statusWait.Forget();
        ScanProgress = Strings.Status_StartingScan;
        ScanTicker = string.Empty;
        var sw = Stopwatch.StartNew();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        try
        {
            // Throttled on the same terms as the startup scan's: the ticker fires
            // once per installed program and many times while the files are
            // counted, and each update that gets through crosses to the
            // dispatcher and replaces the overlay's second line.
            var progress = new ThrottledScanProgress(
                new Progress<ScanProgressUpdate>(ApplyProgressUpdate));
            var scanTask = RunScanCoreAsync(progress, cts.Token);
            if (await Task.WhenAny(scanTask, Task.Delay(200, cts.Token)) != scanTask)
                IsScanning = true;
            await scanTask;

            sw.Stop();
            LastScanDurationMs = sw.ElapsedMilliseconds;
            LastScanError = string.Empty;
            UnfinishedRefreshMessage = string.Empty;
            ScanProgress = string.Format(Strings.Status_ScanComplete, DisplayHelpers.FormatElapsed(sw.Elapsed));
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            LastScanWasCancelled = true;
            ScanProgress = Strings.Status_ScanCancelled;
        }
        catch (Exception ex)
        {
            // One error ladder, shared with the startup scan (which shows the
            // message inline in the window rather than a modal) and the act-time
            // re-verify. LastScanError is set on every path so the main window can
            // reflect a failed Re-scan the same way it reflects a failed startup
            // scan; the modal is the immediate feedback for the explicit click.
            //
            // The earlier scan's result goes, so the window under the modal is the
            // one a failed startup scan opens: the message, Re-scan, and nothing
            // from a list this scan did not produce. A cancel, in the arm above,
            // keeps it.
            var failure = DescribeScanFailure(ex);
            UnfinishedRefreshMessage = string.Empty;
            LastScanErrorTitle = failure.Title;
            LastScanError = failure.Message;
            DropResult();
            ScanProgress = failure.StatusLine;
            if (failure.IsError)
                _dialogService.ShowError(failure.Message, failure.Title);
            else
                _dialogService.ShowWarning(failure.Message, failure.Title);
        }
        finally
        {
            // Capture, null, dispose: a concurrent CancelScanCommand
            // reading _scanCts after the null sees no CTS and no-ops.
            // Mirrors CleanupViewModel.DisposeOperationCts.
            var local = _scanCts;
            _scanCts = null;
            local?.Dispose();
            IsScanning = false;
        }
    }

    /// <summary>
    /// Routes one scan progress update to the overlay's two lines:
    /// milestones to the announced status text, the ticker to the
    /// display-only line beneath it. A milestone also clears the ticker
    /// so the last program name or count does not sit stale beside the
    /// next phase's message.
    /// </summary>
    private void ApplyProgressUpdate(ScanProgressUpdate update)
    {
        // A wait takes the announced status text and gives it back when it ends, leaving
        // the ticker as it was. Once Cancel is pressed a wait is neither shown nor put
        // back, so "Cancelling..." stays in front of the reader.
        if (update.IsWait)
        {
            if (_scanCts?.IsCancellationRequested != true) _statusWait.Show(update);
            return;
        }

        if (update.IsMilestone)
        {
            _statusWait.Forget();
            ScanProgress = update.Message;
            ScanTicker = string.Empty;
        }
        else
        {
            ScanTicker = update.Message;
        }
    }

    /// <summary>The waits the scan reports, shown on <see cref="ScanProgress"/>.</summary>
    private readonly WaitLine _statusWait;

    [RelayCommand]
    private void CancelScan()
    {
        // Set the status before cancelling. The synchronous write updates
        // the overlay the instant Esc is pressed (the ScanAsync progress
        // reporter only fires on its next callback). Ordering it before
        // _scanCts.Cancel() also guarantees ScanAsync's own
        // "Scan cancelled." write lands after this one: cancelling first
        // leaves a window where the scan can complete and write
        // "Scan cancelled." before this line overwrites it with
        // "Cancelling...", harmless on the single UI thread but a race in
        // a SynchronizationContext-free unit test.
        ScanProgress = Strings.Status_Cancelling;
        try { _scanCts?.Cancel(); }
        catch (ObjectDisposedException) { /* scan already finished */ }
    }

    /// <summary>
    /// Splash-driven startup scan. Caller controls the progress
    /// reporter (it pipes to the splash UI) and the cancellation token
    /// (it ties to the splash Cancel button). Raises
    /// <see cref="ScanCompleted"/> on success so MainViewModel can
    /// trigger the all-clear path if appropriate.
    /// </summary>
    public async Task ScanWithProgressAsync(IProgress<ScanProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        LastScanWasCancelled = false;
        LastScanError = string.Empty;
        UnfinishedRefreshMessage = string.Empty;
        var sw = Stopwatch.StartNew();
        try
        {
            await RunScanCoreAsync(progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelling the splash leaves the app with no scan at all, and the
            // main window says so. Recording it here (the caller lets the
            // cancellation through to close the splash) is what lets that window
            // say why, rather than showing a bare "nothing scanned yet" to a user
            // who knows perfectly well they pressed Cancel.
            LastScanWasCancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            // A failed startup scan opens the main window in an error state rather
            // than exiting: record the tailored message through the one ladder,
            // shared with the Scan command, so the window's not-yet-scanned state
            // shows it with Re-scan focused. Do NOT rethrow: the exception then
            // propagates to App.OnStartup, which tells an already-elevated user to
            // run as administrator and exits. An app that diagnoses "your
            // installer database is empty" and then vanishes is strictly worse than
            // one that says it and offers Re-scan.
            //
            // No scan has completed before this one, so there is nothing to drop,
            // and the call is here so that every scan that fails ends the same way.
            var failure = DescribeScanFailure(ex);
            LastScanErrorTitle = failure.Title;
            LastScanError = failure.Message;
            DropResult();
            return;
        }
        sw.Stop();
        LastScanDurationMs = sw.ElapsedMilliseconds;
        ScanProgress = string.Format(Strings.Status_ScanComplete, DisplayHelpers.FormatElapsed(sw.Elapsed));
        ScanCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-checks the pending-reboot gate at the moment of action, off the
    /// dispatcher, and updates <see cref="PendingRebootResult"/> so the banner
    /// paints and the Move/Delete commands drop out through the existing
    /// <see cref="HasPendingReboot"/> wiring. Returns true when the gate now
    /// blocks. The scan samples the gate once; a Windows Installer transaction
    /// that starts after that sample, hours later if the window sits open, is
    /// invisible until this runs immediately before a Move or Delete acts.
    /// Mirrors the scan's own off-dispatcher Check() so a registry or mutex read
    /// cannot stall the UI thread.
    /// </summary>
    public async Task<bool> RecheckPendingRebootAsync()
    {
        var result = await Task.Run(() => _rebootService.Check());
        PendingRebootResult = result;
        return result.IsBlocked;
    }

    /// <summary>
    /// The silent scan Cleanup runs at the end of a Move or Delete. Skips the scan
    /// overlay (IsScanning stays false) so the operating overlay can stay visible
    /// until its own finally block clears it.
    ///
    /// <paramref name="cancellationToken"/> is what makes it interruptible, and
    /// it is not optional in practice: this is a full folder walk plus a full
    /// API enumeration on a folder that can hold millions of files, and the
    /// caller runs it behind an overlay the user has usually just pressed
    /// Cancel on. Without a token every checkpoint inside the scan is unreachable
    /// and the wait reads as a hang.
    ///
    /// It never throws, because its callers run it behind the operating overlay
    /// and go on to report their batch whatever it does. A scan that ends without
    /// a result, by a failure or by the user's cancel, drops the result on screen
    /// instead: that list describes the folder as it stood before the batch, so it
    /// is not a list the window may show or Move and Delete may act on. The
    /// summary card, where the batch shows one, is filled from the batch's own
    /// tally and is unaffected. The window says the scan did not finish, in
    /// <see cref="UnfinishedRefreshMessage"/>.
    /// </summary>
    /// <param name="progress">
    /// Told what the scan reports, for a caller that shows its waits on source folders
    /// (<see cref="ScanProgressUpdate.IsWait"/>) behind its own overlay.
    /// </param>
    public async Task RefreshAsync(
        CancellationToken cancellationToken = default, IProgress<ScanProgressUpdate>? progress = null)
    {
        LastScanWasCancelled = false;
        LastScanError = string.Empty;
        UnfinishedRefreshMessage = string.Empty;
        try
        {
            await RunScanCoreAsync(progress, cancellationToken);
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Not written to crash.log, unlike the failure below, because the
            // user asked for it. LastScanWasCancelled puts "Scan cancelled." under
            // the message, as it does in the window a cancelled startup scan
            // leaves.
            UnfinishedRefreshMessage = Strings.Body_RescanNotFinished_Why;
            LastScanWasCancelled = true;
            DropResult();
        }
        catch (Exception ex)
        {
            // The account goes to crash.log and the window names the file, in the
            // two forms DescribeDeliberateStop uses: a message naming a file that
            // was never written sends somebody looking for it.
            var crash = CrashLog.TryWrite(ex);
            UnfinishedRefreshMessage = crash.Written
                ? Strings.Body_RescanNotFinished_Why + Environment.NewLine + Environment.NewLine
                    + string.Format(Strings.Body_RescanNotFinished_Recorded, crash.Path)
                : Strings.Body_RescanNotFinished_Why;
            DropResult();
        }
    }
}
