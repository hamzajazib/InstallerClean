using System.IO.Abstractions;
using InstallerClean.Interop;
using InstallerClean.Models;

namespace InstallerClean.Services;

/// <summary>
/// Production <see cref="IDeclaredProductCheck"/>. For each candidate installation
/// package it reads the package's own ProductCode through
/// <see cref="IPackageIdentityReader"/>, puts it to Windows through the same keyed
/// enumeration the patch-target route uses, and for an installed product reads the
/// <c>LocalPackage</c> each installation records and the package each one's source
/// list points at. For each candidate patch it reads the patch's own code and the
/// products its Template names, finds the registrations of that patch through the
/// machine-wide patch enumeration and the keyed patch read, and reads the
/// <c>LocalPackage</c> each registration records and the patch package the patch's
/// source list points at in each registration's account and context.
///
/// IT COMPOSES THINGS THAT ALREADY EXIST. The reading of each file, package or
/// patch, is the reader's. The asking is
/// <see cref="InstallerQueryService.ResolveProductInstances"/>,
/// <see cref="InstallerQueryService.EnumeratePatchHoldersAcrossAllProducts"/>,
/// <see cref="InstallerQueryService.ReadProductProperty"/>,
/// <see cref="InstallerQueryService.GetPatchProperty"/> and
/// <see cref="InstallerQueryService.ReadSourceListProperty"/>, shared rather than copied
/// because the part of them that decides anything is which returns are allowed to
/// mean "not installed", "not registered", "the end of the list" or "no value":
/// those allowlists are the difference between a file kept and a file offered, and
/// a second copy of them is a second thing to keep right. The comparison of recorded
/// packages against the candidate is <see cref="IFileIdentityReader"/>, the reader
/// the scan's own path comparison uses.
/// </summary>
public sealed class DeclaredProductCheck : IDeclaredProductCheck
{
    private readonly IMsiApi _msi;
    private readonly IPackageIdentityReader _identityReader;
    private readonly IFileIdentityReader? _fileIdentities;
    private readonly IFileSystem? _fileSystem;

    /// <param name="fileIdentities">
    /// Identifies the file each recorded package path opens, and the candidate's own.
    /// </param>
    /// <param name="fileSystem">
    /// Answers whether a recorded package path names a file, so that a value naming a
    /// folder is not taken for a package.
    /// </param>
    /// <remarks>
    /// WITHOUT BOTH FILE READERS NO RECORDED PACKAGE IS LOOKED AT, and every candidate
    /// whose declared product is installed is kept as
    /// <see cref="DeclaredProductOutcome.DeclaredProductInstalled"/>, and every
    /// candidate whose declared patch is registered as
    /// <see cref="DeclaredProductOutcome.DeclaredPatchRegistered"/>. That is the
    /// direction a missing dependency has to fail in. The composition root supplies
    /// both.
    /// </remarks>
    public DeclaredProductCheck(
        IMsiApi msi,
        IPackageIdentityReader identityReader,
        IFileIdentityReader? fileIdentities = null,
        IFileSystem? fileSystem = null)
    {
        _msi = msi;
        _identityReader = identityReader;
        _fileIdentities = fileIdentities;
        _fileSystem = fileSystem;
    }

    /// <summary>Whether this check compares recorded packages with the candidate.</summary>
    internal bool ComparesRecordedPackages => _fileIdentities is not null && _fileSystem is not null;

