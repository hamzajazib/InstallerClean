namespace InstallerClean.Services;

/// <summary>
/// Gets a newer version ready to start: downloaded, and where the copy replaces its
/// own file, written beside it to take its place.
/// </summary>
public interface IAppUpdater
{
    /// <summary>The kind of copy running, decided once at start.</summary>
    AppCopyKind Kind { get; }

    /// <summary>Whether this copy can put a newer version in its own place.</summary>
    bool CanUpdateItself { get; }

    /// <summary>
    /// Downloads <paramref name="version"/> as the same kind of copy, checks that the file
    /// carries that version, and answers with what to start. Reports the fraction
    /// downloaded, or null where the length is not known. Throws
    /// <see cref="UpdateFailedException"/> where any step fails, a download that does not
    /// carry the version included, and
    /// <see cref="OperationCanceledException"/> where the token is cancelled; neither
    /// leaves anything changed on disk.
    /// </summary>
    Task<PreparedUpdate> PrepareAsync(
        string version, IProgress<double?>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// A newer version ready to start, and the step that takes away what getting it ready
/// left on disk if it is not started.
/// </summary>
public sealed class PreparedUpdate
{
    private Action? _undo;

    public PreparedUpdate(string version, string fileToStart, string arguments, Action undo, string? replacement = null)
    {
        Version = version;
        FileToStart = fileToStart;
        Arguments = arguments;
        Replacement = replacement;
        _undo = undo;
    }

    /// <summary>The version this is, as the update check gave it.</summary>
    public string Version { get; }

    /// <summary>The program to start: the newer setup, or the app's own path, where the newer version is to go.</summary>
    public string FileToStart { get; }

    /// <summary>The command line it is started with.</summary>
    public string Arguments { get; }

    /// <summary>
    /// Where the copy replaces its own file, the newer version written beside it
    /// (<see cref="SelfReplacement.StageAsync"/>), which <see cref="SelfReplacement.SwapIn"/>
    /// puts at <see cref="FileToStart"/>. Null for the setup's copy, whose setup does that.
    /// </summary>
    public string? Replacement { get; }

    /// <summary>
    /// Takes away what getting it ready left: the download, or the newer version written
    /// beside the running exe. Runs once, however often it is called. Never throws.
    /// </summary>
    public void Undo()
    {
        try { Interlocked.Exchange(ref _undo, null)?.Invoke(); }
        catch (Exception ex) { Helpers.CrashLog.TryWrite(ex); }
    }
}

/// <summary>
/// The update from inside the app. The setup's copy downloads the newer setup and runs
/// it; the portable, the slim and the command line's own download download the newer
/// version of the same file and write it beside their own. Each download is checked for
/// the version it was downloaded as before anything uses it. A build from source and
/// Scoop's copy have no download, and <see cref="PrepareAsync"/> refuses them.
/// </summary>
public sealed class AppUpdater : IAppUpdater
{
    /// <summary>
    /// What the window starts the newer setup with. /SILENT shows Setup's progress and
    /// asks nothing, /SP- leaves out the page asking whether to install, and /NORESTART
    /// keeps Setup from restarting Windows. /UPDATEFROMAPP is the app's own: the setup
    /// script starts the app again once it has installed when it sees it, which it
    /// otherwise never does on a silent install.
    /// </summary>
    public const string WindowSetupArguments = "/SILENT /SP- /NORESTART /UPDATEFROMAPP";

    private readonly string _exePath;
    private readonly string _setupArguments;
    private readonly UpdateDownloadService _downloader;
    private readonly string _stagingRoot;
    private readonly string _stagingDescriptor;

    /// <param name="kind">The kind of copy running.</param>
    /// <param name="exePath">The running exe's path.</param>
    /// <param name="downloader">What fetches the newer version.</param>
    /// <param name="stagingRoot">
    /// The folder the download's own folder is made in: %ProgramData% everywhere but
    /// a test.
    /// </param>
    /// <param name="setupArguments">
    /// What the newer setup is started with, where the copy is the setup's:
    /// <see cref="WindowSetupArguments"/> for the window, and the command line's own,
    /// which leave the window closed.
    /// </param>
    public AppUpdater(
        AppCopyKind kind, string exePath, UpdateDownloadService downloader, string stagingRoot,
        string setupArguments)
        : this(kind, exePath, downloader, stagingRoot, setupArguments, UpdateStagingFolder.AdministratorsOnly) { }

    /// <summary>
    /// Test seam: the staging folder's descriptor given as SDDL, so a suite without
    /// administrator rights can write into it.
    /// </summary>
    internal AppUpdater(
        AppCopyKind kind, string exePath, UpdateDownloadService downloader, string stagingRoot,
        string setupArguments, string stagingDescriptor)
    {
        Kind = kind;
        _exePath = exePath;
        _setupArguments = setupArguments;
        _downloader = downloader;
        _stagingRoot = stagingRoot;
        _stagingDescriptor = stagingDescriptor;
    }

    public AppCopyKind Kind { get; }

    public bool CanUpdateItself => AppCopy.CanUpdateItself(Kind);

    public async Task<PreparedUpdate> PrepareAsync(
        string version, IProgress<double?>? progress, CancellationToken cancellationToken)
    {
        var address = AppCopy.DownloadAddress(Kind, version);
        var fileName = AppCopy.DownloadFileName(Kind, version);
        if (address is null || fileName is null)
            throw new InvalidOperationException($"A copy of kind {Kind} has no download to update from.");

        UpdateStagingFolder folder;
        try
        {
            folder = UpdateStagingFolder.Create(_stagingRoot, _stagingDescriptor);
        }
        catch (Exception ex)
        {
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The folder the download is saved in could not be created.", ex);
        }

        var staged = Path.Combine(folder.FullPath, fileName);
        try
        {
#if DEBUG
            if (LocalFile() is { } local)
                await new UpdateDownloadService(new LocalFileAnswer(local), null)
                    .DownloadAsync(address, staged, progress, cancellationToken).ConfigureAwait(false);
            else
#endif
            await _downloader.DownloadAsync(address, staged, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            folder.TryRemoveIfEmpty();
            throw;
        }

        // Read in the staging folder, which only Administrators and SYSTEM can write to. A file
        // that is not the version asked for is deleted here, before anything copies or starts it.
        if (Helpers.FileVersionResource.Read(staged) is not { } carried
            || !Helpers.FileVersionResource.IsRelease(carried, version))
        {
            Helpers.StorageHelpers.TryDeleteTempFile(staged);
            folder.TryRemoveIfEmpty();
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The downloaded file does not carry the version it was downloaded as.");
        }

        if (Kind == AppCopyKind.Setup)
        {
            return new PreparedUpdate(version, staged, _setupArguments, () =>
            {
                Helpers.StorageHelpers.TryDeleteTempFile(staged);
                folder.TryRemoveIfEmpty();
            });
        }

        string replacement;
        try
        {
            replacement = await SelfReplacement.StageAsync(_exePath, staged, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Helpers.StorageHelpers.TryDeleteTempFile(staged);
            folder.TryRemoveIfEmpty();
        }
        return new PreparedUpdate(version, _exePath, string.Empty,
            () => Helpers.StorageHelpers.TryDeleteTempFile(replacement), replacement);
    }

#if DEBUG
    /// <summary>
    /// Set to the path of a setup, a Debug build whose copy is the setup's runs that
    /// setup in place of downloading one, so a setup built from this tree's script can
    /// be tried before any release carries it. The setup has to carry the version the
    /// update check names, as any download does, which
    /// <see cref="UpdateCheckService.PretendLatestVariable"/> can set. The name and its read
    /// sit only inside <c>#if DEBUG</c> blocks, and <c>scripts/check-debug-only-switches.mjs</c>
    /// holds them there.
    /// </summary>
    internal const string LocalSetupVariable = "INSTALLERCLEAN_DEBUG_LOCAL_SETUP";

    /// <summary>
    /// Set to the full path of a newer copy of the kind this one replaces itself with (the
    /// portable, the slim or the command line), a Debug build that replaces itself takes
    /// that file as the body of its download, and then stages and swaps it in as it would
    /// any download, so an update to a build from this tree can be tried offline. It has to
    /// carry the version the update check names, as the setup does. Held inside
    /// <c>#if DEBUG</c> the same way.
    /// </summary>
    internal const string LocalDownloadVariable = "INSTALLERCLEAN_DEBUG_LOCAL_DOWNLOAD";

    /// <summary>
    /// The file <see cref="LocalSetupVariable"/> names for the setup's copy, or the one
    /// <see cref="LocalDownloadVariable"/> names for every other kind; null where it is unset.
    /// </summary>
    private string? LocalFile()
    {
        var path = Kind == AppCopyKind.Setup
            ? Environment.GetEnvironmentVariable(LocalSetupVariable)
            : Environment.GetEnvironmentVariable(LocalDownloadVariable);
        return path is { Length: > 0 } ? path : null;
    }

    /// <summary>
    /// Answers every request with the file at <paramref name="path"/>, so the file goes
    /// through every check, write and clean-up a download does. A path that is not a full
    /// path, or a file that cannot be opened, fails as a download that could not be made.
    /// </summary>
    private sealed class LocalFileAnswer(string path) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Path.IsPathFullyQualified(path))
                throw new HttpRequestException($"The local file {path} is not a full path.");
            FileStream file;
            try
            {
                file = File.OpenRead(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HttpRequestException($"The local file {path} could not be opened.", ex);
            }
            var content = new StreamContent(file);
            content.Headers.ContentLength = file.Length;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
#endif
}
