using System.Globalization;
using NSubstitute;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;
using InstallerClean.ViewModels;

namespace InstallerClean.Tests.ViewModels;

public class CompletionViewModelTests
{
    /// <summary>
    /// A category whose sentence varies with the file. No shipped category does
    /// that today, which is why this is declared here rather than borrowed:
    /// RecycleFailed was the one that did, and it went with the Recycle Bin.
    /// The behaviour it stands for did not go, so it is held from here.
    /// </summary>
    private sealed record VaryingMessage(string FilePath, string Message)
        : FileOperationError(FilePath)
    {
        public override string LocalisedMessage => Message;
    }

    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo culture) => Localisation.Set(culture, culture);

        public void Dispose() => Localisation.Reset();
    }

    [Fact]
    public void FormatErrorBreakdown_splits_one_type_whose_sentence_varies_by_file()
    {
        // The grouping key is (type, message) rather than type alone, and this
        // is what that buys: a bucket's heading is taken from its FIRST member,
        // so a type that ever tailors its wording would otherwise print one
        // file's sentence over files that failed differently. Nothing shipped
        // tailors one today; the comment on FormatErrorBreakdown keeps the key
        // that way for the category that will, and this is the test that says
        // what "that way" means.
        var errors = new List<FileOperationError>
        {
            new VaryingMessage(@"C:\Windows\Installer\a.msi", "One thing went wrong."),
            new VaryingMessage(@"C:\Windows\Installer\b.msi", "A different thing went wrong."),
        };

        var text = CompletionViewModel.FormatErrorBreakdown(errors);

        Assert.Contains("One thing went wrong.", text);
        Assert.Contains("A different thing went wrong.", text);
        Assert.Contains("- a.msi", text);
        Assert.Contains("- b.msi", text);
    }

    [Fact]
    public void FormatErrorBreakdown_heads_a_bucket_with_its_pluralised_sentence()
    {
        var errors = new List<FileOperationError>
        {
            new FileInUse(@"C:\Windows\Installer\a.msi"),
            new FileInUse(@"C:\Windows\Installer\b.msi"),
        };

        var text = CompletionViewModel.FormatErrorBreakdown(errors);

        // The heading introduces the list, and carries no "(2)" bracket. Such a
        // bracket is a count no language can inflect, sitting on a sentence that
        // is already singular or plural, and it reads as a reference number.
        Assert.StartsWith(Strings.Error_FileInUse_Plural, text);
        Assert.DoesNotContain("(2)", text);
    }

    [Fact]
    public void FormatErrorBreakdown_keeps_a_single_failure_singular()
    {
        var errors = new List<FileOperationError> { new FileInUse(@"C:\Windows\Installer\a.msi") };

        var text = CompletionViewModel.FormatErrorBreakdown(errors);

        Assert.StartsWith(Strings.Error_FileInUse_Singular, text);
    }

    [Fact]
    public void FormatErrorBreakdown_marks_each_filename_with_a_hyphen()
    {
        var errors = new List<FileOperationError>
        {
            new AccessDenied(@"C:\Windows\Installer\a.msi"),
            new AccessDenied(@"C:\Windows\Installer\b.msi"),
        };

        var text = CompletionViewModel.FormatErrorBreakdown(errors);

        // Leading spaces alone vanish in a proportional font, so the hyphen is
        // what separates a filename from the sentence above it.
        Assert.Contains("- a.msi", text);
        Assert.Contains("- b.msi", text);
        // Filenames only: the full path can name another user's profile under
        // elevation.
        Assert.DoesNotContain(@"C:\Windows\Installer", text);
    }

    [Fact]
    public void ShowDeleteSummary_reads_freed_and_carries_no_restore_line()
    {
        var vm = TestCompletion.Create();
        vm.ShowDeleteSummary(deletedCount: 2, deletedBytes: 3 * 1024 * 1024,
            errors: new List<FileOperationError>());

        // A delete reclaims the disk at the instant it happens, so the headline
        // says so, and this is a completion state carrying no line under the
        // summary.
        Assert.Contains("freed", vm.Heading);
        Assert.Equal(string.Empty, vm.Restore);
    }

    [Fact]
    public void ShowMoveSummary_reads_freed_only_when_the_folder_is_on_another_drive()
    {
        var vm = TestCompletion.Create();

        vm.ShowMoveSummary(movedCount: 1, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);
        Assert.Contains("freed", vm.Heading);

        // A same-drive move is a rename: nothing is reclaimed until the folder
        // goes, so the claim-less verb is used and the line beneath says when
        // the space comes back.
        vm.ShowMoveSummary(movedCount: 1, movedBytes: 1024 * 1024, destination: @"C:\backup",
            errors: [], space: MoveSpaceOutcome.SameDrive);
        Assert.DoesNotContain("freed", vm.Heading);
        Assert.Equal(Strings.Completion_MoveRestoreHintSameDrive, vm.Restore);

        // A volume the classification could not read claims nothing either way,
        // so it takes the same verb but the line that names no drive. The
        // destination is not what selects this and is never read here: the
        // outcome arrives already decided (a share is FreedSpace, not this).
        vm.ShowMoveSummary(movedCount: 2, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.Unclassified);
        Assert.DoesNotContain("freed", vm.Heading);
        Assert.Equal(Strings.Completion_MoveRestoreHint, vm.Restore);
    }

    [Fact]
    public void ShowMoveSummary_sets_summary_destination_to_the_raw_path()
    {
        // The WPF host locates this raw string inside the formatted Summary
        // to force the destination onto its own line; it must be the
        // literal, unformatted path handed to ShowMoveSummary.
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 1, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.Equal(@"D:\backup", vm.SummaryDestination);
        Assert.Contains(@"D:\backup", vm.Summary);
    }

    [Fact]
    public void Summary_destination_from_a_move_is_cleared_by_a_following_delete()
    {
        // The view-model instance is reused across operations, so a stale
        // destination path from a prior move must not bleed into a later
        // delete summary (which has no destination placeholder at all).
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 1, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);
        Assert.NotEqual(string.Empty, vm.SummaryDestination);

        vm.ShowDeleteSummary(deletedCount: 1, deletedBytes: 1024 * 1024,
            errors: new List<FileOperationError>());
        Assert.Equal(string.Empty, vm.SummaryDestination);
    }

    // The three heading states, the failure count line and the acted-on-nothing
    // suppressions. The contract in one line: the heading says one thing, the
    // count line says how many failed, and nothing on the screen describes files
    // arriving somewhere none of them reached.

    private static List<FileOperationError> Failures(int count) =>
        [.. Enumerable.Range(0, count).Select(i =>
            (FileOperationError)new FileInUse($@"C:\Windows\Installer\f{i}.msi"))];

    [Fact]
    public void A_move_that_fully_succeeded_keeps_the_success_heading_and_no_count_line()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 3, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.False(vm.HeadingIsWarning);
        Assert.Contains("freed", vm.Heading);
        Assert.Equal(string.Empty, vm.FailedCount);
    }

    [Fact]
    public void A_partly_failed_move_keeps_the_success_heading_and_states_the_count()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 69, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // Something really was freed, so the heading says so and the failures
        // get their own line. Carrying both in the heading makes one clause too
        // long for the card, and it clips.
        Assert.False(vm.HeadingIsWarning);
        Assert.Contains("freed", vm.Heading);
        Assert.Equal("2 files could not be moved.", vm.FailedCount);
        // The destination line is the plain variant whatever happened. An error
        // count appended here lands immediately after the path, where it reads
        // as part of the folder name.
        Assert.Equal(@"69 files moved to: D:\backup", vm.Summary);
    }

    [Fact]
    public void A_move_that_achieved_nothing_says_so_and_claims_no_destination()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 0, movedBytes: 0, destination: @"D:\backup",
            errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // Routing a total failure through the success heading renders
        // "0 B freed, some files could not be proce": wrong twice, reporting a
        // total failure as a result and then clipping mid-word.
        Assert.True(vm.HeadingIsWarning);
        Assert.Equal(Strings.Completion_NothingMoved, vm.Heading);
        Assert.Equal("2 files could not be moved.", vm.FailedCount);
        // Nothing reached the destination, so nothing on screen may say files
        // are there or invite copying them back.
        Assert.Equal(string.Empty, vm.Summary);
        Assert.Equal(string.Empty, vm.SummaryDestination);
        Assert.Equal(string.Empty, vm.Restore);
        Assert.NotEqual(string.Empty, vm.Errors);
    }

    [Fact]
    public void A_delete_that_achieved_nothing_says_so_and_claims_nothing()
    {
        var vm = TestCompletion.Create();
        vm.ShowDeleteSummary(deletedCount: 0, deletedBytes: 0, errors: Failures(3));

        Assert.True(vm.HeadingIsWarning);
        Assert.Equal(Strings.Completion_NothingDeleted, vm.Heading);
        Assert.Equal("3 files could not be deleted.", vm.FailedCount);
        // Nothing was deleted, so nothing on screen may report a deletion.
        Assert.Equal(string.Empty, vm.Summary);
        Assert.Equal(string.Empty, vm.Restore);
    }

    [Fact]
    public void A_partly_failed_delete_keeps_the_success_heading()
    {
        var vm = TestCompletion.Create();
        vm.ShowDeleteSummary(deletedCount: 5, deletedBytes: 1024, errors: Failures(1));

        // Five files really were deleted and the space really did come back, so
        // the heading says so and the count line carries the rest.
        Assert.False(vm.HeadingIsWarning);
        Assert.Contains("freed", vm.Heading);
        Assert.Equal("1 file could not be deleted.", vm.FailedCount);
    }

    /// <summary>
    /// Every language the app ships, because the English line names the failure
    /// count on its own and the rule under test is about the number beside it.
    /// </summary>
    public static TheoryData<string> ShippedCultures()
    {
        var data = new TheoryData<string>();
        foreach (var name in SupportedLanguages.CultureNames)
            data.Add(name);

        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedCultures))]
    public void A_cancelled_move_names_what_failed_and_leaves_the_batch_to_the_summary(string cultureName)
    {
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo(cultureName));

        var vm = TestCompletion.Create();
        vm.ShowMoveCancelledSummary(movedCount: 3, totalCount: 71, movedBytes: 1024,
            destination: @"D:\backup", errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // The line names what failed and nothing else. 71 were queued and 5 were
        // reached, and neither number belongs beside a failure count: the 66 the
        // cancel never got to are not files that could not be moved, and the 3
        // that moved are not either. The summary line below names the whole
        // batch, which is its own job.
        //
        // WALKED ACROSS THE LANGUAGES RATHER THAN ASSERTED ON ONE STRING, because
        // each language builds this sentence its own way and a number put back
        // into any one of them would show up in that language and nowhere else.
        Assert.Contains("2", vm.FailedCount);
        Assert.DoesNotContain("71", vm.FailedCount);
        Assert.DoesNotContain("5", vm.FailedCount);
        Assert.Contains("71", vm.Summary);
        // A cancel is not a failure, so the heading stays as it was. This is the
        // control on the pair below: three files really did move and the size
        // heading is stating what they freed, so errors alone must not warn.
        Assert.False(vm.HeadingIsWarning);
    }

    // A cancel that reached no file and hit an error, on both paths. Until 3.0.0
    // the heading was "0 B freed" in the success colour over a line saying a file
    // could not be processed, and the delete test below asserted exactly that, in
    // its name as well as its body. The size heading has nothing to state here, so
    // it gives way to the same string the completed paths use.

    [Fact]
    public void A_stopped_move_says_why_it_stopped_and_not_to_delete_the_folder()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveStoppedSummary(movedCount: 62, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.Equal(
            string.Format(Strings.Error_DestinationChangedMidBatch, @"D:\backup"),
            vm.Restore);

        // The control that makes the absence attributable. Without it, a line that
        // had stopped being written at all would satisfy the assertion above just
        // as well as one that had been replaced.
        var finished = TestCompletion.Create();
        finished.ShowMoveSummary(movedCount: 62, movedBytes: 1024 * 1024, destination: @"D:\backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);
        Assert.Equal(Strings.Completion_MoveRestoreHint, finished.Restore);

        // Everything above that line is the finished card's, and it is meant to
        // be: those files really did move and that space really did come back.
        Assert.Equal(finished.Heading, vm.Heading);
        Assert.Equal(finished.Summary, vm.Summary);
        Assert.Equal(finished.SummaryDestination, vm.SummaryDestination);
        Assert.False(vm.HeadingIsWarning);
    }

    [Fact]
    public void A_stopped_move_that_moved_nothing_still_says_why_it_stopped()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveStoppedSummary(movedCount: 0, movedBytes: 0, destination: @"D:\backup",
            errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // The warning heading empties the summary and, on a Move that reached the
        // end, the line under it as well. The stop sentence is not that line's
        // advice about a folder's contents, it is why the run ended, so it is the
        // one thing that survives the arm which clears everything else.
        Assert.True(vm.HeadingIsWarning);
        Assert.Equal(string.Empty, vm.Summary);
        Assert.Equal(
            string.Format(Strings.Error_DestinationChangedMidBatch, @"D:\backup"),
            vm.Restore);
    }

    [Fact]
    public void A_cancelled_delete_that_deleted_nothing_and_hit_an_error_warns()
    {
        var vm = TestCompletion.Create();
        vm.ShowDeleteCancelledSummary(deletedCount: 0, totalCount: 40, deletedBytes: 0,
            errors: Failures(1));

        Assert.True(vm.HeadingIsWarning);
        Assert.Equal(Strings.Completion_NothingDeleted, vm.Heading);
        Assert.Equal("1 file could not be deleted.", vm.FailedCount);
        // And the cancelled sentence survives the warning, which is where these two
        // part company with ShowMoveSummary and ShowDeleteSummary. It is the only
        // line on the screen saying the run was stopped rather than that it failed.
        Assert.NotEqual(string.Empty, vm.Summary);
    }

    [Fact]
    public void A_cancelled_move_that_moved_nothing_and_hit_an_error_warns()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveCancelledSummary(movedCount: 0, totalCount: 40, movedBytes: 0,
            destination: @"D:\backup", errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        Assert.True(vm.HeadingIsWarning);
        Assert.Equal(Strings.Completion_NothingMoved, vm.Heading);
        Assert.NotEqual(string.Empty, vm.Summary);
    }

    // The three cancelled paths reach the same nothing-was-acted-on state the
    // completed paths guard against, by a different route: a cancel pressed
    // after the first few files failed. The advice is about files that arrived
    // somewhere, so it is as false here as it is there.

    [Fact]
    public void A_cancelled_move_names_the_folder_its_files_went_to()
    {
        // The two sentences on this card are a pair: the first names where the
        // files that moved have gone, the second names where they came from, and
        // between them they say how to put things back. Neither is any use to a
        // reader on its own, so the destination reaching the summary is held here
        // rather than left to the call site.
        var vm = TestCompletion.Create();
        vm.ShowMoveCancelledSummary(movedCount: 4, totalCount: 20, movedBytes: 1024,
            destination: @"D:\InstallerBackup", errors: Array.Empty<FileOperationError>(),
            space: MoveSpaceOutcome.FreedSpace);

        Assert.Contains(@"D:\InstallerBackup", vm.Summary);
        Assert.Contains("4", vm.Summary);
        Assert.Contains("20", vm.Summary);
        // And it stays in the sentence rather than going onto a line of its own,
        // which is what leaving SummaryDestination empty buys: the host splits the
        // summary at this value, and here the cancel follows the path.
        Assert.Equal(string.Empty, vm.SummaryDestination);
    }

    [Fact]
    public void A_cancelled_move_that_moved_nothing_drops_its_restore_hint()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveCancelledSummary(movedCount: 0, totalCount: 40, movedBytes: 0,
            destination: @"D:\backup", errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // Nothing reached the destination, so nothing may invite copying it back.
        Assert.Equal(string.Empty, vm.Restore);
    }

    [Fact]
    public void A_cancelled_move_that_moved_something_keeps_its_restore_hint()
    {
        var vm = TestCompletion.Create();
        vm.ShowMoveCancelledSummary(movedCount: 3, totalCount: 40, movedBytes: 1024,
            destination: @"D:\backup", errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);

        // The cancelled screen's own line, not the completed screen's. They share
        // no key, so a change to one cannot silently move the other.
        Assert.Equal(Strings.Completion_MoveCancelledRestoreHint, vm.Restore);
        Assert.NotEqual(Strings.Completion_MoveRestoreHint, vm.Restore);
    }

    [Fact]
    public void A_cancelled_delete_carries_no_restore_line_whatever_it_reached()
    {
        // Same rule as the completed delete: nothing under the summary at any
        // count.
        var vm = TestCompletion.Create();
        vm.ShowDeleteCancelledSummary(deletedCount: 3, totalCount: 40, deletedBytes: 1024,
            errors: Failures(2));

        Assert.Equal(string.Empty, vm.Restore);
    }

    [Fact]
    public void The_count_line_and_warning_heading_do_not_survive_into_the_next_operation()
    {
        // The view-model instance is reused across operations, so a count line
        // or a warning heading left over from a failed run would sit under the
        // next run's green heading.
        var vm = TestCompletion.Create();
        vm.ShowMoveSummary(movedCount: 0, movedBytes: 0, destination: @"D:\backup",
            errors: Failures(2), space: MoveSpaceOutcome.FreedSpace);
        Assert.True(vm.HeadingIsWarning);
        Assert.NotEqual(string.Empty, vm.FailedCount);

        vm.ShowDeleteSummary(deletedCount: 2, deletedBytes: 1024, errors: []);
        Assert.False(vm.HeadingIsWarning);
        Assert.Equal(string.Empty, vm.FailedCount);

        vm.ShowAllClear(scannedFileCount: 5, scanDurationMs: 10);
        Assert.False(vm.HeadingIsWarning);
        Assert.Equal(string.Empty, vm.FailedCount);
    }

    [Fact]
    public void The_wholesale_screen_says_what_was_held_back_and_shows_the_scan_receipt()
    {
        // The body's count and size are those of the files held back, not of the
        // folder. It says the app held back files it might otherwise have offered,
        // and a folder total would tell somebody that much space was going spare,
        // which the scan has not established.
        var vm = TestCompletion.Create();

        vm.ShowNothingOffered(
            HeldBack(3, Wholesale(3)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.True(vm.IsComplete);
        Assert.False(vm.HeadingIsWarning);
        Assert.Equal(Strings.Completion_NothingOffered, vm.Heading);
        Assert.Equal(
            string.Format(
                Strings.Completion_NothingOfferedBody_Plural,
                3, DisplayHelpers.PluraliseFile(3), DisplayHelpers.FormatSize(3072)),
            vm.Summary);

        // THE RECEIPT IS NOT DECORATION. A heading and a body with no evidence that a
        // scan ran reads as a failure rather than as a result, which is the opposite
        // of what this screen has to say. It is the same line the all-clear carries.
        //
        // ITS NOUN IS FILES, WHICH IS WHAT THE NUMBER COUNTS. The caller hands over
        // the cached files the scan accounted for, and the held-back files this
        // screen has just named are among them: they are in the folder and the scan
        // opened them.
        Assert.Equal(
            string.Format(
                Strings.Completion_NothingToCleanUpReceipt,
                5, DisplayHelpers.PluraliseFile(5),
                DisplayHelpers.FormatElapsedLong(TimeSpan.FromMilliseconds(10))),
            vm.Restore);
        Assert.Equal("Scanned 5 files in less than a second", vm.Restore);
    }

    [Fact]
    public void The_wholesale_screen_at_one_file_does_not_say_all_1_files()
    {
        // A COUNT OF ONE IS REACHABLE, being a folder holding a single unclaimed file,
        // and the plural form renders "held back all 1 files" for it. The one-form
        // drops the numeral and names the size alone. The literal assertion below is
        // the control: formatting the right key with the wrong argument, or selecting
        // the plural at a count of one, both still satisfy a key-level assertion.
        var vm = TestCompletion.Create();

        vm.ShowNothingOffered(
            HeldBack(1, Wholesale(1)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(
            string.Format(
                Strings.Completion_NothingOfferedBody_Singular,
                1, DisplayHelpers.PluraliseFile(1), DisplayHelpers.FormatSize(1024)),
            vm.Summary);
        Assert.Contains("the one file", vm.Summary);
        Assert.Contains(DisplayHelpers.FormatSize(1024), vm.Summary);
        Assert.DoesNotContain("1 files", vm.Summary);

        // The one-form spends {2} alone, so the noun argument must not reach the
        // screen: "the one file (file)" is what a wrong index looks like here, and it
        // is the shape that hid the size while the value still carried {1} for it.
        Assert.DoesNotContain($"({DisplayHelpers.PluraliseFile(1)})", vm.Summary);
    }

    [Fact]
    public void The_wholesale_screen_and_the_all_clear_do_not_share_a_sentence()
    {
        // THE WHOLE POINT OF THE SECOND SCREEN, pinned so that a later tidy cannot
        // collapse them back into one. One says the folder holds nothing to remove;
        // the other says the app could not establish enough to offer anything, on a
        // machine whose folder may be full. Showing the first where the second is true
        // is a claim about somebody's disk the scan never made.
        var allClear = TestCompletion.Create();
        var nothingOffered = TestCompletion.Create();

        allClear.ShowAllClear(scannedFileCount: 5, scanDurationMs: 10);
        nothingOffered.ShowNothingOffered(
            HeldBack(3, Wholesale(3)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.NotEqual(allClear.Heading, nothingOffered.Heading);
        Assert.NotEqual(allClear.Summary, nothingOffered.Summary);
        // The receipt IS shared, deliberately, and is the one thing that should match.
        Assert.Equal(allClear.Restore, nothingOffered.Restore);
    }

    [Fact]
    public void The_two_withholding_bodies_do_not_share_a_sentence()
    {
        // THE SAME POINT ONE LEVEL DOWN, and the level where it is easiest to lose.
        // One heading carries two bodies: one says the scan could not tell which
        // cached files belong to which installed programs, the other only that it
        // could not establish the files it counts are unneeded. Each is false of the
        // other's machine, so a tidy that collapsed them would put a cause on a set
        // that did not earn it, with the heading and the receipt still matching and
        // nothing else to notice.
        var wholesale = TestCompletion.Create();
        var perFile = TestCompletion.Create();

        wholesale.ShowNothingOffered(
            HeldBack(3, Wholesale(3)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);
        perFile.ShowNothingOffered(
            HeldBack(3, PerFile(3)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.NotEqual(wholesale.Summary, perFile.Summary);
        // Everything else about the screen IS shared, which is what makes the body the
        // only thing carrying the difference and the only thing worth pinning.
        Assert.Equal(wholesale.Heading, perFile.Heading);
        Assert.Equal(wholesale.Restore, perFile.Restore);
    }

    [Fact]
    public void The_per_file_body_is_the_one_the_per_file_reading_renders()
    {
        // The key-level assertion the test above cannot make: NotEqual would be
        // satisfied by any two different strings, including the right key formatted
        // with the wrong arguments. This names the value.
        var vm = TestCompletion.Create();

        vm.ShowNothingOffered(
            HeldBack(3, PerFile(3)), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(
            string.Format(
                Strings.Completion_NothingOfferedPerFileBody_Plural,
                3, DisplayHelpers.PluraliseFile(3), DisplayHelpers.FormatSize(3072)),
            vm.Summary);
    }

    // The line about files held back for being under a day old. The card places the
    // sentence UnderADayOldReport.Line gives for the scan it speaks for, in the zone
    // TestCompletion gives it, UTC unless a test says otherwise, and
    // UnderADayOldReportTests pins that sentence, so these compare with what it returns.

    private static readonly DateTime DayOldAt = new(2030, 6, 16, 9, 40, 0, DateTimeKind.Utc);

    private static string DayOldLineOf(ScanResult scan) => UnderADayOldReport.Line(scan, TimeZoneInfo.Utc);

    [Fact]
    public void Nothing_offered_keeps_its_body_and_adds_the_day_old_line_where_other_files_are_held_too()
    {
        var vm = TestCompletion.Create();
        var scan = HeldBack(5, new WithholdingSplit(DeclaredProductUnestablishedCount: 2, UnderADayOldCount: 3),
            DayOldAt);

        vm.ShowNothingOffered(scan, scannedFileCount: 9, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(
            string.Format(
                Strings.Completion_NothingOfferedPerFileBody_Plural,
                5, DisplayHelpers.PluraliseFile(5), DisplayHelpers.FormatSize(5120)),
            vm.Summary);
        Assert.NotEqual(string.Empty, DayOldLineOf(scan));
        Assert.Equal(DayOldLineOf(scan), vm.UnderADayOld);
    }

    [Fact]
    public void Nothing_offered_shows_the_day_old_line_as_its_body_where_those_are_every_file_it_counts()
    {
        // Shown once, in the body's place, with the line naming a drive or share still
        // under it, and nothing left in the zone below.
        var vm = TestCompletion.Create();
        var scan = HeldBack(3, new WithholdingSplit(UnderADayOldCount: 3), DayOldAt);

        vm.ShowNothingOffered(scan, scannedFileCount: 9, scanDurationMs: 10, sourcesGivenUp: "Drive line.");

        Assert.NotEqual(string.Empty, DayOldLineOf(scan));
        Assert.Equal(DayOldLineOf(scan) + Environment.NewLine + "Drive line.", vm.Summary);
        Assert.Equal(string.Empty, vm.UnderADayOld);
    }

    [Fact]
    public void Nothing_offered_counts_files_under_a_day_old_in_its_body_where_the_scan_gives_no_time_for_them()
    {
        // With no time to give there is no day-old line, so the body counts the files.
        var vm = TestCompletion.Create();
        var scan = HeldBack(2, new WithholdingSplit(UnderADayOldCount: 2));

        vm.ShowNothingOffered(scan, scannedFileCount: 9, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(
            string.Format(
                Strings.Completion_NothingOfferedPerFileBody_Plural,
                2, DisplayHelpers.PluraliseFile(2), DisplayHelpers.FormatSize(2048)),
            vm.Summary);
        Assert.Equal(string.Empty, vm.UnderADayOld);
    }

    public static TheoryData<string> CardsAfterAMoveOrDelete() => new()
    {
        "finished Move", "stopped Move", "cancelled Move",
        "finished Delete", "cancelled Delete", "everything held back",
    };

    private static void ShowCard(CompletionViewModel vm, string card)
    {
        var reverify = new ReverifyResult([], ["a.msi"], new HeldBackReasons(Reclaimed: 1));
        switch (card)
        {
            case "finished Move":
                vm.ShowMoveSummary(1, 1024, @"D:\Backup", [], MoveSpaceOutcome.FreedSpace);
                break;
            case "stopped Move":
                vm.ShowMoveStoppedSummary(1, 1024, @"D:\Backup", [], MoveSpaceOutcome.FreedSpace);
                break;
            case "cancelled Move":
                vm.ShowMoveCancelledSummary(1, 2, 1024, @"D:\Backup", [], MoveSpaceOutcome.FreedSpace);
                break;
            case "finished Delete":
                vm.ShowDeleteSummary(1, 1024, []);
                break;
            case "cancelled Delete":
                vm.ShowDeleteCancelledSummary(1, 2, 1024, []);
                break;
            case "everything held back":
                vm.ShowReverifyAllSkipped(reverify, deleting: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(card), card, null);
        }
    }

    [Theory]
    [MemberData(nameof(CardsAfterAMoveOrDelete))]
    public void Every_card_after_a_Move_or_Delete_carries_the_day_old_line_of_the_scan_the_window_shows(string card)
    {
        var scan = HeldBack(2, new WithholdingSplit(UnderADayOldCount: 2), DayOldAt);
        var vm = TestCompletion.Create(lastScan: () => scan);

        ShowCard(vm, card);

        Assert.True(vm.IsComplete);
        Assert.NotEqual(string.Empty, DayOldLineOf(scan));
        Assert.Equal(DayOldLineOf(scan), vm.UnderADayOld);
    }

    [Theory]
    [MemberData(nameof(CardsAfterAMoveOrDelete))]
    public void A_card_reads_the_scan_as_it_is_revealed_and_clears_a_line_the_scan_no_longer_has(string card)
    {
        // The view model is reused across operations, and the scan behind the card changes
        // between them, so a card whose scan held no file back for its age takes the
        // previous card's line away.
        ScanResult? scan = HeldBack(2, new WithholdingSplit(UnderADayOldCount: 2), DayOldAt);
        var vm = TestCompletion.Create(lastScan: () => scan);
        vm.ShowDeleteSummary(1, 1024, []);
        Assert.NotEqual(string.Empty, vm.UnderADayOld);

        scan = HeldBack(2, PerFile(2));
        ShowCard(vm, card);

        Assert.Equal(string.Empty, vm.UnderADayOld);
    }

    [Fact]
    public void The_day_old_line_gives_its_time_in_the_zone_as_it_is_when_the_card_is_revealed()
    {
        // The zone moves between two cards on one view model, as it does when somebody
        // changes it with the window open, and each card gives the time in the zone of
        // its own moment. Both screens that carry the line are walked.
        var sevenAhead = TimeZoneInfo.CreateCustomTimeZone("Test+7", TimeSpan.FromHours(7), "Test+7", "Test+7");
        var zone = TimeZoneInfo.Utc;
        var scan = HeldBack(2, new WithholdingSplit(UnderADayOldCount: 2), DayOldAt);
        var vm = TestCompletion.Create(lastScan: () => scan, zone: () => zone);

        vm.ShowDeleteSummary(1, 1024, []);
        var inUtc = vm.UnderADayOld;
        zone = sevenAhead;
        vm.ShowDeleteSummary(1, 1024, []);
        var afterTheChange = vm.UnderADayOld;
        vm.ShowNothingOffered(scan, scannedFileCount: 9, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(UnderADayOldReport.Line(scan, TimeZoneInfo.Utc), inUtc);
        Assert.Equal(UnderADayOldReport.Line(scan, sevenAhead), afterTheChange);
        Assert.NotEqual(inUtc, afterTheChange);
        Assert.Equal(UnderADayOldReport.Line(scan, sevenAhead), vm.Summary);
    }

    [Fact]
    public void The_all_clear_clears_the_day_old_line()
    {
        var scan = HeldBack(2, new WithholdingSplit(UnderADayOldCount: 2), DayOldAt);
        var vm = TestCompletion.Create(lastScan: () => scan);
        vm.ShowDeleteSummary(1, 1024, []);
        Assert.NotEqual(string.Empty, vm.UnderADayOld);

        vm.ShowAllClear(scannedFileCount: 5, scanDurationMs: 10);

        Assert.Equal(string.Empty, vm.UnderADayOld);
    }

    /// <summary>
    /// A scan that offered nothing and held back <paramref name="files"/> files of 1 KB
    /// each, counted by <paramref name="split"/>, the files under a day old all a day old
    /// at <paramref name="allADayOldAtUtc"/>.
    /// </summary>
    private static ScanResult HeldBack(int files, WithholdingSplit split, DateTime? allADayOldAtUtc = null) =>
        new(Array.Empty<OrphanedFile>(), Array.Empty<RegisteredPackage>(), 0,
            WithheldFiles: Enumerable.Range(0, files)
                .Select(i => new OrphanedFile($@"C:\Windows\Installer\{i}.msi", 1024, false, false, false, "unclaimed"))
                .ToArray(),
            WithheldBy: split,
            WithheldUnderADayOldBytes: 1024L * split.UnderADayOldCount,
            WithheldUnderADayOldAllADayOldAtUtc: allADayOldAtUtc);

    /// <summary>Every held file kept by the rule about the machine's records, in one go.</summary>
    private static WithholdingSplit Wholesale(int files) => new(WholesaleCount: files);

    /// <summary>Every held file judged on its own and kept.</summary>
    private static WithholdingSplit PerFile(int files) => new(DeclaredProductUnestablishedCount: files);

    // The kept-back block. One sentence since 3.0.0, naming no cause, carrying the
    // batch total. What is pinned here is that the mix of causes cannot be read off
    // it and that the number is every file held back.

    private static string Line(int count) =>
        string.Format(
            count == 1 ? Strings.Completion_HeldBack_Singular : Strings.Completion_HeldBack_Plural,
            count);

    [Fact]
    public void A_batch_kept_back_for_one_cause_carries_the_sentence_at_its_count()
    {
        var vm = TestCompletion.Create();

        vm.ShowDeleteSummary(deletedCount: 2, deletedBytes: 4096, errors: [],
            reverify: new ReverifyResult([], ["a.msp"], new HeldBackReasons(Reclaimed: 1)));

        Assert.Equal(Line(1), vm.Skipped);
    }

    [Fact]
    public void A_batch_kept_back_three_ways_carries_ONE_line_at_the_batch_total()
    {
        // Where three sentences used to print. The four counts still exist on the
        // tally and travel in the result log; what the user gets is one line whose
        // number is 2 + 1 + 1 rather than any one cause's.
        var vm = TestCompletion.Create();

        vm.ShowDeleteSummary(deletedCount: 1, deletedBytes: 4096, errors: [],
            reverify: new ReverifyResult([], ["a.msp", "b.msp", "c.msp", "d.msp"],
                new HeldBackReasons(Reclaimed: 2, RecordsChanged: 1, RecordsUnreadable: 1)));

        Assert.Equal(new[] { Line(4) }, vm.Skipped.Split(Environment.NewLine));
    }

    [Fact]
    public void The_kept_back_line_does_not_say_which_cause_a_batch_met()
    {
        // Two batches of the same size reached by different causes, one of them the
        // cause that is about the machine rather than about any file. The screen
        // cannot tell them apart, which is the whole of what "names no cause" means
        // and is worth a test rather than a comment.
        var perFile = TestCompletion.Create();
        perFile.ShowDeleteSummary(deletedCount: 1, deletedBytes: 4096, errors: [],
            reverify: new ReverifyResult([], ["a.msp", "b.msp", "c.msp"],
                new HeldBackReasons(Reclaimed: 2, RecordsChanged: 1)));

        var machineWide = TestCompletion.Create();
        machineWide.ShowDeleteSummary(deletedCount: 1, deletedBytes: 4096, errors: [],
            reverify: new ReverifyResult([], ["a.msp", "b.msp", "c.msp"],
                new HeldBackReasons(OwnershipUnestablished: 3)));

        Assert.Equal(Line(3), perFile.Skipped);
        Assert.Equal(perFile.Skipped, machineWide.Skipped);
    }

    [Fact]
    public void A_batch_that_kept_nothing_back_carries_no_line_at_all()
    {
        // The commonest run. An empty string is what collapses the block, so a
        // count of zero must not reach it as a sentence.
        var vm = TestCompletion.Create();

        vm.ShowDeleteSummary(deletedCount: 2, deletedBytes: 4096, errors: [],
            reverify: new ReverifyResult(["a.msi", "b.msi"], []));

        Assert.Equal(string.Empty, vm.Skipped);
    }

    [Fact]
    public void The_all_skipped_screen_carries_the_kept_back_line_in_its_summary()
    {
        // This screen routes the line through Summary instead of Skipped, and
        // Summary is the one completion field with no Text binding: its inlines are
        // composed in the window's code-behind. That path splits on newlines, so
        // one sentence yields one Run and no break, which is why the collapse needs
        // nothing done to it.
        var vm = TestCompletion.Create();

        vm.ShowReverifyAllSkipped(new ReverifyResult([], ["a.msp", "b.msp"],
            new HeldBackReasons(Reclaimed: 1, RecordsUnreadable: 1)), deleting: false);

        Assert.Equal(Line(2), vm.Summary);
        Assert.DoesNotContain(Environment.NewLine, vm.Summary, System.StringComparison.Ordinal);
        // The heading beside the block, because a summary naming causes under a
        // heading saying there were none is the state this screen was rewritten out
        // of. Completion_AllClean is right for a machine with nothing to do and
        // wrong here, where everything was kept back. Which of the two per-button
        // headings appears is the theory below's question, not this one's.
        Assert.Equal(Strings.Completion_NothingMoved, vm.Heading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_all_skipped_screen_names_the_button_that_was_pressed(bool deleting)
    {
        // The screen only ever follows Move or Delete, so a heading naming neither
        // is a word the user never pressed. Both strings are in the file and already
        // picked between by ShowMoveSummary and ShowDeleteSummary, so DO NOT COLLAPSE
        // THE PAIR INTO ONE WORD.
        var vm = TestCompletion.Create();

        vm.ShowReverifyAllSkipped(
            new ReverifyResult([], ["a.msp"], new HeldBackReasons(Reclaimed: 1)), deleting);

        Assert.Equal(
            deleting ? Strings.Completion_NothingDeleted : Strings.Completion_NothingMoved,
            vm.Heading);
        // And not in the warning colour on either branch, which is what separates
        // this screen from the two it borrows the strings from. There they mean an
        // operation that got nowhere; here they mean the check ahead of the batch
        // doing its job, and painting that as a failure would contradict the one
        // rule the app is built on.
        Assert.False(vm.HeadingIsWarning);
    }

    // The card's two sets of buttons. After a Move or Delete that moved or
    // deleted files it carries the large Donate over Close without donating; every
    // other card carries Done and the small Donate under it.

    [Fact]
    public void A_delete_that_freed_bytes_asks_for_a_donation()
    {
        var vm = TestCompletion.Create();

        vm.ShowDeleteSummary(deletedCount: 3, deletedBytes: 4096, errors: []);

        Assert.True(vm.AsksForDonation);
    }

    [Fact]
    public void A_delete_that_freed_nothing_keeps_done()
    {
        var vm = TestCompletion.Create();

        vm.ShowDeleteSummary(deletedCount: 0, deletedBytes: 0, errors: []);

        Assert.False(vm.AsksForDonation);
    }

    [Fact]
    public void A_same_drive_move_asks_for_a_donation_though_it_freed_no_space()
    {
        // The gate is the bytes the run shifted, not whether the disk got any
        // emptier. This is the card that separates the two: the files went to the
        // drive they came from, so nothing is reclaimed until the user deletes the
        // backup folder, and the heading says "moved" rather than "freed".
        var vm = TestCompletion.Create();

        vm.ShowMoveSummary(movedCount: 3, movedBytes: 4096, destination: @"C:\Backup",
            errors: [], space: MoveSpaceOutcome.SameDrive);

        Assert.True(vm.AsksForDonation);
        Assert.Equal(
            string.Format(Strings.Completion_Moved, DisplayHelpers.FormatSize(4096)),
            vm.Heading);
    }

    [Fact]
    public void A_move_the_app_stopped_keeps_done_though_files_moved()
    {
        // Files reached the backup folder before the stop, so the bytes alone would
        // ask. The stopped flag is what keeps this card on Done.
        var vm = TestCompletion.Create();

        vm.ShowMoveStoppedSummary(movedCount: 3, movedBytes: 4096, destination: @"D:\Backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.False(vm.AsksForDonation);
    }

    [Fact]
    public void A_move_that_ran_to_the_end_asks_for_a_donation()
    {
        // The control for the stopped Move above: the same arguments through the
        // method that shares its body, so the flag is the only difference.
        var vm = TestCompletion.Create();

        vm.ShowMoveSummary(movedCount: 3, movedBytes: 4096, destination: @"D:\Backup",
            errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.True(vm.AsksForDonation);
    }

    [Fact]
    public void A_cancelled_move_that_moved_files_asks_for_a_donation()
    {
        var vm = TestCompletion.Create();

        vm.ShowMoveCancelledSummary(movedCount: 3, totalCount: 40, movedBytes: 1024,
            destination: @"D:\Backup", errors: [], space: MoveSpaceOutcome.FreedSpace);

        Assert.True(vm.AsksForDonation);
    }

    [Fact]
    public void A_cancelled_delete_asks_for_a_donation_only_where_it_deleted_files()
    {
        var deleted = TestCompletion.Create();
        var nothing = TestCompletion.Create();

        deleted.ShowDeleteCancelledSummary(deletedCount: 3, totalCount: 40, deletedBytes: 1024, errors: []);
        nothing.ShowDeleteCancelledSummary(deletedCount: 0, totalCount: 40, deletedBytes: 0, errors: []);

        Assert.True(deleted.AsksForDonation);
        Assert.False(nothing.AsksForDonation);
    }

    [Fact]
    public void Donate_opens_the_donate_page_and_closes_the_card()
    {
        var windowService = Substitute.For<IWindowService>();
        var vm = TestCompletion.Create(windowService);
        vm.ShowDeleteSummary(deletedCount: 3, deletedBytes: 4096, errors: []);
        Assert.True(vm.IsComplete);

        vm.DonateCommand.Execute(null);

        windowService.Received(1).OpenUrl(SupportLink.Url);
        Assert.False(vm.IsComplete);
    }

    [Fact]
    public void A_run_the_reverify_held_back_entirely_keeps_done()
    {
        // Nothing was moved, so the card keeps Done, even though the screen is
        // green and the check ahead of the batch did its job.
        var vm = TestCompletion.Create();

        vm.ShowReverifyAllSkipped(
            new ReverifyResult([], ["a.msp"], new HeldBackReasons(Reclaimed: 1)), deleting: false);

        Assert.False(vm.AsksForDonation);
    }

    [Fact]
    public void An_all_clear_puts_done_back()
    {
        // The view-model instance is reused across operations, so a flag left on
        // by the Delete would follow the user onto the all-clear that the
        // post-operation rescan produces, asking for a donation for a scan.
        var vm = TestCompletion.Create();
        vm.ShowDeleteSummary(deletedCount: 3, deletedBytes: 4096, errors: []);
        Assert.True(vm.AsksForDonation);

        vm.ShowAllClear(scannedFileCount: 5, scanDurationMs: 10);

        Assert.False(vm.AsksForDonation);
    }

    // The line naming the drives and shares carried on without. On the nothing-offered
    // card it follows the body, or is the body; on every card after a Move or Delete it
    // follows the held-back count on a line of its own.

    private const string DriveDLine =
        "InstallerClean carried on without drive D: and left alone any file still to be checked against it. "
        + "Once it's responding normally, Re-scan.";

    private static IReadOnlyList<SourceRootGivenUp> DriveD(int filesKept) =>
        [new SourceRootGivenUp("d:", SourceRootGiveUpRoute.StoppedWaiting, filesKept)];

    [Fact]
    public void The_nothing_offered_screen_with_nothing_held_back_has_the_line_as_its_whole_body()
    {
        // The scan held back nothing the count counts, so there is no sentence to put
        // the line under: a file kept for an installed program at a drive given up is
        // outside that count, and the line is everything the card has to say about it.
        var vm = TestCompletion.Create();

        vm.ShowNothingOffered(
            HeldBack(0, default), scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: DriveDLine);

        Assert.Equal(Strings.Completion_NothingOffered, vm.Heading);
        Assert.Equal(DriveDLine, vm.Summary);
        Assert.Equal("Scanned 5 files in less than a second", vm.Restore);
        Assert.True(vm.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_nothing_offered_screen_puts_the_line_under_the_held_back_sentence(bool wholesale)
    {
        var vm = TestCompletion.Create();
        var withoutLine = TestCompletion.Create();

        var scan = HeldBack(3, wholesale ? Wholesale(3) : PerFile(3));
        vm.ShowNothingOffered(scan, scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: DriveDLine);
        withoutLine.ShowNothingOffered(scan, scannedFileCount: 5, scanDurationMs: 10, sourcesGivenUp: string.Empty);

        Assert.Equal(new[] { withoutLine.Summary, DriveDLine }, vm.Summary.Split(Environment.NewLine));
        Assert.DoesNotContain(Environment.NewLine, withoutLine.Summary, System.StringComparison.Ordinal);
    }

    public enum Card { Move, MoveStopped, Delete, MoveCancelled, DeleteCancelled, AllSkipped }

    // Each card a Move or Delete can end on, shown with the check's result, and the text
    // that card draws the check's lines in: Summary on the card where the check kept every
    // file, Skipped on the rest.
    private static string CheckLinesOn(Card card, ReverifyResult reverify)
    {
        var vm = TestCompletion.Create();
        switch (card)
        {
            case Card.Move:
                vm.ShowMoveSummary(2, 4096, @"E:\Backup", [], MoveSpaceOutcome.FreedSpace, reverify);
                return vm.Skipped;
            case Card.MoveStopped:
                vm.ShowMoveStoppedSummary(2, 4096, @"E:\Backup", [], MoveSpaceOutcome.FreedSpace, reverify);
                return vm.Skipped;
            case Card.Delete:
                vm.ShowDeleteSummary(2, 4096, [], reverify);
                return vm.Skipped;
            case Card.MoveCancelled:
                vm.ShowMoveCancelledSummary(1, 2, 2048, @"E:\Backup", [], MoveSpaceOutcome.FreedSpace, reverify);
                return vm.Skipped;
            case Card.DeleteCancelled:
                vm.ShowDeleteCancelledSummary(1, 2, 2048, [], reverify);
                return vm.Skipped;
            default:
                vm.ShowReverifyAllSkipped(reverify, deleting: false);
                return vm.Summary;
        }
    }

    [Theory]
    [InlineData(Card.Move)]
    [InlineData(Card.MoveStopped)]
    [InlineData(Card.Delete)]
    [InlineData(Card.MoveCancelled)]
    [InlineData(Card.DeleteCancelled)]
    [InlineData(Card.AllSkipped)]
    public void Every_card_after_a_Move_or_Delete_names_the_drive_the_check_carried_on_without(Card card)
    {
        var reverify = new ReverifyResult([], ["a.msi", "b.msi", "c.msi"],
            new HeldBackReasons(FileNotConfirmed: 2, Reclaimed: 1), SourceRootsGivenUp: DriveD(2));

        Assert.Equal(new[] { Line(3), DriveDLine }, CheckLinesOn(card, reverify).Split(Environment.NewLine));
    }

    [Theory]
    [InlineData(Card.Move)]
    [InlineData(Card.MoveStopped)]
    [InlineData(Card.Delete)]
    [InlineData(Card.MoveCancelled)]
    [InlineData(Card.DeleteCancelled)]
    [InlineData(Card.AllSkipped)]
    public void A_drive_the_check_gave_up_with_no_file_kept_there_is_not_named(Card card)
    {
        // Given up after its last read was used, so nothing the check decided turned on
        // it, and the card says only what the check held back.
        var reverify = new ReverifyResult([], ["a.msi"],
            new HeldBackReasons(Reclaimed: 1), SourceRootsGivenUp: DriveD(0));

        Assert.Equal(Line(1), CheckLinesOn(card, reverify));
    }

    // The report panel's "See exactly what's sent" link. The card carrying the box is
    // revealed before its report is written, so the window waits for the write; and it
    // shows what the read the send also uses returned, so it cannot differ from what goes.

    private const string Report = "{\"schemaVersion\":5}";

    private sealed class ReportCard
    {
        public IResultLogService ResultLog { get; } = Substitute.For<IResultLogService>();
        public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();
        public IFirstRunMark FirstRunMark { get; } = Substitute.For<IFirstRunMark>();
        public IWindowService Windows { get; } = Substitute.For<IWindowService>();
        public TaskCompletionSource<bool> Write { get; } = new();
        public CompletionViewModel Vm { get; }

        /// <summary>
        /// A first card with its box on, its write started and held at
        /// <see cref="Write"/>, and a read of <c>last-run.json</c> that returns
        /// <see cref="Report"/>.
        /// </summary>
        public ReportCard()
        {
            ResultLog.WriteAsync(Arg.Any<ResultLogEntry>(), Arg.Any<CancellationToken>()).Returns(Write.Task);
            ResultLog.ReadLastLogAsync(Arg.Any<CancellationToken>()).Returns(Report);
            ResultLog.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ResultLogSendOutcome.Sent);
            Settings.Update(Arg.Any<Action<AppSettings>>()).Returns(true);
            Vm = new CompletionViewModel(ResultLog, Settings, Substitute.For<IEarlierRunCheck>(), FirstRunMark,
                Substitute.For<IWindowsRegion>(), Windows, () => null);
            Assert.True(Vm.TakeReport());
            Vm.ShowAllClear(scannedFileCount: 5, scanDurationMs: 10);
            Writing = Vm.WriteReportAsync(null!);
        }

        public Task Writing { get; }
    }

    [Fact]
    public async Task The_report_window_opens_once_the_write_has_landed_and_not_before()
    {
        var card = new ReportCard();

        var showing = card.Vm.ShowReportCommand.ExecuteAsync(null);

        await card.ResultLog.DidNotReceive().ReadLastLogAsync(Arg.Any<CancellationToken>());
        card.Windows.DidNotReceiveWithAnyArgs().ShowReport(default);

        card.Write.SetResult(true);
        await card.Writing;
        await showing;

        card.Windows.Received(1).ShowReport(Report);
    }

    [Fact]
    public async Task The_report_window_shows_the_text_the_send_posts()
    {
        var card = new ReportCard();
        card.Write.SetResult(true);
        await card.Writing;
        card.Vm.SendsReport = true;
        string? shown = null;
        card.Windows.ShowReport(Arg.Do<string?>(text => shown = text));

        await card.Vm.ShowReportCommand.ExecuteAsync(null);
        card.Vm.DismissCommand.Execute(null);
        await card.Vm.ReportWork;

        var sent = (string)card.ResultLog.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IResultLogService.SendAsync))
            .GetArguments()[0]!;
        Assert.Equal(Report, sent);
        Assert.Equal(sent, shown);
    }

    [Fact]
    public async Task The_report_window_is_given_null_where_the_file_cannot_be_read()
    {
        var card = new ReportCard();
        card.ResultLog.ReadLastLogAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        card.Write.SetResult(true);
        await card.Writing;

        await card.Vm.ShowReportCommand.ExecuteAsync(null);

        card.Windows.Received(1).ShowReport(null);
    }

    [Fact]
    public async Task The_report_window_does_not_open_where_the_card_closed_during_the_read()
    {
        var card = new ReportCard();
        var read = new TaskCompletionSource<string?>();
        card.ResultLog.ReadLastLogAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
        card.Write.SetResult(true);
        await card.Writing;

        var showing = card.Vm.ShowReportCommand.ExecuteAsync(null);
        await card.ResultLog.Received(1).ReadLastLogAsync(Arg.Any<CancellationToken>());
        card.Vm.DismissCommand.Execute(null);
        read.SetResult(Report);
        await showing;

        card.Windows.DidNotReceiveWithAnyArgs().ShowReport(default);
    }

    [Fact]
    public async Task The_report_window_does_not_open_where_the_write_failed()
    {
        var card = new ReportCard();
        card.Write.SetResult(false);
        await card.Writing;
        Assert.False(card.Vm.OffersReport);

        await card.Vm.ShowReportCommand.ExecuteAsync(null);

        await card.ResultLog.DidNotReceive().ReadLastLogAsync(Arg.Any<CancellationToken>());
        card.Windows.DidNotReceiveWithAnyArgs().ShowReport(default);
    }

    [Fact]
    public async Task Opening_the_report_window_sends_nothing_saves_nothing_and_leaves_the_box_as_it_was()
    {
        var card = new ReportCard();
        card.Write.SetResult(true);
        await card.Writing;
        await card.Vm.ReportWork;
        card.Vm.SendsReport = true;
        await card.Vm.ReportWork;
        card.ResultLog.ClearReceivedCalls();
        card.Settings.ClearReceivedCalls();
        card.FirstRunMark.ClearReceivedCalls();

        await card.Vm.ShowReportCommand.ExecuteAsync(null);
        await card.Vm.ReportWork;

        card.Windows.Received(1).ShowReport(Report);
        await card.ResultLog.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await card.ResultLog.DidNotReceive().WriteAsync(Arg.Any<ResultLogEntry>(), Arg.Any<CancellationToken>());
        card.Settings.DidNotReceiveWithAnyArgs().Update(default!);
        card.FirstRunMark.DidNotReceiveWithAnyArgs().Set();
        Assert.True(card.Vm.OffersReport);
        Assert.True(card.Vm.SendsReport);
        Assert.True(card.Vm.IsComplete);
    }
}
