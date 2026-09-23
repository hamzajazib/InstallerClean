using System.IO.Abstractions;
using InstallerClean.Interop;
using InstallerClean.Models;

namespace InstallerClean.Services;

/// <summary>
/// Production <see cref="IDeclaredProductCheck"/>: reads each candidate
/// installation package's own ProductCode through
/// <see cref="IPackageIdentityReader"/>, puts it to Windows through the same
/// keyed enumeration the patch-target route uses, and for an installed product
/// reads the <c>LocalPackage</c> each installation records.
///
/// IT COMPOSES THINGS THAT ALREADY EXIST. The reading is the reader's, which has
/// always been able to take the product reading and has only ever been asked for
/// the patch one. The asking is
/// <see cref="InstallerQueryService.ResolveProductInstances"/> and
/// <see cref="InstallerQueryService.ReadProductProperty"/>, shared rather than
/// copied because the part of them that decides anything is which returns are
/// allowed to mean "not installed" or "no value": those allowlists are the
/// difference between a file kept and a file offered, and a second copy of them is
/// a second thing to keep right. The comparison of recorded packages against the
/// candidate is <see cref="IFileIdentityReader"/>, the reader the scan's own
/// path comparison uses.
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
    /// <see cref="DeclaredProductOutcome.DeclaredProductInstalled"/>. That is the
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
        Action<Exception, string>? recordRefusal = null)
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
        var asked = new Dictionary<string, ProductAnswer>(StringComparer.Ordinal);

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates[i];

            // THE RESTRICTION, AND IT IS ENFORCED HERE RATHER THAN AT THE CALL
            // SITE ON PURPOSE. Asked of a patch this question keeps back every
            // registered superseded patch on every machine for ever: Windows
            // holds a record of such a patch's code by construction, that being
            // what superseded means, so the keeping arm would be true of the
            // whole class. A caller that passes the whole candidate list in gets
            // the patches back untouched instead of screening them by accident.
            if (candidate.IsPatch)
            {
                outcomes[i] = DeclaredProductOutcome.NotAProductPackage;
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
                answer = Ask(code);
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
    private ProductAnswer Ask(string code)
    {
        var resolved = InstallerQueryService.ResolveProductInstances(_msi, code);

        // THE ORDER OF THE ARMS IS THE WHOLE OF IT, and the unaskable one is
        // first because it is the one that reads as an answer if it is left
        // last. A call that could not be made has not shown the product to be
        // absent, and treating "no answer" as "no product" would offer the
        // file on the strength of a question that was never really put.
        if (resolved.Unaskable)
            return new ProductAnswer(DeclaredProductOutcome.Unestablished, null);

        if (resolved.Instances.Count == 0)
            return new ProductAnswer(DeclaredProductOutcome.DeclaredProductNotInstalled, null);

        return new ProductAnswer(
            DeclaredProductOutcome.DeclaredProductInstalled,
            RecordedPackagesOf(code, resolved.Instances));
    }

    /// <summary>
    /// The identity of the cached package every installation of <paramref name="code"/>
    /// records, one per installation, or null where any one of them cannot be read.
    ///
    /// NULL IS THE ANSWER THAT KEEPS THE FILE, and every way an installation's package
    /// can fail to be seen reaches it: a <c>LocalPackage</c> read that failed or came
    /// back empty, a value that names nothing, names a folder, will not open to an
    /// identity, or names a file that does not declare <paramref name="code"/>. One
    /// such installation is enough, because its package is the one this candidate
    /// could be.
    /// </summary>
    private IReadOnlyList<FileIdentity>? RecordedPackagesOf(
        string code,
        IReadOnlyList<(string? Sid, MsiInstallContext Context)> instances)
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
        }

        return identities;
    }

    /// <summary>
    /// Whether the candidate at <paramref name="candidatePath"/> is a different file
    /// from every recorded package. A candidate whose own identity will not read is
    /// not shown to be different, so it answers false and is kept.
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

    /// <param name="Outcome">The verdict the product code alone gives.</param>
    /// <param name="RecordedPackages">
    /// For an installed product, the identity of the package each installation
    /// records, or null where any installation's package could not be identified.
    /// Null for every other verdict.
    /// </param>
    private readonly record struct ProductAnswer(
        DeclaredProductOutcome Outcome,
        IReadOnlyList<FileIdentity>? RecordedPackages);
}