    /// <inheritdoc />
    public IReadOnlyList<DeclaredProductOutcome> Screen(
        IReadOnlyList<OrphanedFile> candidates,
        CancellationToken cancellationToken = default,
        Action<Exception, string>? recordRefusal = null,
        Func<string, bool?>? namesAFileInInstallerFolder = null)
    {
        var outcomes = new DeclaredProductOutcome[candidates.Count];

        // Per pass, so it cannot outlive the machine state it describes. A folder
        // holding six cached packages of one program declares one product code
        // six times, and the answer to a keyed enumeration does not change inside
        // a scan.
        //
        // ORDINAL, because the reader canonicalises every code it returns to the
        // braced upper-case form for exactly this: two readings of one code are
        // then the same string, and a comparer that folded case would be covering
        // for a reader that had stopped doing that.
        var asked = new Dictionary<string, DeclarationAnswer>(StringComparer.Ordinal);

        // What the pass has asked about installations and patch registrations, shared
        // by both halves, for the same reason and with the same lifetime.
        var pass = new PassAnswers(_msi, cancellationToken);

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates[i];

            // A PATCH DECLARES A PATCH CODE AND NOT A PRODUCT CODE, so it is screened
            // against the registrations of that patch rather than against the
            // installations of a product.
            if (candidate.IsPatch)
            {
                outcomes[i] = ScreenPatch(candidate.FullPath, pass, recordRefusal, namesAFileInInstallerFolder);
                continue;
            }

            var identity = _identityReader.Read(candidate.FullPath, isPatch: false, out var detail);

            // THREE REFUSALS UNDER ONE ARM, and each of them is the file failing
            // to give this pass something to ask about. Null is the reader's own
            // "nothing here to ask", documented as covering everything from a
            // file that would not open to a code that is not a GUID. An empty
            // code is the same outcome reached without a null, which the seam's
            // do-nothing implementations produce. A reading that came back
            // marked as a patch is a reader that did not answer the question
            // asked, and a code of the wrong kind put to a keyed product
            // enumeration would be answered about nothing.
            if (identity is null
                || identity.Value.Code.Length == 0
                || identity.Value.IsPatch)
            {
                // ONLY THE NULL ARM HAS A DETAIL TO KEEP. The other two are answers
                // the reader gave rather than failures it had, so it wrote nothing down
                // about them and a note saying the file did not yield a code would be
                // untrue of one that did. What the detail is for, and why no path goes
                // with it, is at the sibling site in InstallerQueryService.
                if (identity is null)
                    recordRefusal?.Invoke(
                        new InvalidOperationException(
                            "A cached package did not yield the product code it declares, so it is "
                            + "kept rather than offered. Reader detail: "
                            + (detail.Length == 0 ? "none given" : detail) + "."),
                        detail);

                outcomes[i] = DeclaredProductOutcome.Unestablished;
                continue;
            }

            var code = identity.Value.Code;
            if (!asked.TryGetValue(code, out var answer))
            {
                answer = Ask(code, pass, namesAFileInInstallerFolder);
                asked[code] = answer;
            }

            // The product-level answer is shared by every candidate declaring the
            // code; whether the recorded packages are OTHER files is a question about
            // this candidate, so it is asked per file.
            outcomes[i] = answer.Outcome == DeclaredProductOutcome.DeclaredProductInstalled
                && answer.RecordedPackages is { } recorded
                && IsNoneOf(candidate.FullPath, recorded)
                    ? DeclaredProductOutcome.DeclaredProductCachedAsAnotherFile
                    : answer.Outcome;
        }

