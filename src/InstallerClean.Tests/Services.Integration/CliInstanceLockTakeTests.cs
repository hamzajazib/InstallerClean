using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// A command-line run's own take of the single-instance mutex and the line saying it runs
/// with this version (<c>Program.TakeInstanceLock</c>), against real named mutexes. Each
/// test names a fresh <c>Local\</c> mutex, never <see cref="InstanceLock.Name"/>, so a copy
/// of InstallerClean running on the machine neither fails a test nor is kept out by one.
/// </summary>
/// <remarks>
/// Stdout is read back through <c>Console.SetOut</c>, which is process-global, and the log
/// through <see cref="EventLogRecorder"/>; the assembly disables test parallelisation,
/// which is what makes both safe here.
/// </remarks>
public class CliInstanceLockTakeTests
{
    private const string RunningInsteadLine = "running with this version";
    private readonly string _name = $@"Local\InstallerClean_CliTakeTest_{Guid.NewGuid():N}";

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public void A_Move_or_Delete_says_it_runs_with_this_version_only_once_it_holds_the_mutex(string arg)
    {
        bool? heldWhenSaid = null;
        InstanceLock? made = null;

        var take = Captured(() => Program.TakeInstanceLock(arg, standsForCopy: false,
            () => made = new InstanceLock(_name),
            () =>
            {
                heldWhenSaid = made?.IsHeld;
                return RunningInsteadLine;
            },
            CancellationToken.None));

        Assert.Null(take.Result.ExitCode);
        Assert.Same(made, take.Result.Lock);
        Assert.True(heldWhenSaid);
        Assert.Equal([RunningInsteadLine], take.Lines);
        Assert.Empty(take.Entries);
        Release(take.Result.Lock);
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public void A_Move_or_Delete_the_mutex_turns_away_does_not_say_it_runs(string arg)
    {
        using var holding = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();
        var holder = HoldOnAnotherThread(holding, letGo);
        var said = false;

        var take = Captured(() => Program.TakeInstanceLock(arg, standsForCopy: false,
            () => new InstanceLock(_name),
            () =>
            {
                said = true;
                return RunningInsteadLine;
            },
            CancellationToken.None));

        letGo.Set();
        holder.Join();
        Assert.Equal(CliExitCode.Transient, take.Result.ExitCode);
        Assert.Null(take.Result.Lock);
        Assert.False(said);
        Assert.Equal([Strings.Cli_MutexBlocked], take.Lines);
        Assert.Equal(CliEventClass.TransientSkip, Assert.Single(take.Entries).Class);
    }

    [Fact]
    public void Ctrl_C_that_comes_as_the_mutex_is_taken_leaves_the_line_unsaid()
    {
        using var cts = new CancellationTokenSource();
        var said = false;

        var take = Captured(() => Program.TakeInstanceLock("/d", standsForCopy: false,
            () =>
            {
                cts.Cancel();
                return new InstanceLock(_name);
            },
            () =>
            {
                said = true;
                return RunningInsteadLine;
            },
            cts.Token));

        Assert.Null(take.Result.ExitCode);
        Assert.True(take.Result.Lock?.IsHeld);
        Assert.False(said);
        Assert.Empty(take.Lines);
        Release(take.Result.Lock);
    }

    [Fact]
    public void A_run_standing_for_a_copy_that_holds_the_mutex_goes_on_without_it()
    {
        using var holding = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();
        var holder = HoldOnAnotherThread(holding, letGo);

        var take = Captured(() => Program.TakeInstanceLock("/d", standsForCopy: true,
            () => new InstanceLock(_name), null, CancellationToken.None));

        letGo.Set();
        holder.Join();
        Assert.Null(take.Result.ExitCode);
        Assert.Null(take.Result.Lock);
        Assert.Empty(take.Lines);
        Assert.Empty(take.Entries);
    }

    [Fact]
    public void A_scan_takes_no_mutex_and_says_it_runs_with_this_version()
    {
        var made = false;

        var take = Captured(() => Program.TakeInstanceLock("/s", standsForCopy: false,
            () =>
            {
                made = true;
                return new InstanceLock(_name);
            },
            () => RunningInsteadLine,
            CancellationToken.None));

        Assert.False(made);
        Assert.Null(take.Result.ExitCode);
        Assert.Null(take.Result.Lock);
        Assert.Equal([RunningInsteadLine], take.Lines);
    }

    [Fact]
    public void A_line_that_cannot_be_built_still_hands_back_the_mutex_it_took()
    {
        var take = Captured(() => Program.TakeInstanceLock("/d", standsForCopy: false,
            () => new InstanceLock(_name),
            () => throw new FormatException("a translation with a placeholder its English lacks"),
            CancellationToken.None));

        Assert.Null(take.Result.ExitCode);
        Assert.True(take.Result.Lock?.IsHeld);
        Assert.Empty(take.Lines);
        Assert.Empty(take.Entries);
        Release(take.Result.Lock);
    }

    private Thread HoldOnAnotherThread(ManualResetEventSlim holding, ManualResetEventSlim letGo)
    {
        var holder = new Thread(() =>
        {
            using var first = new InstanceLock(_name);
            first.TryTake();
            holding.Set();
            letGo.Wait();
            first.Release();
        }) { IsBackground = true };
        holder.Start();
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));
        return holder;
    }

    private static void Release(InstanceLock? instanceLock)
    {
        instanceLock?.Release();
        instanceLock?.Dispose();
    }

    private static Taken Captured(Func<Program.LockTake> act)
    {
        var original = Console.Out;
        using var console = new StringWriter();
        try
        {
            Console.SetOut(console);
            EventLogRecorder.Clear();
            var result = act();
            var lines = console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            return new Taken(result, lines, EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed record Taken(
        Program.LockTake Result, string[] Lines, IReadOnlyList<(CliEventClass Class, string Text)> Entries);
}
