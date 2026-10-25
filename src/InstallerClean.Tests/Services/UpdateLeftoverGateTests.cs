using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The gate between the start-up clean-up and an update this process begins
/// (<see cref="UpdateLeftoverGate"/>): a pass runs until an update has begun, and none
/// after.
/// </summary>
public sealed class UpdateLeftoverGateTests
{
    [Fact]
    public void A_pass_runs_while_no_update_has_begun()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        Assert.True(gate.TryRunPass(() => passes++));
        Assert.True(gate.TryRunPass(() => passes++));

        Assert.Equal(2, passes);
    }

    [Fact]
    public void No_pass_runs_once_an_update_has_begun()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        gate.MarkUpdateStarted();

        Assert.False(gate.TryRunPass(() => passes++));
        Assert.Equal(0, passes);
    }

    [Fact]
    public async Task An_update_that_begins_during_a_pass_waits_for_it_to_end()
    {
        var gate = new UpdateLeftoverGate();
        using var inPass = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pass = Task.Run(() => gate.TryRunPass(() =>
        {
            inPass.Set();
            release.Wait();
        }));
        inPass.Wait();

        var mark = Task.Run(gate.MarkUpdateStarted);

        Assert.False(mark.Wait(TimeSpan.FromMilliseconds(200)));
        release.Set();
        Assert.True(await pass);
        await mark;
        Assert.True(gate.UpdateStarted);
    }

    [Fact]
    public async Task Preparing_an_update_marks_it_begun_even_where_the_update_then_fails()
    {
        var gate = new UpdateLeftoverGate();
        var updater = new AppUpdater(AppCopyKind.Undetermined, Path.Combine(Path.GetTempPath(), "InstallerClean.exe"),
            new UpdateDownloadService(), Path.GetTempPath(), AppUpdater.WindowSetupArguments,
            UpdateStagingFolder.AdministratorsOnly, NSubstitute.Substitute.For<IRegistryReader>(), gate);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.PrepareAsync("9.9.9", null, CancellationToken.None));

        Assert.True(gate.UpdateStarted);
        Assert.False(gate.TryRunPass(() => { }));
    }
}
