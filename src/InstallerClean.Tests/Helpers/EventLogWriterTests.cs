using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Pins the guarantee the CLI's audit paths are built on: an event-log write
/// swallows every failure it can meet, the summary build included, and records
/// the miss in <c>EventLogUnavailable</c> instead of throwing.
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
        }
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
