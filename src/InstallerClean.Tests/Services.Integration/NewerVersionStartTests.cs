using System.Diagnostics;
using InstallerClean.Helpers;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The wait a copy updating itself makes for the newer version it started to say its own
/// code is running (<see cref="SelfReplacement.AwaitStart"/>), and the ending of a newer
/// version that did not say so, against real processes standing in for it: PowerShell
/// setting the event or sleeping, and programs of Windows that end at once.
/// </summary>
public sealed class NewerVersionStartTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-start-").FullName;
    private readonly List<Process> _started = [];

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly string PowerShell =
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    private Process Start(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = Process.Start(info)!;
        _started.Add(process);
        return process;
    }

    private Process PowerShellRunning(string script) =>
        Start(PowerShell, "-NoProfile", "-NonInteractive", "-Command", script);

    private static string Sets(Guid id) =>
        $"[System.Threading.EventWaitHandle]::OpenExisting('{HandOverContract.StartedEventName(id)}').Set() | Out-Null";

    private static (Guid Id, EventWaitHandle Event) NewEvent()
    {
        var id = Guid.NewGuid();
        return (id, HandOverContract.CreateStartedEvent(id)!);
    }

    [Fact]
    public void A_newer_version_that_sets_the_event_is_answered_as_started_and_left_running()
    {
        var (id, started) = NewEvent();
        using var _ = started;
        var newer = PowerShellRunning(Sets(id) + "; Start-Sleep -Seconds 60");

        var answer = SelfReplacement.AwaitStart(newer, started, TimeSpan.FromSeconds(60), cancel: null);

        Assert.Equal(NewerVersionStart.Started, answer);
        Assert.False(newer.HasExited);
    }

    [Fact]
    public void A_newer_version_that_set_the_event_and_has_ended_is_answered_as_started()
    {
        var (id, started) = NewEvent();
        using var _ = started;
        var newer = PowerShellRunning(Sets(id));
        Assert.True(newer.WaitForExit(TimeSpan.FromSeconds(60)));

        var answer = SelfReplacement.AwaitStart(newer, started, TimeSpan.FromSeconds(60), cancel: null);

        Assert.Equal(NewerVersionStart.Started, answer);
    }

    [Fact]
    public void A_newer_version_that_ends_without_setting_the_event_is_answered_at_once_with_its_exit_code()
    {
        var (_, started) = NewEvent();
        using var __ = started;
        var newer = Start(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c", "exit 7");
        var bound = TimeSpan.FromSeconds(60);
        var clock = Stopwatch.StartNew();

        var answer = SelfReplacement.AwaitStart(newer, started, bound, cancel: null);

        Assert.Equal(NewerVersionStart.Ended, answer);
        Assert.True(clock.Elapsed < bound / 2, $"The wait took {clock.Elapsed}.");
        Assert.Contains("exit code 7", SelfReplacement.NotStartedFailure(answer, newer, bound).Message);
        Assert.True(SelfReplacement.EndNewerVersion(newer));
    }

    [Fact]
    public void A_newer_version_that_never_sets_the_event_is_ended_once_the_bound_has_passed()
    {
        var (_, started) = NewEvent();
        using var __ = started;
        var newer = PowerShellRunning("Start-Sleep -Seconds 600");

        var answer = SelfReplacement.AwaitStart(newer, started, TimeSpan.FromSeconds(2), cancel: null);

        Assert.Equal(NewerVersionStart.TimedOut, answer);
        Assert.False(newer.HasExited);
        Assert.True(SelfReplacement.EndNewerVersion(newer));
        Assert.True(newer.HasExited);
    }

    [Fact]
    public void A_newer_version_that_would_set_the_event_after_the_bound_is_ended_before_it_does()
    {
        var (id, started) = NewEvent();
        using var _ = started;
        var newer = PowerShellRunning("Start-Sleep -Seconds 20; " + Sets(id));

        var answer = SelfReplacement.AwaitStart(newer, started, TimeSpan.FromSeconds(1), cancel: null);

        Assert.Equal(NewerVersionStart.TimedOut, answer);
        Assert.True(SelfReplacement.EndNewerVersion(newer));
        Assert.True(newer.HasExited);
        Assert.False(started.WaitOne(0));
    }

    [Fact]
    public void Ctrl_C_while_waiting_is_answered_as_cancelled_and_the_newer_version_is_ended()
    {
        var (_, started) = NewEvent();
        using var __ = started;
        using var cancel = new ManualResetEvent(false);
        var newer = PowerShellRunning("Start-Sleep -Seconds 600");
        using var timer = new Timer(_ => cancel.Set(), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

        var answer = SelfReplacement.AwaitStart(newer, started, TimeSpan.FromSeconds(60), cancel);

        Assert.Equal(NewerVersionStart.Cancelled, answer);
        Assert.True(SelfReplacement.EndNewerVersion(newer));
        Assert.True(newer.HasExited);
    }

    [Fact]
    public void A_hand_over_whose_newer_version_ends_without_saying_it_started_puts_everything_back()
    {
        // hostname.exe stands in for a newer version that starts and cannot run: it takes the
        // switch as an argument it does not know and ends at once.
        var exe = Path.Combine(_root, "InstallerClean.exe");
        var original = new byte[] { (byte)'M', (byte)'Z', 1, 1 };
        File.WriteAllBytes(exe, original);
        var newer = Path.Combine(Environment.SystemDirectory, "HOSTNAME.EXE");
        File.Copy(newer, exe + SelfReplacement.NewSuffix);
        var clock = Stopwatch.StartNew();

        Assert.False(SelfReplacement.HandOverAndEnd(exe, "9.9.9", TimeSpan.FromSeconds(60)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The hand-over took {clock.Elapsed}.");
        Assert.Equal(original, File.ReadAllBytes(exe));
        Assert.Equal(File.ReadAllBytes(newer), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }
}
