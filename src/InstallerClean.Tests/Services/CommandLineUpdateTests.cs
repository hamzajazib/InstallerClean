using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The command line's update before its scan: what it asks, what it runs, and what it
/// leaves behind when a part fails. The console, the mutex and the processes are a
/// recording host; the running exe is a real file in a temporary folder, since the
/// setup's copy renames it aside.
/// </summary>
public sealed class CommandLineUpdateTests : IDisposable
{
    private const string Current = "3.4.0";
    private const string Latest = "3.5.0";

    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ic-cli-update-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly string _exe;
    private readonly RecordingHost _host;

    public CommandLineUpdateTests()
    {
        _exe = Path.Combine(_folder, "installerclean-cli.exe");
        _host = new RecordingHost(_exe);
        File.WriteAllText(_exe, "old");
        _host.Versions[_exe] = new Version(3, 4, 0, 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static Func<CancellationToken, Task<UpdateCheckResult>> Answers(UpdateCheckResult result) =>
        _ => Task.FromResult(result);

    private static readonly Func<CancellationToken, Task<UpdateCheckResult>> NewerOut =
        Answers(new UpdateAvailable(Current, Latest));

    private CommandLineUpdateResult Run(
        FakeUpdater updater, bool canAsk, string command = "/s", string[]? arguments = null,
        Func<CancellationToken, Task<UpdateCheckResult>>? check = null, CancellationToken token = default) =>
        new CommandLineUpdate(check ?? NewerOut, updater, _host, TimeSpan.FromMilliseconds(200))
            .Run(canAsk, command, arguments ?? [command], _exe, token);

    /// <summary>The separate download: the updater has put the newer file at the exe's path.</summary>
    private FakeUpdater Download() => new(AppCopyKind.CommandLineDownload, _exe, onPrepare: () =>
    {
        File.WriteAllText(_exe, "new");
        _host.Versions[_exe] = new Version(3, 5, 0, 0);
    });

    private FakeUpdater Setup() => new(AppCopyKind.Setup, Path.Combine(_folder, "InstallerClean-3.5.0-setup.exe"));

    /// <summary>The setup writes the newer command line at the exe's path and exits with 0.</summary>
    private void SetupInstalls()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Versions[_exe] = new Version(3, 5, 0, 0);
            return 0;
        };
    }

