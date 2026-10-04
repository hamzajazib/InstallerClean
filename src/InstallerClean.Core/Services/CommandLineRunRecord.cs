using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using InstallerClean.Helpers;

namespace InstallerClean.Services;

/// <summary>
/// Production <see cref="ICommandLineRunRecord"/>: one query on the Application log,
/// filtered by the log service to the command line's source and to the Event IDs a
/// run that acted on files writes, then each entry's text read until one counts.
/// </summary>
internal sealed class CommandLineRunRecord : ICommandLineRunRecord
{
    /// <summary>
    /// The command line's source, and the Event IDs an entry recording a Move or a
    /// Delete that moved or deleted files can carry: Ok, Partial for a run with a
    /// failed file, a cancelled run or a stopped Move, and 0, which is the number an
    /// entry written without one carries and is how every entry from a version before
    /// 1.9.0 reads. Ok also carries scans and runs of no files, which the text tells
    /// apart. The other Event IDs are skips, notices and failures, and no failure's
    /// entry counts a file moved or deleted.
    /// </summary>
    private static readonly string Query =
        $"*[System[Provider[@Name='{EventLogWriter.SourceName}'] and "
        + $"(EventID=0 or EventID={CliContract.EventIdFor(CliEventClass.Ok)} "
        + $"or EventID={CliContract.EventIdFor(CliEventClass.Partial)})]]";

    /// <summary>
    /// The four lines that record a run which moved or deleted files: a Delete's
    /// summary, a Move's summary, a Move the app stopped part way, and a run cancelled
    /// at the console. Each pattern takes every spelling a version of the command line
    /// has written. Versions 1.5.2 and 1.5.3 open the two summaries with
    /// "Delete mode (/d):" and "Move mode (/m):", and later versions with "/d mode:"
    /// and "/m mode:". A Delete's summary says "sent to the Recycle Bin" before 3.0.0
    /// and "deleted permanently" from it. A cancelled run's line, written from 1.6.0,
    /// says "interrupted by Ctrl+C" before 3.0.0 and "cancelled at the console" from
    /// it, and the stopped Move's line is written from 3.0.0.
    ///
    /// Every version writes these lines in English whatever language Windows is in,
    /// from 2.0.0 through the machine contract (<c>MachineContract</c> in the command
    /// line), which builds them in en-GB. Every count is in the digits 0 to 9, which is
    /// how .NET formats an integer in every culture. The first group is the line's
    /// count of files, and the line counts only where that is above nought: an Ok entry
    /// also records a Delete or a Move of no files at all.
    /// </summary>
    private static readonly Regex[] ActedOnFilesLines =
    [
        new(@"^(?:/d mode|Delete mode \(/d\)): ([0-9]+) of [0-9]+ \S+ (?:sent to the Recycle Bin|deleted permanently), ",
            RegexOptions.CultureInvariant),
        new(@"^(?:/m mode|Move mode \(/m\)): ([0-9]+) of [0-9]+ \S+ moved to ",
            RegexOptions.CultureInvariant),
        new(@"^/m mode stopped: could no longer confirm the destination\. ([0-9]+) of [0-9]+ \S+ had already moved to ",
            RegexOptions.CultureInvariant),
        new(@"^/[dm] mode (?:interrupted by Ctrl\+C|cancelled at the console): ([0-9]+) of [0-9]+ \S+ processed before cancellation\.",
            RegexOptions.CultureInvariant),
    ];

    public CommandLineRunRecordState Read() =>
        NewestEntryActingOnFiles() switch
        {
            (false, _) => CommandLineRunRecordState.Unreadable,
            (_, not null) => CommandLineRunRecordState.ActedOnFiles,
            _ => CommandLineRunRecordState.NothingActedOn,
        };

    /// <summary>
    /// The text of the newest entry that records a Move or a Delete acting on files, or
    /// null where none does. <c>Read</c> is false where the log could not be read, and
    /// the failure is logged to crash.log.
    /// </summary>
    internal static (bool Read, string? Entry) NewestEntryActingOnFiles()
    {
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName, Query) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    // The command line writes its line as the entry's one insertion string.
                    if (record.Properties.Count > 0
                        && record.Properties[0].Value is string text
                        && ActedOnFiles(text))
                        return (true, text);
                }
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return (false, null);
        }
    }

    /// <summary>
    /// Whether <paramref name="entry"/> is one of the four lines that record a run which
    /// moved or deleted files, with a count above nought.
    /// </summary>
    internal static bool ActedOnFiles(string entry)
    {
        foreach (var line in ActedOnFilesLines)
        {
            var match = line.Match(entry);
            if (match.Success)
                return match.Groups[1].Value.Any(digit => digit != '0');
        }
        return false;
    }
}
