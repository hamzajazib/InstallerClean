using System.Runtime.CompilerServices;
using InstallerClean.Interop.Native;

namespace InstallerClean;

/// <summary>
/// Sets the library search for the window's process before any of its code runs, so a library
/// asked for by its name alone is not looked for in the folder the exe is run from, and a
/// Windows library a .NET or WPF assembly imports is loaded from System32.
/// </summary>
internal static class LibrarySearch
{
    /// <summary>
    /// The assemblies the window can load that import a Windows library, loaded before its
    /// Main so each has its resolver before any thread of the app's can use it, all but three:
    /// System.Drawing.Common, whose being loaded WPF reads, and the ribbon and Windows Forms
    /// hosting assemblies, which add types to the XAML namespace the window's markup uses.
    /// Those three are given their resolver as they are loaded. The native-libraries job in
    /// ci.yml fails where this list differs from what the build's .NET and WPF runtime packs
    /// give.
    /// </summary>
    internal static readonly string[] ImportingAssemblies =
    [
        "DirectWriteForwarder", "Microsoft.VisualBasic.Core", "Microsoft.VisualBasic.Forms",
        "Microsoft.Win32.Registry", "Microsoft.Win32.SystemEvents", "PresentationCore", "PresentationFramework",
        "PresentationUI", "ReachFramework", "System.Console", "System.Diagnostics.EventLog",
        "System.Diagnostics.FileVersionInfo", "System.Diagnostics.PerformanceCounter", "System.Diagnostics.Process",
        "System.DirectoryServices", "System.Drawing.Primitives", "System.Formats.Tar",
        "System.IO.FileSystem.AccessControl", "System.IO.FileSystem.DriveInfo", "System.IO.FileSystem.Watcher",
        "System.IO.MemoryMappedFiles", "System.IO.Pipes", "System.Net.Http", "System.Net.HttpListener",
        "System.Net.NameResolution", "System.Net.NetworkInformation", "System.Net.Ping", "System.Net.Primitives",
        "System.Net.Quic", "System.Net.Security", "System.Net.Sockets", "System.Printing",
        "System.Private.Windows.Core", "System.Private.Windows.GdiPlus", "System.Security.AccessControl",
        "System.Security.Cryptography", "System.Security.Cryptography.Pkcs",
        "System.Security.Cryptography.ProtectedData", "System.Security.Principal.Windows",
        "System.Text.Encoding.CodePages", "System.Threading.AccessControl", "System.Transactions.Local",
        "System.Windows.Extensions", "System.Windows.Forms.Primitives", "System.Windows.Primitives",
        "UIAutomationClient", "UIAutomationClientSideProviders", "UIAutomationProvider", "UIAutomationTypes",
        "WindowsBase",
    ];

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "The search has to be set before the window's first library is loaded.")]
    internal static void SetBeforeMain()
    {
        SystemLibraries.SearchSystem32For(typeof(LibrarySearch).Assembly);
        FrameworkLibraries.ResolveFor(typeof(LibrarySearch).Assembly, ImportingAssemblies);
    }
}
