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
}
