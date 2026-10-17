using InstallerClean.Resources;

namespace InstallerClean.Services;

/// <summary>
/// What a command-line run's update ended in, which the host turns into an
/// Application-log notice and an exit code.
/// </summary>
internal abstract record CommandLineUpdateResult
{
    private CommandLineUpdateResult() { }

    /// <summary>
    /// The run goes on with the version it is. <paramref name="FailedAt"/> is null where
    /// nothing was tried: no newer version, the check failing, a no at the question, or a
    /// build from source. Otherwise it names the part that failed, and the two versions
    /// are the running one and the one it could not install.
    /// </summary>
    public sealed record CarriedOn(UpdateFailureStage? FailedAt, string? FromVersion, string? ToVersion)
        : CommandLineUpdateResult;

    /// <summary>
    /// The newer version ran in this console with <paramref name="ArgumentsRun"/> and has
    /// finished; <paramref name="ExitCode"/> is its exit code, and so this run's.
    /// </summary>
    public sealed record HandedOver(int ExitCode, string FromVersion, string ToVersion, string ArgumentsRun)
        : CommandLineUpdateResult;

    /// <summary>Ctrl+C at the question or during the download. Nothing is left changed on disk.</summary>
    public sealed record Cancelled : CommandLineUpdateResult;

    /// <summary>The answer where nothing was tried.</summary>
    public static readonly CarriedOn NothingTried = new(null, null, null);
}

/// <summary>
/// What <see cref="CommandLineUpdate"/> needs from the console host: the console, the
/// single-instance mutex, the version a file carries, and starting a program in this
/// console. Each member is the host's because each touches the console or the process.
/// </summary>
internal interface ICommandLineUpdateHost
{
    /// <summary>Prints a line.</summary>
    void WriteLine(string line);

    /// <summary>Prints text and leaves the cursor after it, for the question.</summary>
    void Write(string text);

    /// <summary>Reads one line of the person's answer, or null at the end of input.</summary>
    string? ReadLine();

    /// <summary>
    /// Takes the single-instance mutex without waiting, on the calling thread. Answers
    /// whether it is now held.
    /// </summary>
    bool TryTakeInstanceLock();

    /// <summary>Releases the mutex <see cref="TryTakeInstanceLock"/> took, on the same thread.</summary>
    void ReleaseInstanceLock();

    /// <summary>The version the file at <paramref name="path"/> carries, or null where it carries none or cannot be read.</summary>
    Version? FileVersion(string path);

    /// <summary>
    /// Starts the setup at <paramref name="file"/> with <paramref name="arguments"/>,
    /// waits for it to end and answers with its exit code, or null where it could not be
    /// started.
    /// </summary>
    int? RunSetup(string file, IReadOnlyList<string> arguments);

    /// <summary>
    /// Starts the newer version at <paramref name="file"/> with <paramref name="arguments"/>
    /// in this console, notes in the Application log that this run updated itself from
    /// <paramref name="fromVersion"/> to <paramref name="toVersion"/> once it is running,
    /// waits for it to end and answers with its exit code, or null where it could not be
    /// started. While it runs, Ctrl+C is the newer version's to answer.
    /// </summary>
    int? RunNewerVersion(string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion);
}

/// <summary>
/// The command line's update, run before the scan of a <c>/s</c>, <c>/d</c> or <c>/m</c>:
/// it checks for a newer version and, where one is out, installs it and runs it in the
/// same console. Where a person can see the console and answer it, it asks first, and
/// after a yes the newer version scans and lists as <c>/s</c> does and moves or deletes
/// nothing. Where nobody can answer, it updates without asking and the newer version
/// runs the command as given. Any part that fails leaves the run going on with the
/// version it is, and says so.
/// </summary>
/// <remarks>
/// Synchronous on purpose. The host calls it on the thread that owns the single-instance
/// mutex, and the mutex has to be released on the thread that took it, so the check and
/// the download are waited on here rather than awaited. A console host has no
/// SynchronizationContext, so the wait cannot deadlock.
/// </remarks>
internal sealed class CommandLineUpdate
{
    /// <summary>
    /// How long the check may take where a person is waiting on the console. Where nobody
    /// is, the check's own <see cref="UpdateCheckService.RequestTimeout"/> bounds it.
    /// </summary>
    internal static readonly TimeSpan WatchedCheckBound = TimeSpan.FromSeconds(3);

    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly IAppUpdater _updater;
    private readonly ICommandLineUpdateHost _host;
    private readonly TimeSpan _watchedCheckBound;

    /// <param name="check">Asks GitHub for the newest version.</param>
    /// <param name="updater">
    /// Gets the newer version ready, built with <see cref="SetupArguments"/> for the
    /// same answer to whether a person can answer.
    /// </param>
    /// <param name="host">The console host.</param>
    public CommandLineUpdate(
        Func<CancellationToken, Task<UpdateCheckResult>> check, IAppUpdater updater, ICommandLineUpdateHost host)
        : this(check, updater, host, WatchedCheckBound) { }