        return outcomes;
    }

    /// <summary>
    /// What Windows holds for one declared product code, asked once per code per
    /// pass.
    /// </summary>
    private DeclarationAnswer Ask(
        string code, PassAnswers pass, Func<string, bool?>? namesAFileInInstallerFolder)
    {
        var resolved = pass.InstancesOf(code);

        // THE ORDER OF THE ARMS IS THE WHOLE OF IT, and the unaskable one is
        // first because it is the one that reads as an answer if it is left
        // last. A call that could not be made has not shown the product to be
        // absent, and treating "no answer" as "no product" would offer the
        // file on the strength of a question that was never really put.
        if (resolved.Unaskable)
            return new DeclarationAnswer(DeclaredProductOutcome.Unestablished, null);

        if (resolved.Instances.Count == 0)
            return new DeclarationAnswer(DeclaredProductOutcome.DeclaredProductNotInstalled, null);

        return new DeclarationAnswer(
            DeclaredProductOutcome.DeclaredProductInstalled,
            PackagesOpenedBy(code, resolved.Instances, namesAFileInInstallerFolder));
    }

    /// <summary>
    /// The identity of every file an installation of <paramref name="code"/> opens as
    /// its package: the cached package each installation records, and the original
    /// package at each folder on its source list that holds one. Null where any of
    /// them cannot be seen.
    ///
    /// NULL IS THE ANSWER THAT KEEPS THE FILE, and every way an installation's package
    /// can fail to be seen reaches it: a <c>LocalPackage</c> read that failed or came
    /// back empty, a value that names nothing, names a folder, will not open to an
    /// identity, or names a file that does not declare <paramref name="code"/>; and
    /// any source the check cannot rule out, which <see cref="AddSourcePackages"/>
    /// sets out. One such installation is enough, because its package is the one this
    /// candidate could be.
    /// </summary>
    private IReadOnlyList<FileIdentity>? PackagesOpenedBy(
        string code,
        IReadOnlyList<(string? Sid, MsiInstallContext Context)> instances,
        Func<string, bool?>? namesAFileInInstallerFolder)
    {
        if (_fileIdentities is null || _fileSystem is null) return null;

        var identities = new List<FileIdentity>(instances.Count);
        foreach (var (sid, context) in instances)
        {
            var read = InstallerQueryService.ReadProductProperty(
                _msi, code, sid, context, MsiInstallProperty.LocalPackage);
            if (read.Unreadable) return null;

            var path = read.Value.TrimEnd('\0');
            if (path.Length == 0) return null;

            // File.Exists is false for a folder and for a path that will not parse,
            // and the identity read below opens folders too, so this is what keeps a
            // value naming a folder from standing in for a package.
            if (!_fileSystem.File.Exists(path)) return null;

            if (_fileIdentities.ReadOutcome(path, out var recorded) != FileIdentityRead.Read)
                return null;

            // THE RECORDED PACKAGE HAS TO DECLARE THE SAME PRODUCT. The Windows Installer
            // record says which file the installation uses and this reads the file
            // itself, so the verdict rests on the two agreeing: a value naming a file that
            // is not this product's package shows nothing about where the package is, and
            // keeps the candidate.
            var declared = _identityReader.Read(path, isPatch: false, out _);
            if (declared is null
                || declared.Value.IsPatch
                || !string.Equals(declared.Value.Code, code, StringComparison.Ordinal))
                return null;

            identities.Add(recorded);

            if (!AddSourcePackages(code, isPatch: false, sid, context, namesAFileInInstallerFolder, identities))
                return null;
        }

        return identities;
    }

    /// <summary>
    /// Adds to <paramref name="opened"/> the identity of the original package at each
    /// folder on one network source list, a product's for one installation or a
    /// patch's in one account and context, and answers false where those sources
    /// cannot be ruled out.
    ///
    /// WHAT IT READS, AND WHY THAT IS ALL. When Windows Installer needs a product's
    /// original package rather than its cached copy, a repair among other things, it
    /// looks for the file named by <c>PackageName</c> in the folders on the product's
    /// source list, starting with the last one it used, which is itself an entry on
    /// that list. A patch has a source list and a package name of its own, and this
    /// reads them as it reads a product's. The network sources are the ones that are
    /// folders; the others are web addresses and removable media. So the package name
    /// and the network sources between them name every file a source can be.
    ///
    /// FALSE, WHICH KEEPS THE FILE, for: no way to compare against the Installer
    /// folder; a package name or a source list that will not read; an empty package
    /// name; a source entry holding a null, one that will not expand and one still
    /// holding a '%' once expanded; a source whose package would be a file directly in
    /// the Installer folder, or where that cannot be established; and a source package
    /// that exists and will not identify. A source package that is not there is
    /// skipped, being no file.
    /// </summary>
    /// <param name="isPatch">
    /// Whether <paramref name="code"/> is a patch code rather than a product code. The
    /// source-list calls are told which.
    /// </param>
    private bool AddSourcePackages(
        string code,
        bool isPatch,
        string? sid,
        MsiInstallContext context,
        Func<string, bool?>? namesAFileInInstallerFolder,
        List<FileIdentity> opened)
    {
        if (namesAFileInInstallerFolder is null || _fileIdentities is null) return false;

        // A patch's package name is read off its source list, MsiGetPatchInfoEx not
        // taking the property.
        var name = isPatch
            ? InstallerQueryService.ReadSourceListProperty(
                _msi, code, sid, context, MsiSourceListOptions.Patch, MsiInstallProperty.PackageName)
            : InstallerQueryService.ReadProductProperty(
                _msi, code, sid, context, MsiInstallProperty.PackageName);
        if (name.Unreadable) return false;

        var packageName = name.Value.TrimEnd('\0');
        if (packageName.Length == 0) return false;

        var sources = NetworkSourcesOf(code, isPatch, sid, context);
        if (sources is null) return false;

        foreach (var entry in sources)
        {
            // Expanded the way the recorded cached-package path is, so a folder spelled
            // with a variable is compared as the folder it names. Where the source
            // points is not known, and the copy is kept, for an entry holding a null,
            // which the expansion would cut short; for one that will not expand; and
            // for one still holding a '%' once expanded, as a variable that is not set
            // leaves it.
            if (entry.Contains('\0')) return false;
            string folder;
            try
            {
                folder = InstallerCacheHelpers.ExpandRecordedPath(entry);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                return false;
            }
            if (folder.Contains('%')) return false;

            // Joined with a backslash by hand rather than with Path.Combine, whose
            // separator is the host's.
            var package = folder.EndsWith('\\') ? folder + packageName : folder + '\\' + packageName;

            // A source in the Installer folder keeps every copy of the product or the
            // patch, not only the one it names: the folder it was installed or applied
            // from is the cache itself.
            if (namesAFileInInstallerFolder(package) is not false) return false;

            switch (_fileIdentities.ReadOutcome(package, out var identity))
            {
                case FileIdentityRead.Read:
                    opened.Add(identity);
                    break;
                case FileIdentityRead.NamesNothing:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The folders on one network source list, a product's or a patch's as
    /// <paramref name="isPatch"/> says, in the order Windows lists them, or null where
    /// the list did not read to its end.
    ///
    /// ONLY <see cref="MsiError.NoMoreItems"/> ENDS THE LIST. Every other return keeps
    /// the file, and so does a list that runs past <see cref="MaxSourceIndex"/> without
    /// ending, since what lies beyond it is unread.
    /// </summary>
    private IReadOnlyList<string>? NetworkSourcesOf(
        string code, bool isPatch, string? sid, MsiInstallContext context)
    {
        var options = (isPatch ? MsiSourceListOptions.Patch : MsiSourceListOptions.Product)
            | MsiSourceListOptions.Network;
        var sources = new List<string>();

        for (uint index = 0; index < MaxSourceIndex; index++)
        {
            uint length = 0;
            var error = _msi.EnumSources(code, sid, context, options, index, null, ref length);
            if (error == MsiError.NoMoreItems) return sources;
            if (error != MsiError.Success && error != MsiError.MoreData) return null;

            length++; // space for the terminator
            var buffer = new char[length];
            error = _msi.EnumSources(code, sid, context, options, index, buffer, ref length);
            if (error != MsiError.Success) return null;

            var source = new string(buffer, 0, (int)Math.Min(length, (uint)buffer.Length)).TrimEnd('\0');
            if (source.Length == 0) return null;
            sources.Add(source);
        }

        return null;
    }

    /// <summary>
    /// How many entries of one source list are read before the list is taken as not
    /// having ended. A source list holds a handful of folders; the bound is there so an
    /// answer that never reports an end cannot hold the scan.
    /// </summary>
    private const uint MaxSourceIndex = 1024;

    /// <summary>
    /// The verdict for one patch copy: whether Windows holds a registration of the patch
    /// it declares, and if so whether this file is shown to be a different file from
    /// every copy each registration opens, cached or original.
    /// </summary>
    private DeclaredProductOutcome ScreenPatch(
        string path,
        PassAnswers pass,
        Action<Exception, string>? recordRefusal,
        Func<string, bool?>? namesAFileInInstallerFolder)
    {
        var identity = _identityReader.Read(path, isPatch: true, out var detail);

        // FOUR READINGS LEAVE NOTHING TO ASK ABOUT, and each of them is the file
        // failing to give this pass a patch code and the products to put it to. Null is
        // the reader's own "nothing here to ask". An empty code is the same outcome
        // reached without a null, which the seam's do-nothing implementations produce.
        // A reading not marked as a patch did not answer the question asked. And a
        // patch naming no product gives the keyed patch read no installation to ask.
        if (identity is null
            || identity.Value.Code.Length == 0
            || !identity.Value.IsPatch
            || identity.Value.TargetProductCodes.Count == 0)
        {
            // ONLY THE NULL READING HAS A DETAIL TO KEEP, for the reason given at the
            // product half's arm: the other three are answers the reader gave rather
            // than failures it had, and it wrote nothing down about them.
            if (identity is null)
                recordRefusal?.Invoke(
                    new InvalidOperationException(
                        "A cached patch did not yield the patch code and target products it "
                        + "declares, so it is kept rather than offered. Reader detail: "
                        + (detail.Length == 0 ? "none given" : detail) + "."),
                    detail);

            return DeclaredProductOutcome.DeclaredPatchUnestablished;
        }

        var code = identity.Value.Code;
        var targets = identity.Value.TargetProductCodes;

        // KEYED BY THE TARGET LIST AS WELL AS THE CODE. The keyed patch read asks the
        // products the file names, so two files carrying one patch code with different
        // lists put different questions, and each is answered from its own list.
        var key = code + "|" + string.Join(";", targets);
        if (!pass.Patches.TryGetValue(key, out var answer))
        {
            answer = AskAboutPatch(code, targets, pass, namesAFileInInstallerFolder);
            pass.Patches[key] = answer;
        }

        // As for a product: the registrations are shared by every copy declaring the
        // patch, and whether the copies they open are OTHER files is asked per file.
        return answer.Outcome == DeclaredProductOutcome.DeclaredPatchRegistered
            && answer.RecordedPackages is { } recorded
            && IsNoneOf(path, recorded)
                ? DeclaredProductOutcome.DeclaredPatchCachedAsAnotherFile
                : answer.Outcome;
    }

    /// <summary>
    /// What Windows holds for one declared patch, asked once per patch code and target
    /// list per pass: every registration the machine-wide patch enumeration lists for
    /// the code, unioned with every installation of a named target product that
    /// answers the keyed patch read with a state.
    ///
    /// THE UNION IS WHY IT IS BOTH. The enumeration names a registration against a
    /// product the patch's Template does not list; the keyed read reaches an
    /// installation of a listed product the enumeration does not name. Each can only
    /// add a registration.
    ///
    /// AND EITHER FAILING KEEPS THE FILE. An enumeration that did not run to its end, a
    /// named product whose installations would not list, and an installation that would
    /// not answer the keyed read each leave registrations unfound, and the answer is
    /// <see cref="DeclaredProductOutcome.DeclaredPatchUnestablished"/>. The enumeration
    /// is walked once per pass, so where it fails, every patch copy the pass asks about
    /// is kept.
    /// </summary>
    private DeclarationAnswer AskAboutPatch(
        string code,
        IReadOnlyList<string> targets,
        PassAnswers pass,
        Func<string, bool?>? namesAFileInInstallerFolder)
    {
        var holders = pass.PatchHolders;
        if (holders is null)
            return new DeclarationAnswer(DeclaredProductOutcome.DeclaredPatchUnestablished, null);

        var registrations = new List<(string ProductCode, string? Sid, MsiInstallContext Context)>();
        if (holders.TryGetValue(code, out var listed)) registrations.AddRange(listed);

        foreach (var target in targets)
        {
            pass.CancellationToken.ThrowIfCancellationRequested();

            var resolved = pass.InstancesOf(target);
            if (resolved.Unaskable)
                return new DeclarationAnswer(DeclaredProductOutcome.DeclaredPatchUnestablished, null);

            foreach (var (sid, context) in resolved.Instances)
            {
                // Already a registration: the enumeration listed it, and its copy is
                // read below whatever the keyed read would say.
                if (IsListed(registrations, target, sid, context)) continue;

                var state = InstallerQueryService.GetPatchProperty(
                    _msi, code, target, sid, context, MsiInstallProperty.State);

                // ONLY THE INSTALLATION ANSWERING THAT IT HOLDS NO RECORD OF THE PATCH IS
                // SKIPPED, and that answer is read first because it is marked unreadable
                // as well, for the other readers of the same call. An answer that the
                // installation's product is not installed is not that answer: the keyed
                // product enumeration listed this installation moments earlier, in this
                // account and context, so it contradicts what the pass established and
                // keeps the file with every other read that did not answer.
                if (state.PatchNotHeld) continue;
                if (state.Unreadable)
                    return new DeclarationAnswer(DeclaredProductOutcome.DeclaredPatchUnestablished, null);

                registrations.Add((target, sid, context));
            }
        }

        if (registrations.Count == 0)
            return new DeclarationAnswer(DeclaredProductOutcome.DeclaredPatchNotRegistered, null);

        return new DeclarationAnswer(
            DeclaredProductOutcome.DeclaredPatchRegistered,
            CopiesOpenedBy(code, registrations, namesAFileInInstallerFolder));
    }

    /// <summary>
    /// Whether <paramref name="registrations"/> already holds the installation of
    /// <paramref name="productCode"/> in <paramref name="sid"/> and
    /// <paramref name="context"/>. Codes and accounts are compared without case, the
    /// enumeration and the product walk each handing back their own spelling; the
    /// context is compared exactly.
    /// </summary>
    private static bool IsListed(
        List<(string ProductCode, string? Sid, MsiInstallContext Context)> registrations,
        string productCode,
        string? sid,
        MsiInstallContext context)
    {
        foreach (var registration in registrations)
            if (registration.Context == context
                && string.Equals(registration.ProductCode, productCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(registration.Sid, sid, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// The identity of every file a registration of <paramref name="code"/> opens as the
    /// patch: the cached copy each registration records, and the patch package at each
    /// folder on the patch's source list in each registration's account and context.
    /// Null where any of them cannot be seen.
    ///
    /// NULL IS THE ANSWER THAT KEEPS THE FILE, as it is for
    /// <see cref="PackagesOpenedBy"/>, and every way a registration's copy can fail to
    /// be seen reaches it: a <c>LocalPackage</c> read that failed or came back empty, a
    /// value that names nothing, names a folder, will not open to an identity, or names
    /// a file that does not read as patch <paramref name="code"/>; and any source the
    /// check cannot rule out, which <see cref="AddSourcePackages"/> sets out. One such
    /// registration is enough, because its copy is the one this candidate could be.
    ///
    /// A READ ANSWERING THAT THE PATCH IS NOT THERE KEEPS THE FILE LIKE ANY OTHER
    /// FAILED READ. Every registration here was named by one of the two routes moments
    /// earlier, so that answer contradicts it, and which copy the registration records
    /// is then not known.
    ///
    /// Several registrations can record one copy, so each path is looked at once.
    /// </summary>
    private IReadOnlyList<FileIdentity>? CopiesOpenedBy(
        string code,
        IReadOnlyList<(string ProductCode, string? Sid, MsiInstallContext Context)> registrations,
        Func<string, bool?>? namesAFileInInstallerFolder)
    {
        if (_fileIdentities is null || _fileSystem is null) return null;

        var identities = new List<FileIdentity>(registrations.Count);
        var looked = new Dictionary<string, FileIdentity>(StringComparer.Ordinal);

        // ONE SOURCE LIST PER ACCOUNT AND CONTEXT, NOT ONE PER REGISTRATION. The
        // source-list calls take the patch code with an account and a context and no
        // product, so a patch registered against several products in one account has
        // one list there, read the first time a registration in it is reached. Accounts
        // are compared without case, as IsListed compares them.
        var sourceListsRead = new HashSet<(string? Sid, MsiInstallContext Context)>();

        foreach (var (productCode, sid, context) in registrations)
        {
            var read = InstallerQueryService.GetPatchProperty(
                _msi, code, productCode, sid, context, MsiInstallProperty.LocalPackage);
            if (read.Unreadable) return null;

            var path = read.Value.TrimEnd('\0');
            if (path.Length == 0) return null;

            if (!looked.TryGetValue(path, out var recorded))
            {
                // File.Exists is false for a folder and for a path that will not parse,
                // and the identity read below opens folders too, so this is what keeps a
                // value naming a folder from standing in for a copy.
                if (!_fileSystem.File.Exists(path)) return null;

                if (_fileIdentities.ReadOutcome(path, out recorded) != FileIdentityRead.Read)
                    return null;

                // THE RECORDED COPY HAS TO READ AS THE SAME PATCH, for the reason the
                // product half's recorded package has to declare the same product: the
                // verdict rests on the record and the file agreeing.
                var declared = _identityReader.Read(path, isPatch: true, out _);
                if (declared is null
                    || !declared.Value.IsPatch
                    || !string.Equals(declared.Value.Code, code, StringComparison.Ordinal))
                    return null;

                looked[path] = recorded;
            }

            identities.Add(recorded);

            if (sourceListsRead.Add((sid?.ToUpperInvariant(), context))
                && !AddSourcePackages(code, isPatch: true, sid, context, namesAFileInInstallerFolder, identities))
                return null;
        }

        return identities;
    }

    /// <summary>
    /// Whether the candidate at <paramref name="candidatePath"/> is a different file
    /// from every package an installation opens. A candidate whose own identity will
    /// not read is not shown to be different, so it answers false and is kept.
    /// </summary>
    private bool IsNoneOf(string candidatePath, IReadOnlyList<FileIdentity> recorded)
    {
        if (_fileIdentities is null) return false;
        if (_fileIdentities.ReadOutcome(candidatePath, out var candidate) != FileIdentityRead.Read)
            return false;

        foreach (var package in recorded)
            if (package == candidate) return false;

        return true;
    }

    /// <param name="Outcome">The verdict the declared code alone gives.</param>
    /// <param name="RecordedPackages">
    /// For an installed product, the identity of every file an installation opens as
    /// its package; for a registered patch, the identity of every file a registration
    /// opens as the patch. Null where any of them could not be seen, and null for every
    /// other verdict.
    /// </param>
    private readonly record struct DeclarationAnswer(
        DeclaredProductOutcome Outcome,
        IReadOnlyList<FileIdentity>? RecordedPackages);

    /// <summary>
    /// What one pass has asked Windows about installations and patch registrations,
    /// kept so that nothing is asked twice inside the pass and nothing outlives it.
    /// </summary>
    private sealed class PassAnswers
    {
        private readonly IMsiApi _msi;
        private readonly Dictionary<string, (IReadOnlyList<(string? Sid, MsiInstallContext Context)> Instances, bool Unaskable)>
            _instances = new(StringComparer.Ordinal);
        private Dictionary<string, List<(string ProductCode, string? Sid, MsiInstallContext Context)>>? _holders;
        private bool _holdersRead;

        internal PassAnswers(IMsiApi msi, CancellationToken cancellationToken)
        {
            _msi = msi;
            CancellationToken = cancellationToken;
        }

        internal CancellationToken CancellationToken { get; }

        /// <summary>Each declared patch's answer, keyed by patch code and target list.</summary>
        internal Dictionary<string, DeclarationAnswer> Patches { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Every installation of one product code, asked once per pass whichever half
        /// asks.
        /// </summary>
        internal (IReadOnlyList<(string? Sid, MsiInstallContext Context)> Instances, bool Unaskable)
            InstancesOf(string productCode)
        {
            if (!_instances.TryGetValue(productCode, out var resolved))
            {
                resolved = InstallerQueryService.ResolveProductInstances(_msi, productCode);
                _instances[productCode] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Every patch registration the machine-wide enumeration lists, keyed by patch
        /// code, or null where the enumeration did not run to its end. Walked the first
        /// time a patch asks, and not at all on a pass holding none.
        /// </summary>
        internal Dictionary<string, List<(string ProductCode, string? Sid, MsiInstallContext Context)>>? PatchHolders
        {
            get
            {
                if (!_holdersRead)
                {
                    _holders = InstallerQueryService.EnumeratePatchHoldersAcrossAllProducts(_msi, CancellationToken);
                    _holdersRead = true;
                }

                return _holders;
            }
        }
    }
}
