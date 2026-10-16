using System.ComponentModel;
using NSubstitute;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using InstallerClean.ViewModels;

namespace InstallerClean.Tests.ViewModels;

/// <summary>
/// The update check's two paths. They call the same service and differ
/// entirely in what they are allowed to say: the automatic check runs
/// unasked at every launch, so it reports a newer version and nothing else,
/// while the manual check is a click that must always answer. A newer version
/// either finds shows the notice.
/// </summary>
public class ChromeViewModelTests
{
    // The one destination every update control opens; the found release's
    // own tag page is nowhere in the flow.
    private const string ReleasesPage = UpdateCheckService.ReleasesPageUrl;

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IMsiFileInfoService _msiInfoService = Substitute.For<IMsiFileInfoService>();
    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();
    private readonly IUpdateCheckService _updateCheckService = Substitute.For<IUpdateCheckService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly IFileSystemScanService _scanService = Substitute.For<IFileSystemScanService>();
    private readonly IPendingRebootService _rebootService = Substitute.For<IPendingRebootService>();

    /// <summary>
    /// Cooldown zeroed so a manual check runs to its end (including the
    /// clear that follows the spacing window) inside the awaited command
    /// rather than five real seconds later.
    /// </summary>
    private ChromeViewModel CreateViewModel(bool autoUpdateCheck = true)
    {
        _settingsService.Load().Returns(new AppSettings { AutoUpdateCheck = autoUpdateCheck });
        var scan = new ScanViewModel(_scanService, _rebootService, _dialogService);
        return new ChromeViewModel(
            _windowService, _msiInfoService, _settingsService,
            _updateCheckService, _dialogService, scan,
            checkCooldownOverride: TimeSpan.Zero);
    }

