using System.IO.Abstractions.TestingHelpers;
using InstallerClean.Interop;
using InstallerClean.Models;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The scan's screen of what a cached file says it is: the product an installation
/// package declares it belongs to, or the patch a patch file declares it is, put to
/// Windows.
///
/// IT IS THE ONLY THING IN THE TREE THAT STARTS AT THE FILE. Every other way the
/// scan decides a cached file is spare starts at a registration and looks for the
/// file it names, so where the records hold no usable path there is nothing for any
/// of them to work from. That is the class this reaches, and the tests below are
/// about the direction it fails in. For a package two answers let a file through: a
/// POSITIVE answer that Windows does not hold the declared product, and every
/// installation of that product recording a package that is present and is another
/// file. For a patch the same two: a POSITIVE answer that Windows holds no
/// registration of the declared patch, and every registration of it recording a
/// cached copy that is present and is another file. Every inability keeps the file,
/// except the patch half's, which the tests under their own heading pin as leaving
/// the file to the rest of the scan.
///
/// THE FAKES THROW ON ANYTHING NO TEST SCRIPTED, which is the point of them rather
/// than strictness. A fake answering an unscripted question with a plausible default
/// is how a test comes to pass without ever reaching its own subject, and the
/// questions here have a permissive answer each: "Windows does not hold that
/// product", "no product holds that patch" and "there was nothing to read". Any of
/// them as a default would let a test assert the file was offered while proving
/// nothing about why.
/// </summary>
public class DeclaredProductCheckTests
{
    private const string ProductA = "{11111111-1111-1111-1111-111111111111}";
    private const string ProductB = "{22222222-2222-2222-2222-222222222222}";

    private static OrphanedFile Package(string path) =>
        new(path, 100, IsPatch: false, IsRemovablePatch: false, IsObsoleted: false, Reason: "orphaned");

    private static OrphanedFile Patch(string path) =>
        new(path, 100, IsPatch: true, IsRemovablePatch: false, IsObsoleted: false, Reason: "orphaned");

    // ---- The keeping arm: Windows still holds the declared product ----

    [Fact]
    public void A_package_whose_declared_product_Windows_holds_is_kept_back()
    {
        // THE WHOLE POINT OF THE CHECK. Nothing registered names this file, so
        // every other mechanism in the scan has already let it through; the file
        // itself says which product it belongs to, and Windows still has that
        // product. Built without the file readers, the check cannot look at what
        // the product records, which is the section further down.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcomes[0]);
        Assert.True(outcomes[0].Withholds());
    }

    // ---- The one permitting arm, which is the must-miss control for the above ----

