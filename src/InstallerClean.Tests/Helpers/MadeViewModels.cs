using InstallerClean.Helpers;
using InstallerClean.ViewModels;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The view models a test made, and a crash-log folder of that test's own for as long as it
/// runs. A test reading <see cref="TestCrashLog.Text"/> sees only what its own work wrote.
/// Disposing flushes the destination save each view model may still be waiting to make,
/// which otherwise lands about 400 ms later, and then puts the previous folder back and
/// deletes this one. Flushing is safe on a view model a test has already disposed or
/// flushed. The folder is the one process-wide <see cref="CrashLog.FolderForTests"/>, which
/// is why the tests run one at a time (AssemblyInfo.cs).
/// </summary>
internal sealed class MadeViewModels : IDisposable
{
    private readonly List<MainViewModel> _made = new();
    private readonly string? _previousFolder = CrashLog.FolderForTests;
    private readonly string _folder = TestCrashLog.NewFolder();

    public MadeViewModels()
    {
        CrashLog.FolderForTests = _folder;
    }

    public MainViewModel Add(MainViewModel vm)
    {
        _made.Add(vm);
        return vm;
    }

    public void Dispose()
    {
        try
        {
            foreach (var vm in _made)
                vm.Cleanup.FlushMoveDestination();
        }
        finally
        {
            CrashLog.FolderForTests = _previousFolder;
            TestCrashLog.TryDelete(_folder);
        }
    }
}
