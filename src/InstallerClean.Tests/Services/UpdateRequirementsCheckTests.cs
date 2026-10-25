using System.Net;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// An update's read of what the release's update.json requires
/// (<see cref="AppUpdater.CheckRequirementsAsync"/>) when the token is cancelled while it
/// runs: a cancel is answered as a cancel, never as a refusal or a read that failed, and a
/// file read in full is kept.
/// </summary>
public sealed class UpdateRequirementsCheckTests
{
    [Fact]
    public async Task A_cancelled_check_against_a_kept_refusing_file_answers_the_cancel_not_the_refusal()
    {
        var updater = Updater(new Answers(HttpStatusCode.OK, RefusesEveryDownload));
        Assert.Equal(UpdateRefusal.CannotInstallFromThisCopy,
            await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => updater.CheckRequirementsAsync("9.9.9", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task A_release_file_read_in_full_as_the_check_is_cancelled_is_kept()
    {
        using var cts = new CancellationTokenSource();
        var network = new Answers(HttpStatusCode.OK, AllowsEveryDownload, cancelAtBodysEnd: cts);
        var updater = Updater(network);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));

        Assert.Null(await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));
        Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task A_server_error_as_the_check_is_cancelled_answers_the_cancel()
    {
        using var cts = new CancellationTokenSource();
        var updater = Updater(new Answers(HttpStatusCode.InternalServerError, null, cancelAsItAnswers: cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));
    }

    [Fact]
    public async Task A_check_cancelled_while_the_pc_is_read_for_dotnet_framework_answers_the_cancel_not_the_refusal()
    {
        using var cts = new CancellationTokenSource();
        var updater = Updater(new Answers(HttpStatusCode.OK, NeedsNetFramework48), CancelsAsItIsRead(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckRequirementsAsync("9.9.9", cts.Token));
        Assert.Equal(UpdateRefusal.NeedsNetFramework48,
            await updater.CheckRequirementsAsync("9.9.9", CancellationToken.None));
    }

    [Fact]
    public async Task An_update_cancelled_while_the_pc_is_read_for_dotnet_framework_answers_the_cancel_not_the_refusal()
    {
        using var cts = new CancellationTokenSource();
        var updater = Updater(new Answers(HttpStatusCode.OK, NeedsNetFramework48), CancelsAsItIsRead(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.PrepareAsync("9.9.9", null, cts.Token));
    }

    /// <summary>An update.json that lists no download, so every copy is refused.</summary>
    private const string RefusesEveryDownload = """{"schema": 1, "downloads": {}}""";

    /// <summary>An update.json whose every download needs .NET Framework 4.8.</summary>
    private const string NeedsNetFramework48 =
        """{"schema": 1, "downloads": {"setup": {"requires": ["netfx-4.8"]}, "portable": {"requires": ["netfx-4.8"]}, "slim": {"requires": ["netfx-4.8"]}, "cli": {"requires": ["netfx-4.8"]}}}""";

    /// <summary>An update.json that lists every download with nothing required.</summary>
    private const string AllowsEveryDownload =
        """{"schema": 1, "downloads": {"setup": {"requires": []}, "portable": {"requires": []}, "slim": {"requires": []}, "cli": {"requires": []}}}""";

    /// <summary>A registry that cancels <paramref name="cts"/> as .NET Framework's release is read, and has none.</summary>
    private static IRegistryReader CancelsAsItIsRead(CancellationTokenSource cts)
    {
        var registry = Substitute.For<IRegistryReader>();
        registry.LocalMachineDwordValue(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            cts.Cancel();
            return new RegistryDwordRead(RegistryDwordState.Absent);
        });
        return registry;
    }

    /// <summary>
    /// A portable copy's updater whose requests <paramref name="network"/> answers, and whose
    /// PC <paramref name="registry"/> reads. Nothing here reaches a write.
    /// </summary>
    private static AppUpdater Updater(HttpMessageHandler network, IRegistryReader? registry = null) =>
        new(AppCopyKind.Portable, Path.Combine(Path.GetTempPath(), "InstallerClean.exe"),
            new UpdateDownloadService(network, null), Path.GetTempPath(), AppUpdater.WindowSetupArguments,
            UpdateStagingFolder.AdministratorsOnly, registry ?? Substitute.For<IRegistryReader>(), new UpdateLeftoverGate());

    /// <summary>
    /// Answers every request with <paramref name="status"/> and <paramref name="body"/>, and
    /// counts the requests. Cancels <paramref name="cancelAsItAnswers"/> as it answers, and
    /// <paramref name="cancelAtBodysEnd"/> once the body's last byte has been read.
    /// </summary>
    private sealed class Answers(
        HttpStatusCode status, string? body,
        CancellationTokenSource? cancelAsItAnswers = null, CancellationTokenSource? cancelAtBodysEnd = null)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            cancelAsItAnswers?.Cancel();
            var bytes = System.Text.Encoding.UTF8.GetBytes(body ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = cancelAtBodysEnd is null
                    ? new ByteArrayContent(bytes)
                    : new StreamContent(new CancelsAtEnd(bytes, cancelAtBodysEnd)),
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
}
