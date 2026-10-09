using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="CommandLineRunRecord"/> against the real Application log: one entry is
/// written the way the command line writes a Move's summary, through its own writer
/// and under its own source, and the reader has to find that entry and count it.
/// </summary>
public class CommandLineRunRecordIntegrationTests
{
    [CiWindowsRunnerFact]
    public async Task An_entry_the_command_line_writes_is_read_back_as_a_run_that_acted_on_files()
    {
        // A folder no other entry can name, so the entry read back is shown to be this one.
        var folder = @"C:\InstallerClean-test-" + Guid.NewGuid().ToString("N");

        // The assembly sends every entry to its recorder, so the writer is pointed at the
        // log for this one write and put back afterwards.
        using (EventLogRecorder.FreshLogState())
        {
            EventLogWriter.Sink = null;
            MachineContract.WriteEventLog(CliEventClass.Ok,
                () => string.Format(Strings.Cli_EventLogMoveSummary,
                    "/m", 1, 1, DisplayHelpers.PluraliseFile(1), folder,
                    DisplayHelpers.FormatSizeForMachine(100), 0, DisplayHelpers.PluraliseError(0)));
            Assert.False(EventLogWriter.EventLogUnavailable, "The entry was not written to the Application log.");
        }

        // Read until the entry shows, for up to five seconds.
        (bool Read, string? Entry) found = default;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            found = CommandLineRunRecord.NewestEntryActingOnFiles();
            if (found.Entry?.Contains(folder, StringComparison.Ordinal) == true) break;
            await Task.Delay(250);
        }

        Assert.True(found.Read, "The Application log could not be read.");
        Assert.NotNull(found.Entry);
        Assert.Contains(folder, found.Entry, StringComparison.Ordinal);
        Assert.Equal(CommandLineRunRecordState.ActedOnFiles, new CommandLineRunRecord().Read());
    }
}
