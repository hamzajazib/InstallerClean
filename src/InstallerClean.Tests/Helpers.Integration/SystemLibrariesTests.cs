using System.Diagnostics;
using System.Runtime.InteropServices;
using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// Core's libraries come from System32 by their full path. Windows only: the libraries
/// are part of Windows.
/// </summary>
public class SystemLibrariesTests
{
    [Fact]
    public void Core_has_its_resolver_in_place_before_its_first_import()
    {
        // A call into Core runs its module initialiser, if nothing has yet. A second
        // resolver for an assembly is then refused where one is set already.
        SystemLibraries.Resolve("kernel32.dll", typeof(SystemLibraries).Assembly, null);
        Assert.Throws<InvalidOperationException>(() =>
            NativeLibrary.SetDllImportResolver(typeof(SystemLibraries).Assembly, (_, _, _) => IntPtr.Zero));
    }

    [Fact]
    public void Core_sets_its_resolver_as_it_is_loaded()
    {
        var setUp = typeof(SystemLibraries).GetMethod(nameof(SystemLibraries.LoadFromSystem32),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(setUp);
        Assert.NotNull(setUp.GetCustomAttributes(typeof(System.Runtime.CompilerServices.ModuleInitializerAttribute), false)
            .SingleOrDefault());
    }

    [Theory]
    [InlineData("msi.dll")]
    [InlineData("rstrtmgr.dll")]
    public void A_library_is_loaded_from_System32(string name)
    {
        var handle = SystemLibraries.Resolve(name, typeof(SystemLibraries).Assembly, null);

        Assert.NotEqual(IntPtr.Zero, handle);
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(m => string.Equals(Path.GetFileName(m.FileName), name, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.FileName)
            .ToList();
        Assert.NotEmpty(loaded);
        Assert.All(loaded, path =>
            Assert.Equal(system32, Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_library_System32_does_not_hold_fails_rather_than_being_searched_for()
    {
        Assert.Throws<DllNotFoundException>(() =>
            SystemLibraries.Resolve("installerclean-no-such-library.dll", typeof(SystemLibraries).Assembly, null));
    }
}
