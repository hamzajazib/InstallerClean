namespace InstallerClean.Models;

/// <summary>
/// What one pass of the declared-product check found about the two conditions under which it
/// holds back every installation package it would otherwise let through, as
/// <see cref="Services.DeclaredProductOutcome.SecondCopyUnestablished"/>. Counts of listed
/// installations, and of files, and nothing naming either.
///
/// THE FIRST CONDITION IS AN INSTALLATION THAT SETS THE HOLD: one whose cached package does
/// not say which product it declares and whose own record does not show an ordinary
/// installation. The check looks for the cached package of every installation the caller
/// listed, once, the first time an installation package reaches that step, and reads the
/// record of each one whose cached package gave no product code. The count is taken there,
/// before any file's verdict, so an installation is counted as setting the hold whether or
/// not any installation package was then let through for it to hold back. Each such
/// installation is counted once among the five members saying what its cached package gave,
/// and once among the three saying why its record did not settle it, so the two groups add up
/// to the same figure.
///
/// THE SECOND CONDITION IS READ ONLY WHERE NO INSTALLATION SETS THE HOLD: an installation the
/// caller could not rule out as a second copy of a program whose packages cannot all be seen.
/// The check reads those installations one at a time, the first time an installation package
/// the answer about its own product lets through reaches that step, and stops at the first
/// one whose packages it cannot see. That installation is counted in one of the nine members
/// from <see cref="UnseenPathUnreadable"/> to <see cref="UnseenSourceNotRuledOut"/> by the step
/// that stopped it, so at most one of them is above zero. This count too is taken before any
/// file's verdict. A package in a folder on the network is read later, for each file whose name
/// it could be, and <see cref="UnseenByNameFiles"/> counts the files held back where one cannot
/// be ruled out.
///
/// A check built without its file readers reads no cached package and no second copy's
/// packages, and counts nothing. A check with them that cannot read sources counts that
/// under <see cref="UnseenSourceNotRuledOut"/>.
/// </summary>
/// <param name="ListedChecked">
/// The listed installations whose cached package the pass looked for: every one, where an
/// installation package reached that step, and none where none did.
/// </param>
/// <param name="KeptPathUnreadable">
/// Installations setting the hold for which Windows gave no answer about the cached package's
/// path: a read that failed, or an answer that it does not know the product in that account
/// and context.
/// </param>
/// <param name="KeptNoneRecorded">
/// Installations setting the hold that record no cached package: the path read as empty, or as
/// a value the record does not carry.
/// </param>
/// <param name="KeptNotThere">
/// Installations setting the hold whose cached package's path names no file that is there, a
/// path naming a folder included.
/// </param>
/// <param name="KeptWouldNotRead">
/// Installations setting the hold whose cached package would not give up its product code: it
/// would not open, or its <c>Property</c> table or the value would not read.
/// </param>
/// <param name="KeptNoProductCode">
/// Installations setting the hold whose cached package read and declares no product code, or
/// one that is not a well-formed GUID, or read as a patch.
/// </param>
/// <param name="KeptAnotherAccount">
/// Installations setting the hold in a per-user context that are not shown to belong to the
/// account this process runs as: another account's, or one whose account, or this process's,
/// is not known. Their record is not read.
/// </param>
/// <param name="KeptPackageCodeUnanswered">
/// Installations setting the hold whose record's <c>PackageCode</c> did not read as a value.
/// </param>
/// <param name="KeptInstanceTypeNotOrdinary">
/// Installations setting the hold whose <c>PackageCode</c> read and whose <c>InstanceType</c>
/// read as a second instance, or did not read.
/// </param>
/// <param name="KeptPerMachine">The installations setting the hold that are in the per-machine context.</param>
/// <param name="ReleasedOrdinary">
/// Installations whose cached package did not say what it declares and whose own record shows
/// an ordinary installation, so they do not set the hold.
/// </param>
/// <param name="UnruledChecked">
/// The listed installations not ruled out as a second copy whose packages the pass looked for,
/// the one it stopped at included. None where an installation sets the hold, or where no
/// installation package the answer about its own product lets through reached that step.
/// </param>
/// <param name="UnseenPathUnreadable">
/// The read stopped at an installation for which Windows gave no answer about its cached
/// package's path.
/// </param>
/// <param name="UnseenNoneRecorded">The read stopped at an installation that records no cached package.</param>
/// <param name="UnseenNotThere">
/// The read stopped at an installation whose cached package's path names no file that is there.
/// </param>
/// <param name="UnseenWouldNotIdentify">
/// The read stopped at an installation whose cached package's volume and file ID would not read.
/// </param>
/// <param name="UnseenWouldNotRead">
/// The read stopped at an installation whose cached package would not give up its product
/// code, as for <see cref="KeptWouldNotRead"/>.
/// </param>
/// <param name="UnseenNoProductCode">
/// The read stopped at an installation whose cached package declares no product code, or one
/// that is not a well-formed GUID, or read as a patch.
/// </param>
/// <param name="UnseenPerUserUnmanaged">
/// The read stopped at an installation in the per-user unmanaged context, whose source list is
/// not read.
/// </param>
/// <param name="UnseenSourcesGivenUp">
/// The read stopped at a package in a folder an installation's sources name, refused for a drive
/// or share given up for the pass, whether before the read or by it.
/// </param>
/// <param name="UnseenSourceNotRuledOut">
/// The read stopped at an installation whose sources could not be ruled out for any other
/// reason: the check had no way to read them; its package name, source list or installed-from
/// folder would not read, held a form the check does not compare, or differed from what the
/// registry holds; or a package there would not identify, or could be a file directly in the
/// Installer folder.
/// </param>
/// <param name="UnseenPerMachine">
/// The read stopped at an installation in the per-machine context: one, where one of the nine
/// members from <see cref="UnseenPathUnreadable"/> to <see cref="UnseenSourceNotRuledOut"/> is
/// above zero and that installation is per-machine, and none otherwise.
/// </param>
/// <param name="UnseenByNameFiles">
/// Files held back because a package in a folder on the network, which the sources of an
/// installation not ruled out as a second copy name and which the file could be by its name,
/// could not be ruled out.
/// </param>
public sealed record CachedPackageCensus(
    int ListedChecked,
    int KeptPathUnreadable,
    int KeptNoneRecorded,
    int KeptNotThere,
    int KeptWouldNotRead,
    int KeptNoProductCode,
    int KeptAnotherAccount,
    int KeptPackageCodeUnanswered,
    int KeptInstanceTypeNotOrdinary,
    int KeptPerMachine,
    int ReleasedOrdinary,
    int UnruledChecked,
    int UnseenPathUnreadable,
    int UnseenNoneRecorded,
    int UnseenNotThere,
    int UnseenWouldNotIdentify,
    int UnseenWouldNotRead,
    int UnseenNoProductCode,
    int UnseenPerUserUnmanaged,
    int UnseenSourcesGivenUp,
    int UnseenSourceNotRuledOut,
    int UnseenPerMachine,
    int UnseenByNameFiles)
{
    /// <summary>A pass that looked for no installation's packages.</summary>
    public static CachedPackageCensus None { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
