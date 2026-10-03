using System.Collections.Concurrent;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using InstallerClean.ViewModels;
using NSubstitute;

namespace InstallerClean.Tests.ViewModels;

/// <summary>
/// The button on the scanning card that gives up on the drive or share a waiting line names.
/// It goes up with the wait's own line and goes with the wait's end or any other write to the
/// line, whatever writes it, whether or not any of these writes changes the text, and the name
/// a screen reader speaks for the line changes with it in the same step. Each scan here is a
/// Re-scan started on a stand-in for the window's dispatcher, so the card's progress reaches
/// the view model there, one report at a time and in order, as it does in the window. The scan
/// service runs on the thread pool and reports the steps a test gives it, each step returning
/// once the card has applied what it reported.
/// </summary>
public class ScanViewModelStopWaitingTests
{
    private const string Milestone = "a step the scan reported";

    private readonly IFileSystemScanService _scanService = Substitute.For<IFileSystemScanService>();
    private readonly IPendingRebootService _rebootService = Substitute.For<IPendingRebootService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly DispatcherStandIn _dispatcher = new();

    public ScanViewModelStopWaitingTests() =>
        _rebootService.Check().Returns(PendingRebootResult.Clean);

    private ScanViewModel NewViewModel() => new(_scanService, _rebootService, _dialogService);

    private static string WaitLine(string root) => DisplayHelpers.WaitingFor(root);

    /// <summary>Reports <paramref name="update"/> and returns once the card has applied it.</summary>
    private async Task ShowAsync(IProgress<ScanProgressUpdate> progress, ScanProgressUpdate update)
    {
        progress.Report(update);
        await _dispatcher.IdleAsync();
    }

