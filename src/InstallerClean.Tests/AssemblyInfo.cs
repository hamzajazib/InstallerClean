using System.Globalization;
using System.Runtime.CompilerServices;

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
