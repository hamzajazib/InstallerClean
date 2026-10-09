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
/// folder above it where no other run's folder is left in it. A CrashLogTests test that
/// changes the folder for its run puts this one back.
/// </summary>
internal static class TestCrashLog
{
    private static readonly string Shared = Path.Combine(Path.GetTempPath(), "InstallerClean.Tests");

    [ModuleInitializer]
    internal static void Redirect()
    {
        var folder = Path.Combine(Shared, Guid.NewGuid().ToString("N"));
        CrashLog.FolderForTests = folder;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            TryDelete(folder);
            // Not recursive, so a folder another run is still using stays.
            try { Directory.Delete(Shared); } catch (Exception) { }
        };
    }

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
