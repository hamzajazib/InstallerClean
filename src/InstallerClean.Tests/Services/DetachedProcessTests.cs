using InstallerClean.Interop.Native;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The detached start's command line and its tries (<see cref="DetachedProcess"/>), through
/// its seam for CreateProcess: leaving the job is asked for first, a refusal of that with
/// access denied is answered by a start inside the job at once, and other failures are
/// tried again.
/// </summary>
public sealed class DetachedProcessTests : IDisposable
{
    private readonly IDisposable _crashLog = TestCrashLog.OwnFolder();

    public void Dispose() => _crashLog.Dispose();

    private static string Line(params string[] arguments) =>
        new string(DetachedProcess.CommandLine(@"C:\staging\installerclean-cli.exe", arguments)).TrimEnd('\0');

    [Theory]
    [InlineData("/s", "/s")]
    [InlineData("", "\"\"")]
    [InlineData(@"D:\My Backup", "\"D:\\My Backup\"")]
    [InlineData(@"D:\My Backup\", "\"D:\\My Backup\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"a\\""b", "\"a\\\\\\\\\\\"b\"")]
    [InlineData(@"C:\no\spaces\", @"C:\no\spaces\")]
    public void Each_argument_is_written_so_it_reads_back_as_given(string argument, string written)
    {
        Assert.Equal($"\"C:\\staging\\installerclean-cli.exe\" {written}", Line(argument));
    }

    [Fact]
    public void The_command_line_ends_in_a_null_and_carries_every_argument_in_order()
    {
        var line = DetachedProcess.CommandLine(@"C:\x.exe", ["/m", "D:\\a b", "/noupdate"]);

        Assert.Equal('\0', line[^1]);
        Assert.Equal("\"C:\\x.exe\" /m \"D:\\a b\" /noupdate", new string(line, 0, line.Length - 1));
    }

    /// <summary>A start that answers each try from <paramref name="errors"/> in turn, 0 being a start, recording each try's flags.</summary>
    private static DetachedProcess.StartCall Answering(List<uint> flags, params int[] errors)
    {
        var next = 0;
        return (string _, char[] _, uint tried, string _, out Kernel32.PROCESS_INFORMATION started, out int error) =>
        {
            flags.Add(tried);
            error = errors[next++];
            started = default;
            if (error != 0) return false;
            // A handle that is never closed: -1 is the value a safe handle takes as no handle.
            started.hProcess = new IntPtr(-1);
            started.hThread = IntPtr.Zero;
            return true;
        };
    }

    [Fact]
    public void Leaving_the_job_is_asked_for_first()
    {
        List<uint> flags = [];

        using var started = DetachedProcess.Start("x.exe", [], "C:\\", Answering(flags, 0), _ => { });

        Assert.NotNull(started);
        Assert.Equal([Kernel32.DETACHED_PROCESS | Kernel32.CREATE_BREAKAWAY_FROM_JOB], flags);
        Assert.Empty(TestCrashLog.Entries());
    }

    [Fact]
    public void A_job_that_refuses_to_let_it_leave_starts_it_inside_at_once()
    {
        List<uint> flags = [];
        var slept = 0;

        using var started = DetachedProcess.Start("x.exe", [], "C:\\", Answering(flags, Kernel32.ERROR_ACCESS_DENIED, 0), _ => slept++);

        Assert.NotNull(started);
        Assert.Equal([Kernel32.DETACHED_PROCESS | Kernel32.CREATE_BREAKAWAY_FROM_JOB, Kernel32.DETACHED_PROCESS], flags);
        Assert.Equal(0, slept);
        Assert.Empty(TestCrashLog.Entries());
    }

    [Fact]
    public void A_start_that_keeps_failing_is_tried_the_set_number_of_times_each_asking_to_leave_the_job_first()
    {
        List<uint> flags = [];
        var slept = 0;
        var errors = Enumerable.Repeat(2, ProcessStartRetry.Attempts).ToArray();

        var started = DetachedProcess.Start("x.exe", [], "C:\\", Answering(flags, errors), _ => slept++);

        Assert.Null(started);
        Assert.Equal(ProcessStartRetry.Attempts, flags.Count);
        Assert.All(flags, f => Assert.Equal(Kernel32.DETACHED_PROCESS | Kernel32.CREATE_BREAKAWAY_FROM_JOB, f));
        Assert.Equal(ProcessStartRetry.Attempts - 1, slept);
        Assert.Equal(ProcessStartRetry.Attempts, TestCrashLog.FailedStartsOf("x.exe"));
    }
}
