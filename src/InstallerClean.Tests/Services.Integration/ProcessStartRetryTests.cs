using System.ComponentModel;
using System.Diagnostics;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="ProcessStartRetry"/> against a file that does not exist, which Windows
/// refuses to start every time.
/// </summary>
public class ProcessStartRetryTests
{
    private static ProcessStartInfo Missing() =>
        new(Path.Combine(Path.GetTempPath(), $"InstallerClean-missing-{Guid.NewGuid():N}.exe"))
        {
            UseShellExecute = false,
        };

    [Fact]
    public void A_start_that_always_fails_is_tried_every_time_with_a_wait_between_tries_and_the_last_failure_thrown()
    {
        var info = Missing();
        var waits = new List<TimeSpan>();
        var crashLogBefore = FailedStartsInCrashLog(info.FileName);

        Assert.Throws<Win32Exception>(() => ProcessStartRetry.Start(info, _ => true, waits.Add));

        Assert.Equal(Enumerable.Repeat(ProcessStartRetry.Interval, ProcessStartRetry.Attempts - 1), waits);
        // Each failed try but the last is written; the last is the caller's to write.
        Assert.Equal(crashLogBefore + ProcessStartRetry.Attempts - 1, FailedStartsInCrashLog(info.FileName));
    }

    [Fact]
    public void A_failure_the_caller_does_not_retry_is_thrown_at_the_first_try_with_no_wait_and_nothing_written()
    {
        var info = Missing();
        var waits = new List<TimeSpan>();
        var crashLogBefore = FailedStartsInCrashLog(info.FileName);

        Assert.Throws<Win32Exception>(() => ProcessStartRetry.Start(info, _ => false, waits.Add));

        Assert.Empty(waits);
        Assert.Equal(crashLogBefore, FailedStartsInCrashLog(info.FileName));
    }

    [Fact]
    public void A_throw_from_the_wait_ends_the_tries()
    {
        var info = Missing();
        var tries = 0;

        Assert.Throws<OperationCanceledException>(() => ProcessStartRetry.Start(
            info,
            _ => { tries++; return true; },
            _ => throw new OperationCanceledException()));

        Assert.Equal(1, tries);
    }

    /// <summary>How many entries in the crash log are a start of <paramref name="file"/> that was refused.</summary>
    private static int FailedStartsInCrashLog(string file) =>
        TestCrashLog.Text().Split('\n').Count(line =>
            line.StartsWith("System.ComponentModel.Win32Exception", StringComparison.Ordinal)
            && line.Contains(file, StringComparison.OrdinalIgnoreCase));
}
