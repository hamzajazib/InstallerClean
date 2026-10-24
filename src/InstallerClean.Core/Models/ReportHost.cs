namespace InstallerClean.Models;

/// <summary>Which of the two programs is sending an opt-in report.</summary>
public enum ReportHost
{
    /// <summary>InstallerClean, the window.</summary>
    Window,

    /// <summary>The command-line tool.</summary>
    CommandLine,
}
