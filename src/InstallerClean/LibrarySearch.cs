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
    /// The assemblies the window's self-contained build carries that import a Windows library,
    /// loaded before its Main so each has its resolver before any thread of the app's can use
    /// it, all but three: System.Drawing.Common, whose being loaded WPF reads, and the ribbon
    /// and Windows Forms hosting assemblies, which add types to the XAML namespace the window's
    /// markup uses. Those three are given their resolver as they are loaded. A name here that
    /// the build does not carry fails to load, and the failure is written to the crash log. The
    /// native-libraries job in ci.yml fails where this list differs from what the
    /// self-contained build carries.
    /// </summary>
    internal static readonly string[] ImportingAssemblies =
    [
        "DirectWriteForwarder", "Microsoft.VisualBasic.Core",
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
        "System.Windows.Extensions", "System.Windows.Primitives",
        "UIAutomationClient", "UIAutomationClientSideProviders", "UIAutomationProvider", "UIAutomationTypes",
        "WindowsBase",
    ];

    /// <summary>
    /// The assemblies the installed desktop runtime carries and the self-contained build does
    /// not that import a Windows library, which the framework-dependent build can load. Each is
    /// loaded before Main where the runtime lists it as a trusted platform assembly. The host
    /// lists every assembly of the installed runtime there and none that a bundle carries, so
    /// the self-contained build never asks for them. The native-libraries job in
    /// ci.yml fails where this list differs from what the runtime packs add to what the
    /// self-contained build carries.
    /// </summary>
    internal static readonly string[] ImportingAssembliesWhereInstalled =
    [
        "Microsoft.VisualBasic.Forms", "System.Windows.Forms.Primitives",
    ];

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "The search has to be set before the window's first library is loaded.")]
    internal static void SetBeforeMain()
    {
        SystemLibraries.SearchSystem32For(typeof(LibrarySearch).Assembly);
        FrameworkLibraries.ResolveFor(
            typeof(LibrarySearch).Assembly, ImportingAssemblies, ImportingAssembliesWhereInstalled);
    }
}
