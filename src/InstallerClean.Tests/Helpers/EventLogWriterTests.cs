using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Pins the guarantee the CLI's audit paths are built on: an event-log write
/// swallows every failure it can meet, the summary build included, and records
/// the miss in <c>EventLogUnavailable</c>, with the step that failed and how in
/// <c>LastFailure</c>, instead of throwing.
/// </summary>
/// <remarks>
/// What a throw escaping would cost is stated where the constraint lives, at the
/// CLI's mutex-blocked exit (Program.cs); this pins the property that comment
/// depends on. A builder that throws fails before the sink is reached, so the
/// assertions hold with or without one.
///
/// The recorder tests below hold the recorder that stands in for the Application
/// channel for the whole suite (EventLogRecorder), because every other test that
/// drives a write depends on it being there.
/// </remarks>
public class EventLogWriterTests
{
    [Fact]
    public void Write_swallows_a_summary_builder_that_throws()
    {
        var built = false;

        // The flag is process-wide and sticky, so it is put back afterwards: a
        // command-line test that prints the unavailable line must not do so because
        // this test ran before it.
        using (EventLogRecorder.FreshLogState())
        {
            var escaped = Record.Exception(() => EventLogWriter.Write(
                CliEventClass.TransientSkip,
                () =>
                {
                    built = true;
                    throw new FormatException("a resx template the summary interpolates");
                }));

            Assert.Null(escaped);
            // The writer has to have asked for the text, not merely declined to
            // rethrow: a Write that never invoked the builder would also not throw.
            Assert.True(built);
            Assert.True(EventLogWriter.EventLogUnavailable);
            Assert.Equal(
                "building the entry threw System.FormatException (HResult 0x80131537): "
                    + "a resx template the summary interpolates",
                EventLogWriter.LastFailure);
        }
    }

    [Fact]
    public void A_sink_that_throws_is_recorded_as_the_step_that_refused()
    {
        using (EventLogRecorder.FreshLogState())
        {
            EventLogWriter.Sink = (_, _) => throw new InvalidOperationException("The log refused the entry.");

            var escaped = Record.Exception(() => EventLogWriter.Write(CliEventClass.Ok, () => "an entry"));

            Assert.Null(escaped);
            Assert.True(EventLogWriter.EventLogUnavailable);
            Assert.Equal(
                "handing the entry to the sink threw System.InvalidOperationException (HResult 0x80131509): "
                    + "The log refused the entry.",
                EventLogWriter.LastFailure);
        }
    }

    [Fact]
    public void The_first_refused_write_in_a_process_goes_to_the_crash_log_and_a_later_one_does_not()
    {
        using var _ = TestCrashLog.OwnFolder();
        using (EventLogRecorder.FreshLogState())
        {
            EventLogWriter.Sink = (_, _) => throw new InvalidOperationException("The log refused the entry.");

            EventLogWriter.Write(CliEventClass.UpdatedNotice, () => "the first entry");
            EventLogWriter.Write(CliEventClass.Ok, () => "the second entry");
        }

        var log = TestCrashLog.Text();
        const string line = "An entry was not written to the Application log: handing the entry to the sink threw "
            + "System.InvalidOperationException (HResult 0x80131509): The log refused the entry.";
        Assert.Equal(1, log.Split(line).Length - 1);
        Assert.Equal(1, log.Split("An entry was not written to the Application log").Length - 1);
    }

    [Fact]
    public void The_recorder_is_the_writers_sink_for_the_whole_suite()
    {
        // Set when the assembly loads. If it were not, a test driving the command
        // line would write a real entry on any host with administrator rights.
        Assert.Same(EventLogRecorder.Sink, EventLogWriter.Sink);
    }

    [Fact]
    public void A_write_hands_the_recorder_its_class_and_the_built_entry()
    {
        EventLogRecorder.Clear();

        EventLogWriter.Write(CliEventClass.Partial, () => "/d mode: an entry");

        Assert.Equal([(CliEventClass.Partial, "/d mode: an entry")], EventLogRecorder.Entries);
    }
}
