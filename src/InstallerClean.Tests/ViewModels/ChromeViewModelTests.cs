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
}
