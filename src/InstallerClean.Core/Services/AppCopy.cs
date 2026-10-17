namespace InstallerClean.Services;

/// <summary>
/// Which kind of copy of InstallerClean is running, which decides what an update
/// downloads and how it puts the newer version in place.
/// </summary>
public enum AppCopyKind
{
    /// <summary>
    /// Built from source rather than published as a single file. It has no download
    /// of its own to replace itself with, so it offers the release page alone.
    /// </summary>
    FromSource,

    /// <summary>
    /// Installed by the setup, which winget and Chocolatey also install. The newer
    /// setup updates it.
    /// </summary>
    Setup,

    /// <summary>
    /// The portable download as Scoop installs it. It updates as the portable does.
    /// </summary>
    Scoop,

    /// <summary>The portable download, which carries its own .NET runtime.</summary>
    Portable,

    /// <summary>The slim download, which runs on the .NET Desktop Runtime installed on the PC.</summary>
    Slim,

    /// <summary>
    /// The command line as its own download, outside the setup's folder. It updates by
    /// replacing its own file, as the portable does.
    /// </summary>
    CommandLineDownload,
}

/// <summary>
/// Decides which <see cref="AppCopyKind"/> is running from facts the caller gathers,
/// and builds the download address for a kind and a version.
/// </summary>
public static class AppCopy
{
    /// <summary>
    /// The uninstall key the setup writes. Inno Setup names it after the script's AppId
    /// with "_is1" on the end, and writes InstallLocation there on every install.
    /// </summary>
    public const string SetupUninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\InstallerClean_is1";

    /// <summary>The value under <see cref="SetupUninstallKeyPath"/> naming the setup's folder.</summary>
    public const string InstallLocationValueName = "InstallLocation";

    /// <summary>
    /// The command line's file name, both in the setup's folder and as its own download,
    /// which carries no version in its name.
    /// </summary>
    public const string CommandLineFileName = "installerclean-cli.exe";

    /// <summary>
    /// Where every download is published. A release's files sit under its tag,
    /// "v" and the version, and copies already out there build their download
    /// address the same way, so the tag and the file names in the release workflow
    /// have to keep this shape.
    /// </summary>
    private const string DownloadRoot = "https://github.com/no-faff/InstallerClean/releases/download/";

    /// <summary>
    /// The kind of copy, in this order: a build that is not a single file is from
    /// source; a copy in the folder the setup recorded is the setup's; a copy beside
    /// the files Scoop writes into each version's folder is Scoop's; otherwise the
    /// build says whether it carries its own runtime, which tells the portable from
    /// the slim.
    ///
    /// The setup installs the very file the portable download is, so only the
    /// setup's record tells those two apart, and it is asked before the build is.
    /// </summary>
    /// <param name="isSingleFile">Whether the running build is a single-file bundle.</param>
    /// <param name="isSelfContained">Whether the build carries its own .NET runtime.</param>
    /// <param name="exeFolder">The folder the running exe is in.</param>
    /// <param name="setupInstallLocation">
    /// The setup's InstallLocation, or null where there is no setup install or its
    /// record cannot be read.
    /// </param>
    /// <param name="fileExists">Answers whether a file exists at a path.</param>
    public static AppCopyKind Decide(
        bool isSingleFile,
        bool isSelfContained,
        string exeFolder,
        string? setupInstallLocation,
        Func<string, bool> fileExists)
    {
        if (!isSingleFile) return AppCopyKind.FromSource;
        if (setupInstallLocation is not null && SameFolder(exeFolder, setupInstallLocation))
            return AppCopyKind.Setup;
        if (IsScoopFolder(exeFolder, fileExists)) return AppCopyKind.Scoop;
        return isSelfContained ? AppCopyKind.Portable : AppCopyKind.Slim;
    }

    /// <summary>
    /// The kind of command-line copy, in this order: a build that is not a single file
    /// is from source; a copy in the folder the setup recorded is the setup's; any other
    /// is the command line's own download. Scoop installs only the window, so no copy of
    /// the command line is Scoop's.
    /// </summary>
    /// <param name="isSingleFile">Whether the running build is a single-file bundle.</param>
    /// <param name="exeFolder">The folder the running exe is in.</param>
    /// <param name="setupInstallLocation">
    /// The setup's InstallLocation, or null where there is no setup install or its
    /// record cannot be read.
    /// </param>
    public static AppCopyKind DecideCommandLine(bool isSingleFile, string exeFolder, string? setupInstallLocation)
    {
        if (!isSingleFile) return AppCopyKind.FromSource;
        if (setupInstallLocation is not null && SameFolder(exeFolder, setupInstallLocation))
            return AppCopyKind.Setup;
        return AppCopyKind.CommandLineDownload;
    }

    /// <summary>
    /// The kind of the command line running from <paramref name="exePath"/>, from this
    /// build and the setup's record.
    /// </summary>
    /// <param name="exePath">The running exe's path.</param>
    /// <param name="registry">Reads the setup's record.</param>
    public static AppCopyKind DetectRunningCommandLine(string exePath, IRegistryReader registry)
    {
#if DEBUG
        if (PretendedCommandLineKind() is { } pretended)
            return pretended;
#endif
        return DecideCommandLine(
            IsSingleFileBuild(),
            Path.GetDirectoryName(exePath) ?? string.Empty,
            ReadSetupInstallLocation(registry));
    }

