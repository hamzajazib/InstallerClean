using System.Reflection;
using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Interop.Native;

/// <summary>
/// Loads every Windows library a .NET or WPF assembly in the process imports from System32,
/// before the runtime searches anywhere for it, as <see cref="SystemLibraries"/> does for
/// this assembly's own imports. The runtime looks for such a library first in the folders
/// its host names for native libraries and in the importing assembly's folder: for a
/// single-file build, the folder it extracts itself to and the folder its exe is run from,
/// either of which its user can write to. A file there of a Windows library's name would
/// otherwise be loaded into the elevated process.
/// </summary>
/// <remarks>
/// A resolver is set on every assembly the process loads except System.Private.CoreLib,
/// whose imports the runtime never passes to a resolver, and this assembly, which sets its
/// own. A library in <see cref="OwnLibraries"/> is handed back to the runtime, which finds it
/// where the build carries it. Every other library is loaded with LOAD_LIBRARY_SEARCH_SYSTEM32
/// and, where that fails, the import fails with the <see cref="DllNotFoundException"/> the
/// runtime gives a missing library, rather than being searched for.
/// </remarks>
internal static class FrameworkLibraries
{
    /// <summary>
    /// The libraries a framework assembly imports that are not part of Windows and that the
    /// app carries: WPF's own, which a self-contained build extracts and the desktop runtime
    /// installs beside its assemblies, and the runtime's compression library, which a
    /// self-contained build's host carries inside itself. Compared by name with or without
    /// ".dll", ignoring case. A library a framework assembly imports that is in neither
    /// System32 nor this list fails to load; the native-libraries job in ci.yml reads this
    /// list and fails where it differs from what the builds carry.
    /// </summary>
    internal static readonly string[] OwnLibraries =
    [
        "D3DCompiler_47_cor3",
        "PenImc_cor3",
        "PresentationNative_cor3",
        "vcruntime140_cor3",
        "wpfgfx_cor3",
        "System.IO.Compression.Native",
    ];

    /// <summary>The assemblies a resolver has been set on, so none is set twice.</summary>
    private static readonly HashSet<Assembly> s_registered = [];

    /// <summary>Set while a refusal is written to the crash log, which can itself load an assembly.</summary>
    [ThreadStatic]
    private static bool t_recording;

