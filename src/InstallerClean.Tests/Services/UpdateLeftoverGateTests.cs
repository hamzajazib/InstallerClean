using System.Net;
using System.Security.Principal;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The gate between the start-up clean-up and this process's own update
/// (<see cref="UpdateLeftoverGate"/>): no pass runs while an update holds it, and passes
/// run again once every hold is released. An update that ends without being prepared
/// releases its hold; a prepared one releases it in its undo
/// (<c>UpdateInPlaceTests</c>).
/// </summary>
public sealed class UpdateLeftoverGateTests
{
    [Fact]
    public void A_pass_runs_while_no_update_holds_the_gate()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        Assert.True(gate.TryRunPass(() => passes++));
        Assert.True(gate.TryRunPass(() => passes++));

        Assert.Equal(2, passes);
    }

    [Fact]
    public void No_pass_runs_while_an_update_holds_the_gate()
    {
        var gate = new UpdateLeftoverGate();
        var passes = 0;

        gate.HoldForUpdate();

        Assert.False(gate.TryRunPass(() => passes++));
        Assert.Equal(0, passes);
    }

    [Fact]
    public void Passes_run_again_once_every_hold_is_released()
    {
        var gate = new UpdateLeftoverGate();
        var first = gate.HoldForUpdate();
        var second = gate.HoldForUpdate();

        first.Release();
        first.Release();
        Assert.False(gate.TryRunPass(() => { }));

        second.Release();
        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task A_wait_for_the_gate_ends_when_the_last_hold_is_released()
    {
        var gate = new UpdateLeftoverGate();
        Assert.True(gate.WhenFree().IsCompleted);
        var first = gate.HoldForUpdate();
        var second = gate.HoldForUpdate();

        var free = gate.WhenFree();
        first.Release();
        Assert.False(free.IsCompleted);

        second.Release();
        await free.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_update_taking_the_gate_during_a_pass_waits_for_it_to_end()
    {
        var gate = new UpdateLeftoverGate();
        using var inPass = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var marking = new ManualResetEventSlim();
        var pass = Task.Run(() => gate.TryRunPass(() =>
        {
            inPass.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        }));
        Task? mark = null;
        try
        {
            Assert.True(inPass.Wait(TimeSpan.FromSeconds(10)), "The pass never ran.");
            mark = Task.Run(() =>
            {
                marking.Set();
                gate.HoldForUpdate();
            });
            // The hold has started, so a wait that ends here is the lock not holding it.
            Assert.True(marking.Wait(TimeSpan.FromSeconds(10)), "The hold was never started.");

            await Assert.ThrowsAsync<TimeoutException>(() => mark.WaitAsync(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            release.Set();
        }

        Assert.True(await pass.WaitAsync(TimeSpan.FromSeconds(10)));
        await mark.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_with_its_release_file_kept_waits_for_a_running_pass_off_the_callers_thread()
    {
        var gate = new UpdateLeftoverGate();
        // A file where the staging root should be, so the update ends at its first write.
        var root = Path.GetTempFileName();
        using var inPass = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // Set as the pass ends, inside the gate's lock, so a hold can only follow it.
        var passEnded = false;
        var pass = Task.Run(() => gate.TryRunPass(() =>
        {
            inPass.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            Volatile.Write(ref passEnded, true);
        }));
        try
        {
            Assert.True(inPass.Wait(TimeSpan.FromSeconds(10)), "The pass never ran.");
            var updater = Updater(gate, root, AllowsEveryDownload);
            Assert.Null(await updater.CheckRequirementsAsync("9.9.9", null, CancellationToken.None));

            // The release file is kept, so nothing the update does before its hold waits on
            // the network. The call comes back while the pass still runs, and the update
            // then waits for the pass: one that went on to its first write would already
            // have failed there.
            var update = updater.PrepareAsync("9.9.9", null, CancellationToken.None);
            Assert.False(Volatile.Read(ref passEnded), "The call came back only once the pass had ended.");
            await Assert.ThrowsAsync<TimeoutException>(() => update.WaitAsync(TimeSpan.FromMilliseconds(200)));
            release.Set();

            Assert.Equal(UpdateFailureStage.Install,
                (await Assert.ThrowsAsync<UpdateFailedException>(() => update.WaitAsync(TimeSpan.FromSeconds(10)))).Stage);
            Assert.True(await pass.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(gate.TryRunPass(() => { }));
        }
        finally
        {
            release.Set();
            File.Delete(root);
        }
    }

    [Fact]
    public async Task An_update_whose_first_write_fails_leaves_the_clean_up_running()
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
            Assert.True(gate.TryRunPass(() => { }));
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
        var updater = Updater(gate, Path.GetTempPath(), AllowsEveryDownload, kind: AppCopyKind.Undetermined);

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

    [Fact]
    public async Task An_update_cancelled_before_it_writes_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var updater = Updater(gate, Path.GetTempPath(), AllowsEveryDownload);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => updater.PrepareAsync("9.9.9", null, new CancellationToken(canceled: true)));

        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_cancelled_after_its_release_file_was_read_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var updater = Updater(gate, Path.GetTempPath(), AllowsEveryDownload);
        Assert.Null(await updater.CheckRequirementsAsync("9.9.9", null, CancellationToken.None));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => updater.PrepareAsync("9.9.9", null, new CancellationToken(canceled: true)));

        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task An_update_cancelled_during_its_download_leaves_the_clean_up_running()
    {
        var gate = new UpdateLeftoverGate();
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var cts = new CancellationTokenSource();
            var heldDuringDownload = false;
            var updater = Updater(gate, root, AllowsEveryDownload, ThisAccountOnly, onDownload: () =>
            {
                heldDuringDownload = !gate.TryRunPass(() => { });
                cts.Cancel();
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.PrepareAsync("9.9.9", null, cts.Token));

            Assert.True(heldDuringDownload);
            Assert.True(gate.TryRunPass(() => { }));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The staging folder's descriptor owned by, and open to, the account running the suite,
    /// which runs without administrator rights and so cannot make Administrators the owner.
    /// </summary>
    private static readonly string ThisAccountOnly = DescriptorFor(WindowsIdentity.GetCurrent().User!.Value);

    private static string DescriptorFor(string sid) => $"O:{sid}D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{sid})";

    /// <summary>An update.json that lists every download with nothing required.</summary>
    private const string AllowsEveryDownload =
        """{"schema": 1, "downloads": {"setup": {"requires": []}, "portable": {"requires": []}, "slim": {"requires": []}, "cli": {"requires": []}}}""";

    /// <summary>
    /// An updater for a copy of <paramref name="kind"/> whose requests are answered here:
    /// the release's update.json with <paramref name="manifest"/>, or not found where it
    /// is null, and any other request with nothing, after <paramref name="onDownload"/> runs.
    /// Its staging folder is made with <paramref name="descriptor"/>, or the shipping one.
    /// </summary>
    private static AppUpdater Updater(
        UpdateLeftoverGate gate, string stagingRoot, string? manifest, string? descriptor = null,
        AppCopyKind kind = AppCopyKind.Portable, Action? onDownload = null) =>
        new(kind, Path.Combine(Path.GetTempPath(), "InstallerClean.exe"),
            new UpdateDownloadService(new Answers(manifest, onDownload), null), stagingRoot,
            AppUpdater.WindowSetupArguments, descriptor ?? UpdateStagingFolder.AdministratorsOnly,
            Substitute.For<IRegistryReader>(), gate);

    private sealed class Answers(string? manifest, Action? onDownload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/" + UpdateManifest.FileName, StringComparison.Ordinal))
                return Task.FromResult(manifest is not null
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            onDownload?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
