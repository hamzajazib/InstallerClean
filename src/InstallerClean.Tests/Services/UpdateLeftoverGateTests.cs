using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The gate between the start-up clean-up and this process's own update
/// (<see cref="UpdateLeftoverGate"/>): a pass runs until an update reaches its first
/// write, and none after, whatever becomes of that update.
/// </summary>
public sealed class UpdateLeftoverGateTests
{
    [Fact]
    public void A_pass_runs_while_no_update_has_reached_its_first_write()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        Assert.True(gate.TryRunPass(() => passes++));
        Assert.True(gate.TryRunPass(() => passes++));

        Assert.Equal(2, passes);
    }

    [Fact]
    public void No_pass_runs_once_an_update_has_reached_its_first_write()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        gate.MarkUpdateWriting();

        Assert.False(gate.TryRunPass(() => passes++));
        Assert.Equal(0, passes);
    }

    [Fact]
    public async Task An_update_reaching_its_first_write_during_a_pass_waits_for_it_to_end()
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
        Task mark;
        try
        {
            mark = Task.Run(gate.MarkUpdateWriting);

            Assert.False(mark.Wait(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            release.Set();
        }

        Assert.True(await pass);
        await mark;
        Assert.False(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_that_fails_at_its_first_write_still_stops_the_clean_up()
    {
        var gate = new UpdateLeftoverGate();
        // A file where the staging root should be, so the staging folder cannot be made.
        var root = Path.GetTempFileName();
        try
        {
            var updater = Updater(gate, root, AllowsEveryDownload);

            var failed = await Assert.ThrowsAsync<UpdateFailedException>(
                () => updater.PrepareAsync("9.9.9", null, CancellationToken.None));

            Assert.Equal(UpdateFailureStage.Install, failed.Stage);
            Assert.False(gate.TryRunPass(() => { }));
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public async Task An_update_refused_before_it_writes_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var updater = Updater(gate, Path.GetTempPath(), manifest: null);

        await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.PrepareAsync("9.9.9", null, CancellationToken.None));

        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_from_a_copy_with_nothing_to_download_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var updater = Updater(gate, Path.GetTempPath(), AllowsEveryDownload, AppCopyKind.Undetermined);

        await Assert.ThrowsAsync<InvalidOperationException>(() => updater.PrepareAsync("9.9.9", null, CancellationToken.None));

        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_whose_release_file_cannot_be_read_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var updater = Updater(gate, Path.GetTempPath(), "not json");

        await Assert.ThrowsAsync<UpdateFailedException>(() => updater.PrepareAsync("9.9.9", null, CancellationToken.None));

        Assert.True(gate.TryRunPass(() => { }));
    }

    /// <summary>An update.json that lists every download with nothing required.</summary>
    private const string AllowsEveryDownload =
        """{"schema": 1, "downloads": {"setup": {"requires": []}, "portable": {"requires": []}, "slim": {"requires": []}, "cli": {"requires": []}}}""";

    /// <summary>
    /// An updater for a copy of <paramref name="kind"/> whose requests are answered here:
    /// the release's update.json with <paramref name="manifest"/>, or not found where it
    /// is null, and any other request with nothing.
    /// </summary>
    private static AppUpdater Updater(
        UpdateLeftoverGate gate, string stagingRoot, string? manifest, AppCopyKind kind = AppCopyKind.Portable) =>
        new(kind, Path.Combine(Path.GetTempPath(), "InstallerClean.exe"),
            new UpdateDownloadService(new Answers(manifest), null), stagingRoot, AppUpdater.WindowSetupArguments,
            UpdateStagingFolder.AdministratorsOnly, NSubstitute.Substitute.For<IRegistryReader>(), gate);

    private sealed class Answers(string? manifest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("/" + UpdateManifest.FileName, StringComparison.Ordinal)
                && manifest is not null
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(manifest) }
                    : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
