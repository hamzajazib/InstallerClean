using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The label <see cref="MoveDestinationClassifier"/> gives a Move destination for the result log
/// and the opt-in report.
/// </summary>
/// <remarks>
/// THE ARMS THAT ANSWER FROM THE PATH'S SPELLING ARE HERE, and they are worth
/// pinning because they are the ones that run BEFORE anything goes to the
/// network. Classify reads the volume a folder is on
/// rather than the letter its path starts with, so the fixed and removable arms
/// go on to ask what is mounted where and want real storage to answer. The
/// same-drive arm takes its answer from the resolve the method is handed, and it
/// is covered end to end by
/// <c>CleanupPreFlightTests.A_move_to_the_same_drive_tells_the_confirmation_it_frees_no_space</c>
/// with the predicate underneath it in
/// <c>Helpers.Integration.MoveSpaceCheckIntegrationTests</c>.
///
/// AND "ANSWERS FROM THE SPELLING" IS PROVED HERE RATHER THAN READ. Both
/// theories hand the classifier a resolve that throws, so an arm that consulted
/// the volume before answering would fail rather than return the right kind by a
/// route these tests cannot see.
/// </remarks>
public class MoveDestinationClassifierTests
{
    /// <summary>
    /// The volume question, handed to <c>Classify</c> by the two
    /// theories below. Both cover an arm that answers from the spelling of the
    /// path, and reaching this is the failure those theories exist to catch:
    /// a reordering that asked the volume first would still return the right
    /// kind, and would have moved those answers onto the network to get it.
    /// </summary>
    private static bool? TheVolumeIsNeverAsked(string dest) =>
        throw new InvalidOperationException(
            $"the volume was asked about '{dest}' on an arm that answers from the path's spelling");

    [Theory]
    [InlineData(@"\\server\backup")]
    // Both separators and both device prefixes, for the reason
    // StorageHelpersRemotePathTests gives at length: a share that reads as local
    // here is a share sent to a call Win32 validates over the network. The
    // classifier runs inside the pre-flight, off the dispatcher, so the cost is
    // not the hazard; the wrong LABEL in the result log is, because that field
    // is one of the few things this project knows about real machines.
    [InlineData("//server/backup")]
    [InlineData(@"\\?\UNC\server\backup")]
    public void Classify_calls_a_share_a_share(string dest) =>
        Assert.Equal(MoveDestinationKinds.UncShare,
            MoveDestinationClassifier.Classify(dest, TheVolumeIsNeverAsked));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_says_unknown_when_there_is_nothing_to_classify(string dest) =>
        Assert.Equal(MoveDestinationKinds.Unknown,
            MoveDestinationClassifier.Classify(dest, TheVolumeIsNeverAsked));
}
