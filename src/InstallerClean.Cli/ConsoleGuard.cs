using System.Text;
using InstallerClean.Helpers;

namespace InstallerClean.Cli;

/// <summary>
/// Standard output for the whole run. Every write goes to the writer the console gave the
/// run, and a write that fails, such as one to an output redirected to a disk that is full,
/// is recorded in crash.log and ends the run's output: every later write is dropped. Nothing
/// the run prints can throw, so its Application-log entry and its exit code describe what the
/// run did whatever became of its output. A Delete that deleted files is logged and exits as
/// that Delete, and a run refused for something that clears by itself keeps the code that
/// tells a scheduler to come back.
///
/// <see cref="Program"/>'s Main installs it once, after it sets the code page.
/// Setting <see cref="Console.OutputEncoding"/> replaces a writer the console made for
/// itself and keeps one installed through <see cref="Console.SetOut"/>, so with the code
/// page set first this guard wraps a writer that already writes UTF-8, and putting the
/// code page back at the end leaves it in place.
/// </summary>
internal sealed class ConsoleGuard(TextWriter inner) : TextWriter
{
    /// <summary>Whether a write has failed, after which nothing more is written.</summary>
    internal bool Failed { get; private set; }

    public override Encoding Encoding => inner.Encoding;

    public override IFormatProvider FormatProvider => inner.FormatProvider;

    public override void Write(char value) => Guard(() => inner.Write(value));

    public override void Write(char[] buffer, int index, int count) =>
        Guard(() => inner.Write(buffer, index, count));

    public override void Write(string? value) => Guard(() => inner.Write(value));

    public override void WriteLine() => Guard(inner.WriteLine);

    public override void WriteLine(string? value) => Guard(() => inner.WriteLine(value));

    public override void Flush() => Guard(inner.Flush);

    private void Guard(Action write)
    {
        if (Failed) return;
        try
        {
            write();
        }
        catch (Exception ex)
        {
            // One entry for the run: a writer that has refused one line refuses the rest
            // for the same cause, and an entry each would spend crash.log's rotation on
            // copies of it.
            Failed = true;
            CrashLog.TryWrite(ex);
        }
    }
}
