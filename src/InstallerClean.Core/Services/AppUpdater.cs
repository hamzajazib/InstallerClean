namespace InstallerClean.Services;

/// <summary>
/// Gets a newer version ready to start: downloaded, and where the copy replaces its
/// own file, put in place.
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
/// A newer version ready to start, and the step that puts everything back if starting
/// it fails.
/// </summary>
public sealed class PreparedUpdate
{
    private readonly Action _undo;

    public PreparedUpdate(string fileToStart, string arguments, Action undo)
    {
        FileToStart = fileToStart;
        Arguments = arguments;
        _undo = undo;
    }

    /// <summary>The program to start: the newer setup, or the newer version at the app's own path.</summary>
    public string FileToStart { get; }

    /// <summary>The command line it is started with.</summary>
    public string Arguments { get; }

    /// <summary>
    /// Puts back what getting it ready changed: the running exe's own file, and the
    /// download. Never throws.
    /// </summary>
    public void Undo()
    {
        try { _undo(); }
        catch (Exception ex) { Helpers.CrashLog.TryWrite(ex); }
    }
}

/// <summary>
/// The update from inside the app. The setup's copy downloads the newer setup and runs
/// it; the portable, the slim and Scoop's copy download the newer version of the same
/// file and put it at their own path.
/// </summary>
public sealed class AppUpdater : IAppUpdater
{
    /// <summary>
    /// What the newer setup is started with. /SILENT shows Setup's progress and asks
    /// nothing, /SP- leaves out the page asking whether to install, and /NORESTART
    /// keeps Setup from restarting Windows. /UPDATEFROMAPP is the app's own: the setup
    /// script starts the app again once it has installed when it sees it, which it
    /// otherwise never does on a silent install.
    /// </summary>
    internal const string SetupArguments = "/SILENT /SP- /NORESTART /UPDATEFROMAPP";

    private readonly string _exePath;
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
    public AppUpdater(AppCopyKind kind, string exePath, UpdateDownloadService downloader, string stagingRoot)
        : this(kind, exePath, downloader, stagingRoot, UpdateStagingFolder.AdministratorsOnly) { }

    /// <summary>
    /// Test seam: the staging folder's descriptor given as SDDL, so a suite without
    /// administrator rights can write into it.
    /// </summary>
    internal AppUpdater(
        AppCopyKind kind, string exePath, UpdateDownloadService downloader, string stagingRoot,
        string stagingDescriptor)
    {
        Kind = kind;
        _exePath = exePath;
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
            return new PreparedUpdate(staged, SetupArguments, () =>
            {
                Helpers.StorageHelpers.TryDeleteTempFile(staged);
                folder.TryRemoveIfEmpty();
            });
        }

        Action putBack;
        try
        {
            putBack = SelfReplacement.Replace(_exePath, staged);
        }
        catch
        {
            Helpers.StorageHelpers.TryDeleteTempFile(staged);
            folder.TryRemoveIfEmpty();
            throw;
        }
        folder.TryRemoveIfEmpty();
        return new PreparedUpdate(_exePath, string.Empty, putBack);
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
