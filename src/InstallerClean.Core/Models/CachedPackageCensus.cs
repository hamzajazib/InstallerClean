namespace InstallerClean.Models;

/// <summary>
/// What one pass of the declared-product check read of the cached package of each
/// installation the caller listed, which it reads once, the first time an installation
/// package reaches that step, and of the record of each installation whose cached package
/// did not say which product it declares. Counts of installations, and nothing naming one.
///
/// AN INSTALLATION WHOSE CACHED PACKAGE DOES NOT SAY WHAT IT DECLARES, AND WHOSE OWN RECORD
/// DOES NOT SHOW AN ORDINARY INSTALLATION, KEEPS EVERY INSTALLATION PACKAGE the check would
/// otherwise let through, as <see cref="Services.DeclaredProductOutcome.SecondCopyUnestablished"/>.
/// Each such installation is counted once among the five members saying what its cached
/// package gave, and once among the three saying why its record did not settle it, so the
/// two groups add up to the same figure. A check built without its file readers reads no
/// cached package, and counts nothing.
/// </summary>
/// <param name="InstallationsRead">
/// The listed installations whose cached package the pass read: every one, where an
/// installation package reached that step, and none where none did.
/// </param>
/// <param name="KeptPathUnreadable">Keeping installations whose cached package's path would not read.</param>
/// <param name="KeptNoneRecorded">
/// Keeping installations that record no cached package: the path read as empty, or as a
/// value the record does not carry.
/// </param>
/// <param name="KeptNotThere">
/// Keeping installations whose cached package's path names no file that is there, a path
/// naming a folder included.
/// </param>
/// <param name="KeptWouldNotRead">Keeping installations whose cached package would not read.</param>
/// <param name="KeptNoProductCode">
/// Keeping installations whose cached package read and declares no product code, or read as
/// a patch.
/// </param>
/// <param name="KeptAnotherAccount">
/// Keeping installations in a per-user context that belong to an account other than the one
/// this process runs as, or whose account, or this process's, is not known. Their record is
/// not read.
/// </param>
/// <param name="KeptPackageCodeUnanswered">
/// Keeping installations whose record's <c>PackageCode</c> did not read as a value.
/// </param>
/// <param name="KeptInstanceTypeNotOrdinary">
/// Keeping installations whose <c>PackageCode</c> read and whose <c>InstanceType</c> read as
/// a second instance, or did not read.
/// </param>
/// <param name="KeptPerMachine">The keeping installations in the per-machine context.</param>
/// <param name="ReleasedOrdinary">
/// Installations whose cached package did not say what it declares and whose own record
/// shows an ordinary installation, so they keep nothing.
/// </param>
public sealed record CachedPackageCensus(
    int InstallationsRead,
    int KeptPathUnreadable,
    int KeptNoneRecorded,
    int KeptNotThere,
    int KeptWouldNotRead,
    int KeptNoProductCode,
    int KeptAnotherAccount,
    int KeptPackageCodeUnanswered,
    int KeptInstanceTypeNotOrdinary,
    int KeptPerMachine,
    int ReleasedOrdinary)
{
    /// <summary>A pass that read no installation's cached package.</summary>
    public static CachedPackageCensus None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
