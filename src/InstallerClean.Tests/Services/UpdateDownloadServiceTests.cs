using System.Net;
using System.Net.Http;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The download an update makes: GitHub's redirects followed only to https, the body
/// refused when it is too large, not a Windows program, cut short or stalled, and
/// nothing left at the destination after any failure.
/// </summary>
public sealed class UpdateDownloadServiceTests : IDisposable
{
    private static readonly Uri ReleaseAddress =
        new("https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/InstallerClean-portable.exe");

    private static readonly Uri AssetAddress =
        new("https://release-assets.githubusercontent.com/some/asset");

    private readonly string _folder = Directory.CreateTempSubdirectory("ic-download-").FullName;

    private string Destination => Path.Combine(_folder, "download.exe");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    /// <summary>Answers each request from a table keyed by its address, and records the addresses.</summary>
    private sealed class RoutedHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }

    private static HttpResponseMessage Redirect(Uri to)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = to;
        return response;
    }

    private static HttpResponseMessage Body(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static byte[] Program(int length = 4096)
    {
        var bytes = new byte[length];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        for (var i = 2; i < length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    [Fact]
    public async Task Follows_the_redirect_to_the_asset_and_writes_what_it_answered()
    {
        var program = Program();
        var handler = new RoutedHandler(uri => uri == ReleaseAddress ? Redirect(AssetAddress) : Body(program));
        var service = new UpdateDownloadService(handler, null);
        var reported = new List<double?>();

        await service.DownloadAsync(ReleaseAddress, Destination, new SyncProgress(reported.Add), CancellationToken.None);

        Assert.Equal([ReleaseAddress, AssetAddress], handler.Requested);
        Assert.Equal(program, await File.ReadAllBytesAsync(Destination));
        Assert.Equal(1d, reported[^1]);
    }

    /// <summary>A stream that cannot seek, so content over it carries no length.</summary>
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static readonly Uri ManifestAddress =
        new("https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/update.json");

    [Fact]
    public async Task A_read_follows_the_redirect_and_answers_the_bytes_in_memory()
    {
        var text = "{}"u8.ToArray();
        var handler = new RoutedHandler(uri => uri == ManifestAddress ? Redirect(AssetAddress) : Body(text));
        var service = new UpdateDownloadService(handler, null);

        var read = await service.ReadAsync(ManifestAddress, 1024, CancellationToken.None);

        Assert.Equal(text, read);
        Assert.Equal([ManifestAddress, AssetAddress], handler.Requested);
    }

    [Fact]
    public async Task A_read_answers_null_for_a_404_after_a_redirect()
    {
        var handler = new RoutedHandler(uri => uri == ManifestAddress
            ? Redirect(AssetAddress)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new UpdateDownloadService(handler, null);

        Assert.Null(await service.ReadAsync(ManifestAddress, 1024, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_read_fails_as_a_download_for_any_status_but_404(HttpStatusCode status)
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(status));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.ReadAsync(ManifestAddress, 1024, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
    }

    [Fact]
    public async Task A_read_over_its_cap_fails_as_a_download_with_or_without_a_length()
    {
        var big = new byte[2048];
        var withLength = new UpdateDownloadService(new RoutedHandler(_ => Body(big)), null);
        var withoutLength = new UpdateDownloadService(new RoutedHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnseekableStream(big)) }), null);

        foreach (var service in new[] { withLength, withoutLength })
        {
            var failure = await Assert.ThrowsAsync<UpdateFailedException>(
                () => service.ReadAsync(ManifestAddress, 1024, CancellationToken.None));
            Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        }
    }

    [Fact]
    public async Task A_404_is_still_a_failed_download_for_a_download()
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_redirect_to_an_address_that_is_not_https_is_refused()
    {
        var handler = new RoutedHandler(uri => uri == ReleaseAddress
            ? Redirect(new Uri("http://release-assets.githubusercontent.com/some/asset"))
            : Body(Program()));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.Single(handler.Requested);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_first_address_that_is_not_https_is_refused_before_any_request()
    {
        var handler = new RoutedHandler(_ => Body(Program()));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(() => service.DownloadAsync(
            new Uri("http://github.com/no-faff/InstallerClean/releases/download/v3.5.0/x.exe"),
            Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task A_redirect_that_never_ends_is_refused()
    {
        var handler = new RoutedHandler(_ => Redirect(AssetAddress));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
    }

    [Fact]
    public async Task An_error_from_the_server_is_a_download_failure()
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_length_over_the_ceiling_is_refused_before_anything_is_written()
    {
        var handler = new RoutedHandler(_ =>
        {
            var response = Body(Program());
            response.Content.Headers.ContentLength = UpdateDownloadService.MaxBytes + 1;
            return response;
        });
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_page_in_place_of_a_program_is_refused_and_not_left_behind()
    {
        var handler = new RoutedHandler(_ => Body("<html><body>Sign in</body></html>"u8.ToArray()));
        var service = new UpdateDownloadService(handler, null);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_body_that_stops_arriving_fails_on_the_stall_timeout()
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream("MZ"u8.ToArray())),
        });
        var service = new UpdateDownloadService(handler, TimeSpan.FromMilliseconds(200));

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Cancelling_throws_cancellation_and_leaves_nothing()
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream("MZ"u8.ToArray())),
        });
        var service = new UpdateDownloadService(handler, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, cts.Token));

        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_connection_broken_off_by_cancelling_throws_cancellation_and_leaves_nothing()
    {
        var handler = new RoutedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream("MZ"u8.ToArray(), breaksOffOnCancel: true)),
        });
        var service = new UpdateDownloadService(handler, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync(ReleaseAddress, Destination, null, cts.Token));

        Assert.False(File.Exists(Destination));
    }

    /// <summary>
    /// Reports synchronously, so the test reads every value once the call returns,
    /// where <see cref="Progress{T}"/> posts each to the thread pool.
    /// </summary>
    private sealed class SyncProgress(Action<double?> report) : IProgress<double?>
    {
        public void Report(double? value) => report(value);
    }

    /// <summary>
    /// Hands over its bytes, then waits on every later read until the read is
    /// cancelled, as a connection that has stopped sending does. Where
    /// <paramref name="breaksOffOnCancel"/> is set, the cancelled read throws an
    /// IOException, as a socket read the cancel breaks off can.
    /// </summary>
    private sealed class StallingStream(byte[] first, bool breaksOffOnCancel = false) : Stream
    {
        private bool _sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                first.CopyTo(buffer);
                return first.Length;
            }
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException ex) when (breaksOffOnCancel)
            {
                throw new IOException("The read was broken off.", ex);
            }
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
