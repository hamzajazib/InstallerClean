using System.Diagnostics;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A fact that runs only on a GitHub Actions Windows runner, as
/// <see cref="CiWindowsRunnerFactAttribute"/> does, and only where a console program
/// started there gets a console window of its own, which a test can close. Reported skipped
/// anywhere else, with the reason. Whether the window comes is asked once per run, of a
/// cmd.exe started the same way the test starts its program
/// (<see cref="OtherConsole.StartInOwnConsole"/>).
/// </summary>
public sealed class OwnConsoleWindowFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> NoWindow = new(Probe);

    public OwnConsoleWindowFactAttribute()
    {
        if (CiWindowsRunnerFactAttribute.SkipReason() is { } elsewhere)
            Skip = elsewhere;
        else if (NoWindow.Value is { } why)
            Skip = $"This runner gives a console program started in a console of its own no conhost console window to close ({why}).";
    }

    /// <summary>
    /// Null where a cmd.exe started in a console of its own has a conhost console window, or
    /// where starting it or asking about it failed, so the test runs and reports that failure
    /// itself; otherwise what Windows answered: no window, or a window of another class.
    /// </summary>
    private static string? Probe()
    {
        Process? cmd = null;
        Process? host = null;
        try
        {
            (cmd, host) = OtherConsole.StartInOwnConsole(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/k", out _);
            if (cmd is null) return null;
            Thread.Sleep(1000);
            var answer = OtherConsole.WindowClass(cmd.Id);
            if (answer == "!nowindow") return "GetConsoleWindow answered no window";
            if (OtherConsole.Failed(answer) || answer == "ConsoleWindowClass") return null;
            return $"its console window is of class {answer}";
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            OtherConsole.End(cmd);
            OtherConsole.End(host);
        }
    }
}