    [Fact]
    public void Up_to_date_carries_on_and_prints_nothing()
    {
        var updater = Download();

        var result = Run(updater, canAsk: true, check: Answers(new UpToDate(Current)));

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void A_failed_check_carries_on_and_prints_nothing()
    {
        var result = Run(Download(), canAsk: false,
            check: Answers(new CheckFailed(UpdateCheckFailureReason.NetworkUnavailable)));

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
    }

    [Fact]
    public void A_watched_check_that_outruns_its_bound_carries_on_and_prints_nothing()
    {
        var result = Run(Download(), canAsk: true,
            check: async ct => { await Task.Delay(Timeout.Infinite, ct); return new UpToDate(Current); });

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("y")]
    [InlineData("Yes")]
    [InlineData("  Y  ")]
    public void A_watched_yes_installs_and_runs_the_newer_version_as_a_scan(string answer)
    {
        _host.Answers.Enqueue(answer);
        _host.NewerVersionExitCode = 0;

        var result = Run(Download(), canAsk: true);

        var handedOver = Assert.IsType<CommandLineUpdateResult.HandedOver>(result);
        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), handedOver);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.UpdateNotice_Recommended,
            Prompt(),
            string.Format(Strings.UpdateNotice_Downloading, Latest),
            string.Format(Strings.Cli_UpdateInstalled, Latest),
        }, _host.Output);
        var run = _host.NewerVersionRun!.Value;
        Assert.Equal(_exe, run.File);
        Assert.Equal(new[] { "/s" }, run.Arguments);
        Assert.Equal((Current, Latest), (run.From, run.To));
    }

    [Fact]
    public void The_newer_versions_exit_code_is_the_runs()
    {
        _host.Answers.Enqueue("");
        _host.NewerVersionExitCode = 130;

        var result = Run(Download(), canAsk: true);

        Assert.Equal(130, Assert.IsType<CommandLineUpdateResult.HandedOver>(result).ExitCode);
    }

    [Theory]
    [InlineData("/d", "Cli.UpdateListsOnlyAfterDelete")]
    [InlineData("/m", "Cli.UpdateListsOnlyAfterMove")]
    public void A_watched_yes_after_a_delete_or_a_move_says_the_scan_only_lists_before_it_starts(
        string command, string line)
    {
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: true, command: command, arguments: [command, "D:\\Backup"]);

        Assert.Equal(Strings.Get(line), _host.Output[^1]);
        Assert.Equal(new[] { "/s" }, _host.NewerVersionRun!.Value.Arguments);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("No")]
    [InlineData(null)]
    public void A_watched_no_or_the_end_of_input_carries_on_with_nothing_downloaded(string? answer)
    {
        _host.Answers.Enqueue(answer);
        var updater = Download();

        var result = Run(updater, canAsk: true);

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.False(updater.Prepared);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void Any_other_answer_asks_again()
    {
        _host.Answers.Enqueue("maybe");
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: true);

        Assert.Equal(2, _host.Output.Count(line => line == Prompt()));
    }

    [Fact]
    public void Nobody_watching_is_asked_nothing_and_the_newer_version_runs_the_command_as_given()
    {
        _host.NewerVersionExitCode = 2;

        var result = Run(Download(), canAsk: false, command: "/m", arguments: ["/m", "D:\\My Backup"]);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(2, Current, Latest, "/m D:\\My Backup"), result);
        Assert.Equal(new[] { "/m", "D:\\My Backup" }, _host.NewerVersionRun!.Value.Arguments);
        Assert.Equal(0, _host.Reads);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            string.Format(Strings.UpdateNotice_Downloading, Latest),
            string.Format(Strings.Cli_UpdateInstalled, Latest),
        }, _host.Output);
    }

    [Fact]
    public void Ctrl_C_at_the_question_cancels_with_nothing_downloaded()
    {
        using var cts = new CancellationTokenSource();
        _host.OnRead = () => { cts.Cancel(); return null; };
        var updater = Download();

        var result = Run(updater, canAsk: true, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void Ctrl_C_during_the_download_cancels_and_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, _exe, onPrepare: () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        });

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(_host.HoldsLock);
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download, "UpdateNotice.DownloadFailed")]
    [InlineData(UpdateFailureStage.Install, "UpdateNotice.InstallFailed")]
    public void A_failed_download_or_install_says_so_and_carries_on_with_this_version(
        UpdateFailureStage stage, string line)
    {
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, _exe,
            onPrepare: () => throw new UpdateFailedException(stage, "failed"));

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(stage, Current, Latest), result);
        Assert.Equal(Strings.Get(line), _host.Output[^2]);
        Assert.Equal(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output[^1]);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(_host.HoldsLock);
    }

    [Fact]
    public void A_download_that_does_not_carry_the_version_it_was_downloaded_as_is_put_back_and_never_run()
    {
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, _exe, onPrepare: () =>
            _host.Versions[_exe] = new Version(3, 4, 0, 0));

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.True(updater.Undone);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_newer_version_that_will_not_start_is_put_back_and_the_run_carries_on()
    {
        _host.NewerVersionExitCode = null;
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.True(updater.Undone);
    }

    [Fact]
    public void The_separate_download_goes_ahead_where_the_mutex_is_held_elsewhere()
    {
        _host.LockAvailable = false;
        _host.NewerVersionExitCode = 0;

        var result = Run(Download(), canAsk: false);

        Assert.IsType<CommandLineUpdateResult.HandedOver>(result);
    }

    [Fact]
    public void The_setups_copy_fails_before_any_download_where_the_mutex_is_held_elsewhere()
    {
        _host.LockAvailable = false;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.False(updater.Prepared);
        Assert.Null(_host.SetupRun);
    }

    [Fact]
    public void The_setups_copy_runs_the_setup_with_this_exe_renamed_aside_and_no_mutex_held()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.IsType<CommandLineUpdateResult.HandedOver>(result);
        Assert.Equal(updater.FileToStart, _host.SetupRun!.Value.File);
        Assert.Equal(new[] { "/VERYSILENT", "/SP-" }, _host.SetupRun!.Value.Arguments);
        Assert.False(_host.SetupRun!.Value.HeldLock);
        Assert.True(_host.SetupRun!.Value.ExeWasAside);
        Assert.Equal("old", File.ReadAllText(_exe + SelfReplacement.OldSuffix));
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.True(updater.Undone);
        Assert.Equal(_exe, _host.NewerVersionRun!.Value.File);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void A_setup_that_fails_or_will_not_start_puts_this_exe_back(int? exitCode)
    {
        _host.OnSetup = () => exitCode;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(updater.Undone);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_setup_that_reports_success_without_the_newer_command_line_is_never_run()
    {
        _host.OnSetup = () => 0;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void Ctrl_C_while_the_setup_runs_stops_the_run_before_the_newer_version_starts()
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Versions[_exe] = new Version(3, 5, 0, 0);
            cts.Cancel();
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_build_from_source_names_the_release_page_and_asks_nothing()
    {
        var updater = new FakeUpdater(AppCopyKind.FromSource, _exe);

        var result = Run(updater, canAsk: true);

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(0, _host.Reads);
        Assert.False(updater.Prepared);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.UpdateNotice_Recommended,
            string.Format(Strings.Cli_UpdateDownloadLink, UpdateCheckService.ReleasesPageUrl),
        }, _host.Output);
    }

    [Fact]
    public void The_mutex_is_taken_and_released_on_the_calling_thread_and_never_held_once_anything_starts()
    {
        _host.NewerVersionExitCode = 0;
        var thread = Environment.CurrentManagedThreadId;

        Run(Download(), canAsk: false);

        Assert.Equal(new[] { thread }, _host.LockThreads.Distinct());
        Assert.Equal(2, _host.LockThreads.Count);
        Assert.False(_host.NewerVersionHeldLock);
    }

    [Theory]
    [InlineData(true, "/SILENT")]
    [InlineData(false, "/VERYSILENT")]
    public void The_setup_never_opens_the_window_and_shows_progress_only_to_a_person(bool canAsk, string shown)
    {
        var arguments = CommandLineUpdate.SetupArguments(canAsk).Split(' ');

        Assert.Equal(shown, arguments[0]);
        Assert.DoesNotContain("/UPDATEFROMAPP", arguments);
        Assert.Contains("/SUPPRESSMSGBOXES", arguments);
        Assert.Contains("/NOCLOSEAPPLICATIONS", arguments);
    }

    private static string Prompt() =>
        string.Format(Strings.Cli_UpdatePrompt,
            Strings.Cli_UpdateAnswerYes.ToUpper(System.Globalization.CultureInfo.CurrentUICulture),
            Strings.Cli_UpdateAnswerNo.ToLower(System.Globalization.CultureInfo.CurrentUICulture)) + " ";

    private sealed class FakeUpdater(AppCopyKind kind, string fileToStart, Action? onPrepare = null) : IAppUpdater
    {
        public AppCopyKind Kind => kind;
        public bool CanUpdateItself => AppCopy.CanUpdateItself(kind);
        public string FileToStart => fileToStart;
        public bool Prepared { get; private set; }
        public bool Undone { get; private set; }

        public Task<PreparedUpdate> PrepareAsync(
            string version, IProgress<double?>? progress, CancellationToken cancellationToken)
        {
            Prepared = true;
            onPrepare?.Invoke();
            return Task.FromResult(new PreparedUpdate(
                fileToStart, kind == AppCopyKind.Setup ? "/VERYSILENT /SP-" : string.Empty, () => Undone = true));
        }
    }

    private sealed class RecordingHost(string exe) : ICommandLineUpdateHost
    {
        public List<string> Output { get; } = [];
        public Queue<string?> Answers { get; } = new();
        public Func<string?>? OnRead { get; set; }
        public int Reads { get; private set; }
        public bool LockAvailable { get; set; } = true;
        public bool HoldsLock { get; private set; }
        public List<int> LockThreads { get; } = [];
        public Dictionary<string, Version> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Func<int?> OnSetup { get; set; } = () => 0;
        public int? NewerVersionExitCode { get; set; }
        public (string File, string[] Arguments, bool HeldLock, bool ExeWasAside)? SetupRun { get; private set; }
        public (string File, string[] Arguments, string From, string To)? NewerVersionRun { get; private set; }
        public bool NewerVersionHeldLock { get; private set; }

        public void WriteLine(string line) => Output.Add(line);

        // The question and the answer share a line on screen; recorded as an entry of its own.
        public void Write(string text) => Output.Add(text);

        public string? ReadLine()
        {
            Reads++;
            if (OnRead is not null) return OnRead();
            return Answers.Count > 0 ? Answers.Dequeue() : null;
        }

        public bool TryTakeInstanceLock()
        {
            LockThreads.Add(Environment.CurrentManagedThreadId);
            HoldsLock = LockAvailable;
            return HoldsLock;
        }

        public void ReleaseInstanceLock()
        {
            LockThreads.Add(Environment.CurrentManagedThreadId);
            HoldsLock = false;
        }

        public Version? FileVersion(string path) =>
            File.Exists(path) && Versions.TryGetValue(path, out var version) ? version : null;

        public int? RunSetup(string file, IReadOnlyList<string> arguments)
        {
            SetupRun = (file, arguments.ToArray(), HoldsLock,
                !File.Exists(exe) && File.Exists(exe + SelfReplacement.OldSuffix));
            return OnSetup();
        }

        public int? RunNewerVersion(string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion)
        {
            NewerVersionHeldLock = HoldsLock;
            if (NewerVersionExitCode is null) return null;
            NewerVersionRun = (file, arguments.ToArray(), fromVersion, toVersion);
            return NewerVersionExitCode;
        }
    }
}
