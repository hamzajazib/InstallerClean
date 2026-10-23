#if DEBUG
using System.Diagnostics;
using System.Text.RegularExpressions;
using InstallerClean.Helpers;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.EndToEnd;

/// <summary>
/// The built command line in a console window of its own, closed by that window's WM_CLOSE
/// while a /s waits at its update question: it ends as a Ctrl+C there ends it, with the
/// cancelled exit code and one entry in the Application log. The run is held at the
/// question by the Debug build's switches: a copy downloaded on its own, a newer release,
/// and that release's update.json read from a file of the test's. Those switches exist in a
/// Debug build only, which is the build the tests run against.
/// </summary>
public sealed class ConsoleCloseTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-close-").FullName;
    private Process? _run;
    private Process? _host;

    private static readonly string Cli = Path.Combine(AppContext.BaseDirectory, "installerclean-cli.exe");

    // The question ends in "[<yes>/<no>]" in every language.
    private static readonly Regex AtQuestion = new(@"\[[^\]/]+/[^\]/]+\]\s*$", RegexOptions.Compiled);

    private static readonly string[] Switches =
        [AppCopy.PretendCopyVariable, UpdateCheckService.PretendLatestVariable, AppUpdater.LocalManifestVariable];

    public void Dispose()
    {
        foreach (var name in Switches) Environment.SetEnvironmentVariable(name, null);
        OtherConsole.End(_run);
        OtherConsole.End(_host);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [OwnConsoleWindowFact]
    public async Task A_run_whose_console_window_is_closed_at_its_question_ends_cancelled_with_one_entry()
    {
        Assert.True(File.Exists(Cli), $"The built command line is not beside the tests: {Cli}");
        await SourceRegisteredAsync();

        // A release newer than the files the update reads is refused without a question
        // where it has no update.json, so the run is given one that lets it update.
        var manifest = Path.Combine(_root, "update.json");
        File.WriteAllText(manifest, """{"schema":1,"downloads":{"cli":{"requires":[]}}}""");
        var after = NewestEntryIndex();

        // A program started under conhost.exe takes this process's environment.
        Environment.SetEnvironmentVariable(AppCopy.PretendCopyVariable, "cli");
        Environment.SetEnvironmentVariable(UpdateCheckService.PretendLatestVariable, "v9.9.9");
        Environment.SetEnvironmentVariable(AppUpdater.LocalManifestVariable, manifest);
        (_run, _host) = OtherConsole.StartInOwnConsole(Cli, "/s", out var notStarted);
        foreach (var name in Switches) Environment.SetEnvironmentVariable(name, null);
        Assert.True(_run is not null, $"The command line did not start: {notStarted}");
        var window = OtherConsole.WindowClass(_run.Id);
        if (OtherConsole.Failed(window)) Assert.Fail($"The run's console window could not be asked about: {window}");
        Assert.Equal("ConsoleWindowClass", window);

        // The last reading that came back is kept, so a run that ends first says what it printed.
        var screen = "";
        var failedReading = "";
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(60) && !_run.HasExited)
        {
            var reading = OtherConsole.Read(_run.Id);
            if (OtherConsole.Failed(reading)) failedReading = reading; else screen = reading;
            if (AtQuestion.IsMatch(screen)) break;
            await Task.Delay(250);
        }
        if (_run.HasExited)
            Assert.Fail($"The run ended with {_run.ExitCode} before it asked its question. Its console read:\n{screen}\nIts entries: {Describe(EntriesAfter(after))}");
        Assert.True(AtQuestion.IsMatch(screen), $"The run did not reach its question in 60 seconds. Its console read:\n{screen}\nThe last failed reading: {failedReading}");

        // Read once more, so the close goes only to a run seen at its question.
        var last = OtherConsole.Read(_run.Id);
        Assert.True(AtQuestion.IsMatch(last), $"The run was not at its question just before the close. Its console read:\n{last}");

        // Timed from before the helper starts, so the time measured is never shorter than the real one.
        var closedAt = Stopwatch.StartNew();
        var closed = OtherConsole.Close(_run.Id);
        Assert.False(OtherConsole.Failed(closed), $"The window could not be closed: {closed}");
        Assert.True(_run.WaitForExit(15_000), "The run was still running 15 seconds after its window was closed.");
        var took = closedAt.Elapsed;

        // Read until the entry shows, for up to five seconds.
        List<(int Id, string Message)> entries = [];
        for (var attempt = 0; attempt < 20 && entries.Count == 0; attempt++)
        {
            entries = EntriesAfter(after);
            if (entries.Count == 0) await Task.Delay(250);
        }

        Assert.True(_run.ExitCode == CliExitCode.Cancelled,
            $"The run ended with {_run.ExitCode} (window class {closed}), {took.TotalSeconds:N1} s after the close. Its entries: {Describe(entries)}");
        Assert.True(took < TimeSpan.FromSeconds(5), $"The run took {took.TotalSeconds:N1} s to end after the close.");
        var entry = Assert.Single(entries);
        Assert.Equal(CliContract.EventIdFor(CliEventClass.TransientSkip), entry.Id);
        Assert.Contains("cancelled at the console before any work", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Registers the command line's source where it is not yet, and waits for it, so the run
    /// writes its entry at once rather than waiting for a registration of its own to settle.
    /// </summary>
    private static async Task SourceRegisteredAsync()
    {
        if (!EventLog.SourceExists(EventLogWriter.SourceName))
            EventLog.CreateEventSource(EventLogWriter.SourceName, "Application");
        for (var attempt = 0; attempt < 40 && !EventLog.SourceExists(EventLogWriter.SourceName); attempt++)
            await Task.Delay(250);
    }

    /// <summary>The index of the newest entry in the Application log, or -1 where it is empty.</summary>
    private static int NewestEntryIndex()
    {
        using var log = new EventLog("Application");
        var count = log.Entries.Count;
        return count == 0 ? -1 : log.Entries[count - 1].Index;
    }

    /// <summary>The command line's entries in the Application log after the one at <paramref name="index"/>, oldest first.</summary>
    private static List<(int Id, string Message)> EntriesAfter(int index)
    {
        using var log = new EventLog("Application");
        var found = new List<(int, string)>();
        for (var i = log.Entries.Count - 1; i >= 0; i--)
        {
            var e = log.Entries[i];
            if (e.Index <= index) break;
            if (e.Source == EventLogWriter.SourceName) found.Add(((int)(e.InstanceId & 0xFFFF), e.Message));
        }
        found.Reverse();
        return found;
    }

    private static string Describe(List<(int Id, string Message)> entries) =>
        entries.Count == 0 ? "none" : string.Join(" | ", entries.Select(e => $"{e.Id}: {e.Message}"));
}
#endif