    /// <summary>Test seam: the bound on a watched check, so a test need not wait it out.</summary>
    internal CommandLineUpdate(
        Func<CancellationToken, Task<UpdateCheckResult>> check, IAppUpdater updater, ICommandLineUpdateHost host,
        TimeSpan watchedCheckBound)
    {
        _check = check;
        _updater = updater;
        _host = host;
        _watchedCheckBound = watchedCheckBound;
    }

    /// <summary>
    /// What the command line starts the newer setup with. /SP- leaves out the page asking
    /// whether to install, /NORESTART keeps Setup from restarting Windows, and
    /// /SUPPRESSMSGBOXES answers any box Setup would show with its default, so it never
    /// waits on one; a box it would have shown fails the setup. /NOCLOSEAPPLICATIONS keeps
    /// Setup from closing programs holding files it replaces, this run among them, which
    /// waits on it. /SILENT shows Setup's progress where a person is watching, and
    /// /VERYSILENT shows nothing where nobody is. /UPDATEFROMAPP is left out: it is what
    /// opens the window afterwards.
    /// </summary>
    internal static string SetupArguments(bool canAsk) =>
        (canAsk ? "/SILENT" : "/VERYSILENT") + " /SP- /NORESTART /SUPPRESSMSGBOXES /NOCLOSEAPPLICATIONS";

