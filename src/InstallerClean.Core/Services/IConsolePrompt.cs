namespace InstallerClean.Services;

/// <summary>
/// The console as a question reaches it: lines out, text left waiting at the cursor,
/// and one line of the person's answer back. The command line's update and its report
/// offer both ask through it.
///
/// <see cref="WriteLine"/> and <see cref="Write"/> never throw. The command line's host
/// writes through the guard its Main puts on the console's output, which takes a failed
/// write, records the run's first one in crash.log and lets the run go on, so a caller
/// needs no guard of its own. A host added later keeps that promise.
/// </summary>
internal interface IConsolePrompt
{
    /// <summary>Prints a line. Never throws.</summary>
    void WriteLine(string line);

    /// <summary>Prints text and leaves the cursor after it, for the question. Never throws.</summary>
    void Write(string text);

    /// <summary>
    /// Reads one line of the person's answer, or null at the end of input, which is also
    /// what Ctrl+C at the prompt can end the read with, and null where
    /// <paramref name="token"/> is cancelled first, as a console being closed cancels it
    /// with the read still waiting.
    /// </summary>
    string? ReadLine(CancellationToken token);
}