    /// <summary>
    /// Whether <paramref name="libraryName"/> is a Windows library, to be loaded from System32.
    /// False for a library in <see cref="OwnLibraries"/> and for a name carrying a path, both of
    /// which the runtime finds as it would without a resolver.
    /// </summary>
    internal static bool IsWindowsLibrary(string libraryName)
    {
        if (string.IsNullOrEmpty(libraryName))
            throw new DllNotFoundException("A library with no name cannot be loaded.");
        if (libraryName.IndexOfAny(['\\', '/', ':']) >= 0)
            return false;
        var name = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName[..^4]
            : libraryName;
        return !OwnLibraries.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The resolver each assembly is given.</summary>
    internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        Resolve(libraryName, SystemLibraries.LoadLibraryFromSystem32);

    /// <summary>
    /// <paramref name="libraryName"/> loaded with <paramref name="loadFromSystem32"/> where it is
    /// a Windows library, and <see cref="IntPtr.Zero"/>, which hands it to the runtime's own
    /// search, where it is not.
    /// </summary>
    internal static IntPtr Resolve(string libraryName, Func<string, IntPtr> loadFromSystem32) =>
        IsWindowsLibrary(libraryName) ? loadFromSystem32(libraryName) : IntPtr.Zero;

    /// <summary>
    /// Sets the resolver on every assembly the process has loaded and will load, where
    /// <paramref name="exe"/> is the assembly the process was started for, then loads the
    /// assemblies named in <paramref name="importing"/>, and those named in
    /// <paramref name="importingWhereTrusted"/> that the runtime lists as trusted platform
    /// assemblies. Each exe calls this from a module initializer, which runs before its Main,
    /// naming the assemblies its self-contained build carries that import a Windows library,
    /// and the window also those the installed desktop runtime adds.
    /// </summary>
    /// <remarks>
    /// The resolver is set as each assembly is loaded, before any of its code runs. Loading
    /// the named assemblies here, on the thread that runs the module initializer and before
    /// the app starts any thread of its own, means no other thread can be loading one of them
    /// at the same moment and bind one of its imports before its resolver is set. An assembly
    /// not named is given its resolver as it is loaded. Loading an assembly runs none of its
    /// code, but it does make the assembly one the process has loaded, which WPF reads for some
    /// assemblies; the native-libraries job in ci.yml says which are left out for that and
    /// fails where an exe's lists differ from what its builds carry. Nothing here throws, since a
    /// module initializer that throws stops the exe before its Main; a refusal is written to
    /// the crash log.
    /// </remarks>
    internal static void ResolveFor(
        Assembly exe, IReadOnlyList<string> importing, IReadOnlyList<string>? importingWhereTrusted = null)
    {
        try
        {
            var domain = AppDomain.CurrentDomain;
            ResolveFor(
                exe,
                Assembly.GetEntryAssembly(),
                register => domain.AssemblyLoad += (_, e) => register(e.LoadedAssembly),
                domain.GetAssemblies,
                assembly => Register(assembly, s_registered, NativeLibrary.SetDllImportResolver),
                () => Load(ToLoad(
                    importing,
                    AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string,
                    importingWhereTrusted)));
        }
        catch (Exception ex)
        {
            Record(ex);
        }
    }

    /// <summary>
    /// <see cref="ResolveFor(Assembly, IReadOnlyList{string})"/> with its parts given, answering whether
    /// the resolvers were set. Where <paramref name="exe"/> is not <paramref name="entry"/>,
    /// as in a test run, whose process was started for its test host, nothing is done.
    /// Otherwise <paramref name="register"/> is subscribed to every load first, then given
    /// every assembly already loaded, and only then does <paramref name="loadImporting"/>
    /// load the rest, so no assembly is loaded between the two without its resolver.
    /// </summary>
    internal static bool ResolveFor(
        Assembly exe,
        Assembly? entry,
        Action<Action<Assembly>> subscribe,
        Func<IEnumerable<Assembly>> loaded,
        Action<Assembly> register,
        Action loadImporting)
    {
        if (entry != exe) return false;
        subscribe(register);
        foreach (var assembly in loaded())
            register(assembly);
        loadImporting();
        return true;
    }

    /// <summary>
    /// Sets the resolver on <paramref name="assembly"/> with <paramref name="setResolver"/>
    /// and adds it to <paramref name="registered"/>, unless it is System.Private.CoreLib, this
    /// assembly, a dynamic assembly or one <paramref name="registered"/> already holds. Never
    /// throws: it runs inside the load that raised the event, where the runtime discards an
    /// exception without a trace and skips every handler after this one, so a refusal is
    /// written to the crash log here.
    /// </summary>
    internal static void Register(
        Assembly assembly, ISet<Assembly> registered, Action<Assembly, DllImportResolver> setResolver)
    {
        try
        {
            if (assembly == typeof(object).Assembly || assembly == typeof(FrameworkLibraries).Assembly
                || assembly.IsDynamic)
                return;
            lock (registered)
            {
                if (!registered.Add(assembly)) return;
            }
            setResolver(assembly, Resolve);
        }
        catch (Exception ex)
        {
            Record(ex);
        }
    }

    /// <summary>
    /// The assemblies to load before Main, in order: every name in <paramref name="importing"/>,
    /// then each name in <paramref name="importingWhereTrusted"/> that
    /// <paramref name="platformAssemblies"/> names.
    /// </summary>
    internal static IReadOnlyList<string> ToLoad(
        IReadOnlyList<string> importing, string? platformAssemblies, IReadOnlyList<string>? importingWhereTrusted) =>
        [.. importing, .. Trusted(platformAssemblies, importingWhereTrusted)];

    /// <summary>
    /// The names in <paramref name="names"/> whose file, without ".dll" and ignoring case, is one
    /// of the paths <paramref name="platformAssemblies"/> lists, in the form of the runtime's
    /// TRUSTED_PLATFORM_ASSEMBLIES property. The host of a framework-dependent build lists every
    /// assembly of the installed runtime there; that of a single-file build lists none of the
    /// assemblies its bundle carries. Where the value is missing or names none of them, nothing
    /// is returned, so nothing is loaded and nothing is written to the crash log, and such an
    /// assembly is given its resolver as it is loaded.
    /// </summary>
    internal static IReadOnlyList<string> Trusted(string? platformAssemblies, IReadOnlyList<string>? names)
    {
        if (string.IsNullOrEmpty(platformAssemblies) || names is null || names.Count == 0)
            return [];
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var file = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                files.Add(file[..^4]);
        }
        return [.. names.Where(files.Contains)];
    }

    /// <summary>Loads each assembly <paramref name="names"/> names.</summary>
    private static void Load(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                Assembly.Load(new AssemblyName(name));
            }
            catch (Exception ex)
            {
                Record(ex);
            }
        }
    }

    /// <summary>Writes <paramref name="ex"/> to the crash log, where the log can be written.</summary>
    private static void Record(Exception ex)
    {
        if (t_recording) return;
        t_recording = true;
        try
        {
            CrashLog.TryWrite(ex);
        }
        catch (Exception)
        {
            // The crash log could not be reached either; the exe starts without the entry.
        }
        finally
        {
            t_recording = false;
        }
    }
}
