using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.ViewModels;

/// <summary>
/// Window-management slice. Holds the commands wired to the chrome
/// (About / Donate / Close), the two "open the details
/// window" commands that take the user from the main scan summary
/// into the registered- or orphaned-files detail windows, and the
/// update check (the manual button and the once-per-session
/// automatic check behind <c>AppSettings.AutoUpdateCheck</c>).
///
/// Reads <see cref="ScanViewModel.LastScanResult"/> for the details
/// commands so the detail windows always show the same scan the main
/// window is currently summarising. The details commands' CanExecute
/// reflects whether the scan view model holds a result, so the buttons are
/// disabled (greyed out by the pill styles' IsEnabled trigger) before the
/// first scan completes and at each ending <see cref="ScanViewModel.HasScanned"/>
/// lists.
/// </summary>
public partial class ChromeViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Minimum spacing between successive update checks from the manual
    /// button. Only bites if something is hammering the button: a
    /// hand-driven user clicks once and reads the result. Five seconds
    /// keeps a stuck button or a UI-automation loop from running an
    /// elevated process into GitHub's abuse protection, which answers a
    /// caller with retry-after rather than a version. The command stays
    /// non-executable for the duration, so the pill's disabled paint
    /// doubles as the feedback.
    /// </summary>
    private static readonly TimeSpan CheckForUpdatesCooldown = TimeSpan.FromSeconds(5);

    private readonly IWindowService _windowService;
    private readonly IMsiFileInfoService _msiInfoService;
    private readonly ISettingsService _settings;
    private readonly IUpdateCheckService _updateCheck;
    private readonly IDialogService _dialogs;
    private readonly ScanViewModel _scan;
    private readonly Func<bool> _isBusy;
    private readonly TimeSpan _checkCooldown;
    private readonly PropertyChangedEventHandler _scanHandler;
    private CancellationTokenSource? _updateCts;

    /// <param name="isBusy">
    /// MainViewModel.IsBusy: a scan or a Move/Delete in flight. Null in a bare
    /// test construction (never busy).
    /// </param>
    /// <param name="checkCooldownOverride">
    /// Replaces <see cref="CheckForUpdatesCooldown"/>. Exists so a test can
    /// run the manual check to completion without waiting out five real
    /// seconds; production callers pass nothing.
    /// </param>
    public ChromeViewModel(
        IWindowService windowService,
        IMsiFileInfoService msiInfoService,
        ISettingsService settings,
        IUpdateCheckService updateCheck,
        IDialogService dialogs,
        ScanViewModel scan,
        Func<bool>? isBusy = null,
        TimeSpan? checkCooldownOverride = null)
    {
        _windowService = windowService;
        _msiInfoService = msiInfoService;
        _settings = settings;
        _updateCheck = updateCheck;
        _dialogs = dialogs;
        _scan = scan;
        _isBusy = isBusy ?? (() => false);
        _checkCooldown = checkCooldownOverride ?? CheckForUpdatesCooldown;

        // Re-evaluate the Details buttons when a scan finishes or its result
        // is dropped. HasScanned is observable; LastScanResult is a plain
        // auto-property and won't raise PropertyChanged, which is why the scan
        // view model drops the result before it sets HasScanned false. Held as
        // a field so Dispose can unhook it; the singleton container disposes
        // this VM on shutdown.
        _scanHandler = OnScanPropertyChanged;
        _scan.PropertyChanged += _scanHandler;
    }

    private void OnScanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanViewModel.HasScanned))
        {
            OpenOrphanedDetailsCommand.NotifyCanExecuteChanged();
            OpenRegisteredDetailsCommand.NotifyCanExecuteChanged();
        }
        // A clean-up empties the orphan list without a fresh HasScanned raise
        // (a post-operation refresh that completes only moves the counts), so
        // the orphaned Details button needs this one to grey out behind the
        // completion overlay rather than sit live over an empty list.
        else if (e.PropertyName == nameof(ScanViewModel.HasOrphans))
        {
            OpenOrphanedDetailsCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        _scan.PropertyChanged -= _scanHandler;
        // Releases a check awaiting the network and a manual check parked
        // in its cooldown; both paths treat the cancellation as "say
        // nothing and stop".
        _updateCts?.Cancel();
        _updateCts?.Dispose();
    }

    /// <summary>
    /// The line beside the update button: "Checking..." while the manual check
    /// runs, and "Up to date." when it answers that way. Empty at rest, which
    /// collapses the bound TextBlock.
    /// </summary>
    [ObservableProperty] private string _updateStatusText = string.Empty;

    /// <summary>
    /// The notice's first line, naming the newer version a check found. Set before
    /// <see cref="HasNewerVersion"/>, so the line is whole by the time the notice
    /// shows.
    /// </summary>
    [ObservableProperty] private string _updateNoticeText = string.Empty;

    /// <summary>
    /// Set once a check has found a newer version, and held for the session. It
    /// shows the notice at the top of the main window and on the card a scan puts
    /// up when it offers nothing. A later check failing leaves it set; only a check
    /// answering that this build is the newest clears it. A bool because the
    /// bindings need the positive form of the test: a XAML DataTrigger tests
    /// equality only.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReleasePageCommand))]
    private bool _hasNewerVersion;

    /// <summary>
    /// Raised each time a check finds a newer version, whether or not the notice
    /// was already showing, so the window can read the notice out as the answer to
    /// a click that found the version it already names.
    /// </summary>
    public event EventHandler? NewerVersionFound;

    /// <summary>
    /// Starts the once-per-session automatic check. Called by the App as
    /// soon as the view-model exists, before the startup scan is awaited,
    /// so the answer is usually already painted when the window first
    /// shows: a typical session lasts seconds, and a check that arrived
    /// later would routinely arrive after the user had gone.
    /// Fire-and-forget because nothing downstream awaits it and every
    /// outcome but "newer version found" is deliberately invisible; every
    /// exception is swallowed, so it cannot disturb the splash path. The
    /// enabling setting is read from disk inside the call, before anything
    /// touches the network, so an install that has opted out opens no
    /// socket. That read happens on this startup path, before the main
    /// window and with it the About checkbox exist, so unticking the box
    /// governs the next launch rather than this one.
    /// </summary>
    public void StartAutomaticUpdateCheck() => _ = RunAutomaticUpdateCheckAsync();

    /// <summary>
    /// The automatic check's body. Internal rather than private so
    /// ChromeViewModelTests can await the one path production deliberately
    /// leaves unawaited; polling the properties after
    /// <see cref="StartAutomaticUpdateCheck"/> would be a race dressed as a
    /// test.
    /// </summary>
    internal async Task RunAutomaticUpdateCheckAsync()
    {
        var token = ResetUpdateCheckToken();
        try
        {
            if (!_settings.Load().AutoUpdateCheck)
                return;
            var result = await _updateCheck.CheckAsync(UpdateCheckOrigin.Automatic, token);
            if (result is UpdateAvailable available)
                ShowUpdateFound(available);
            // UpToDate and CheckFailed both end in silence: an automatic
            // check the user never asked to watch has nothing to report
            // but a newer version. The Automatic origin extends that
            // silence to crash.log, which is where an unreachable github.com
            // would otherwise leave a trace once per launch.
        }
        catch (OperationCanceledException)
        {
            // Superseded by a manual click, or the app is closing.
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }

    /// <summary>
    /// The manual check. Same service call as the automatic path, but
    /// every outcome is shown: the status line while checking and on
    /// up-to-date, the notice on a find, a warning dialog on a failure. The
    /// command's own execution covers the cooldown, so the button stays
    /// disabled for the spacing window.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var token = ResetUpdateCheckToken();
        UpdateStatusText = Strings.UpdateCheck_Status_Checking;

        UpdateCheckResult result;
        try
        {
            result = await _updateCheck.CheckAsync(UpdateCheckOrigin.Manual, token);
        }
        catch (OperationCanceledException)
        {
            // App closing; the window is on its way out, leave the text be.
            return;
        }

        switch (result)
        {
            case UpToDate:
                HasNewerVersion = false;
                UpdateStatusText = Strings.UpdateCheck_Status_UpToDate;
                break;

            case UpdateAvailable available:
                UpdateStatusText = string.Empty;
                ShowUpdateFound(available);
                break;

            case CheckFailed failed:
                UpdateStatusText = string.Empty;
                _dialogs.ShowWarning(FailureReasonText(failed), Strings.UpdateCheck_Title);
                break;
        }

        try
        {
            await Task.Delay(_checkCooldown, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // "Up to date." clears once read; the notice a find shows stays.
        if (UpdateStatusText == Strings.UpdateCheck_Status_UpToDate)
            UpdateStatusText = string.Empty;
    }

    /// <summary>
    /// Opens the releases page; bound to the link on the notice, in the main
    /// window and on the card. The CanExecute is the link's tab-order gate, not
    /// politeness: WPF's keyboard navigation descends into a collapsed TextBlock
    /// regardless of visibility (a TextBlock is an IContentHost) and admits a
    /// content element on Focusable, IsTabStop and IsEnabled alone, content
    /// elements having no IsVisible to test, so an always-executable command
    /// leaves the hidden link an invisible extra Tab stop. Disabled while there is
    /// no notice, it drops out of the walk; once the notice shows it is enabled
    /// and a real stop. The body's guard stays because ICommand.Execute does not
    /// consult CanExecute.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasNewerVersion))]
    private void OpenReleasePage()
    {
        if (HasNewerVersion)
            _windowService.OpenUrl(UpdateCheckService.ReleasesPageUrl);
    }

    private void ShowUpdateFound(UpdateAvailable available)
    {
        UpdateNoticeText = string.Format(Strings.UpdateNotice_Available, available.LatestVersion);
        HasNewerVersion = true;
        NewerVersionFound?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Swap-then-dispose, matching CleanupViewModel.ScheduleMoveDestinationSave:
    /// cancelling the previous source releases whichever check is parked on it
    /// (the automatic check awaiting the network, most often, since a manual
    /// click can land while it is still in flight) without leaking one CTS per
    /// supersession.
    /// </summary>
    private CancellationToken ResetUpdateCheckToken()
    {
        var previous = _updateCts;
        _updateCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();
        return _updateCts.Token;
    }

    /// <summary>
    /// The sentence for a failed check. Four of the five reasons are the reason
    /// alone; the fifth offers the crash log, so it takes the whole result and
    /// reads the path off it. A path is there only where this check wrote an entry
    /// and the write succeeded, so the sibling naming the file is chosen by whether
    /// the file exists to be named rather than by which branch ran.
    /// </summary>
    private static string FailureReasonText(CheckFailed failed) => failed.ReasonCode switch
    {
        UpdateCheckFailureReason.NetworkUnavailable => Strings.UpdateCheck_Failed_NetworkUnavailable,
        UpdateCheckFailureReason.ServerError => Strings.UpdateCheck_Failed_ServerError,
        UpdateCheckFailureReason.ResponseParseError => Strings.UpdateCheck_Failed_ResponseParseError,
        UpdateCheckFailureReason.Timeout => Strings.UpdateCheck_Failed_Timeout,
        _ => failed.CrashLogPath is { } path
            ? string.Format(Strings.UpdateCheck_Failed_Unknown, path)
            : Strings.UpdateCheck_Failed_Unknown_NoLog,
    };

    private bool HasScanResult => _scan.LastScanResult is not null;

    // The orphaned-files window lists what the scan found. With nothing found
    // there is nothing to list, and a live button onto an empty list is a dead
    // end; the registered window, which after any scan the app will accept has
    // content, keeps the plain has-a-result gate; the window itself no longer
    // depends on that.
    private bool HasOrphansToShow => HasScanResult && _scan.HasOrphans;

    [RelayCommand(CanExecute = nameof(HasOrphansToShow))]
    private void OpenOrphanedDetails()
    {
        if (_scan.LastScanResult is null) return;

        var viewModel = new OrphanedFilesViewModel(
            _scan.LastScanResult.RemovableFiles,
            _msiInfoService);

        _windowService.ShowOrphanedDetails(viewModel);
    }

    [RelayCommand(CanExecute = nameof(HasScanResult))]
    private void OpenRegisteredDetails()
    {
        if (_scan.LastScanResult is null) return;

        var viewModel = new RegisteredFilesViewModel(
            _scan.LastScanResult.RegisteredPackages,
            _scan.LastScanResult.RegisteredTotalBytes,
            _msiInfoService,
            // The withheld files, so this window's own header counts the same two
            // populations the main window's left-alone line does. The two are one
            // click apart and must not disagree.
            _scan.LastScanResult.WithheldFiles);

        _windowService.ShowRegisteredDetails(viewModel);
    }

    [RelayCommand]
    private void ShowAbout() => _windowService.ShowAbout();

    // Bound to the small Donate under Done on the completion card, which leaves
    // the card up. The card's large Donate button goes through CompletionViewModel,
    // which closes the card as well.
    [RelayCommand]
    private void Donate() => _windowService.OpenUrl(SupportLink.Url);

    [RelayCommand]
    private void CloseApp() => _windowService.CloseMainWindow();

    // Invoked by the bottom-bar language menu with a culture name
    // ("en-GB", "it"). Re-picking the language already on screen is a no-op,
    // so it does not pointlessly restart; the comparison is against the
    // DISPLAYED language (SupportedLanguages.Active), not the saved setting
    // (which this write changes) nor an explicit override alone (a default
    // install follows the OS with no override, yet still shows a language).
    // A real change is saved and applied by a relaunch, because the resx
    // strings resolve once when each window is built and do not re-read a
    // culture swapped at runtime.
    [RelayCommand]
    private void SetLanguage(string? culture)
    {
        if (string.Equals(culture, SupportedLanguages.Active(Localisation.UiCulture), StringComparison.OrdinalIgnoreCase))
            return;
        // Refuse a relaunch while a scan or a Move/Delete is in flight: the
        // relaunch tears the process down and the child cannot inherit the running
        // operation, so it would end exactly like closing the window mid-move. The
        // bottom-nav globe is already disabled while the scanning or operating
        // overlay is up, but a Move/Delete pre-flight runs with the overlay not yet
        // shown, so this is the belt that covers that window.
        if (_isBusy())
            return;
        _settings.Update(s => s.Language = culture);
        _windowService.RelaunchForLanguageChange();
    }
}
