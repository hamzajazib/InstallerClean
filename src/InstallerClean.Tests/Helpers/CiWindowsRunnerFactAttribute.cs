using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A fact that runs only on a GitHub Actions Windows runner and is reported skipped
/// anywhere else, for a test that leaves something on the machine it runs on. GitHub
/// documents each of its hosted runners as a new virtual machine and its Windows ones as
/// running as administrators, and sets <c>GITHUB_ACTIONS</c> to <c>true</c> whenever
/// Actions runs the workflow.
/// </summary>
public sealed class CiWindowsRunnerFactAttribute : FactAttribute
{
    public CiWindowsRunnerFactAttribute()
    {
        if (!OperatingSystem.IsWindows()
            || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || !AdministratorRights.Held())
            Skip = "Leaves an entry in this machine's Application log, so it runs only on a GitHub Actions Windows runner.";
    }
}
