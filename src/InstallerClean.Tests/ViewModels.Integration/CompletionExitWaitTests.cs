using InstallerClean.Models;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;
using InstallerClean.ViewModels;
using NSubstitute;

namespace InstallerClean.Tests.ViewModels.Integration;

/// <summary>
/// The wait <see cref="CompletionViewModel.SettleReportOnExit"/> makes on the window's thread
/// for report work still running, made through Windows itself.
/// </summary>
public class CompletionExitWaitTests
{
    [Fact]
    public async Task The_wait_for_work_still_running_hands_no_wait_to_the_threads_synchronisation_context_and_says_it_ran_out()
    {
        using var crashLog = TestCrashLog.OwnFolder();
        var neverAnswered = new TaskCompletionSource<bool>();
        var earlierRunCheck = Substitute.For<IEarlierRunCheck>();
        earlierRunCheck.ShowsAnEarlierRunAsync().Returns(neverAnswered.Task);
        var card = new CompletionViewModel(
            Substitute.For<IResultLogService>(),
            Substitute.For<ISettingsService>(),
            earlierRunCheck,
            FirstRunMarks.Claiming(),
            Substitute.For<IWindowsRegion>(),
            Substitute.For<IWindowService>(),
            AppCopyKind.FromSource,
            () => null);

        // A retry that waits on a check that never answers, so the work is still running.
        card.StartSavedReportRetry();
        try
        {
            var (waits, took, controlWaits) = WaitRecordingContext.RunOnThreadWithIt(context =>
            {
                // The control before and after: a WaitOne on the same thread is handed to
                // the context, so it is in place on both sides of the wait measured.
                using var signalled = new ManualResetEvent(initialState: true);
                signalled.WaitOne(TimeSpan.FromSeconds(5));
                if (context.Waits != 1) return (-1, TimeSpan.Zero, context.Waits);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                card.SettleReportOnExit();
                var elapsed = clock.Elapsed;
                var during = context.Waits - 1;
                signalled.WaitOne(TimeSpan.FromSeconds(5));
                return (during, elapsed, context.Waits - 1 - during);
            });

            Assert.Equal(0, waits);
            Assert.Equal(1, controlWaits);
            Assert.True(took >= CompletionViewModel.ExitWaitBound - TimeSpan.FromMilliseconds(100), $"The wait took {took}.");
            Assert.Contains("was still running", TestCrashLog.Text());
        }
        finally
        {
            neverAnswered.SetResult(false);
        }

        // Throws where the retry does not end once the check answers.
        await card.ReportWork.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Work_already_finished_writes_nothing()
    {
        using var crashLog = TestCrashLog.OwnFolder();
        var card = TestCompletion.Create();

        card.SettleReportOnExit();

        Assert.Equal("", TestCrashLog.Text());
    }
}
