namespace InstallerClean.Services;

/// <summary>
/// The console as a question reaches it: lines out, text left waiting at the cursor,
/// and one line of the person's answer back. The command line's update and its report
/// offer both ask through it.
/// </summary>
internal interface IConsolePrompt
{
    /// <summary>Prints a line.</summary>
    void WriteLine(string line);

    /// <summary>Prints text and leaves the cursor after it, for the question.</summary>
    void Write(string text);

    /// <summary>
    /// Reads one line of the person's answer, or null at the end of input, which is also
    /// what Ctrl+C at the prompt can end the read with, and null where
    /// <paramref name="token"/> is cancelled first, as a console being closed cancels it
    /// with the read still waiting.
    /// </summary>
    string? ReadLine(CancellationToken token);
}
