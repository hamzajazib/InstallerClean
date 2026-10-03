using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The rule the main window and the splash queue their screen-reader raises by. Each test
/// stands a list in for the dispatcher: a raise posted goes on the end of it, and
/// <see cref="Drain"/> runs what is on it in order, as the dispatcher runs what was posted at
/// one priority.
/// </summary>
public class PendingRaisesTests
{
    private const string Banner = "the pending-reboot line";
    private const string Headline = "the headline";

    private readonly List<Action> _posted = [];
    private readonly List<string> _raised = [];
    private readonly PendingRaises<string> _raises;

    public PendingRaisesTests() => _raises = new PendingRaises<string>(_posted.Add, _raised.Add);

    /// <summary>Runs everything posted, in order, including anything posted while it runs.</summary>
    private void Drain()
    {
        while (_posted.Count > 0)
        {
            var next = _posted[0];
            _posted.RemoveAt(0);
            next();
        }
    }

    /// <summary>A test answering <paramref name="answer"/>, or none for null.</summary>
    private static Func<bool>? Test(bool? answer) => answer is { } a ? () => a : null;

    [Fact]
    public void Requests_for_one_line_before_its_raise_runs_make_one_raise_in_the_place_of_the_first()
    {
        _raises.Queue(Banner, null);
        _raises.Queue(Headline, null);
        _raises.Queue(Banner, null);

        Drain();

        Assert.Equal([Banner, Headline], _raised);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Two_requests_for_one_line_raise_it_only_where_every_test_either_gave_passes(
        bool? first, bool? second, bool raised)
    {
        _raises.Queue(Banner, Test(first));
        _raises.Queue(Banner, Test(second));

        Drain();

        Assert.Equal(raised ? new[] { Banner } : [], _raised);
    }

    [Fact]
    public void A_test_is_asked_when_the_raise_runs_and_not_when_it_is_queued()
    {
        var shown = true;
        _raises.Queue(Banner, () => shown);
        shown = false;

        Drain();

        Assert.Empty(_raised);
    }

    [Fact]
    public void A_request_after_the_raise_has_run_posts_a_raise_of_its_own_under_its_own_test()
    {
        _raises.Queue(Banner, () => false);
        Drain();
        _raises.Queue(Banner, null);
        Drain();

        Assert.Equal([Banner], _raised);
    }

    [Fact]
    public void A_test_given_for_one_line_does_not_reach_the_raise_of_another()
    {
        _raises.Queue(Banner, () => false);
        _raises.Queue(Headline, null);

        Drain();

        Assert.Equal([Headline], _raised);
    }
}