    /// <summary>
    /// Runs the update for a run of <paramref name="command"/> given
    /// <paramref name="arguments"/>.
    /// </summary>
    /// <param name="canAsk">Whether a person can see the console and answer it.</param>
    /// <param name="command">The command typed, lower-cased: /s, /d or /m.</param>
    /// <param name="arguments">The arguments as given, which the newer version gets where nobody can answer.</param>
    /// <param name="exePath">The running command line's own path.</param>
    /// <param name="token">Cancelled by Ctrl+C.</param>
    public CommandLineUpdateResult Run(
        bool canAsk, string command, IReadOnlyList<string> arguments, string exePath, CancellationToken token)
    {
        if (Check(canAsk, token) is not { } available)
            return token.IsCancellationRequested ? new CommandLineUpdateResult.Cancelled() : CommandLineUpdateResult.NothingTried;

        var current = available.CurrentVersion;
        var latest = available.LatestVersion;
        _host.WriteLine(string.Format(Strings.UpdateNotice_Available, latest));

        if (!_updater.CanUpdateItself)
        {
            _host.WriteLine(Strings.UpdateNotice_Recommended);
            _host.WriteLine(string.Format(Strings.Cli_UpdateDownloadLink, UpdateCheckService.ReleasesPageUrl));
            return CommandLineUpdateResult.NothingTried;
        }

        if (canAsk)
        {
            _host.WriteLine(Strings.UpdateNotice_Recommended);
            switch (Ask(token))
            {
                case Answer.Cancelled: return new CommandLineUpdateResult.Cancelled();
                case Answer.No: return CommandLineUpdateResult.NothingTried;
            }
        }

        try
        {
            return Update(canAsk, command, arguments, exePath, current, latest, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new CommandLineUpdateResult.Cancelled();
        }
        catch (Exception ex)
        {
            var stage = ex is UpdateFailedException failed ? failed.Stage : UpdateFailureStage.Install;
            Helpers.CrashLog.TryWrite(ex);
            _host.WriteLine(stage == UpdateFailureStage.Download
                ? Strings.UpdateNotice_DownloadFailed
                : Strings.UpdateNotice_InstallFailed);
            _host.WriteLine(string.Format(Strings.Cli_UpdateRunningInstead, command, current));
            return new CommandLineUpdateResult.CarriedOn(stage, current, latest);
        }
    }

    /// <summary>
    /// The check, bounded by <see cref="WatchedCheckBound"/> where a person is waiting.
    /// Answers the newer version, or null for anything else: up to date, a failed check,
    /// the bound running out, or Ctrl+C, which the caller reads off the token.
    /// </summary>
    private UpdateAvailable? Check(bool canAsk, CancellationToken token)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (canAsk) bound.CancelAfter(_watchedCheckBound);
        try
        {
            return _check(bound.Token).GetAwaiter().GetResult() as UpdateAvailable;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private enum Answer { Yes, No, Cancelled }

    /// <summary>
    /// Asks the question until it gets an answer. Enter, or an answer starting with this
    /// language's letter for yes or with Y, is yes; one starting with its letter for no or
    /// with N is no; the end of input is no; anything else asks again. Ctrl+C is read off
    /// the token once the read returns, whatever it returned.
    /// </summary>
    private Answer Ask(CancellationToken token)
    {
        var yes = Strings.Cli_UpdateAnswerYes.Trim();
        var no = Strings.Cli_UpdateAnswerNo.Trim();
        var prompt = string.Format(Strings.Cli_UpdatePrompt,
            yes.ToUpper(System.Globalization.CultureInfo.CurrentUICulture),
            no.ToLower(System.Globalization.CultureInfo.CurrentUICulture));
        while (true)
        {
            _host.Write(prompt + " ");
            var line = _host.ReadLine();
            if (token.IsCancellationRequested) return Answer.Cancelled;
            if (line is null) return Answer.No;
            var answer = line.Trim();
            if (answer.Length == 0 || StartsWith(answer, yes) || StartsWith(answer, "Y")) return Answer.Yes;
            if (StartsWith(answer, no) || StartsWith(answer, "N")) return Answer.No;
        }

        static bool StartsWith(string answer, string letter) =>
            letter.Length > 0 && answer.StartsWith(letter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Downloads the newer version, puts it in place and runs it. Throws where a part
    /// fails, having put back what it changed; the caller says so and carries on.
    /// </summary>
    private CommandLineUpdateResult Update(
        bool canAsk, string command, IReadOnlyList<string> arguments, string exePath,
        string current, string latest, CancellationToken token)
    {
        var isSetup = _updater.Kind == AppCopyKind.Setup;
        PreparedUpdate prepared;

        // The mutex is held through the download, so the window's start, which clears out
        // earlier updates' download folders behind the mutex, cannot take this one's. The
        // setup names the same mutex and stops for anything holding it, the window or a
        // Move or Delete among them, so for the setup's copy a held mutex fails the update
        // before anything is downloaded. The command line's own download replaces only
        // its own file, so it goes ahead without the mutex where it is held.
        var holdsLock = _host.TryTakeInstanceLock();
        try
        {
            if (isSetup && !holdsLock)
                throw new UpdateFailedException(UpdateFailureStage.Install,
                    "InstallerClean is open, or another Move or Delete is running, so the setup cannot run.");
            _host.WriteLine(string.Format(Strings.UpdateNotice_Downloading, latest));
            prepared = _updater.PrepareAsync(latest, null, token).GetAwaiter().GetResult();
        }
        finally
        {
            if (holdsLock) _host.ReleaseInstanceLock();
        }

        if (isSetup)
        {
            RunSetup(prepared, exePath, latest);
            // Ctrl+C while the setup ran: the newer version is in place, and this run stops
            // before its scan.
            token.ThrowIfCancellationRequested();
        }
        else if (!Carries(exePath, latest))
        {
            prepared.Undo();
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The downloaded file does not carry the version it was downloaded as.");
        }

        _host.WriteLine(string.Format(Strings.Cli_UpdateInstalled, latest));
        if (canAsk && command == "/d") _host.WriteLine(Strings.Cli_UpdateListsOnlyAfterDelete);
        if (canAsk && command == "/m") _host.WriteLine(Strings.Cli_UpdateListsOnlyAfterMove);

        IReadOnlyList<string> run = canAsk ? ["/s"] : arguments;
        var exitCode = _host.RunNewerVersion(exePath, run, current, latest);
        if (exitCode is null)
        {
            // The setup's copy stays: the setup has updated the window beside it too, and
            // this run goes on from the renamed file whichever is at the path.
            if (!isSetup) prepared.Undo();
            throw new UpdateFailedException(UpdateFailureStage.Install, "The newer version could not be started.");
        }
        return new CommandLineUpdateResult.HandedOver(exitCode.Value, current, latest, string.Join(' ', run));
    }

    /// <summary>
    /// Runs the newer setup with this command line renamed aside, so the setup writes the
    /// newer version at its path rather than meeting a file in use. Any exit code but 0,
    /// or a file at the path that does not carry the newer version, fails; the rename is
    /// undone wherever nothing is at the path. The downloaded setup and its folder are
    /// removed either way, the setup having finished with them.
    /// </summary>
    private void RunSetup(PreparedUpdate prepared, string exePath, string latest)
    {
        var moveBack = SelfReplacement.MoveAside(exePath);
        int? exitCode;
        try
        {
            exitCode = _host.RunSetup(
                prepared.FileToStart,
                prepared.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch
        {
            moveBack();
            prepared.Undo();
            throw;
        }
        prepared.Undo();

        if (exitCode != 0)
        {
            moveBack();
            throw new UpdateFailedException(UpdateFailureStage.Install,
                exitCode is null ? "The setup could not be started." : $"The setup ended with exit code {exitCode}.");
        }
        if (!Carries(exePath, latest))
        {
            moveBack();
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The setup finished without putting the newer command line in place.");
        }
    }

    /// <summary>
    /// Whether the file at <paramref name="path"/> carries <paramref name="version"/>, its
    /// first three parts compared. Checked before the newer version starts, so a file that
    /// is not what was asked for is never run in this run's place, and a newer version
    /// that is somehow still behind can never start another update in turn.
    /// </summary>
    private bool Carries(string path, string version) =>
        _host.FileVersion(path) is { } carried
        && Version.TryParse(version, out var expected)
        && carried.Major == expected.Major && carried.Minor == expected.Minor
        && Math.Max(carried.Build, 0) == Math.Max(expected.Build, 0);
}
