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
    /// Downloads <paramref name="version"/> as the same kind of copy, and answers with
    /// what to start. Reports the fraction downloaded, or null where the length is not
    /// known. Throws <see cref="UpdateFailedException"/> where any step fails, and
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

    public PreparedUpdate(string fileToStart, string arguments, Action undo, string? replacement = null)
    {
        FileToStart = fileToStart;
        Arguments = arguments;
        Replacement = replacement;
        _undo = undo;
    }

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
/// it; the portable, the slim, Scoop's copy and the command line's own download
/// download the newer version of the same file and write it beside their own.
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
            throw new InvalidOperationException("A build from source has no download to update from.");

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
            if (Kind == AppCopyKind.Setup && LocalSetup() is { } local)
                await CopyLocalSetupAsync(local, staged, progress, cancellationToken).ConfigureAwait(false);
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

        if (Kind == AppCopyKind.Setup)
        {
            return new PreparedUpdate(staged, _setupArguments, () =>
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
        return new PreparedUpdate(_exePath, string.Empty,
            () => Helpers.StorageHelpers.TryDeleteTempFile(replacement), replacement);
    }

#if DEBUG
    /// <summary>
    /// Set to the path of a setup, a Debug build whose copy is the setup's runs that
    /// setup in place of downloading one, so a setup built from this tree's script can
    /// be tried before any release carries it. The name and its read sit only inside
    /// <c>#if DEBUG</c> blocks, and <c>scripts/check-debug-only-switches.mjs</c> holds
    /// them there.
    /// </summary>
    internal const string LocalSetupVariable = "INSTALLERCLEAN_DEBUG_LOCAL_SETUP";

    private static string? LocalSetup() =>
        Environment.GetEnvironmentVariable(LocalSetupVariable) is { Length: > 0 } path ? path : null;

    /// <summary>
    /// Copies the setup named by <see cref="LocalSetupVariable"/> to where a download
    /// would have gone, through the same write the download uses.
    /// </summary>
    private static async Task CopyLocalSetupAsync(
        string source, string destination, IProgress<double?>? progress, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = File.OpenRead(source);
            using var handle = Helpers.StorageHelpers.OpenAtomic(
                    destination, FileAccess.Write, Helpers.StorageHelpers.AtomicOpenMode.CreateAlways)
                ?? throw new IOException("The file the setup is copied to could not be created.");
            await using var output = new FileStream(handle, FileAccess.Write, bufferSize: 0);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            progress?.Report(1d);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Helpers.StorageHelpers.TryDeleteTempFile(destination);
            throw new UpdateFailedException(UpdateFailureStage.Install, "The local setup could not be copied.", ex);
        }
    }
#endif
}
