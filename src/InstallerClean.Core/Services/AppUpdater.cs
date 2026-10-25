using InstallerClean.Helpers;

namespace InstallerClean.Services;

/// <summary>
/// Gets a newer version ready to start: downloaded, and where the copy replaces its
/// own file, written beside it to take its place.
/// </summary>
public interface IAppUpdater
{
    /// <summary>
    /// The kind of copy running, decided once at start. <see cref="AppCopy.CanUpdateItself"/>
    /// says whether a kind can put a newer version in its own place.
    /// </summary>
    AppCopyKind Kind { get; }

    /// <summary>
    /// Scoop's update command for this copy where <see cref="Kind"/> is
    /// <see cref="AppCopyKind.Scoop"/>, or null where the folder the copy runs from does
    /// not say which app Scoop installed it as, and null for every other kind.
    /// </summary>
    ScoopUpdate? ScoopUpdate { get; }

    /// <summary>
    /// Reads what the release of <paramref name="version"/> says its downloads need
    /// (<see cref="UpdateManifest"/>) and answers why this copy does not install it, or
    /// null where it may. Throws <see cref="UpdateFailedException"/> at the Download stage
    /// where the release's file could not be read,
    /// <see cref="InvalidOperationException"/> where the kind of copy cannot update itself,
    /// and <see cref="OperationCanceledException"/> where the token is cancelled.
    /// </summary>
    Task<UpdateRefusal?> CheckRequirementsAsync(string version, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads <paramref name="version"/> as the same kind of copy, where its release
    /// allows it (<see cref="CheckRequirementsAsync"/>), checks that the file carries that
    /// version, and answers with what to start. Reports the fraction
    /// downloaded, or null where the length is not known. Throws
    /// <see cref="UpdateFailedException"/> where any step fails, a download that does not
    /// carry the version included, <see cref="UpdateRefusedException"/> before anything is
    /// downloaded where the release rules this copy out,
    /// <see cref="InvalidOperationException"/> where the kind of copy cannot update itself,
    /// and <see cref="OperationCanceledException"/> where the token is cancelled; none of
    /// them leaves anything changed on disk.
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
    /// beside the running exe, which stays where nothing is at the exe's path. Runs once,
    /// however often it is called. Never throws.
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
/// the version it was downloaded as before anything uses it. A build from source,
/// Scoop's copy and an undetermined copy have no download, and
/// <see cref="PrepareAsync"/> refuses them.
/// </summary>
public sealed class AppUpdater : IAppUpdater
{
    /// <summary>
    /// What the window starts the newer setup with. /SILENT shows Setup's progress and
    /// asks nothing, /SP- leaves out the page asking whether to install, and /NORESTART
    /// keeps Setup from restarting Windows. /UPDATEFROMAPP is the app's own: seeing it,
    /// the setup script first waits for the app to close its handle to the single-instance
    /// mutex, so the setup's check for a running InstallerClean does not stop for it, and
    /// starts the app again once it has installed, which it otherwise never does on a
    /// silent install.
    /// </summary>
    public const string WindowSetupArguments = "/SILENT /SP- /NORESTART /UPDATEFROMAPP";

    private readonly string _exePath;
    private readonly string _setupArguments;
    private readonly UpdateDownloadService _downloader;
    private readonly string _stagingRoot;
    private readonly string _stagingDescriptor;
    private readonly IRegistryReader _registry;
    private readonly UpdateLeftoverGate _leftovers;
    private (string Version, byte[]? Manifest)? _manifest;

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
        : this(kind, exePath, downloader, stagingRoot, setupArguments, UpdateStagingFolder.AdministratorsOnly,
            new RegistryReader()) { }

    /// <summary>
    /// Test seam: the staging folder's descriptor given as SDDL, so a suite without
    /// administrator rights can write into it, the registry the PC's .NET Framework
    /// is read from, and the gate the update marks before it writes anything, which is
    /// <see cref="UpdateLeftoverGate.ForThisProcess"/> where none is given.
    /// </summary>
    internal AppUpdater(
        AppCopyKind kind, string exePath, UpdateDownloadService downloader, string stagingRoot,
        string setupArguments, string stagingDescriptor, IRegistryReader registry,
        UpdateLeftoverGate? leftovers = null)
    {
        _leftovers = leftovers ?? UpdateLeftoverGate.ForThisProcess;
        Kind = kind;
        _exePath = exePath;
        _setupArguments = setupArguments;
        _downloader = downloader;
        _stagingRoot = stagingRoot;
        _stagingDescriptor = stagingDescriptor;
        _registry = registry;
        _scoopUpdate = new Lazy<ScoopUpdate?>(ReadScoopUpdate);
    }

    public AppCopyKind Kind { get; }

    // Read the first time it is asked for, which is once a newer version has been found.
    private readonly Lazy<ScoopUpdate?> _scoopUpdate;

    public ScoopUpdate? ScoopUpdate => _scoopUpdate.Value;

    private static string? ScoopVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>The largest Scoop settings file that is read.</summary>
    private const long MaxScoopConfigBytes = 1024 * 1024;

    private static string? ReadScoopConfig(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return null;
        if (file.Length > MaxScoopConfigBytes) throw new IOException("The settings file is larger than is read.");
        return File.ReadAllText(path);
    }

    private ScoopUpdate? ReadScoopUpdate()
    {
        if (Kind != AppCopyKind.Scoop) return null;
        try
        {
            return AppCopy.ReadScoopUpdate(
                Path.GetDirectoryName(_exePath) ?? string.Empty,
                ScoopVariable,
                ReadScoopConfig,
                KnownFolders.GetPath(Environment.SpecialFolder.CommonApplicationData));
        }
        catch (Exception ex)
        {
            Helpers.CrashLog.TryWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// A file the release has is kept for the version it was read for, so the read made as
    /// a newer version is found is not made again before its download. A release with no
    /// file is asked again each time, in case the file has been published since, and so is
    /// the PC, so a requirement met since the last answer is seen. A body that is not JSON,
    /// such as a page a network puts in GitHub's place, fails as a read that could not be
    /// made and is not kept.
    /// </summary>
    public async Task<UpdateRefusal?> CheckRequirementsAsync(string version, CancellationToken cancellationToken)
    {
        if (UpdateManifest.DownloadKey(Kind) is null)
            throw new InvalidOperationException($"A copy of kind {Kind} has no download to update from.");

        // A cancel is answered as a cancel wherever the file came from, a kept file and a
        // Debug build's local file included, and before the body or the answer is judged,
        // so a cancelled check never reports a refusal or a read that failed.
        byte[]? manifest;
        if (_manifest is { } kept && kept.Version == version)
        {
            manifest = kept.Manifest;
        }
        else
        {
#if DEBUG
            if (LocalManifest() is { } local)
                manifest = local.Absent ? null : ReadLocalManifest(local.Path);
            else
#endif
            manifest = await _downloader.ReadAsync(UpdateManifest.Address(version), UpdateManifest.MaxBytes, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest is not null)
            {
                if (!UpdateManifest.IsJson(manifest))
                    throw new UpdateFailedException(UpdateFailureStage.Download, "The release's update.json is not JSON.");
                _manifest = (version, manifest);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        return UpdateManifest.Judge(manifest, version, Kind, () => UpdateManifest.HasNetFramework48(_registry));
    }

    public async Task<PreparedUpdate> PrepareAsync(
        string version, IProgress<double?>? progress, CancellationToken cancellationToken)
    {
        var address = AppCopy.DownloadAddress(Kind, version);
        var fileName = AppCopy.DownloadFileName(Kind, version);
        if (address is null || fileName is null)
            throw new InvalidOperationException($"A copy of kind {Kind} has no download to update from.");

        if (await CheckRequirementsAsync(version, cancellationToken).ConfigureAwait(false) is { } refusal)
            throw new UpdateRefusedException(refusal, version);

        // Before the first file this update writes, so no pass of the start-up clean-up
        // deletes one (UpdateLeftoverGate). Everything above only reads, so an update that
        // ends before this point, refused, failed or cancelled, leaves the clean-up running.
        // A write added above this line has to move the mark above it too. A cancel that
        // came after the requirements were checked is answered here, before the mark.
        cancellationToken.ThrowIfCancellationRequested();
        _leftovers.MarkUpdateWriting();
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
        // With nothing at the exe's path, the newer version is one of the two files that can
        // go there, so it stays.
        return new PreparedUpdate(version, _exePath, string.Empty,
            () => { if (File.Exists(_exePath)) Helpers.StorageHelpers.TryDeleteTempFile(replacement); },
            replacement);
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
    /// Set to the full path of an update.json, a Debug build reads that file as the
    /// release's, and set to absent, it reads the release as having none, so the answer
    /// a release's file gives can be tried offline. Held inside <c>#if DEBUG</c> the same way.
    /// </summary>
    internal const string LocalManifestVariable = "INSTALLERCLEAN_DEBUG_LOCAL_MANIFEST";

    /// <summary>What <see cref="LocalManifestVariable"/> says, or null where it is unset.</summary>
    private static (bool Absent, string Path)? LocalManifest()
    {
        var value = Environment.GetEnvironmentVariable(LocalManifestVariable);
        if (value is not { Length: > 0 }) return null;
        return string.Equals(value, "absent", StringComparison.OrdinalIgnoreCase) ? (true, string.Empty) : (false, value);
    }

    /// <summary>
    /// The file a Debug build reads as the release's update.json, held to the read's size
    /// cap. A file that is not a full path, cannot be read or is over the cap fails as a
    /// read that could not be made.
    /// </summary>
    private static byte[] ReadLocalManifest(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new IOException($"The local file {path} is not a full path.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > UpdateManifest.MaxBytes) throw new IOException($"The local file {path} is over the cap.");
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateFailedException(UpdateFailureStage.Download, "The local update.json could not be read.", ex);
        }
    }

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