    [Fact]
    public void A_package_Windows_says_it_does_not_hold_is_left_where_it_was()
    {
        // The must-miss half. Without it the test above passes just as well
        // against a check that keeps every file back, which is a check that has
        // emptied the offer and looks identical from the outside.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.NotInstalled(ProductA, MsiError.UnknownProduct);

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.DeclaredProductNotInstalled, outcomes[0]);
        Assert.False(outcomes[0].Withholds());
    }

    [Fact]
    public void NoMoreItems_is_the_other_return_that_means_the_product_is_not_there()
    {
        // Two returns are allowed to mean absence and the code has to accept both,
        // so pinning only the obvious one would leave the second free to be
        // dropped: a keyed enumeration that runs out of rows has answered, and
        // reading that as an inability would keep every file on every machine.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.NotInstalled(ProductA, MsiError.NoMoreItems);

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.DeclaredProductNotInstalled, outcomes[0]);
    }

    // ---- Every inability keeps the file, which is the half easiest to get wrong ----

    [Fact]
    public void A_return_that_is_not_on_the_absence_allowlist_keeps_the_file()
    {
        // THE ARM THAT DECIDES WHETHER THIS CHECK IS WORTH HAVING. A call that
        // could not be made has not shown the product to be absent. Treating any
        // non-success as "no product" would offer the file on the strength of a
        // question that was never really put, which is the exact collapse the
        // check exists to prevent, and it would look like a working check.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Answers(ProductA, MsiError.AccessDenied);

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
        Assert.True(outcomes[0].Withholds());
    }

    [Fact]
    public void A_package_that_will_not_yield_an_identity_is_kept_back()
    {
        // A file that would not open, a database with no Property table, a
        // ProductCode that is not a GUID: the reader reports all of them as
        // nothing to ask about, and none of them is evidence that the file is
        // spare. This is the outcome an earlier design of this work got backwards.
        var identities = new ScriptedPackageIdentities();
        identities.YieldsNothing(@"C:\Windows\Installer\a.msi");

        var outcomes = new DeclaredProductCheck(new ScriptedMsiProducts(), identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
        Assert.True(outcomes[0].Withholds());
    }

    [Fact]
    public void A_reading_that_yields_an_empty_code_is_kept_back()
    {
        // Not the same shape as the test above and not redundant with it. A
        // do-nothing reader hands back an identity carrying no code rather than a
        // null, which is a value that would reach a keyed enumeration and be
        // answered about nothing.
        var identities = new ScriptedPackageIdentities();
        identities.Yields(@"C:\Windows\Installer\a.msi",
            new PackageIdentity(string.Empty, IsPatch: false, Array.Empty<string>()));

        var outcomes = new DeclaredProductCheck(new ScriptedMsiProducts(), identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
    }

    [Fact]
    public void A_reading_that_comes_back_marked_as_a_patch_is_kept_back()
    {
        // The product reading was asked for and something else came back. A patch
        // code put to a keyed PRODUCT enumeration is a question about nothing, and
        // the answer would be an absence that means only that the wrong thing was
        // asked.
        var identities = new ScriptedPackageIdentities();
        identities.Yields(@"C:\Windows\Installer\a.msi",
            new PackageIdentity(ProductA, IsPatch: true, new[] { ProductB }));

        var outcomes = new DeclaredProductCheck(new ScriptedMsiProducts(), identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
    }

    // ---- A patch and a package in one pass ----

    [Fact]
    public void A_patch_and_a_package_in_one_pass_are_each_screened_on_their_own_terms()
    {
        // Same list, same pass, opposite outcomes. The package declares an installed
        // product and is kept; the patch declares a patch no product holds and is let
        // through. Each verdict answers its own file, and the patch is read as a patch.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);
        identities.DeclaresPatch(@"C:\Windows\Installer\p.msp", PatchQ, ProductB);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.NotInstalled(ProductB, MsiError.UnknownProduct);
        msi.HoldsNoPatches();

        var outcomes = new DeclaredProductCheck(msi, identities).Screen(new[]
        {
            Patch(@"C:\Windows\Installer\p.msp"),
            Package(@"C:\Windows\Installer\a.msi"),
        });

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchNotRegistered, outcomes[0]);
        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcomes[1]);
        Assert.Equal(new[] { @"C:\Windows\Installer\p.msp" }, identities.PatchReads);
    }

    // ---- A product code the machine holds more than once ----

    [Fact]
    public void A_package_whose_declared_product_is_installed_twice_is_kept_back()
    {
        // One code, two installations: per machine and for a user at once. Built
        // without the file readers, the screen asks only whether the machine holds the
        // code the file declares, so the answer is the same as for one installation,
        // and this pins that a walk over several rows still reaches it.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA,
            (null, MsiInstallContext.Machine),
            ("S-1-5-21-9-9-9-1001", MsiInstallContext.UserUnmanaged));

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcomes[0]);
        Assert.True(outcomes[0].Withholds());
    }

    [Fact]
    public void A_package_whose_declared_product_has_a_row_Windows_will_not_read_is_kept_back()
    {
        // The first row says the machine holds the code and the second will not
        // answer. The file is kept back as unestablished rather than on the first
        // row's word, because what the rest of the scan does with the answer is put
        // keyed questions to each instance, and one of them is missing.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA,
            (null, MsiInstallContext.Machine),
            ("S-1-5-21-9-9-9-1001", MsiInstallContext.UserUnmanaged));
        msi.AnswersAtRow(ProductA, index: 1, MsiError.AccessDenied);

        var outcomes = new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") });

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
        Assert.True(outcomes[0].Withholds());
    }

    // ---- The pass's own contract ----

    [Fact]
    public void The_verdicts_line_up_with_the_candidates_they_answer()
    {
        // Positional, so a caller reads verdict i as candidate i's. Three
        // candidates with three different answers, deliberately not in the order
        // the enum declares them, so a check that returned a fixed sequence or
        // sorted its output would fail here.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\gone.msi", ProductA);
        identities.YieldsNothing(@"C:\Windows\Installer\unreadable.msi");
        identities.Declares(@"C:\Windows\Installer\held.msi", ProductB);

        var msi = new ScriptedMsiProducts();
        msi.NotInstalled(ProductA, MsiError.UnknownProduct);
        msi.Installed(ProductB);

        var outcomes = new DeclaredProductCheck(msi, identities).Screen(new[]
        {
            Package(@"C:\Windows\Installer\gone.msi"),
            Package(@"C:\Windows\Installer\unreadable.msi"),
            Package(@"C:\Windows\Installer\held.msi"),
        });

        Assert.Equal(new[]
        {
            DeclaredProductOutcome.DeclaredProductNotInstalled,
            DeclaredProductOutcome.Unestablished,
            DeclaredProductOutcome.DeclaredProductInstalled,
        }, outcomes);
    }

    [Fact]
    public void One_product_code_is_put_to_Windows_once_however_many_files_declare_it()
    {
        // A folder holding six cached packages of one program declares one product
        // code six times. The cache is what keeps the pass proportional to the
        // number of PRODUCTS rather than to the number of files, and it must not
        // change any verdict: all three files here get the same answer.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\v1.msi", ProductA);
        identities.Declares(@"C:\Windows\Installer\v2.msi", ProductA);
        identities.Declares(@"C:\Windows\Installer\v3.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);

        var outcomes = new DeclaredProductCheck(msi, identities).Screen(new[]
        {
            Package(@"C:\Windows\Installer\v1.msi"),
            Package(@"C:\Windows\Installer\v2.msi"),
            Package(@"C:\Windows\Installer\v3.msi"),
        });

        Assert.All(outcomes, o => Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, o));
        Assert.Equal(new[] { ProductA }, msi.Asked);
        // Every file is still opened: two packages declaring one code is the
        // ordinary case, and the only way to know a file declares that code is to
        // read it.
        Assert.Equal(3, identities.Reads.Count);
    }

    [Fact]
    public void A_cancelled_scan_stops_the_pass()
    {
        // The pass opens a database per candidate, so on a folder of any size it
        // is the part of a scan a user is most likely to cancel during.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new DeclaredProductCheck(new ScriptedMsiProducts(), new ScriptedPackageIdentities())
                .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") }, cts.Token));
    }

    // ---- An installed product whose recorded package is another file ----
    //
    // Windows Installer opens a product's cached package through the LocalPackage
    // value each installation records. A copy in the folder that no such value names,
    // and that no source reaches, is let through, and only when EVERY installation's
    // recorded package is present and is another file. Each test after the first is
    // one way an installation's package can fail to be seen, and every one of them
    // keeps the file. The sources have their own tests further down.

    private const string Candidate = @"C:\Windows\Installer\a.msi";
    private const string Recorded = @"C:\Windows\Installer\b.msi";
    private const string UserSid = "S-1-5-21-9-9-9-1001";
    private const string InstallerFolder = @"C:\Windows\Installer";
    private const string SetupFolder = @"D:\Setup\";
    private const string SetupName = "setup.msi";
    private const string SetupPackage = @"D:\Setup\setup.msi";

    /// <summary>
    /// The scan's answer to whether a path names a file directly in the Installer
    /// folder, on the spelling alone, which is all a scripted path has.
    /// </summary>
    private static bool? InInstallerFolder(string path) =>
        path.StartsWith(InstallerFolder + @"\", StringComparison.OrdinalIgnoreCase)
        && path.IndexOf('\\', InstallerFolder.Length + 1) < 0;

    /// <summary>
    /// Product A installed once per machine, recording <see cref="Recorded"/>, with
    /// both files on disk as two different files that both declare product A, and
    /// installed from <see cref="SetupPackage"/>, which is no longer there. Each test
    /// changes one thing.
    /// </summary>
    private static (ScriptedPackageIdentities Packages, ScriptedMsiProducts Msi,
        ScriptedFileIdentities Files, MockFileSystem Disk) ACopyBesideTheRecordedPackage()
    {
        var packages = new ScriptedPackageIdentities();
        packages.Declares(Candidate, ProductA);
        packages.Declares(Recorded, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.RecordsPackage(ProductA, null, MsiInstallContext.Machine, Recorded);
        msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, SetupName, SetupFolder);

        var files = new ScriptedFileIdentities();
        files.Opens(Candidate, 1);
        files.Opens(Recorded, 2);
        files.Answers(SetupPackage, FileIdentityRead.NamesNothing);

        var disk = new MockFileSystem();
        disk.AddFile(Candidate, new MockFileData(new byte[100]));
        disk.AddFile(Recorded, new MockFileData(new byte[100]));

        return (packages, msi, files, disk);
    }

    private static DeclaredProductOutcome ScreenTheCopy(
        (ScriptedPackageIdentities Packages, ScriptedMsiProducts Msi,
            ScriptedFileIdentities Files, MockFileSystem Disk) f,
        Func<string, bool?>? namesAFileInInstallerFolder = null) =>
        new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Package(Candidate) }, default, null,
                namesAFileInInstallerFolder ?? InInstallerFolder)[0];

    [Fact]
    public void A_copy_beside_the_package_its_installed_product_records_is_let_through()
    {
        var f = ACopyBesideTheRecordedPackage();

        var outcome = ScreenTheCopy(f);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile, outcome);
        Assert.False(outcome.Withholds());
        // Both files were identified, which is what the verdict rests on.
        Assert.Contains(Recorded, f.Files.Reads);
        Assert.Contains(Candidate, f.Files.Reads);
    }

    [Fact]
    public void A_copy_is_let_through_when_every_installation_records_another_present_package()
    {
        const string UsersPackage = @"C:\Windows\Installer\c.msi";
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.Installed(ProductA,
            (null, MsiInstallContext.Machine),
            (UserSid, MsiInstallContext.UserUnmanaged));
        f.Msi.RecordsPackage(ProductA, UserSid, MsiInstallContext.UserUnmanaged, UsersPackage);
        f.Msi.RecordsSources(ProductA, UserSid, MsiInstallContext.UserUnmanaged, SetupName, SetupFolder);
        f.Packages.Declares(UsersPackage, ProductA);
        f.Files.Opens(UsersPackage, 3);
        f.Disk.AddFile(UsersPackage, new MockFileData(new byte[100]));

        var outcome = ScreenTheCopy(f);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile, outcome);
        Assert.Equal(2, f.Msi.PackageReads.Count);
    }

    [Fact]
    public void A_copy_is_kept_when_one_installation_records_no_package()
    {
        // The per-machine installation records another file; the per-user one records
        // nothing, so the package that installation opens cannot be seen and this copy
        // could be it.
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.Installed(ProductA,
            (null, MsiInstallContext.Machine),
            (UserSid, MsiInstallContext.UserUnmanaged));
        f.Msi.RecordsPackage(ProductA, UserSid, MsiInstallContext.UserUnmanaged, "");

        var outcome = ScreenTheCopy(f);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcome);
        Assert.True(outcome.Withholds());
    }

    [Fact]
    public void A_copy_is_kept_when_the_only_installation_records_no_package()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsPackage(ProductA, null, MsiInstallContext.Machine, "");

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_record_carries_no_package_property_at_all()
    {
        // ERROR_UNKNOWN_PROPERTY is a record that never carried the value. It reads as
        // an empty value rather than a failure, and an empty value names no package.
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.PackageReadAnswers(ProductA, null, MsiInstallContext.Machine, MsiError.UnknownProperty);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_will_not_read()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.PackageReadAnswers(ProductA, null, MsiInstallContext.Machine, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_is_not_on_disk()
    {
        // The identity fake still answers for the recorded path, so this is decided by
        // the file being absent and not by an identity that would not read.
        var f = ACopyBesideTheRecordedPackage();
        f.Disk.RemoveFile(Recorded);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_path_names_a_folder()
    {
        // A folder opens to an identity like a file does, so without the file test a
        // value naming a folder would read as another package.
        const string AFolder = @"C:\Windows\Installer\{11111111-1111-1111-1111-111111111111}";
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsPackage(ProductA, null, MsiInstallContext.Machine, AFolder);
        f.Files.Opens(AFolder, 4);
        f.Disk.AddDirectory(AFolder);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_opens_as_the_copy_itself()
    {
        // The record names this very file under another spelling: a short name, a
        // long-path prefix, a link. The two paths open to one file ID.
        const string ShortName = @"C:\Windows\Installer\A~1.MSI";
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsPackage(ProductA, null, MsiInstallContext.Machine, ShortName);
        f.Packages.Declares(ShortName, ProductA);
        f.Files.Opens(ShortName, 1);
        f.Disk.AddFile(ShortName, new MockFileData(new byte[100]));

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_will_not_identify()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Files.Answers(Recorded, FileIdentityRead.OpenRefused);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_declares_another_product()
    {
        // The Windows Installer record names a present file, and that file is not
        // product A's package, so the record shows nothing about where A's package is.
        var f = ACopyBesideTheRecordedPackage();
        f.Packages.Declares(Recorded, ProductB);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_recorded_package_declares_nothing()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Packages.YieldsNothing(Recorded, "no Property table");

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_copy_itself_will_not_identify()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Files.Answers(Candidate, FileIdentityRead.IdentityUnavailable);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    // ---- The installation's sources ----
    //
    // When Windows Installer needs a product's original package rather than its
    // cached copy, it looks for the package name in the folders on the product's
    // source list. A copy in the Installer folder that such a source can reach is
    // kept, and so is one whose sources cannot be ruled out. Each keeping test is
    // the fixture above, which lets the copy through, with one thing changed.

    [Fact]
    public void A_copy_is_kept_when_its_product_was_installed_from_the_Installer_folder()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, "c.msi", InstallerFolder + @"\");

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_a_source_package_is_the_copy_itself()
    {
        // A source outside the folder whose package opens as this file.
        var f = ACopyBesideTheRecordedPackage();
        f.Files.Opens(SetupPackage, 1);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_whether_a_source_is_in_the_Installer_folder_is_not_established()
    {
        var f = ACopyBesideTheRecordedPackage();

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f, _ => null));
    }

    [Fact]
    public void A_copy_is_kept_when_the_screen_has_no_Installer_folder_to_compare_against()
    {
        var f = ACopyBesideTheRecordedPackage();

        var outcome = new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Package(Candidate) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcome);
    }

    [Fact]
    public void A_copy_is_kept_when_the_source_list_will_not_read()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.SourceListAnswers(ProductA, null, MsiInstallContext.Machine, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_source_list_does_not_end()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.SourceListNeverEnds(ProductA, null, MsiInstallContext.Machine);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_a_source_entry_is_empty()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, SetupName, "");

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_a_source_entry_holds_a_variable_that_is_not_set()
    {
        // The entry keeps its '%' signs through the expansion, so where it points is
        // not known. Read as it stands, the path finds no file and would be skipped.
        const string Variable = "INSTALLERCLEAN_TEST_UNSET_SOURCE";
        Assert.Null(Environment.GetEnvironmentVariable(Variable));
        var entry = $@"%{Variable}%\Setup\";
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, SetupName, entry);
        f.Files.Answers(entry + SetupName, FileIdentityRead.NamesNothing);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_package_name_will_not_read()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.PackageNameAnswers(ProductA, null, MsiInstallContext.Machine, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_the_package_name_is_empty()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, "", SetupFolder);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_kept_when_a_source_package_will_not_identify()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Files.Answers(SetupPackage, FileIdentityRead.OpenRefused);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, ScreenTheCopy(f));
    }

    [Fact]
    public void A_copy_is_let_through_when_its_source_package_is_another_file()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Files.Opens(SetupPackage, 9);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile, ScreenTheCopy(f));
        Assert.Contains(SetupPackage, f.Files.Reads);
    }

    [Fact]
    public void A_copy_is_let_through_when_its_product_records_no_network_source()
    {
        var f = ACopyBesideTheRecordedPackage();
        f.Msi.RecordsSources(ProductA, null, MsiInstallContext.Machine, SetupName);

        Assert.Equal(DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile, ScreenTheCopy(f));
    }

    [Fact]
    public void Without_the_file_readers_no_recorded_package_is_read()
    {
        // The same fixture as the tests above, which scripts every read, handed to a
        // check built without its two file readers. Nothing may be read: the
        // assertions below are that the package reads and the identity reads never
        // happened.
        var f = ACopyBesideTheRecordedPackage();

        var outcome = new DeclaredProductCheck(f.Msi, f.Packages)
            .Screen(new[] { Package(Candidate) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredProductInstalled, outcome);
        Assert.Empty(f.Msi.PackageReads);
        Assert.Empty(f.Files.Reads);
    }

    [Fact]
    public void Two_copies_of_one_product_ask_Windows_once_and_are_each_compared()
    {
        const string SecondCopy = @"C:\Windows\Installer\a2.msi";
        var f = ACopyBesideTheRecordedPackage();
        f.Packages.Declares(SecondCopy, ProductA);
        f.Files.Opens(SecondCopy, 5);
        f.Disk.AddFile(SecondCopy, new MockFileData(new byte[100]));

        var outcomes = new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Package(Candidate), Package(SecondCopy) }, default, null, InInstallerFolder);

        Assert.All(outcomes, o => Assert.Equal(DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile, o));
        Assert.Single(f.Msi.Asked);
        Assert.Single(f.Msi.PackageReads);
        Assert.Contains(Candidate, f.Files.Reads);
        Assert.Contains(SecondCopy, f.Files.Reads);
    }

    [Fact]
    public void The_composition_root_gives_the_check_both_file_readers()
    {
        // Constructed by hand everywhere else in this file. Without both readers the
        // check keeps every copy of an installed product, and nothing on any screen
        // would show that it had stopped comparing.
        using var services = new ServiceCollection().AddInstallerCleanCore().BuildServiceProvider();

        var check = Assert.IsType<DeclaredProductCheck>(services.GetRequiredService<IDeclaredProductCheck>());

        Assert.True(check.ComparesRecordedPackages);
    }

    // ---- A patch copy and its patch's registrations ----
    //
    // A cached patch declares its own patch code and the products it may be applied to.
    // Windows Installer opens a registered patch's cached copy through the LocalPackage
    // value each registration records. A copy in the folder that no such value names is
    // let through only when EVERY registration of the patch records a copy that is
    // present, is another file and declares the same patch. Each test after the first is
    // the fixture with one thing changed, and every one of them keeps the file.

    private const string PatchQ = "{33333333-3333-3333-3333-333333333333}";
    private const string PatchR = "{44444444-4444-4444-4444-444444444444}";
    private const string PatchCopy = @"C:\Windows\Installer\copy.msp";
    private const string RecordedPatch = @"C:\Windows\Installer\cached.msp";

    /// <summary>
    /// Patch Q, declaring product A as its target, registered against A's one
    /// per-machine installation and recording <see cref="RecordedPatch"/>, with both
    /// files on disk as two different files that both declare patch Q. The machine-wide
    /// patch enumeration lists that registration. Each test changes one thing.
    /// </summary>
    private static (ScriptedPackageIdentities Packages, ScriptedMsiProducts Msi,
        ScriptedFileIdentities Files, MockFileSystem Disk) APatchCopyBesideTheRecordedCopy()
    {
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);
        packages.DeclaresPatch(RecordedPatch, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.HoldsPatch(PatchQ, ProductA, null, MsiInstallContext.Machine);
        msi.RecordsPatchPackage(PatchQ, ProductA, null, MsiInstallContext.Machine, RecordedPatch);

        var files = new ScriptedFileIdentities();
        files.Opens(PatchCopy, 1);
        files.Opens(RecordedPatch, 2);

        var disk = new MockFileSystem();
        disk.AddFile(PatchCopy, new MockFileData(new byte[100]));
        disk.AddFile(RecordedPatch, new MockFileData(new byte[100]));

        return (packages, msi, files, disk);
    }

    private static DeclaredProductOutcome ScreenThePatchCopy(
        (ScriptedPackageIdentities Packages, ScriptedMsiProducts Msi,
            ScriptedFileIdentities Files, MockFileSystem Disk) f) =>
        new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Patch(PatchCopy) }, default, null, InInstallerFolder)[0];

    [Fact]
    public void A_patch_copy_beside_the_copy_its_registration_records_is_let_through()
    {
        var f = APatchCopyBesideTheRecordedCopy();

        var outcome = ScreenThePatchCopy(f);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile, outcome);
        Assert.False(outcome.Withholds());
        // Both files were identified and both were read as patches, which is what the
        // verdict rests on.
        Assert.Contains(RecordedPatch, f.Files.Reads);
        Assert.Contains(PatchCopy, f.Files.Reads);
        Assert.Equal(new[] { PatchCopy, RecordedPatch }, f.Packages.PatchReads);
        // The registration the machine-wide enumeration listed is read for its copy and
        // not asked about again.
        Assert.Empty(f.Msi.PatchStateReads);
    }

    [Fact]
    public void A_patch_copy_is_kept_when_its_registration_records_no_copy()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.RecordsPatchPackage(PatchQ, ProductA, null, MsiInstallContext.Machine, "");

        var outcome = ScreenThePatchCopy(f);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, outcome);
        Assert.True(outcome.Withholds());
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_registration_carries_no_package_property_at_all()
    {
        // ERROR_UNKNOWN_PROPERTY is a record that does not carry the value. It reads as
        // an empty value, and an empty value names no copy.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchPackageReadAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.UnknownProperty);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_will_not_read()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchPackageReadAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_a_listed_registration_answers_that_the_patch_is_not_there()
    {
        // The enumeration listed the registration and the keyed read of its copy answers
        // that the patch is not registered against that product. The two answers
        // disagree, so which copy that registration records is not known.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchPackageReadAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.UnknownPatch);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_is_not_on_disk()
    {
        // The identity fake still answers for the recorded path, so this is decided by
        // the file being absent and not by an identity that would not read.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Disk.RemoveFile(RecordedPatch);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_path_names_a_folder()
    {
        // A folder opens to an identity like a file does, so without the file test a
        // value naming a folder would read as another copy.
        const string AFolder = @"C:\Windows\Installer\{33333333-3333-3333-3333-333333333333}";
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.RecordsPatchPackage(PatchQ, ProductA, null, MsiInstallContext.Machine, AFolder);
        f.Files.Opens(AFolder, 4);
        f.Disk.AddDirectory(AFolder);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_opens_as_the_copy_itself()
    {
        // The registration names this very file under another spelling: a short name, a
        // long-path prefix, a link. The two paths open to one file ID.
        const string ShortName = @"C:\Windows\Installer\COPY~1.MSP";
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.RecordsPatchPackage(PatchQ, ProductA, null, MsiInstallContext.Machine, ShortName);
        f.Packages.DeclaresPatch(ShortName, PatchQ, ProductA);
        f.Files.Opens(ShortName, 1);
        f.Disk.AddFile(ShortName, new MockFileData(new byte[100]));

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_will_not_identify()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Files.Answers(RecordedPatch, FileIdentityRead.OpenRefused);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_declares_another_patch()
    {
        // The registration names a present file, and that file is not patch Q, so the
        // record shows nothing about where Q's cached copy is.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Packages.DeclaresPatch(RecordedPatch, PatchR, ProductA);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_declares_nothing()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Packages.YieldsNothing(RecordedPatch, "patch summary stream would not open");

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_recorded_copy_reads_as_something_other_than_a_patch()
    {
        // The reading carries patch Q's code and is not marked as a patch, so it is not
        // the reading that was asked for and shows nothing about the file.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Packages.Yields(RecordedPatch, new PackageIdentity(PatchQ, IsPatch: false, Array.Empty<string>()));

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_the_copy_itself_will_not_identify()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Files.Answers(PatchCopy, FileIdentityRead.IdentityUnavailable);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_kept_when_a_second_registration_records_no_copy()
    {
        // Product B holds patch Q as well, for a user, and records nothing, so the copy
        // that registration opens cannot be seen and this copy could be it. B is not in
        // the patch's own target list: the machine-wide enumeration is what names it.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.HoldsPatch(PatchQ, ProductB, UserSid, MsiInstallContext.UserUnmanaged);
        f.Msi.RecordsPatchPackage(PatchQ, ProductB, UserSid, MsiInstallContext.UserUnmanaged, "");

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_copy_is_let_through_when_every_registration_records_the_same_present_copy()
    {
        // A patch is cached once and shared by the products holding it, so two
        // registrations name one file. Each registration's value is read and the file
        // they name is identified once.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.HoldsPatch(PatchQ, ProductB, UserSid, MsiInstallContext.UserUnmanaged);
        f.Msi.RecordsPatchPackage(PatchQ, ProductB, UserSid, MsiInstallContext.UserUnmanaged, RecordedPatch);

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile, ScreenThePatchCopy(f));
        Assert.Equal(2, f.Msi.PatchPackageReads.Count);
        Assert.Single(f.Files.Reads, p => p == RecordedPatch);
    }

    [Fact]
    public void A_patch_registration_in_a_user_account_is_read_in_that_account()
    {
        // The only registration is per user. Its copy is asked for in that account and
        // context, and the fake answers nothing else, so a read in any other place fails
        // the test rather than being answered.
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);
        packages.DeclaresPatch(RecordedPatch, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA, (UserSid, MsiInstallContext.UserUnmanaged));
        msi.HoldsPatch(PatchQ, ProductA, UserSid, MsiInstallContext.UserUnmanaged);
        msi.RecordsPatchPackage(PatchQ, ProductA, UserSid, MsiInstallContext.UserUnmanaged, RecordedPatch);

        var files = new ScriptedFileIdentities();
        files.Opens(PatchCopy, 1);
        files.Opens(RecordedPatch, 2);

        var disk = new MockFileSystem();
        disk.AddFile(PatchCopy, new MockFileData(new byte[100]));
        disk.AddFile(RecordedPatch, new MockFileData(new byte[100]));

        var outcome = ScreenThePatchCopy((packages, msi, files, disk));

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile, outcome);
        Assert.Equal(
            new[] { (PatchQ, ProductA, (string?)UserSid, MsiInstallContext.UserUnmanaged) },
            msi.PatchPackageReads);
    }

    // ---- Finding the registrations ----
    //
    // Two ways, unioned. The machine-wide patch enumeration lists every registration it
    // will name. The keyed question puts the patch to each installation of each product
    // the patch declares it may be applied to, which reaches an installation the
    // enumeration does not list. Either can only add a registration.

    [Fact]
    public void A_patch_Windows_holds_no_registration_of_is_left_where_it_was()
    {
        // The must-miss half. Product A is installed and answers that patch Q is not
        // registered against it, and the enumeration lists nothing.
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.HoldsNoPatches();
        msi.PatchStateAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.UnknownPatch);

        var outcome = new DeclaredProductCheck(msi, packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchNotRegistered, outcome);
        Assert.False(outcome.Withholds());
        Assert.Single(msi.PatchStateReads);
    }

    [Fact]
    public void A_patch_whose_target_products_are_not_installed_is_left_where_it_was()
    {
        // No installation of product A to ask, so the keyed question is not put at all.
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.NotInstalled(ProductA, MsiError.UnknownProduct);
        msi.HoldsNoPatches();

        var outcome = new DeclaredProductCheck(msi, packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchNotRegistered, outcome);
        Assert.Empty(msi.PatchStateReads);
    }

    [Fact]
    public void A_registration_only_the_keyed_question_finds_keeps_the_copy_like_any_other()
    {
        // The enumeration lists nothing and product A answers that patch Q is registered
        // against it, recording no copy. The test above is this one with A answering the
        // other way.
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.HoldsNoPatches();
        msi.PatchState(PatchQ, ProductA, null, MsiInstallContext.Machine, "1");
        msi.RecordsPatchPackage(PatchQ, ProductA, null, MsiInstallContext.Machine, "");

        var outcome = new DeclaredProductCheck(msi, packages, new ScriptedFileIdentities(), new MockFileSystem())
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, outcome);
        Assert.True(outcome.Withholds());
    }

    [Fact]
    public void Copies_declaring_one_patch_for_different_products_are_each_asked_about_their_own()
    {
        // Two files carrying patch Q's code with different target lists. The keyed
        // question for the first finds nothing on product A; the second's target, product
        // B, holds the patch and records no copy. Each file is answered from its own
        // target list.
        const string OtherCopy = @"C:\Windows\Installer\other.msp";
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);
        packages.DeclaresPatch(OtherCopy, PatchQ, ProductB);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.Installed(ProductB);
        msi.HoldsNoPatches();
        msi.PatchStateAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.UnknownPatch);
        msi.PatchState(PatchQ, ProductB, null, MsiInstallContext.Machine, "1");
        msi.RecordsPatchPackage(PatchQ, ProductB, null, MsiInstallContext.Machine, "");

        var outcomes = new DeclaredProductCheck(msi, packages, new ScriptedFileIdentities(), new MockFileSystem())
            .Screen(new[] { Patch(PatchCopy), Patch(OtherCopy) });

        Assert.Equal(new[]
        {
            DeclaredProductOutcome.DeclaredPatchNotRegistered,
            DeclaredProductOutcome.DeclaredPatchRegistered,
        }, outcomes);
    }

    [Fact]
    public void Two_copies_of_one_patch_ask_Windows_once_and_are_each_compared()
    {
        const string SecondCopy = @"C:\Windows\Installer\copy2.msp";
        var f = APatchCopyBesideTheRecordedCopy();
        f.Packages.DeclaresPatch(SecondCopy, PatchQ, ProductA);
        f.Files.Opens(SecondCopy, 5);
        f.Disk.AddFile(SecondCopy, new MockFileData(new byte[100]));

        var outcomes = new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Patch(PatchCopy), Patch(SecondCopy) }, default, null, InInstallerFolder);

        Assert.All(outcomes, o => Assert.Equal(DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile, o));
        Assert.Equal(1, f.Msi.PatchEnumerations);
        Assert.Single(f.Msi.Asked);
        Assert.Single(f.Msi.PatchPackageReads);
        Assert.Contains(PatchCopy, f.Files.Reads);
        Assert.Contains(SecondCopy, f.Files.Reads);
    }

    [Fact]
    public void One_pass_walks_the_machine_wide_patch_enumeration_once_for_every_patch()
    {
        // Two different patches. The enumeration lists every patch on the machine, so the
        // second patch is answered from the same walk.
        const string OtherPatchCopy = @"C:\Windows\Installer\r.msp";
        var f = APatchCopyBesideTheRecordedCopy();
        f.Packages.DeclaresPatch(OtherPatchCopy, PatchR, ProductB);
        f.Msi.NotInstalled(ProductB, MsiError.UnknownProduct);

        var outcomes = new DeclaredProductCheck(f.Msi, f.Packages, f.Files, f.Disk)
            .Screen(new[] { Patch(PatchCopy), Patch(OtherPatchCopy) }, default, null, InInstallerFolder);

        Assert.Equal(new[]
        {
            DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile,
            DeclaredProductOutcome.DeclaredPatchNotRegistered,
        }, outcomes);
        Assert.Equal(1, f.Msi.PatchEnumerations);
    }

    [Fact]
    public void Without_the_file_readers_a_registered_patch_s_copy_is_kept_and_nothing_is_read()
    {
        // The fixture that lets the copy through, handed to a check built without its two
        // file readers. The copy is kept, and neither the recorded copy nor any file's
        // identity is read.
        var f = APatchCopyBesideTheRecordedCopy();

        var outcome = new DeclaredProductCheck(f.Msi, f.Packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.DeclaredPatchRegistered, outcome);
        Assert.Empty(f.Msi.PatchPackageReads);
        Assert.Empty(f.Files.Reads);
    }

    // ---- A patch the check can say nothing about ----
    //
    // Each of these is the check failing to establish the patch's registrations. The
    // check says nothing about such a file and the rest of the scan decides it.

    [Fact]
    public void A_patch_that_will_not_yield_its_code_is_left_to_the_rest_of_the_scan()
    {
        var packages = new ScriptedPackageIdentities();
        packages.YieldsNothing(PatchCopy, "patch summary stream would not open (1627)");

        var outcome = new DeclaredProductCheck(new ScriptedMsiProducts(), packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, outcome);
    }

    [Fact]
    public void A_patch_reading_with_an_empty_code_is_left_to_the_rest_of_the_scan()
    {
        var packages = new ScriptedPackageIdentities();
        packages.Yields(PatchCopy, new PackageIdentity(string.Empty, IsPatch: true, new[] { ProductA }));

        var outcome = new DeclaredProductCheck(new ScriptedMsiProducts(), packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, outcome);
    }

    [Fact]
    public void A_patch_reading_that_comes_back_as_a_product_is_left_to_the_rest_of_the_scan()
    {
        // The reading names a product, so the only thing stopping it is that it is not
        // marked as a patch.
        var packages = new ScriptedPackageIdentities();
        packages.Yields(PatchCopy, new PackageIdentity(PatchQ, IsPatch: false, new[] { ProductA }));

        var outcome = new DeclaredProductCheck(new ScriptedMsiProducts(), packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, outcome);
    }

    [Fact]
    public void A_patch_reading_that_names_no_target_is_left_to_the_rest_of_the_scan()
    {
        // With no product named, there is no installation to put the keyed question to.
        var packages = new ScriptedPackageIdentities();
        packages.Yields(PatchCopy, new PackageIdentity(PatchQ, IsPatch: true, Array.Empty<string>()));

        var outcome = new DeclaredProductCheck(new ScriptedMsiProducts(), packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, outcome);
    }

    [Fact]
    public void A_patch_is_left_to_the_rest_of_the_scan_when_the_patch_enumeration_will_not_start()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchEnumerationAnswersAt(0, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_is_left_to_the_rest_of_the_scan_when_the_patch_enumeration_stops_part_way()
    {
        // One row, then a return that is not the end of the list: what lies past it is
        // unread.
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchEnumerationAnswersAt(1, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_is_left_to_the_rest_of_the_scan_when_the_patch_enumeration_does_not_end()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.PatchEnumerationNeverEnds(PatchR, ProductB);

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_is_left_to_the_rest_of_the_scan_when_a_target_s_installations_will_not_list()
    {
        var f = APatchCopyBesideTheRecordedCopy();
        f.Msi.Answers(ProductA, MsiError.AccessDenied);

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, ScreenThePatchCopy(f));
    }

    [Fact]
    public void A_patch_is_left_to_the_rest_of_the_scan_when_the_keyed_question_is_not_answered()
    {
        var packages = new ScriptedPackageIdentities();
        packages.DeclaresPatch(PatchCopy, PatchQ, ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.HoldsNoPatches();
        msi.PatchStateAnswers(PatchQ, ProductA, null, MsiInstallContext.Machine, MsiError.AccessDenied);

        var outcome = new DeclaredProductCheck(msi, packages)
            .Screen(new[] { Patch(PatchCopy) })[0];

        Assert.Equal(DeclaredProductOutcome.NotAProductPackage, outcome);
    }

    // ---- What the outcomes mean, pinned over the whole enum ----

    [Fact]
    public void Exactly_five_outcomes_let_a_file_through_and_an_unset_verdict_does_not()
    {
        // The rule is written as "anything but these five" so that a member added
        // later withholds rather than silently not withholding. This pins the
        // permitting set by name, so adding one that permits has to be a
        // deliberate edit here as well as there.
        var permitting = Enum.GetValues<DeclaredProductOutcome>()
            .Where(o => !o.Withholds())
            .ToArray();

        Assert.Equal(
            new[]
            {
                DeclaredProductOutcome.NotAProductPackage,
                DeclaredProductOutcome.DeclaredProductNotInstalled,
                DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile,
                DeclaredProductOutcome.DeclaredPatchNotRegistered,
                DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile,
            },
            permitting);

        // And the value a verdict nobody set carries. An array of these is
        // allocated before anything fills it, so the zero has to keep the file.
        Assert.True(default(DeclaredProductOutcome).Withholds());
    }

    [Fact]
    public void A_file_that_yields_no_identity_hands_on_the_reader_s_own_note()
    {
        var identities = new ScriptedPackageIdentities();
        identities.YieldsNothing(@"C:\Windows\Installer\a.msi", note: "no Property table");

        var recorded = new List<(Exception Ex, string Cause)>();

        var outcomes = new DeclaredProductCheck(new ScriptedMsiProducts(), identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") },
                recordRefusal: (ex, cause) => recorded.Add((ex, cause)));

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);

        var only = Assert.Single(recorded);
        Assert.Equal("no Property table", only.Cause);
        Assert.Contains("no Property table", only.Ex.Message, StringComparison.Ordinal);

        // The path is not named, for the reason the reader's own contract gives: the
        // app runs elevated and this is read long after a report about another file.
        Assert.DoesNotContain(@"a.msi", only.Ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_reading_that_answers_but_answers_nothing_useful_records_nothing()
    {
        // THE DISTINCTION THE ARM'S COMMENT IS ABOUT, and without this nothing held it.
        // Three refusals share one arm. Only the first is the reader failing; the other
        // two are answers it gave, so it wrote no note about them and a log entry saying
        // the file "did not yield the product code it declares" would be untrue of a file
        // that yielded one. Moving the record outside the null test passes every other
        // test in this file.
        var identities = new ScriptedPackageIdentities();
        identities.Yields(@"C:\Windows\Installer\a.msi",
            new PackageIdentity(string.Empty, IsPatch: false, Array.Empty<string>()));

        var recorded = new List<Exception>();

        var outcomes = new DeclaredProductCheck(new ScriptedMsiProducts(), identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") },
                recordRefusal: (ex, _) => recorded.Add(ex));

        Assert.Equal(DeclaredProductOutcome.Unestablished, outcomes[0]);
        Assert.Empty(recorded);
    }

    [Fact]
    public void A_file_that_yields_an_identity_records_nothing()
    {
        // The must-miss control for the test above. A screen that recorded on every
        // candidate would satisfy it while saying nothing about the refusal path.
        var identities = new ScriptedPackageIdentities();
        identities.Declares(@"C:\Windows\Installer\a.msi", ProductA);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);

        var recorded = new List<Exception>();

        new DeclaredProductCheck(msi, identities)
            .Screen(new[] { Package(@"C:\Windows\Installer\a.msi") },
                recordRefusal: (ex, _) => recorded.Add(ex));

        Assert.Empty(recorded);
    }
}

/// <summary>
/// A scripted <see cref="IPackageIdentityReader"/>. Shared with
/// <see cref="FileSystemScanServiceDeclaredProductTests"/>, which drives the real
/// check through the real scan.
///
/// AN UNSCRIPTED PATH THROWS RATHER THAN YIELDING NOTHING. "Nothing to read" is
/// one of the two answers under test and it is the one that keeps a file, so a
/// fake handing it back by default would let a test assert a withholding that the
/// fixture, not the code, produced.
/// </summary>
internal sealed class ScriptedPackageIdentities : IPackageIdentityReader
{
    private readonly Dictionary<string, PackageIdentity?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _notes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every path this reader was asked about, in order.</summary>
    public List<string> Reads { get; } = new();

    /// <summary>
    /// Every path this reader was asked to take the patch reading of, in order. The real
    /// reader opens a patch's summary stream and an installation package's database, and
    /// a patch read the other way yields nothing, so which reading was asked for is part
    /// of what a test pins.
    /// </summary>
    public List<string> PatchReads { get; } = new();

    public void Declares(string path, string productCode) =>
        _byPath[path] = new PackageIdentity(productCode, IsPatch: false, Array.Empty<string>());

    /// <summary>The file is a patch declaring its own code and the products it may be applied to.</summary>
    public void DeclaresPatch(string path, string patchCode, params string[] targets) =>
        _byPath[path] = new PackageIdentity(patchCode, IsPatch: true, targets);

    public void Yields(string path, PackageIdentity identity) => _byPath[path] = identity;

    /// <summary>
    /// The file would not give up an identity at all. The note is what the real
    /// reader writes to say WHICH of its refusals this was, and it is the thing the
    /// screen is meant to pass on rather than drop.
    /// </summary>
    public void YieldsNothing(string path, string note = "")
    {
        _byPath[path] = null;
        _notes[path] = note;
    }

    public PackageIdentity? Read(string filePath, bool isPatch, out string detail)
    {
        Reads.Add(filePath);
        if (isPatch) PatchReads.Add(filePath);
        detail = _notes.TryGetValue(filePath, out var note) ? note : string.Empty;
        if (!_byPath.TryGetValue(filePath, out var identity))
            throw new InvalidOperationException(
                $"the fake reader was asked to read {filePath}, which no test scripted");
        return identity;
    }
}

/// <summary>
/// A scripted <see cref="IMsiApi"/> answering the questions this area asks: the keyed
/// product enumeration, the LocalPackage and PackageName each installation records,
/// each installation's network source list, the machine-wide patch enumeration, and
/// the State and LocalPackage a patch records against each product holding it.
///
/// AN UNSCRIPTED CODE THROWS, for the reason the reader's does: "Windows does not
/// hold that product" is the single answer that lets a file through, so a fake
/// giving it by default would let a test assert an offer nothing established. The
/// patch questions throw on anything unscripted for the same reason: "no registration
/// of that patch" is the answer that lets a patch copy through.
/// </summary>
internal sealed class ScriptedMsiProducts : IMsiApi
{
    private readonly Dictionary<string, uint> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Sid, MsiInstallContext Context)[]> _instances =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string ProductCode, uint Index), uint> _rowAnswers = new();

    /// <summary>
    /// Every product code this API was asked about, in order, once each. Recorded at
    /// index 0, because the walk over a code's rows is one question about one code.
    /// </summary>
    public List<string> Asked { get; } = new();

    /// <summary>
    /// Every keyed row this API answered, ending row included, so a test can pin that
    /// the walk stops where the rows do rather than at a number.
    /// </summary>
    public int Rows { get; private set; }

    /// <summary>
    /// Installed as one ordinary per-machine instance, which is what a fixture with
    /// nothing to say about instances means.
    /// </summary>
    public void Installed(string productCode) =>
        Installed(productCode, (null, MsiInstallContext.Machine));

    /// <summary>
    /// Installed as the instances given, in enumeration order. One product code can
    /// name more than one installation, per machine and per user at once or under two
    /// accounts, and each is its own row with its own account and context.
    /// </summary>
    public void Installed(string productCode, params (string? Sid, MsiInstallContext Context)[] instances)
    {
        _answers[productCode] = MsiError.Success;
        _instances[productCode] = instances;
    }

    /// <summary>
    /// What one ROW of a code's enumeration returns, keyed by index. It wins over the
    /// per-code answer, which is what builds a walk that reads an instance and then
    /// meets a return it cannot read.
    /// </summary>
    public void AnswersAtRow(string productCode, uint index, uint error) =>
        _rowAnswers[(productCode, index)] = error;

    /// <param name="absence">
    /// Which of the returns that mean absence to give. Named by the caller rather
    /// than picked here, because which returns are allowed to carry that meaning
    /// is the thing under test.
    /// </param>
    public void NotInstalled(string productCode, uint absence) => _answers[productCode] = absence;

    public void Answers(string productCode, uint error) => _answers[productCode] = error;

    public uint EnumProducts(string? productCode, string? userSid, MsiInstallContext context, uint index,
        char[]? installedProductCode, out MsiInstallContext installedContext, char[]? sid, ref uint sidLength)
    {
        installedContext = MsiInstallContext.Machine;

        if (productCode is null)
            throw new InvalidOperationException(
                "the fake was asked to walk every product; this area only asks keyed questions");

        Rows++;
        if (index == 0) Asked.Add(productCode);

        if (!_answers.TryGetValue(productCode, out var result))
            throw new InvalidOperationException(
                $"the fake was asked about {productCode}, which no test scripted");

        if (_rowAnswers.TryGetValue((productCode, index), out var row)) return row;
        if (result != MsiError.Success) return result;

        // A code scripted to succeed with nothing said about instances is one ordinary
        // per-machine instance, which is what Installed(code) means and what every
        // fixture that never mentions them is describing.
        var instances = _instances.TryGetValue(productCode, out var scripted)
            ? scripted
            : new[] { ((string?)null, MsiInstallContext.Machine) };
        if (index >= instances.Length) return MsiError.NoMoreItems;

        // The buffer is written on success because the real API does, and a fake that
        // leaves it empty is a fake with a shape the code has never met. The caller
        // reads the SID back only outside the machine context, which is the rule the
        // real API's own output follows.
        if (installedProductCode is not null)
            for (var i = 0; i < productCode.Length && i < installedProductCode.Length - 1; i++)
                installedProductCode[i] = productCode[i];

        var (instanceSid, instanceContext) = instances[(int)index];
        installedContext = instanceContext;
        if (instanceSid is not null && sid is not null)
        {
            for (var i = 0; i < instanceSid.Length && i < sid.Length; i++) sid[i] = instanceSid[i];
            sidLength = (uint)instanceSid.Length;
        }

        return MsiError.Success;
    }

    private readonly List<(string PatchCode, string ProductCode, string? Sid, MsiInstallContext Context)> _patchRows = new();
    private readonly Dictionary<uint, uint> _patchRowAnswers = new();
    private bool _patchRowsScripted;
    private bool _patchRowsEndless;

    /// <summary>
    /// How many times the machine-wide patch enumeration was started, counted at its
    /// first index, so a test can pin that one pass walks it once.
    /// </summary>
    public int PatchEnumerations { get; private set; }

    /// <summary>
    /// One row of the machine-wide patch enumeration: the patch is registered against
    /// the product in that account and context. Rows come back in the order scripted.
    /// </summary>
    public void HoldsPatch(string patchCode, string productCode, string? sid, MsiInstallContext context)
    {
        _patchRowsScripted = true;
        _patchRows.Add((patchCode, productCode, sid, context));
    }

    /// <summary>The machine-wide patch enumeration ends at once, naming no registration.</summary>
    public void HoldsNoPatches() => _patchRowsScripted = true;

    /// <summary>What one index of the machine-wide patch enumeration returns instead of a row.</summary>
    public void PatchEnumerationAnswersAt(uint index, uint error)
    {
        _patchRowsScripted = true;
        _patchRowAnswers[index] = error;
    }

    /// <summary>A machine-wide patch enumeration whose every index answers with one row, and which never ends.</summary>
    public void PatchEnumerationNeverEnds(string patchCode, string productCode)
    {
        _patchRowsScripted = true;
        _patchRowsEndless = true;
        _patchRows.Add((patchCode, productCode, null, MsiInstallContext.Machine));
    }

    /// <summary>
    /// Answers the machine-wide patch enumeration, the one call made with no product
    /// code, one scripted row per index and <see cref="MsiError.NoMoreItems"/> past the
    /// last. Anything narrower is a question this area does not ask, and throws.
    ///
    /// AN UNSCRIPTED ENUMERATION THROWS. A machine holding no patch registration is an
    /// answer that lets a patch copy through, so a fake giving it by default would let a
    /// test assert an offer nothing established.
    /// </summary>
    public uint EnumPatches(string? productCode, string? userSid, MsiInstallContext context, MsiPatchFilter filter,
        uint index, char[]? patchCode, char[]? targetProductCode, out MsiInstallContext targetProductContext,
        char[]? targetUserSid, ref uint targetUserSidLength)
    {
        targetProductContext = MsiInstallContext.Machine;

        if (productCode is not null || userSid != "S-1-1-0"
            || context != MsiInstallContext.All || filter != MsiPatchFilter.All)
            throw new InvalidOperationException(
                "the declared-product check enumerates every patch of every product in every account, "
                + $"and was asked for {productCode ?? "every product"} as {userSid ?? "the current user"} "
                + $"in {context} with filter {filter}");

        if (!_patchRowsScripted)
            throw new InvalidOperationException(
                "the fake was asked for the machine's patch registrations, which no test scripted");

        if (index == 0) PatchEnumerations++;

        if (_patchRowAnswers.TryGetValue(index, out var error)) return error;
        if (!_patchRowsEndless && index >= _patchRows.Count) return MsiError.NoMoreItems;

        var (rowPatch, rowProduct, rowSid, rowContext) = _patchRows[_patchRowsEndless ? 0 : (int)index];

        // Both GUID buffers and the account are written, because the real API writes
        // them and the caller reads the account back only outside the machine context.
        if (patchCode is not null)
            for (var i = 0; i < rowPatch.Length && i < patchCode.Length - 1; i++) patchCode[i] = rowPatch[i];
        if (targetProductCode is not null)
            for (var i = 0; i < rowProduct.Length && i < targetProductCode.Length - 1; i++)
                targetProductCode[i] = rowProduct[i];

        targetProductContext = rowContext;
        if (rowSid is not null && targetUserSid is not null)
        {
            for (var i = 0; i < rowSid.Length && i < targetUserSid.Length; i++) targetUserSid[i] = rowSid[i];
            targetUserSidLength = (uint)rowSid.Length;
        }
        else targetUserSidLength = 0;

        return MsiError.Success;
    }

    private readonly Dictionary<(string ProductCode, string? Sid, MsiInstallContext Context), (uint Error, string Value)>
        _localPackages = new();

    /// <summary>Every LocalPackage read this API answered, in order.</summary>
    public List<(string ProductCode, string? Sid, MsiInstallContext Context)> PackageReads { get; } = new();

    /// <summary>
    /// The LocalPackage value one installation of a product records. An empty value is
    /// a record that names no package, which the real API returns for a record that
    /// never carried the property.
    /// </summary>
    public void RecordsPackage(string productCode, string? sid, MsiInstallContext context, string localPackage) =>
        _localPackages[(productCode, sid, context)] = (MsiError.Success, localPackage);

    /// <summary>What reading one installation's LocalPackage returns instead of a value.</summary>
    public void PackageReadAnswers(string productCode, string? sid, MsiInstallContext context, uint error) =>
        _localPackages[(productCode, sid, context)] = (error, string.Empty);

    private readonly Dictionary<(string ProductCode, string? Sid, MsiInstallContext Context), (uint Error, string Value)>
        _packageNames = new();

    private readonly Dictionary<(string ProductCode, string? Sid, MsiInstallContext Context), (uint Error, string[] Folders, bool Endless)>
        _sources = new();

    /// <summary>
    /// The package name and the network source folders one installation records, in
    /// list order.
    /// </summary>
    public void RecordsSources(string productCode, string? sid, MsiInstallContext context,
        string packageName, params string[] folders)
    {
        _packageNames[(productCode, sid, context)] = (MsiError.Success, packageName);
        _sources[(productCode, sid, context)] = (MsiError.Success, folders, false);
    }

    /// <summary>What reading one installation's PackageName returns instead of a value.</summary>
    public void PackageNameAnswers(string productCode, string? sid, MsiInstallContext context, uint error) =>
        _packageNames[(productCode, sid, context)] = (error, string.Empty);

    /// <summary>What reading one installation's source list returns instead of an entry.</summary>
    public void SourceListAnswers(string productCode, string? sid, MsiInstallContext context, uint error) =>
        _sources[(productCode, sid, context)] = (error, Array.Empty<string>(), false);

    /// <summary>A source list whose every index answers with another folder, and which never ends.</summary>
    public void SourceListNeverEnds(string productCode, string? sid, MsiInstallContext context) =>
        _sources[(productCode, sid, context)] = (MsiError.Success, new[] { @"D:\Somewhere\" }, true);

    /// <summary>
    /// Answers LocalPackage and PackageName, the two product properties the check
    /// reads, with the real API's two-call shape: a null buffer is answered with the
    /// length, a buffer with the value.
    ///
    /// AN UNSCRIPTED INSTALLATION THROWS. A recorded package that is present and is
    /// another file is the answer that lets a file through, so a fake inventing one
    /// would let a test assert an offer nothing established.
    /// </summary>
    public uint GetProductInfo(string productCode, string? userSid, MsiInstallContext context, string property,
        char[]? value, ref uint valueLength)
    {
        var table = property switch
        {
            MsiInstallProperty.LocalPackage => _localPackages,
            MsiInstallProperty.PackageName => _packageNames,
            _ => throw new InvalidOperationException(
                $"the declared-product check reads LocalPackage and PackageName, and was asked for {property}"),
        };

        if (!table.TryGetValue((productCode, userSid, context), out var scripted))
            throw new InvalidOperationException(
                $"the fake was asked for the {property} {productCode} records for {userSid ?? "the machine"} "
                + $"in {context}, which no test scripted");

        if (value is null && property == MsiInstallProperty.LocalPackage) PackageReads.Add((productCode, userSid, context));
        if (scripted.Error != MsiError.Success) return scripted.Error;

        if (value is not null)
            for (var i = 0; i < scripted.Value.Length && i < value.Length; i++) value[i] = scripted.Value[i];
        valueLength = (uint)scripted.Value.Length;
        return MsiError.Success;
    }

    /// <summary>
    /// Answers the network source list with the real API's two-call shape, one entry
    /// per index and <see cref="MsiError.NoMoreItems"/> past the last.
    ///
    /// AN UNSCRIPTED INSTALLATION THROWS. An empty list is an answer that lets a file
    /// through, so a fake giving one by default would let a test assert an offer
    /// nothing established.
    /// </summary>
    public uint EnumSources(string productCode, string? userSid, MsiInstallContext context, uint options,
        uint index, char[]? source, ref uint sourceLength)
    {
        if (options != (MsiSourceListOptions.Product | MsiSourceListOptions.Network))
            throw new InvalidOperationException(
                $"the declared-product check reads a product's network sources, and was asked with options {options}");

        if (!_sources.TryGetValue((productCode, userSid, context), out var scripted))
            throw new InvalidOperationException(
                $"the fake was asked for the sources {productCode} records for {userSid ?? "the machine"} "
                + $"in {context}, which no test scripted");

        if (scripted.Error != MsiError.Success) return scripted.Error;
        if (!scripted.Endless && index >= scripted.Folders.Length) return MsiError.NoMoreItems;

        var folder = scripted.Folders[scripted.Endless ? 0 : (int)index];
        if (source is not null)
            for (var i = 0; i < folder.Length && i < source.Length; i++) source[i] = folder[i];
        sourceLength = (uint)folder.Length;
        return MsiError.Success;
    }

    private readonly Dictionary<(string PatchCode, string ProductCode, string? Sid, MsiInstallContext Context), (uint Error, string Value)>
        _patchStates = new();

    private readonly Dictionary<(string PatchCode, string ProductCode, string? Sid, MsiInstallContext Context), (uint Error, string Value)>
        _patchPackages = new();

    /// <summary>Every patch State read this API answered, in order.</summary>
    public List<(string PatchCode, string ProductCode, string? Sid, MsiInstallContext Context)> PatchStateReads { get; } = new();

    /// <summary>Every patch LocalPackage read this API answered, in order.</summary>
    public List<(string PatchCode, string ProductCode, string? Sid, MsiInstallContext Context)> PatchPackageReads { get; } = new();

    /// <summary>
    /// The State a patch has against one installation of a product: a value where the
    /// patch is registered against it.
    /// </summary>
    public void PatchState(string patchCode, string productCode, string? sid, MsiInstallContext context, string state) =>
        _patchStates[(patchCode, productCode, sid, context)] = (MsiError.Success, state);

    /// <summary>
    /// What reading a patch's State against one installation returns instead of a value.
    /// <see cref="MsiError.UnknownPatch"/> is the answer for an installation the patch is
    /// not registered against.
    /// </summary>
    public void PatchStateAnswers(string patchCode, string productCode, string? sid, MsiInstallContext context, uint error) =>
        _patchStates[(patchCode, productCode, sid, context)] = (error, string.Empty);

    /// <summary>
    /// The LocalPackage value one registration of a patch records. An empty value is a
    /// registration that names no cached copy.
    /// </summary>
    public void RecordsPatchPackage(string patchCode, string productCode, string? sid, MsiInstallContext context,
        string localPackage) =>
        _patchPackages[(patchCode, productCode, sid, context)] = (MsiError.Success, localPackage);

    /// <summary>What reading one registration's LocalPackage returns instead of a value.</summary>
    public void PatchPackageReadAnswers(string patchCode, string productCode, string? sid, MsiInstallContext context,
        uint error) =>
        _patchPackages[(patchCode, productCode, sid, context)] = (error, string.Empty);

    /// <summary>
    /// Answers State and LocalPackage, the two patch properties the check reads, with the
    /// real API's two-call shape: a null buffer is answered with the length, a buffer
    /// with the value.
    ///
    /// AN UNSCRIPTED PAIRING THROWS. "The patch is not registered against this product"
    /// is an answer that lets a patch copy through, and a recorded copy that is present
    /// and is another file is the other, so a fake inventing either would let a test
    /// assert an offer nothing established.
    /// </summary>
    public uint GetPatchInfo(string patchCode, string productCode, string? userSid, MsiInstallContext context,
        string property, char[]? value, ref uint valueLength)
    {
        var (table, reads) = property switch
        {
            MsiInstallProperty.State => (_patchStates, PatchStateReads),
            MsiInstallProperty.LocalPackage => (_patchPackages, PatchPackageReads),
            _ => throw new InvalidOperationException(
                $"the declared-product check reads a patch's State and LocalPackage, and was asked for {property}"),
        };

        if (!table.TryGetValue((patchCode, productCode, userSid, context), out var scripted))
            throw new InvalidOperationException(
                $"the fake was asked for the {property} patch {patchCode} records against {productCode} "
                + $"for {userSid ?? "the machine"} in {context}, which no test scripted");

        if (value is null) reads.Add((patchCode, productCode, userSid, context));
        if (scripted.Error != MsiError.Success) return scripted.Error;

        if (value is not null)
            for (var i = 0; i < scripted.Value.Length && i < value.Length; i++) value[i] = scripted.Value[i];
        valueLength = (uint)scripted.Value.Length;
        return MsiError.Success;
    }
}

/// <summary>
/// A scripted <see cref="IFileIdentityReader"/>, standing in for the volume and file
/// ID a path opens.
///
/// AN UNSCRIPTED PATH THROWS. "A different file from the candidate" is the answer
/// that lets a file through, and a fake handing out fresh identities by default would
/// make every recorded package look like another file.
/// </summary>
internal sealed class ScriptedFileIdentities : IFileIdentityReader
{
    private readonly Dictionary<string, (FileIdentityRead Outcome, FileIdentity Identity)> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every path this reader was asked about, in order.</summary>
    public List<string> Reads { get; } = new();

    /// <summary>
    /// The path opens as the file numbered <paramref name="fileId"/>. Two paths given
    /// the same number are one file under two spellings.
    /// </summary>
    public void Opens(string path, ulong fileId) =>
        _byPath[path] = (FileIdentityRead.Read, new FileIdentity(1, fileId, 0));

    public void Answers(string path, FileIdentityRead outcome) => _byPath[path] = (outcome, default);

    public FileIdentityRead ReadOutcome(string path, out FileIdentity identity)
    {
        Reads.Add(path);
        if (!_byPath.TryGetValue(path, out var scripted))
            throw new InvalidOperationException(
                $"the fake identity reader was asked about {path}, which no test scripted");
        identity = scripted.Identity;
        return scripted.Outcome;
    }
}
