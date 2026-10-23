using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A write under an event source this process has just registered is tried again for a
/// short bound, since Windows can refuse it until its list of sources has caught up, and
/// nothing else is. These drive the retry with a write that stands in for Windows, so
/// they run on any host.
/// </summary>
public class EventLogWriterRetryTests
{
    [Fact]
    public void A_write_refused_just_after_this_process_registered_the_source_is_tried_again_until_it_goes()
    {
        var tries = 0;
        var waits = new List<TimeSpan>();

        EventLogWriter.WriteRetryingAfterRegistration(
            () => { if (++tries < 4) throw new InvalidOperationException("Cannot open log for source"); },
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250), waits.Add);

        Assert.Equal(4, tries);
        Assert.Equal(3, waits.Count);
    }

    [Fact]
    public void A_write_still_refused_at_the_bound_throws_the_last_refusal()
    {
        var tries = 0;

        var thrown = Assert.Throws<InvalidOperationException>(() => EventLogWriter.WriteRetryingAfterRegistration(
            () => throw new InvalidOperationException($"refusal {++tries}"),
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250), _ => { }));

        Assert.Equal(5, tries);
        Assert.Equal("refusal 5", thrown.Message);
    }

    [Fact]
    public void A_write_refused_with_no_settle_time_left_is_not_tried_again()
    {
        var tries = 0;

        Assert.Throws<InvalidOperationException>(() => EventLogWriter.WriteRetryingAfterRegistration(
            () => { tries++; throw new InvalidOperationException("refused"); },
            TimeSpan.Zero, TimeSpan.FromMilliseconds(250), _ => { }));

        Assert.Equal(1, tries);
    }

    [Fact]
    public void Another_kind_of_failure_is_never_tried_again()
    {
        var tries = 0;

        Assert.Throws<System.Security.SecurityException>(() => EventLogWriter.WriteRetryingAfterRegistration(
            () => { tries++; throw new System.Security.SecurityException("denied"); },
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250), _ => { }));

        Assert.Equal(1, tries);
    }

    [Fact]
    public void A_disposed_object_is_never_tried_again()
    {
        var tries = 0;

        Assert.Throws<ObjectDisposedException>(() => EventLogWriter.WriteRetryingAfterRegistration(
            () => { tries++; throw new ObjectDisposedException("log"); },
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250), _ => { }));

        Assert.Equal(1, tries);
    }
}
