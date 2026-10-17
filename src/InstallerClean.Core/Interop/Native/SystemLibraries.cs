using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InstallerClean.Interop.Native;

/// <summary>
/// Loads every library this assembly imports from System32, before the runtime searches
/// anywhere for it, so a file of the same name in the folder the app runs from, such as a
/// Downloads folder holding the portable copy, or in the folder a single-file build
/// extracts itself to, is not loaded into the elevated process. Every library under
/// Interop/Native/ is part of Windows and lives in System32.
/// </summary>
/// <remarks>
/// Each is loaded with LOAD_LIBRARY_SEARCH_SYSTEM32, which looks in System32 for the
/// library and for every library it depends on. LoadLibraryExW is reached through
/// kernel32's own export rather than an import of this assembly, since an import would
/// come back here to be resolved. A library that cannot be loaded from System32 fails
/// the call that imports it, with the <see cref="DllNotFoundException"/> the runtime
/// gives a missing library, rather than falling back to the runtime's own search. An
/// import of a library that ships with the app needs a path of its own here.
/// </remarks>
internal static unsafe class SystemLibraries
{
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    /// <summary>LoadLibraryExW, read from kernel32 the first time a library is resolved.</summary>
    private static delegate* unmanaged<char*, IntPtr, uint, IntPtr> s_loadLibraryEx;

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "The resolver has to be in place before this assembly's first import, whichever host calls it.")]
    internal static void LoadFromSystem32() =>
        NativeLibrary.SetDllImportResolver(typeof(SystemLibraries).Assembly, Resolve);

    /// <summary>The library <paramref name="libraryName"/> names, loaded from System32.</summary>
    internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var loadLibraryEx = s_loadLibraryEx;
        if (loadLibraryEx == null)
        {
            var kernel32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
            if (!NativeLibrary.TryLoad(kernel32, out var module)
                || !NativeLibrary.TryGetExport(module, "LoadLibraryExW", out var export))
                throw new DllNotFoundException($"{libraryName} could not be loaded: kernel32's LoadLibraryExW is not there.");
            s_loadLibraryEx = loadLibraryEx = (delegate* unmanaged<char*, IntPtr, uint, IntPtr>)export;
        }

        IntPtr handle;
        fixed (char* name = libraryName)
            handle = loadLibraryEx(name, IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        return handle != IntPtr.Zero
            ? handle
            : throw new DllNotFoundException($"{libraryName} could not be loaded from System32.");
    }
}
