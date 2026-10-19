using System.Runtime.CompilerServices;
using InstallerClean.Interop.Native;

namespace InstallerClean.Cli;

/// <summary>
/// Sets the library search for the command line's process before any of its code runs, so a library
/// asked for by its name alone is not looked for in the folder the exe is run from, and a Windows
/// library a .NET assembly imports is loaded from System32.
/// </summary>
internal static class LibrarySearch
{
    /// <summary>
    /// Every assembly the command line's build carries that imports a Windows library, loaded
    /// before its Main so each has its resolver before any thread of the app's can use it. The
    /// native-libraries job in ci.yml fails where this list differs from what the build's bundle
    /// carries.
    /// </summary>
    internal static readonly string[] ImportingAssemblies =
    [
        "Microsoft.VisualBasic.Core", "Microsoft.Win32.Registry", "System.Console", "System.Diagnostics.EventLog",
        "System.Diagnostics.FileVersionInfo", "System.Diagnostics.Process", "System.Drawing.Primitives",
        "System.Formats.Tar", "System.IO.FileSystem.AccessControl", "System.IO.FileSystem.DriveInfo",
        "System.IO.FileSystem.Watcher", "System.IO.MemoryMappedFiles", "System.IO.Pipes", "System.Net.Http",
        "System.Net.HttpListener", "System.Net.NameResolution", "System.Net.NetworkInformation", "System.Net.Ping",
        "System.Net.Primitives", "System.Net.Quic", "System.Net.Security", "System.Net.Sockets",
        "System.Security.AccessControl", "System.Security.Cryptography", "System.Security.Principal.Windows",
        "System.Text.Encoding.CodePages", "System.Threading.AccessControl", "System.Transactions.Local",
    ];

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "The search has to be set before the command line's first library is loaded.")]
    internal static void SetBeforeMain()
    {
        SystemLibraries.SearchSystem32For(typeof(LibrarySearch).Assembly);
        FrameworkLibraries.ResolveFor(typeof(LibrarySearch).Assembly, ImportingAssemblies);
    }
}
