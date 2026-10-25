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
            release.Wait();
        }));
        inPass.Wait();
        Task mark;
        try
        {
            mark = Task.Run(() =>
            {
                marking.Set();
                gate.HoldForUpdate();
            });
            // The hold has started, so a wait that ends here is the lock not holding it.
            marking.Wait();

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
        Assert.Null(await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));

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

    [Fact]
    public async Task A_cancelled_check_against_a_kept_refusing_file_answers_the_cancel_not_the_refusal()
    {
        var updater = Updater(new UpdateLeftoverGate(), Path.GetTempPath(), RefusesEveryDownload);
        Assert.Equal(UpdateRefusal.CannotInstallFromThisCopy,
            await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => updater.CheckRequirementsAsync("9.9.9", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task A_release_file_read_in_full_as_the_check_is_cancelled_is_kept()
    {
        using var cts = new CancellationTokenSource();
        var network = new CancelsAsItAnswers(cts, System.Net.HttpStatusCode.OK, AllowsEveryDownload, atBodysEnd: true);
        var updater = Updater(new UpdateLeftoverGate(), Path.GetTempPath(), network);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));

        Assert.Null(await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));
        Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task A_server_error_as_the_check_is_cancelled_answers_the_cancel()
    {
        using var cts = new CancellationTokenSource();
        var network = new CancelsAsItAnswers(cts, System.Net.HttpStatusCode.InternalServerError, null);
        var updater = Updater(new UpdateLeftoverGate(), Path.GetTempPath(), network);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));
    }

    [Fact]
    public async Task A_check_cancelled_while_the_pc_is_read_for_dotnet_framework_answers_the_cancel_not_the_refusal()
    {
        using var cts = new CancellationTokenSource();
        var registry = NSubstitute.Substitute.For<IRegistryReader>();
        registry.LocalMachineDwordValue(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            cts.Cancel();
            return new RegistryDwordRead(RegistryDwordState.Absent);
        });
        var updater = Updater(new UpdateLeftoverGate(), Path.GetTempPath(), new Answers(NeedsNetFramework48, null),
            registry: registry);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));
        Assert.Equal(UpdateRefusal.NeedsNetFramework48,
            await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));
    }

    /// <summary>
    /// The staging folder's descriptor owned by, and open to, the account running the suite,
    /// which runs without administrator rights and so cannot make Administrators the owner.
    /// </summary>
    private static readonly string ThisAccountOnly = DescriptorFor(WindowsIdentity.GetCurrent().User!.Value);

    private static string DescriptorFor(string sid) => $"O:{sid}D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{sid})";

    /// <summary>An update.json that lists no download, so every copy is refused.</summary>
    private const string RefusesEveryDownload = """{"schema": 1, "downloads": {}}""";

    /// <summary>An update.json whose every download needs .NET Framework 4.8.</summary>
    private const string NeedsNetFramework48 =
        """{"schema": 1, "downloads": {"setup": {"requires": ["netfx-4.8"]}, "portable": {"requires": ["netfx-4.8"]}, "slim": {"requires": ["netfx-4.8"]}, "cli": {"requires": ["netfx-4.8"]}}}""";

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
        Updater(gate, stagingRoot, new Answers(manifest, onDownload), descriptor, kind);

    /// <summary>The same, with every request answered by <paramref name="network"/>.</summary>
    private static AppUpdater Updater(
        UpdateLeftoverGate gate, string stagingRoot, HttpMessageHandler network, string? descriptor = null,
        AppCopyKind kind = AppCopyKind.Portable, IRegistryReader? registry = null) =>
        new(kind, Path.Combine(Path.GetTempPath(), "InstallerClean.exe"),
            new UpdateDownloadService(network, null), stagingRoot,
            AppUpdater.WindowSetupArguments, descriptor ?? UpdateStagingFolder.AdministratorsOnly,
            registry ?? NSubstitute.Substitute.For<IRegistryReader>(), gate);

    /// <summary>
    /// Answers each request with <paramref name="status"/> and <paramref name="body"/>, and
    /// counts the requests. Cancels <paramref name="cts"/> as it answers, or where
    /// <paramref name="atBodysEnd"/> is set, as the body's last byte has been read.
    /// </summary>
    private sealed class CancelsAsItAnswers(
        CancellationTokenSource cts, System.Net.HttpStatusCode status, string? body, bool atBodysEnd = false)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body ?? string.Empty);
            if (!atBodysEnd) cts.Cancel();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = atBodysEnd ? new StreamContent(new CancelsAtEnd(bytes, cts)) : new ByteArrayContent(bytes),
            });
        }
    }

    /// <summary>A body that cancels <paramref name="cts"/> when a read finds nothing left.</summary>
    private sealed class CancelsAtEnd(byte[] bytes, CancellationTokenSource cts) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read == 0) cts.Cancel();
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (read == 0) cts.Cancel();
            return read;
        }
    }

    private sealed class Answers(string? manifest, Action? onDownload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/" + UpdateManifest.FileName, StringComparison.Ordinal))
                return Task.FromResult(manifest is not null
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(manifest) }
                    : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            onDownload?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
