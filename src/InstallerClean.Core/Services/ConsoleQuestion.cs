using System.Globalization;
using InstallerClean.Resources;

namespace InstallerClean.Services;

/// <summary>How a question put to the console was answered.</summary>
internal enum ConsoleAnswer
{
    /// <summary>Enter, or an answer starting with this language's letter for yes or with Y.</summary>
    Yes,

    /// <summary>An answer starting with this language's letter for no or with N, or the end of input.</summary>
    No,

    /// <summary>Ctrl+C or a closing console came while the question waited.</summary>
    Cancelled,

    /// <summary>Anything else, which the caller reads or asks again.</summary>
    Other,
}

/// <summary>
/// A yes-or-no question at the console, put once. The command line's update and its
/// report offer both ask this way, so an answer means the same in either.
/// </summary>
internal static class ConsoleQuestion
{
    /// <summary>
    /// Prints the question and reads one answer. <paramref name="prompt"/> builds the
    /// question from the letters shown in its brackets: this language's letter for yes in
    /// capitals, since Enter answers yes, and its letter for no in lower case
    /// (<c>Cli.UpdateAnswerYes</c> and <c>Cli.UpdateAnswerNo</c>). The read is handed the
    /// token and ends as it is cancelled, by Ctrl+C or by the console being closed, and
    /// the answer is then <see cref="ConsoleAnswer.Cancelled"/>. The end of input is
    /// <see cref="ConsoleAnswer.No"/>, unless the token is cancelled while the question
    /// waits on it, up to <paramref name="controlCBound"/> in a run, since Ctrl+C at the
    /// prompt can itself be read as the end of input. <paramref name="answer"/> is the
    /// answer typed, trimmed.
    /// </summary>
    internal static ConsoleAnswer Ask(
        IConsolePrompt console, Func<string, string, string> prompt,
        CancellationToken token, TimeSpan controlCBound, out string answer)
    {
        var yes = Strings.Cli_UpdateAnswerYes.Trim();
        var no = Strings.Cli_UpdateAnswerNo.Trim();
        console.Write(prompt(
            yes.ToUpper(CultureInfo.CurrentUICulture), no.ToLower(CultureInfo.CurrentUICulture)) + " ");
        var line = console.ReadLine(token);
        if (line is null) token.WaitHandle.WaitOne(controlCBound);
        answer = line?.Trim() ?? string.Empty;
        if (token.IsCancellationRequested) return ConsoleAnswer.Cancelled;
        if (line is null) return ConsoleAnswer.No;
        if (answer.Length == 0 || StartsWith(answer, yes) || StartsWith(answer, "Y")) return ConsoleAnswer.Yes;
        if (StartsWith(answer, no) || StartsWith(answer, "N")) return ConsoleAnswer.No;
        return ConsoleAnswer.Other;

        static bool StartsWith(string answer, string letter) =>
            letter.Length > 0 && answer.StartsWith(letter, StringComparison.CurrentCultureIgnoreCase);
    }
}