    /// <summary>
    /// A scan service that runs <paramref name="steps"/> in turn on the thread pool and then
    /// answers with an empty result. Each step is handed the progress the card gave the scan
    /// and the scan's own token.
    /// </summary>
    private void ScanReports(params Func<IProgress<ScanProgressUpdate>, CancellationToken, Task>[] steps) =>
        _scanService.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.ArgAt<IProgress<ScanProgressUpdate>?>(0)!;
                var token = call.ArgAt<CancellationToken>(1);
                return Task.Run(async () =>
                {
                    foreach (var step in steps) await step(progress, token);
                    token.ThrowIfCancellationRequested();
                    return new ScanResult([], [], 0);
                });
            });

    /// <summary>Starts a Re-scan on the dispatcher, as the button does, and waits for it to end.</summary>
    private async Task RescanAsync(ScanViewModel vm)
    {
        var scan = Task.CompletedTask;
        await _dispatcher.RunAsync(() => scan = vm.ScanCommand.ExecuteAsync(null));
        await scan;
        Assert.Empty(_dispatcher.Thrown);
    }

    [Fact]
    public async Task A_wait_puts_the_button_up_with_its_line_and_its_end_takes_both_back()
    {
        var vm = NewViewModel();
        var wait = new SourceFolderWait("D:", () => { });
        bool? upWithItsLine = null, goneWithIt = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(wait));
                upWithItsLine = vm.ScanProgress == WaitLine("D:")
                    && vm.CanStopWaiting && ReferenceEquals(vm.WaitShown, wait)
                    && vm.StopWaitingName == DisplayHelpers.StopWaitingFor("D:")
                    && vm.ScanProgressName == DisplayHelpers.WaitingLineWithStopKey(WaitLine("D:"))
                    && vm.StopWaitingCommand.CanExecute(null);
            },
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
                goneWithIt = vm.ScanProgress == Milestone
                    && !vm.CanStopWaiting && vm.WaitShown is null && vm.StopWaitingName.Length == 0
                    && vm.ScanProgressName == Milestone && !vm.StopWaitingCommand.CanExecute(null);
            });

        await RescanAsync(vm);

        Assert.True(upWithItsLine);
        Assert.True(goneWithIt);
    }

    [Fact]
    public async Task The_button_stops_the_wait_its_line_names()
    {
        var vm = NewViewModel();
        var stops = 0;
        var wait = new SourceFolderWait(@"\\fileserver\apps", () => Interlocked.Increment(ref stops));
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(wait));
                await _dispatcher.RunAsync(() => vm.StopWaitingCommand.Execute(null));
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
            });

        await RescanAsync(vm);

        Assert.Equal(1, stops);
        Assert.False(vm.CanStopWaiting);
    }

    [Fact]
    public async Task Cancel_takes_the_button_away_and_a_wait_told_after_it_brings_none_back()
    {
        var vm = NewViewModel();
        bool? goneOnCancel = null, upAfterTheLaterWait = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }))),
            async (p, token) =>
            {
                await _dispatcher.RunAsync(() => vm.CancelScanCommand.Execute(null));
                goneOnCancel = !vm.CanStopWaiting && vm.ScanProgress == Strings.Status_Cancelling;

                // A second wait, told once Cancel is pressed and before the scan sees it.
                await ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("E:", () => { })));
                upAfterTheLaterWait = vm.CanStopWaiting;
                token.ThrowIfCancellationRequested();
            });

        await RescanAsync(vm);

        Assert.True(goneOnCancel);
        Assert.False(upAfterTheLaterWait);
        Assert.Equal(Strings.Status_ScanCancelled, vm.ScanProgress);
    }

    [Fact]
    public async Task A_step_reported_during_a_wait_takes_the_button_away_and_the_wait_s_end_leaves_the_step()
    {
        var vm = NewViewModel();
        bool? goneWithTheStep = null;
        string? afterTheEnd = null;
        const string NextStep = "the next step the scan reported";
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }))),
            async (p, _) =>
            {
                await ShowAsync(p, new ScanProgressUpdate(NextStep));
                goneWithTheStep = !vm.CanStopWaiting && vm.ScanProgressName == NextStep;
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
                afterTheEnd = vm.ScanProgress;
            });

        await RescanAsync(vm);

        Assert.True(goneWithTheStep);
        Assert.Equal(NextStep, afterTheEnd);
    }

    [Fact]
    public async Task A_step_worded_as_the_waiting_line_takes_the_button_away_and_raises_only_the_button()
    {
        // The step arrives while the wait is on the line and reads as that line, so the text
        // does not change. The button goes with it and the wait's end then leaves the step.
        // The scan's own timer can raise IsScanning at any point here, so it is left out of
        // the changes recorded.
        var vm = NewViewModel();
        var raised = new ConcurrentQueue<string?>();
        var buttonChecks = 0;
        void Raised(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScanViewModel.IsScanning)) raised.Enqueue(e.PropertyName);
        }
        void ButtonChecked(object? _, EventArgs e) => Interlocked.Increment(ref buttonChecks);
        bool? goneWithTheStep = null;
        (string Line, bool Button)? afterTheEnd = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }))),
            async (p, _) =>
            {
                vm.PropertyChanged += Raised;
                vm.StopWaitingCommand.CanExecuteChanged += ButtonChecked;
                await ShowAsync(p, new ScanProgressUpdate(WaitLine("D:")));
                vm.PropertyChanged -= Raised;
                vm.StopWaitingCommand.CanExecuteChanged -= ButtonChecked;
                goneWithTheStep = vm.WaitShown is null && !vm.StopWaitingCommand.CanExecute(null)
                    && vm.ScanProgress == WaitLine("D:") && vm.ScanProgressName == WaitLine("D:");
            },
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
                afterTheEnd = (vm.ScanProgress, vm.CanStopWaiting);
            });

        await RescanAsync(vm);

        Assert.True(goneWithTheStep);
        Assert.Equal(
            [nameof(ScanViewModel.WaitShown), nameof(ScanViewModel.CanStopWaiting),
                nameof(ScanViewModel.StopWaitingName), nameof(ScanViewModel.ScanProgressName)],
            raised);
        Assert.Equal(1, buttonChecks);
        Assert.Equal((WaitLine("D:"), false), afterTheEnd);
    }

    [Fact]
    public async Task The_scan_s_outcome_takes_the_button_away()
    {
        // The scan ends with a wait still on the line, which the check never leaves, so
        // what takes the button away is the outcome line written over it.
        var vm = NewViewModel();
        bool? upAtTheEnd = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { })));
                upAtTheEnd = vm.CanStopWaiting;
            });

        await RescanAsync(vm);

        Assert.True(upAtTheEnd);
        Assert.StartsWith(Strings.Status_ScanComplete[..Strings.Status_ScanComplete.IndexOf('{')], vm.ScanProgress, StringComparison.Ordinal);
        Assert.False(vm.CanStopWaiting);
    }

    [Fact]
    public async Task A_failed_scan_takes_the_button_away_before_its_error_box_opens()
    {
        // The error box opens over the card before the card goes, so the line under the
        // box has to have lost its button by then.
        var vm = NewViewModel();
        bool? upBeforeTheFailure = null, buttonUnderTheBox = null;
        _dialogService.When(d => d.ShowError(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => buttonUnderTheBox = vm.CanStopWaiting);
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { })));
                upBeforeTheFailure = vm.CanStopWaiting;
            },
            (_, _) => throw new InvalidProgramException("nothing anticipated this"));

        await RescanAsync(vm);

        Assert.True(upBeforeTheFailure);
        Assert.False(buttonUnderTheBox);
        Assert.False(vm.CanStopWaiting);
    }

    [Fact]
    public async Task The_spoken_line_changes_once_for_each_wait_and_never_to_the_bare_waiting_line()
    {
        // Every value the spoken name is raised with, and whether the button was up each
        // time the line itself was raised. Narrator speaks a live region's name as it
        // changes, so a bare waiting line raised before the full one would be heard twice.
        var vm = NewViewModel();
        var spoken = new ConcurrentQueue<string>();
        var buttonWhenTheLineChanged = new ConcurrentQueue<(string Line, bool Button)>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScanViewModel.ScanProgressName)) spoken.Enqueue(vm.ScanProgressName);
            if (e.PropertyName == nameof(ScanViewModel.ScanProgress)) buttonWhenTheLineChanged.Enqueue((vm.ScanProgress, vm.CanStopWaiting));
        };
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }))),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(null)));

        await RescanAsync(vm);

        // The same value raised twice in a row is one change to a screen reader: the line's
        // own change raises the name again with the value it already has.
        var changes = spoken.Aggregate(new List<string>(), (seen, name) =>
        {
            if (seen.Count == 0 || seen[^1] != name) seen.Add(name);
            return seen;
        });
        var full = DisplayHelpers.WaitingLineWithStopKey(WaitLine("D:"));

        Assert.DoesNotContain(WaitLine("D:"), changes);
        Assert.Single(changes, name => name == full);
        var at = changes.IndexOf(full);
        Assert.Equal([Milestone, full, Milestone], changes.Skip(at - 1).Take(3));
        Assert.Contains((WaitLine("D:"), true), buttonWhenTheLineChanged);
    }

    [Fact]
    public async Task A_second_wait_whose_line_reads_the_same_as_the_first_s_takes_the_button()
    {
        // Two waits on one drive with no end between them write the same line twice, so the
        // second write leaves the text as it was. The button stops the second.
        var vm = NewViewModel();
        int firstStops = 0, secondStops = 0;
        var first = new SourceFolderWait("D:", () => Interlocked.Increment(ref firstStops));
        var second = new SourceFolderWait("D:", () => Interlocked.Increment(ref secondStops));
        bool? onTheSecond = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(first)),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(second));
                onTheSecond = vm.ScanProgress == WaitLine("D:") && ReferenceEquals(vm.WaitShown, second);
                await _dispatcher.RunAsync(() => vm.StopWaitingCommand.Execute(null));
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
            });

        await RescanAsync(vm);

        Assert.True(onTheSecond);
        Assert.Equal((0, 1), (firstStops, secondStops));
    }

    [Fact]
    public async Task A_wait_and_its_end_move_the_button_where_the_line_reads_the_same_throughout()
    {
        // A step worded as the waiting line itself, so neither the wait nor its end changes
        // the text on the line.
        var vm = NewViewModel();
        var wait = new SourceFolderWait("D:", () => { });
        bool? upWithTheWait = null, goneWithItsEnd = null;
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(WaitLine("D:"))),
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(wait));
                upWithTheWait = ReferenceEquals(vm.WaitShown, wait) && vm.StopWaitingCommand.CanExecute(null)
                    && vm.ScanProgressName == DisplayHelpers.WaitingLineWithStopKey(WaitLine("D:"));
            },
            async (p, _) =>
            {
                await ShowAsync(p, ScanProgressUpdate.Waiting(null));
                goneWithItsEnd = vm.WaitShown is null && !vm.StopWaitingCommand.CanExecute(null)
                    && vm.ScanProgress == WaitLine("D:") && vm.ScanProgressName == WaitLine("D:");
            });

        await RescanAsync(vm);

        Assert.True(upWithTheWait);
        Assert.True(goneWithItsEnd);
    }

    [Fact]
    public async Task A_wait_and_its_end_each_move_the_button_once()
    {
        // Every change the button is raised with, from the scan's first line to its last.
        var vm = NewViewModel();
        var moves = new ConcurrentQueue<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScanViewModel.CanStopWaiting)) moves.Enqueue(vm.CanStopWaiting);
        };
        ScanReports(
            (p, _) => ShowAsync(p, new ScanProgressUpdate(Milestone)),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }))),
            (p, _) => ShowAsync(p, ScanProgressUpdate.Waiting(null)));

        await RescanAsync(vm);

        Assert.Equal([true, false], moves);
    }
}
