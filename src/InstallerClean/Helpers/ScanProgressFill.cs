namespace InstallerClean.Helpers;

/// <summary>
/// Where the splash's progress bar stands as the scan reports its way through.
/// Holds the arithmetic and none of the drawing, so it can be exercised without a
/// window.
///
/// The scan reports five milestones and, between them, a ticker carrying how far
/// the current phase has reached. Each milestone opens a band of the bar, and the
/// ticker moves the fill inside that band only. The fill therefore makes one
/// visible move per phase on every machine, keeps moving while a long phase
/// works, and no phase can spend a later phase's share of the bar.
///
/// FIVE MILESTONES DIVIDE THE RANGE INTO FOUR BANDS, because a milestone opens a
/// band rather than closing one: the first lands on the floor, and the last lands
/// on the ceiling with the scan's result to show. A band for each milestone
/// instead of for each gap between them makes the range one band longer than the
/// work, and the top of it is then never drawn.
///
/// The fill only ever moves forward. Every position handed out is a band floor or
/// a point inside the current band, so nothing here asks it to go back; the clamp
/// holds that true for whatever the scan reports next. A phase added to the scan
/// with no band left to put it in would otherwise be able to land below where the
/// ticker had already taken the fill, and a bar that steps backwards reads as a
/// fault in the scan rather than in the bar.
/// </summary>
internal sealed class ScanProgressFill
{
    /// <summary>Where the host leaves the bar before the scan starts.</summary>
    public const double FloorPercent = 10;

    /// <summary>Where the host's closing step takes over and finishes the fill.</summary>
    public const double CeilingPercent = 95;

    private const int BandCount = 4;
    private const double BandWidth = (CeilingPercent - FloorPercent) / BandCount;

    // How far into its band a phase that reports no total carries the fill: half
    // the band at this many updates, slower from there. Such a phase keeps moving
    // however long it runs and still arrives at the next milestone with band left
    // to give.
    private const double UntotalledHalfBand = 8;

    private int _bandIndex;
    private int _untotalled;
    private double _fill;

    /// <summary>Where the bar stands right now.</summary>
    public double Fill => _fill;

    /// <summary>The fill as the next phase opens.</summary>
    public double AtMilestone()
    {
        _bandIndex++;
        _untotalled = 0;
        return Advance(BandFloor());
    }

    /// <summary>
    /// The fill partway through the current phase. A position against a total
    /// fills the band in proportion and reaches the next band's floor on the last
    /// item, so the phase hands over with no step in the bar. A phase that reports
    /// no total, meaning <paramref name="total"/> is zero, approaches the top of
    /// its band without reaching it, which is the most a bar can say about work
    /// whose length is not yet known.
    /// </summary>
    public double AtTicker(int position, int total)
    {
        _untotalled++;
        var into = total > 0
            ? BandWidth * Math.Clamp((double)position / total, 0, 1)
            : BandWidth * _untotalled / (_untotalled + UntotalledHalfBand);
        // Bounded by the ceiling as well as by the band: a band resting on the
        // ceiling has no room above it, and the closing step owns what is left.
        return Advance(Math.Min(BandFloor() + into, CeilingPercent));
    }

    /// <summary>
    /// A fill the host sets itself rather than reading off the scan: the opening
    /// step before the scan starts, and the closing step after it finishes.
    /// </summary>
    public double At(double percent) => Advance(percent);

    /// <summary>
    /// Where the bar stands as the current phase opens. Bounded at both ends: a
    /// ticker arriving before any milestone reads the first band rather than one
    /// below it, and a phase beyond the milestones the scan reports rests on the
    /// ceiling rather than running past it into the closing step.
    /// </summary>
    private double BandFloor() => Math.Min(
        FloorPercent + BandWidth * (Math.Max(_bandIndex, 1) - 1),
        CeilingPercent);

    private double Advance(double percent)
    {
        _fill = Math.Max(percent, _fill);
        return _fill;
    }
}
