using System.Globalization;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The test project's language, set for every thread in AssemblyInfo.cs. Each test reads
/// the culture, the UI culture and the language the app's own strings resolve to, and
/// expects en-GB for all three.
/// </summary>
public class TestCultureTests
{
    private const string English = "en-GB";

    private static string Read() =>
        $"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}|{SupportedLanguages.Active(Localisation.UiCulture)}";

    [Fact]
    public void Both_default_cultures_are_en_GB()
    {
        Assert.Equal(English, CultureInfo.DefaultThreadCurrentCulture?.Name);
        Assert.Equal(English, CultureInfo.DefaultThreadCurrentUICulture?.Name);
    }

    [Fact]
    public async Task Every_kind_of_thread_a_test_runs_on_reads_en_GB()
    {
        var seen = new List<string> { "the test thread " + Read() };

        await Task.Yield();
        seen.Add("after an await " + Read());

        seen.Add("Task.Run " + await Task.Run(Read));

        var onThread = "";
        var thread = new Thread(() => onThread = Read());
        thread.Start();
        thread.Join();
        seen.Add("a new thread " + onThread);

        var pooled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.UnsafeQueueUserWorkItem(_ => pooled.SetResult(Read()), null);
        seen.Add("an unsafe thread-pool item " + await pooled.Task);

        Assert.All(seen, s => Assert.EndsWith($" {English}|{English}|{English}", s));
    }
}
