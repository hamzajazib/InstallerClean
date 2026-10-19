using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The resolver on every .NET and WPF assembly: a Windows library from System32, the app's
/// own libraries left to the runtime, set on each assembly once, only in the process the exe
/// was started for, and never an exception out of a module initializer or an assembly load.
/// </summary>
public class FrameworkLibrariesTests
{
    private static readonly Assembly Exe = typeof(InstallerClean.Cli.LibrarySearch).Assembly;
    private static readonly Assembly Other = typeof(FrameworkLibrariesTests).Assembly;

    [Theory]
    [InlineData("uxtheme.dll")]
    [InlineData("KERNEL32.dll")]
    [InlineData("user32")]
    [InlineData("winspool.drv")]
    [InlineData("api-ms-win-core-winrt-l1-1-0.dll")]
    [InlineData("mshwgst.dll")]
    [InlineData("PresentationHost_cor3.dll")]
    public void A_Windows_library_or_one_the_app_does_not_carry_is_loaded_from_System32(string name)
    {
        Assert.True(FrameworkLibraries.IsWindowsLibrary(name));
    }

    [Theory]
    [InlineData("wpfgfx_cor3.dll")]
    [InlineData("PresentationNative_cor3.dll")]
    [InlineData("PenIMC_cor3.dll")]
    [InlineData("D3DCompiler_47_cor3.dll")]
    [InlineData("vcruntime140_cor3.dll")]
    [InlineData("WPFGFX_COR3")]
    [InlineData("System.IO.Compression.Native")]
    [InlineData("System.IO.Compression.Native.dll")]
    [InlineData("system.io.compression.native.DLL")]
    public void A_library_the_app_carries_is_left_to_the_runtime(string name)
    {
        Assert.False(FrameworkLibraries.IsWindowsLibrary(name));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\uxtheme.dll")]
    [InlineData(@"sub\uxtheme.dll")]
    [InlineData("sub/uxtheme.dll")]
    public void A_name_carrying_a_path_is_left_to_the_runtime(string name)
    {
        Assert.False(FrameworkLibraries.IsWindowsLibrary(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void A_library_with_no_name_is_refused(string? name)
    {
        Assert.Throws<DllNotFoundException>(() => FrameworkLibraries.IsWindowsLibrary(name!));
    }

    [Fact]
    public void A_Windows_library_is_loaded_once_by_its_name()
    {
        var loads = new List<string>();

        var handle = FrameworkLibraries.Resolve("uxtheme.dll", name => { loads.Add(name); return 42; });

        Assert.Equal(42, handle);
        Assert.Equal(new[] { "uxtheme.dll" }, loads);
    }

    [Fact]
    public void A_library_the_app_carries_is_never_loaded_here()
    {
        var loads = new List<string>();

        var handle = FrameworkLibraries.Resolve("wpfgfx_cor3.dll", name => { loads.Add(name); return 42; });

        Assert.Equal(IntPtr.Zero, handle);
        Assert.Empty(loads);
    }

    [Fact]
    public void A_Windows_library_System32_does_not_hold_fails_rather_than_being_searched_for()
    {
        Assert.Throws<DllNotFoundException>(() =>
            FrameworkLibraries.Resolve("uxtheme.dll", _ => throw new DllNotFoundException("uxtheme.dll")));
    }

    [Fact]
    public void The_exes_own_process_subscribes_first_then_registers_what_is_loaded_then_loads_the_rest()
    {
        var steps = new List<string>();
        Action<Assembly>? subscribed = null;

        var set = FrameworkLibraries.ResolveFor(
            Exe,
            Exe,
            register => { steps.Add("subscribe"); subscribed = register; },
            () => { steps.Add("loaded"); return [Other]; },
            assembly => steps.Add("register " + assembly.GetName().Name),
            () => steps.Add("load"));

        Assert.True(set);
        Assert.Equal(new[] { "subscribe", "loaded", "register " + Other.GetName().Name, "load" }, steps);
        Assert.NotNull(subscribed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_process_started_for_another_assembly_or_none_sets_no_resolver(bool entryKnown)
    {
        var steps = new List<string>();

        var set = FrameworkLibraries.ResolveFor(
            Exe,
            entryKnown ? Other : null,
            _ => steps.Add("subscribe"),
            () => { steps.Add("loaded"); return [Other]; },
            _ => steps.Add("register"),
            () => steps.Add("load"));

        Assert.False(set);
        Assert.Empty(steps);
    }

    [Fact]
    public void Each_assembly_is_given_the_resolver_once()
    {
        var registered = new HashSet<Assembly>();
        var given = new List<Assembly>();

        FrameworkLibraries.Register(Other, registered, (assembly, _) => given.Add(assembly));
        FrameworkLibraries.Register(Other, registered, (assembly, _) => given.Add(assembly));

        Assert.Equal(new[] { Other }, given);
    }

    [Fact]
    public void CoreLib_this_assembly_and_a_dynamic_assembly_are_given_none()
    {
        var registered = new HashSet<Assembly>();
        var given = new List<Assembly>();
        var dynamic = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FrameworkLibrariesTestsDynamic"),
            AssemblyBuilderAccess.Run);

        foreach (var assembly in new[] { typeof(object).Assembly, typeof(FrameworkLibraries).Assembly, dynamic })
            FrameworkLibraries.Register(assembly, registered, (a, _) => given.Add(a));

        Assert.Empty(given);
    }

    [Fact]
    public void A_refused_resolver_does_not_throw_and_the_next_assembly_is_still_given_one()
    {
        var registered = new HashSet<Assembly>();
        var given = new List<Assembly>();

        FrameworkLibraries.Register(Other, registered, (_, _) => throw new InvalidOperationException("set already"));
        FrameworkLibraries.Register(Exe, registered, (assembly, _) => given.Add(assembly));

        Assert.Equal(new[] { Exe }, given);
    }

    [Fact]
    public void The_resolver_given_is_the_one_that_loads_from_System32()
    {
        DllImportResolver? resolver = null;

        FrameworkLibraries.Register(Other, new HashSet<Assembly>(), (_, r) => resolver = r);

        Assert.NotNull(resolver);
        Assert.Equal(IntPtr.Zero, resolver("wpfgfx_cor3.dll", Other, null));
    }

    private static readonly string[] WhereInstalled = ["Microsoft.VisualBasic.Forms", "System.Windows.Forms.Primitives"];

    private static string PlatformAssemblies(params string[] paths) => string.Join(Path.PathSeparator, paths);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_trusted_platform_assemblies_none_of_the_installed_runtimes_is_loaded(string? value)
    {
        Assert.Empty(FrameworkLibraries.Trusted(value, WhereInstalled));
    }

    [Fact]
    public void Each_installed_runtime_assembly_the_trusted_list_names_is_loaded()
    {
        var value = PlatformAssemblies(
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.13\PresentationCore.dll",
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.13\System.Windows.Forms.Primitives.dll",
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.13\Microsoft.VisualBasic.Forms.dll");

        Assert.Equal(WhereInstalled, FrameworkLibraries.Trusted(value, WhereInstalled));
    }

    [Fact]
    public void Only_the_installed_runtime_assemblies_the_trusted_list_names_are_loaded()
    {
        var value = PlatformAssemblies(
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.13\MICROSOFT.VISUALBASIC.FORMS.DLL",
            @"C:\System.Windows.Forms.Primitives\System.Windows.Forms.dll");

        Assert.Equal(new[] { "Microsoft.VisualBasic.Forms" }, FrameworkLibraries.Trusted(value, WhereInstalled));
    }

    [Fact]
    public void The_listed_assemblies_load_before_the_installed_runtimes()
    {
        var value = PlatformAssemblies(@"C:\shared\System.Windows.Forms.Primitives.dll");

        Assert.Equal(
            new[] { "PresentationCore", "WindowsBase", "System.Windows.Forms.Primitives" },
            FrameworkLibraries.ToLoad(["PresentationCore", "WindowsBase"], value, WhereInstalled));
    }

    [Fact]
    public void An_exe_with_no_installed_runtime_list_loads_its_own_list_alone()
    {
        var value = PlatformAssemblies(@"C:\shared\System.Windows.Forms.Primitives.dll");

        Assert.Equal(new[] { "System.Console" }, FrameworkLibraries.ToLoad(["System.Console"], value, null));
    }
}
