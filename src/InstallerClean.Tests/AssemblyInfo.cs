using System.Globalization;
using System.Runtime.CompilerServices;
using InstallerClean.Helpers;

// Tests run one at a time. The app's chosen language is process-global state:
// Localisation holds a static override, and every Strings lookup and every
// DisplayHelpers format call reads it, so a test that sets a language rewrites
// what every other running test sees. The command-line tests that swap
// Console.Out and read it back rely on this too, and so does EventLogRecorder's
// one list. Under xUnit's default per-class parallelism one test's language and
// output would reach another's run.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

// The tests' own imports name System32 as the place to look for the libraries they load.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(
    System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace InstallerClean.Tests;

/// <summary>
/// Every test runs in en-GB, its culture and its UI culture alike, whatever the
/// language of the PC running it. The two defaults set here apply to every thread that
/// has not set its own: the test threads, async continuations and the thread pool, and
/// a thread already running when the assembly loads. A test that needs another language
/// sets its own for its run and puts the previous one back. TestCultureTests reads both
/// from each kind of thread and fails where this is gone.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Pin()
    {
        var english = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
    }
}

/// <summary>
/// Every test that writes the crash log writes it to a folder of this run's own under the
/// temp folder, never to the log of the PC running the suite. The folder is set before any
/// test runs, so a test reaching the log by a path nobody planned for is covered too, and
/// it is deleted when the test process exits, along with the shared InstallerClean.Tests
/// folder above it where no other run's folder is left in it. A test that changes the
/// folder for its run, as the tests that make view models do (<c>MadeViewModels</c>), the
/// update tests do (<see cref="OwnFolder"/>) and CrashLogTests does, puts this one back.
/// </summary>
internal static class TestCrashLog
{
    private static readonly string Shared = Path.Combine(Path.GetTempPath(), "InstallerClean.Tests");

    /// <summary>A folder of its own under the shared one, not yet made.</summary>
    internal static string NewFolder() => Path.Combine(Shared, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Points the crash log at a new folder until the answer is disposed, which puts back the
    /// folder it replaced and deletes the new one. A test class takes one where nothing after
    /// it in the class's construction can throw, since a test class whose constructor throws
    /// is never disposed, and each of its tests then reads <see cref="Text"/> and sees only
    /// what its own work wrote.
    /// </summary>
    internal static IDisposable OwnFolder() => new Folder();

    private sealed class Folder : IDisposable
    {
        private readonly string? _previous = CrashLog.FolderForTests;
        private readonly string _folder = NewFolder();
        private bool _disposed;

        public Folder() => CrashLog.FolderForTests = _folder;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CrashLog.FolderForTests = _previous;
            TryDelete(_folder);
        }
    }

    [ModuleInitializer]
    internal static void Redirect()
    {
        var folder = NewFolder();
        CrashLog.FolderForTests = folder;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            TryDelete(folder);
            // Not recursive, so a folder another run is still using stays.
            try { Directory.Delete(Shared); } catch (Exception) { }
        };
    }

    /// <summary>
    /// The crash log as it stands, after the copy it was last moved aside to, or empty where
    /// there is neither. Read whole, so a test comparing it before and after a write sees
    /// the entry even where the write moved a full log aside.
    /// </summary>
    internal static string Text() => ReadIfThere(CrashLog.LogPath + CrashLog.ArchiveSuffix) + ReadIfThere(CrashLog.LogPath);

    private static string ReadIfThere(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    /// <summary>
    /// Deletes <paramref name="folder"/> and everything in it, and throws nothing where the
    /// delete is refused: a file an antivirus holds open, or a read-only one on Windows,
    /// leaves the folder in place and the test that made it passing.
    /// </summary>
    internal static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
    }
}
