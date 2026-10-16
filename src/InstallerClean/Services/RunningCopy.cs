using System.Reflection;

namespace InstallerClean.Services;

/// <summary>
/// The kind of copy of the app that is running, from what this build states about
/// itself and what <see cref="AppCopy.DetectRunning"/> reads off the PC.
/// </summary>
internal static class RunningCopy
{
    /// <summary>
    /// The name of the assembly metadata the project file writes from
    /// <c>$(SelfContained)</c>: "true" in a build that carries its own .NET runtime.
    /// </summary>
    internal const string SelfContainedMetadataKey = "InstallerClean.SelfContained";

    /// <summary>
    /// The kind of copy running. A process whose own path Windows does not give is
    /// taken for a build from source, which offers the release page alone.
    /// </summary>
    public static AppCopyKind Kind(IRegistryReader registry) =>
        Environment.ProcessPath is { Length: > 0 } exePath
            ? AppCopy.DetectRunning(exePath, BuildIsSelfContained(), registry)
            : AppCopyKind.FromSource;

    /// <summary>Whether this build carries its own .NET runtime, as its project file stated.</summary>
    internal static bool BuildIsSelfContained() =>
        typeof(RunningCopy).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == SelfContainedMetadataKey
                && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));
}
