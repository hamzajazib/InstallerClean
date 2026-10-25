using InstallerClean.ViewModels;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The view models a test class made. Disposing it stops the destination save each one may
/// still be waiting to make, which otherwise lands about 400 ms later in whichever test is
/// running, in the crash log every test shares. Flushing is safe on a view model a test has
/// already disposed or flushed, and throws nothing a working settings substitute would cause.
/// </summary>
internal sealed class MadeViewModels : IDisposable
{
    private readonly List<MainViewModel> _made = new();

    public MainViewModel Add(MainViewModel vm)
    {
        _made.Add(vm);
        return vm;
    }

    public void Dispose()
    {
        foreach (var vm in _made)
            vm.Cleanup.FlushMoveDestination();
    }

    /// <summary>
    /// <see cref="TestCrashLog.Text"/> without the entries carrying
    /// <see cref="CleanupViewModel.MoveDestinationSaveRefused"/>. A view model another test
    /// left behind saves its destination after a delay, into the one crash log every test
    /// shares, so a test comparing the log before and after its own work reads this and sees
    /// only entries its work could write.
    /// </summary>
    public static string CrashLogWithoutDestinationSaves()
    {
        // Each entry starts with a line opening "---- " (CrashLog.TryWrite).
        var entries = System.Text.RegularExpressions.Regex.Split(
            TestCrashLog.Text(), "(?=^---- )", System.Text.RegularExpressions.RegexOptions.Multiline);
        return string.Concat(entries.Where(
            e => !e.Contains(CleanupViewModel.MoveDestinationSaveRefused, StringComparison.Ordinal)));
    }
}
