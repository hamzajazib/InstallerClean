namespace InstallerClean.Models;

/// <summary>
/// A drive or share, or a path of another form, that the declared-product check stopped
/// reading for the rest of one pass (<see cref="Services.IDeclaredProductCheck.Screen"/>):
/// no read was started there after, and every file whose check still needed one there was
/// kept.
/// </summary>
/// <param name="Root">
/// The drive letter and its colon, the share, or the path of another form, in the spelling
/// the pass first gave it up under. The pass compares roots without case, so it gives each
/// up once whatever spellings its sources use.
/// </param>
/// <param name="Route">How the pass came to give it up.</param>
/// <param name="FilesKept">
/// How many of the pass's files it kept where their check stopped at a read under
/// <paramref name="Root"/> that the pass refused for it: a read not started because the
/// root had been given up, a read that did not answer within the check's time limit, and a
/// read that answered false only after a long wait. A file whose check stopped at anything
/// else is not counted, and each check stops at the first thing that keeps its file.
///
/// IT CAN BE NOUGHT. A read whose answer was used can take a root's total past its budget
/// and give the root up, and where no read is made there after, nothing was refused there.
/// </param>
public sealed record SourceRootGivenUp(string Root, SourceRootGiveUpRoute Route, int FilesKept);

/// <summary>
/// How the declared-product check came to give a drive or share up for the rest of a pass.
///
/// THE MEMBERS ARE IN RANK ORDER, AND THE CHECK READS THE ORDER. Where one read meets more
/// than one of them, its root takes the one declared first, so do not reorder them. A read's
/// own outcome comes before a total that read took past its budget, and the total of the
/// reads that failed comes before the total of all reads.
/// </summary>
public enum SourceRootGiveUpRoute
{
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
