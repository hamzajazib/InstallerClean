namespace InstallerClean.Models;

/// <summary>
/// A drive or share, or a path of another form, that the declared-product check stopped
/// reading for the rest of one pass (<see cref="Services.IDeclaredProductCheck.Screen"/>):
/// no read was started there after, and every file whose check still needed one there was
/// kept.
/// </summary>
/// <param name="Root">
/// The drive letter and its colon, the share, or the path of another form, in the spelling
/// the pass first gave it up under, and for a root the caller stopped waiting for, in the
/// spelling of the wait it stopped. The pass compares roots without case, so it gives each
/// up once whatever spellings its sources use.
/// </param>
/// <param name="Route">How the pass came to give it up.</param>
/// <param name="FilesKept">
/// How many of the pass's files it kept where their check stopped at a read under
/// <paramref name="Root"/> that the pass refused for it: a read not started because the
/// root had been given up or the caller had stopped waiting for it, a read the caller's stop
/// ended before it answered, a read that did not answer within the check's time limit, and a
/// read that answered false only after a long wait. A file whose check stopped at anything
/// else is not counted, and each check stops at the first thing that keeps its file.
///
/// IT CAN BE NOUGHT. A read whose answer was used can take a root's total past its budget,
/// or see the caller's stop as its answer comes in, and give the root up, and where no read
/// is made there after, nothing was refused there.
/// </param>
public sealed record SourceRootGivenUp(string Root, SourceRootGiveUpRoute Route, int FilesKept)
{
    /// <summary>
    /// Whether the pass kept at least one file at <see cref="Root"/>. A root given up with no
    /// file kept at it changed nothing the pass decided, so a host saying that a scan or a
    /// check gave up a drive or share names only the roots this is true of
    /// (<see cref="KeepingFiles"/>).
    /// </summary>
    internal bool KeptAnyFile => FilesKept > 0;

    /// <summary>
    /// The roots in <paramref name="roots"/> at which files were kept (<see cref="KeptAnyFile"/>),
    /// in the order given.
    /// </summary>
    internal static IReadOnlyList<SourceRootGivenUp> KeepingFiles(IReadOnlyList<SourceRootGivenUp> roots) =>
        roots.Where(root => root.KeptAnyFile).ToList();
}

/// <summary>
/// How the declared-product check came to give a drive or share up for the rest of a pass.
///
/// THE MEMBERS ARE IN RANK ORDER, AND THE CHECK READS THE ORDER. Where one read meets more
/// than one of them, its root takes the one declared first, so do not reorder them. The
/// caller's stop comes before every other: a read the stop ends has not answered, and a root
/// whose read saw the stop is listed as stopped whatever else that read met. A read's own
/// outcome comes before a total that read took past its budget, and the total of the reads
/// that failed comes before the total of all reads.
/// </summary>
public enum SourceRootGiveUpRoute
{
    /// <summary>
    /// The pass's caller stopped waiting for it
    /// (<see cref="SourceFolderWait.StopWaiting"/>). The first read there to see the stop gives
    /// it up: one the stop ends while it waits, one whose answer comes in with the stop, or one
    /// not started because of it.
    /// </summary>
    StoppedWaiting,

    /// <summary>
    /// A read there did not answer within the check's time limit
    /// (<see cref="Services.DeclaredProductCheck.SourceFolderTimeLimit"/>): a read of a source
    /// folder's package, or the read asking a drive what kind it is.
    /// </summary>
    NoAnswer,

    /// <summary>
    /// A read of a source folder's package there answered false only after waiting longer
    /// than <see cref="Services.DeclaredProductCheck.SourceFolderSlowFailure"/>.
    /// </summary>
    SlowFailure,

    /// <summary>
    /// The reads of source folders' packages there that answered false took more time
    /// between them than <see cref="Services.DeclaredProductCheck.SourceFolderFailedWaitBudget"/>.
    /// </summary>
    FailedReadsAddUp,

    /// <summary>
    /// All the reads there took more time between them than
    /// <see cref="Services.DeclaredProductCheck.SourceFolderReadBudget"/>, whatever each
    /// answered.
    /// </summary>
    ReadsAddUp,
}