    /// <summary>
    /// Every value the status line was given, in order. The manual check's
    /// wording is transient by design (it clears once the cooldown is out),
    /// so the end state alone cannot show that anything was ever displayed.
    /// </summary>
    private static List<string> RecordStatusText(ChromeViewModel vm)
    {
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChromeViewModel.UpdateStatusText))
                seen.Add(vm.UpdateStatusText);
        };
        return seen;
    }

    private void CheckAnswers(params UpdateCheckResult[] results) =>
        _updateCheckService.CheckAsync(Arg.Any<UpdateCheckOrigin>(), Arg.Any<CancellationToken>())
            .Returns(results[0], results[1..]);

    [Fact]
    public async Task Automatic_check_opens_no_socket_when_the_setting_is_off()
    {
        var vm = CreateViewModel(autoUpdateCheck: false);

        await vm.RunAutomaticUpdateCheckAsync();

        await _updateCheckService.DidNotReceive().CheckAsync(Arg.Any<UpdateCheckOrigin>(), Arg.Any<CancellationToken>());
        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.False(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Automatic_check_shows_the_notice_when_a_newer_version_exists()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        var found = 0;
        vm.NewerVersionFound += (_, _) => found++;

        await vm.RunAutomaticUpdateCheckAsync();

        Assert.True(vm.HasNewerVersion);
        Assert.Equal(string.Format(Strings.UpdateNotice_Available, "9.9.9"), vm.UpdateNoticeText);
        Assert.Equal(1, found);
        // A check nobody asked for opens no window over whatever the user was
        // doing, and the status line beside the button stays empty.
        Assert.Equal(string.Empty, vm.UpdateStatusText);
        _dialogService.DidNotReceive().ShowWarning(Arg.Any<string>(), Arg.Any<string>());
        _windowService.DidNotReceive().OpenUrl(Arg.Any<string>());
    }

    [Fact]
    public async Task Automatic_check_says_nothing_when_it_fails()
    {
        CheckAnswers(new CheckFailed(UpdateCheckFailureReason.NetworkUnavailable));
        var vm = CreateViewModel();

        await vm.RunAutomaticUpdateCheckAsync();

        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.False(vm.HasNewerVersion);
        _dialogService.DidNotReceive().ShowWarning(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// The origin is what keeps a machine with no route to github.com from
    /// writing to crash.log at every launch, so which one each path passes
    /// is part of the contract rather than an implementation detail.
    /// </summary>
    [Fact]
    public async Task Automatic_check_tells_the_service_nobody_asked_for_it()
    {
        CheckAnswers(new CheckFailed(UpdateCheckFailureReason.NetworkUnavailable));
        var vm = CreateViewModel();

        await vm.RunAutomaticUpdateCheckAsync();

        await _updateCheckService.Received(1)
            .CheckAsync(UpdateCheckOrigin.Automatic, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manual_check_tells_the_service_the_user_is_waiting()
    {
        CheckAnswers(new CheckFailed(UpdateCheckFailureReason.NetworkUnavailable));
        var vm = CreateViewModel();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        await _updateCheckService.Received(1)
            .CheckAsync(UpdateCheckOrigin.Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Automatic_check_says_nothing_when_the_build_is_current()
    {
        CheckAnswers(new UpToDate("2.1.0"));
        var vm = CreateViewModel();

        await vm.RunAutomaticUpdateCheckAsync();

        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.False(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Manual_check_reports_up_to_date_then_clears_it()
    {
        CheckAnswers(new UpToDate("2.1.0"));
        var vm = CreateViewModel();
        var seen = RecordStatusText(vm);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains(Strings.UpdateCheck_Status_Checking, seen);
        Assert.Contains(Strings.UpdateCheck_Status_UpToDate, seen);
        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.False(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Manual_check_shows_the_notice_when_a_newer_version_exists()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel(autoUpdateCheck: false);
        var seen = RecordStatusText(vm);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains(Strings.UpdateCheck_Status_Checking, seen);
        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.True(vm.HasNewerVersion);
        Assert.Equal(string.Format(Strings.UpdateNotice_Available, "9.9.9"), vm.UpdateNoticeText);
        _windowService.DidNotReceive().OpenUrl(Arg.Any<string>());
        _dialogService.DidNotReceive().ShowWarning(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// A click that finds the version the notice already names changes nothing on
    /// screen, so the event is the only answer the window can read out for it.
    /// </summary>
    [Fact]
    public async Task Manual_check_answers_a_click_that_finds_the_version_the_notice_names()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"), new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        var found = 0;
        vm.NewerVersionFound += (_, _) => found++;
        await vm.RunAutomaticUpdateCheckAsync();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(2, found);
        Assert.True(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Manual_check_that_fails_keeps_the_notice()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"), new CheckFailed(UpdateCheckFailureReason.Timeout));
        var vm = CreateViewModel();
        await vm.RunAutomaticUpdateCheckAsync();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _dialogService.Received(1).ShowWarning(
            Strings.UpdateCheck_Failed_Timeout, Strings.UpdateCheck_Title);
        Assert.True(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Manual_check_that_finds_this_build_current_takes_the_notice_down()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"), new UpToDate("2.1.0"));
        var vm = CreateViewModel();
        await vm.RunAutomaticUpdateCheckAsync();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(vm.HasNewerVersion);
    }

    [Fact]
    public async Task Manual_check_shows_a_warning_when_it_fails()
    {
        CheckAnswers(new CheckFailed(UpdateCheckFailureReason.Timeout));
        var vm = CreateViewModel();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _dialogService.Received(1).ShowWarning(
            Strings.UpdateCheck_Failed_Timeout, Strings.UpdateCheck_Title);
        Assert.Equal(string.Empty, vm.UpdateStatusText);
        Assert.False(vm.HasNewerVersion);
    }

    [Fact]
    public void OpenReleasePage_does_nothing_before_a_check_has_found_one()
    {
        var vm = CreateViewModel();

        vm.OpenReleasePageCommand.Execute(null);

        _windowService.DidNotReceive().OpenUrl(Arg.Any<string>());
    }

    /// <summary>
    /// The CanExecute coupling is what keeps the hidden link out of the main
    /// window's Tab order while there is no notice (the why is on
    /// OpenReleasePage itself), and the CanExecuteChanged raise is how the link
    /// learns to re-query it, so both are contract rather than convenience.
    /// </summary>
    [Fact]
    public async Task OpenReleasePage_cannot_execute_until_a_check_has_found_one()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        var raised = 0;
        vm.OpenReleasePageCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(vm.OpenReleasePageCommand.CanExecute(null));

        await vm.RunAutomaticUpdateCheckAsync();

        Assert.True(vm.OpenReleasePageCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task OpenReleasePage_opens_the_releases_page_after_the_automatic_check()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        await vm.RunAutomaticUpdateCheckAsync();

        vm.OpenReleasePageCommand.Execute(null);

        _windowService.Received(1).OpenUrl(ReleasesPage);
    }

    // ---- Update now ----

    private readonly IAppUpdater _updater = Substitute.For<IAppUpdater>();
    private readonly List<string> _calls = [];

    /// <summary>
    /// A view model whose copy can update itself, with the settling step recorded in
    /// the order it is called, and a check that has found 9.9.9.
    /// </summary>
    private async Task<ChromeViewModel> CreateUpdatingViewModelAsync()
    {
        _updater.CanUpdateItself.Returns(true);
        _settingsService.Load().Returns(new AppSettings { AutoUpdateCheck = true });
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var scan = new ScanViewModel(_scanService, _rebootService, _dialogService);
        var vm = new ChromeViewModel(
            _windowService, _msiInfoService, _settingsService,
            _updateCheckService, _dialogService, scan,
            checkCooldownOverride: TimeSpan.Zero,
            updater: _updater,
            settleBeforeHandOff: () => _calls.Add("settle"));
        await vm.RunAutomaticUpdateCheckAsync();
        return vm;
    }

    private PreparedUpdate Prepared(Action? undo = null) =>
        new(@"C:\ProgramData\InstallerClean-update-x\InstallerClean-9.9.9-setup.exe", "/SILENT /UPDATEFROMAPP",
            undo ?? (() => _calls.Add("undo")));

    [Fact]
    public async Task A_copy_that_can_update_itself_shows_Update_now_and_not_the_link()
    {
        var vm = await CreateUpdatingViewModelAsync();

        Assert.True(vm.ShowsUpdateNow);
        Assert.False(vm.ShowsReleaseLink);
        Assert.True(vm.UpdateNowCommand.CanExecute(null));
        Assert.False(vm.OpenReleasePageCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_copy_that_cannot_update_itself_shows_the_link_alone()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        await vm.RunAutomaticUpdateCheckAsync();

        Assert.False(vm.ShowsUpdateNow);
        Assert.True(vm.ShowsReleaseLink);
        Assert.False(vm.UpdateNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task Update_now_downloads_the_found_version_settles_and_hands_over()
    {
        var vm = await CreateUpdatingViewModelAsync();
        var prepared = Prepared();
        _updater.PrepareAsync("9.9.9", Arg.Any<IProgress<double?>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _calls.Add("prepare"); return prepared; });
        _windowService.StartUpdateAndCloseAsync(prepared.FileToStart, prepared.Arguments)
            .Returns(_ => { _calls.Add("start"); return true; });

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(["prepare", "settle", "start"], _calls);
        // Handed over, the app is closing, and the card stays up until it has.
        Assert.True(vm.IsUpdating);
        Assert.False(vm.HasUpdateFailure);
        Assert.False(vm.CancelUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_card_names_the_version_being_downloaded()
    {
        var vm = await CreateUpdatingViewModelAsync();
        var seen = new List<string>();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(_ =>
        {
            seen.Add(vm.UpdatingText);
            return Task.FromException<PreparedUpdate>(new UpdateFailedException(UpdateFailureStage.Download, "no"));
        });

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal([string.Format(Strings.UpdateNotice_Downloading, "9.9.9")], seen);
    }

    [Fact]
    public async Task A_start_that_fails_puts_everything_back_and_says_it_could_not_be_installed()
    {
        var vm = await CreateUpdatingViewModelAsync();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(Prepared());
        _windowService.StartUpdateAndCloseAsync(default!, default!).ReturnsForAnyArgs(false);

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Contains("undo", _calls);
        Assert.False(vm.IsUpdating);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, vm.UpdateFailureText);
        // Update now stays, so the person can try again, and the link joins it.
        Assert.True(vm.ShowsUpdateNow);
        Assert.True(vm.ShowsReleaseLink);
        Assert.True(vm.OpenReleasePageCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download)]
    [InlineData(UpdateFailureStage.Install)]
    public async Task A_failed_step_says_which_and_never_starts_anything(UpdateFailureStage stage)
    {
        var vm = await CreateUpdatingViewModelAsync();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(
            Task.FromException<PreparedUpdate>(new UpdateFailedException(stage, "no")));

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdating);
        Assert.Equal(
            stage == UpdateFailureStage.Download ? Strings.UpdateNotice_DownloadFailed : Strings.UpdateNotice_InstallFailed,
            vm.UpdateFailureText);
        await _windowService.DidNotReceiveWithAnyArgs().StartUpdateAndCloseAsync(default!, default!);
        Assert.DoesNotContain("settle", _calls);
    }

    [Fact]
    public async Task Cancel_takes_the_card_away_and_says_nothing()
    {
        var vm = await CreateUpdatingViewModelAsync();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            var token = call.Arg<CancellationToken>();
            Assert.True(vm.CancelUpdateCommand.CanExecute(null));
            vm.CancelUpdateCommand.Execute(null);
            return Task.FromCanceled<PreparedUpdate>(token);
        });

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdating);
        Assert.False(vm.HasUpdateFailure);
        await _windowService.DidNotReceiveWithAnyArgs().StartUpdateAndCloseAsync(default!, default!);
    }

    [Fact]
    public async Task A_new_press_clears_the_line_from_the_last_failure()
    {
        var vm = await CreateUpdatingViewModelAsync();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(
            Task.FromException<PreparedUpdate>(new UpdateFailedException(UpdateFailureStage.Download, "no")),
            Task.FromResult(Prepared()));
        _windowService.StartUpdateAndCloseAsync(default!, default!).ReturnsForAnyArgs(true);

        await vm.UpdateNowCommand.ExecuteAsync(null);
        Assert.True(vm.HasUpdateFailure);

        await vm.UpdateNowCommand.ExecuteAsync(null);
        Assert.False(vm.HasUpdateFailure);
    }

    [Fact]
    public async Task Update_now_waits_while_a_scan_or_a_Move_or_Delete_is_in_flight()
    {
        _updater.CanUpdateItself.Returns(true);
        _settingsService.Load().Returns(new AppSettings { AutoUpdateCheck = true });
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var busy = true;
        var vm = new ChromeViewModel(
            _windowService, _msiInfoService, _settingsService,
            _updateCheckService, _dialogService, new ScanViewModel(_scanService, _rebootService, _dialogService),
            isBusy: () => busy, checkCooldownOverride: TimeSpan.Zero, updater: _updater);
        await vm.RunAutomaticUpdateCheckAsync();

        Assert.False(vm.UpdateNowCommand.CanExecute(null));
        busy = false;
        Assert.True(vm.UpdateNowCommand.CanExecute(null));
    }

    // ---- The card saying a newer version is available ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_automatic_check_s_task_completes_whatever_the_check_answers(bool found)
    {
        CheckAnswers(found
            ? new UpdateAvailable("2.1.0", "9.9.9")
            : new CheckFailed(UpdateCheckFailureReason.Timeout));
        var vm = CreateViewModel();

        vm.StartAutomaticUpdateCheck();
        await vm.AutomaticCheck;

        Assert.Equal(found, vm.HasNewerVersion);
    }

    [Fact]
    public async Task The_automatic_check_s_task_completes_with_the_check_switched_off()
    {
        var vm = CreateViewModel(autoUpdateCheck: false);

        vm.StartAutomaticUpdateCheck();
        await vm.AutomaticCheck;

        await _updateCheckService.DidNotReceive().CheckAsync(Arg.Any<UpdateCheckOrigin>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_automatic_check_s_task_completes_when_a_manual_check_cancels_it()
    {
        var calls = 0;
        _updateCheckService.CheckAsync(Arg.Any<UpdateCheckOrigin>(), Arg.Any<CancellationToken>())
            .Returns(call => ++calls == 1
                ? NeverAnswersAsync(call.Arg<CancellationToken>())
                : Task.FromResult<UpdateCheckResult>(new UpToDate("2.1.0")));
        var vm = CreateViewModel();

        vm.StartAutomaticUpdateCheck();
        Assert.False(vm.AutomaticCheck.IsCompleted);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        await vm.AutomaticCheck;
        Assert.True(vm.AutomaticCheck.IsCompletedSuccessfully);
    }

    private static async Task<UpdateCheckResult> NeverAnswersAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("unreachable");
    }

    [Fact]
    public void The_card_is_not_put_up_before_a_check_has_found_a_newer_version()
    {
        var vm = CreateViewModel();

        vm.OfferUpdate();

        Assert.False(vm.IsOfferingUpdate);
        Assert.False(vm.ContinueWithoutUpdatingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Continue_without_updating_takes_the_card_away_and_keeps_the_notice()
    {
        CheckAnswers(new UpdateAvailable("2.1.0", "9.9.9"));
        var vm = CreateViewModel();
        await vm.RunAutomaticUpdateCheckAsync();

        vm.OfferUpdate();
        Assert.True(vm.IsOfferingUpdate);
        Assert.True(vm.ContinueWithoutUpdatingCommand.CanExecute(null));

        vm.ContinueWithoutUpdatingCommand.Execute(null);

        Assert.False(vm.IsOfferingUpdate);
        Assert.False(vm.ContinueWithoutUpdatingCommand.CanExecute(null));
        Assert.True(vm.HasNewerVersion);
    }

    [Fact]
    public async Task An_update_that_fails_from_the_card_leaves_the_card_up_with_the_failure_and_the_link()
    {
        var vm = await CreateUpdatingViewModelAsync();
        vm.OfferUpdate();
        _updater.PrepareAsync(default!, default, default).ReturnsForAnyArgs(
            Task.FromException<PreparedUpdate>(new UpdateFailedException(UpdateFailureStage.Download, "no")));

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.True(vm.IsOfferingUpdate);
        Assert.Equal(Strings.UpdateNotice_DownloadFailed, vm.UpdateFailureText);
        Assert.True(vm.ShowsUpdateNow);
        Assert.True(vm.ShowsReleaseLink);
        Assert.True(vm.ContinueWithoutUpdatingCommand.CanExecute(null));
    }
}
