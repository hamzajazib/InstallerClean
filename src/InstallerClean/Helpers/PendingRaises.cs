namespace InstallerClean.Helpers;

/// <summary>
/// The rule <see cref="LiveRegionRaises"/> queues its raises by, apart from the dispatcher
/// that runs them and the automation peer that raises them, so it touches no WPF type.
///
/// An element has at most one raise pending. A request for an element whose raise has been
/// posted and has not yet run posts nothing more, so the one raise runs where the first
/// request put it, in the order the requests for different elements came in. The raise is
/// made only where every test given for it, by any of those requests, answers true when it
/// runs. Each says whether the line is still shown, so one answering false means the line has
/// gone, and the tests after it are not asked. Once the raise has run, the next request for
/// that element posts a raise of its own.
/// </summary>
internal sealed class PendingRaises<TElement>(Action<Action> post, Action<TElement> raise)
    where TElement : notnull
{
    /// <summary>
    /// Each element with a raise posted that has not yet run, and the test the raise is made
    /// under, null where no request for it gave one.
    /// </summary>
    private readonly Dictionary<TElement, Func<bool>?> _pending = new();

    /// <summary>
    /// Posts a raise for <paramref name="element"/>, or, where one is already pending, adds
    /// <paramref name="stillShown"/> to the tests it is made under.
    /// </summary>
    internal void Queue(TElement element, Func<bool>? stillShown)
    {
        if (_pending.TryGetValue(element, out var pending))
        {
            _pending[element] = Both(pending, stillShown);
            return;
        }

        _pending[element] = stillShown;
        post(() =>
        {
            _pending.Remove(element, out var test);
            if (test is null || test())
                raise(element);
        });
    }

    /// <summary>A test that passes where both given pass, a missing one passing.</summary>
    private static Func<bool>? Both(Func<bool>? first, Func<bool>? second) =>
        first is null ? second
        : second is null ? first
        : () => first() && second();
}