    /// <summary>
    /// The kind of the copy running from <paramref name="exePath"/>, from this build,
    /// the setup's record and the folder the exe is in.
    /// </summary>
    /// <param name="exePath">The running exe's path.</param>
    /// <param name="isSelfContained">
    /// Whether the build carries its own .NET runtime, as the host's build states it.
    /// </param>
    /// <param name="registry">Reads the setup's record.</param>
    public static AppCopyKind DetectRunning(string exePath, bool isSelfContained, IRegistryReader registry)
    {
#if DEBUG
        if (PretendedKind() is { } pretended)
            return pretended;
#endif
        return Decide(
            IsSingleFileBuild(),
            isSelfContained,
            Path.GetDirectoryName(exePath) ?? string.Empty,
            ReadSetupInstallLocation(registry),
            File.Exists);
    }

    /// <summary>
    /// Whether the running build is a single-file bundle. Inside one, an assembly has
    /// no file of its own, and its Location is empty; every build from source has one.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The empty Location inside a bundle is the answer being asked for.")]
    private static bool IsSingleFileBuild() =>
        string.IsNullOrEmpty(typeof(AppCopy).Assembly.Location);

#if DEBUG
    /// <summary>
    /// Set to setup, portable, slim or scoop, a Debug build of the window takes itself
    /// for that kind of copy, and set to setup or cli, a Debug build of the command line
    /// takes itself for the setup's copy or its own download, so a build from source can
    /// take each route an update takes. The name
    /// and its read sit only inside <c>#if DEBUG</c> blocks, and
    /// <c>scripts/check-debug-only-switches.mjs</c> holds them there, so no Release
    /// build can be told it is another kind of copy.
    /// </summary>
    internal const string PretendCopyVariable = "INSTALLERCLEAN_DEBUG_PRETEND_COPY";

    private static AppCopyKind? PretendedKind() =>
        Environment.GetEnvironmentVariable(PretendCopyVariable)?.Trim().ToLowerInvariant() switch
        {
            "setup" => AppCopyKind.Setup,
            "portable" => AppCopyKind.Portable,
            "slim" => AppCopyKind.Slim,
            "scoop" => AppCopyKind.Scoop,
            _ => null,
        };

    private static AppCopyKind? PretendedCommandLineKind() =>
        Environment.GetEnvironmentVariable(PretendCopyVariable)?.Trim().ToLowerInvariant() switch
        {
            "setup" => AppCopyKind.Setup,
            "cli" => AppCopyKind.CommandLineDownload,
            _ => null,
        };
#endif

    /// <summary>
    /// The setup's InstallLocation from the 64-bit view of the registry, or null where
    /// the key or the value is absent, cannot be read or holds no text.
    /// </summary>
    public static string? ReadSetupInstallLocation(IRegistryReader registry)
    {
        var read = registry.LocalMachineValues(SetupUninstallKeyPath);
        if (read.Presence != RegistryKeyPresence.Present || read.Values is null) return null;
        foreach (var value in read.Values)
        {
            if (string.Equals(value.Name, InstallLocationValueName, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(value.Text) ? null : value.Text;
        }
        return null;
    }

    /// <summary>
    /// Whether this kind of copy can put a newer version in its own place. Only a
    /// build from source cannot.
    /// </summary>
    public static bool CanUpdateItself(AppCopyKind kind) => kind != AppCopyKind.FromSource;

    /// <summary>
    /// The file name of the download a kind updates from, or null for a build from
    /// source. Scoop installs the portable, so it updates from the portable. The
    /// command line's own download carries no version in its name; the release it sits
    /// in says which version it is.
    /// </summary>
    public static string? DownloadFileName(AppCopyKind kind, string version) => kind switch
    {
        AppCopyKind.Setup => $"InstallerClean-{version}-setup.exe",
        AppCopyKind.Portable or AppCopyKind.Scoop => $"InstallerClean-{version}-portable.exe",
        AppCopyKind.Slim => $"InstallerClean-{version}-slim.exe",
        AppCopyKind.CommandLineDownload => CommandLineFileName,
        _ => null,
    };

    /// <summary>
    /// The address of the download a kind updates from, or null for a build from
    /// source. The version is the one the update check read off GitHub's answer,
    /// which it accepts only as numbers and dots.
    /// </summary>
    public static Uri? DownloadAddress(AppCopyKind kind, string version) =>
        DownloadFileName(kind, version) is { } name
            ? new Uri($"{DownloadRoot}v{version}/{name}")
            : null;

    /// <summary>
    /// Whether two spellings name the same folder: each made absolute and stripped of
    /// trailing separators, then compared without regard to case, as Windows compares
    /// names. Inno Setup writes InstallLocation with a trailing backslash.
    /// </summary>
    private static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A recorded value that is not a path names no folder this exe can be in.
            return false;
        }

        static string Normalise(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// Whether the folder holds the two files Scoop writes into each version's folder
    /// it installs: its copy of the app's manifest and its record of the install.
    /// Scoop 0.6.0 and later name them scoop-manifest.json and scoop-install.json;
    /// earlier versions wrote manifest.json and install.json.
    /// </summary>
    private static bool IsScoopFolder(string folder, Func<string, bool> fileExists)
    {
        bool Has(string name) => fileExists(Path.Combine(folder, name));
        return (Has("scoop-manifest.json") || Has("manifest.json"))
            && (Has("scoop-install.json") || Has("install.json"));
    }
}
