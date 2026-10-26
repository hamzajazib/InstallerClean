using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean.Services;

/// <summary>
/// The command line's replace-only mode: a newer command line, started by the newer run of
/// the command once that run has ended, putting itself in place of the copy it ran for.
/// The setup's copy is replaced by the newer setup (<see cref="SetupReplacement"/>), and
/// every other copy by this exe (<see cref="ExeReplacement"/>). Nothing is started at the
/// path afterwards.
/// </summary>
/// <remarks>
/// Nobody is at a console to read what happens here, so a replace that did not put this
/// version in place writes a 3007 to the Application log after crash.log, saying which,
/// and writes the same notice on every run for as long as the replace keeps failing. A
/// replace that did writes nothing: the older copy's notice already named this version.
/// A 3007 is not a run's entry, so it never stands for one.
/// </remarks>
internal static class CommandLineReplacement
{
    /// <summary>
    /// Carries out <paramref name="request"/>, a request carrying no command, and answers
    /// whether this version is now at its path. Never throws.
    /// </summary>
    public static bool Run(ReplaceRequest request)
    {
        try
        {
            if (AppCopy.DetectRunningCommandLine(request.ExePath, new RegistryReader()) == AppCopyKind.Setup)
            {
                var outcome = SetupReplacement.Run(request, out var exitCode);
                if (NoticeFor(outcome, exitCode) is { } setupNotice) WriteNotice(setupNotice);
                return outcome == SetupReplaceOutcome.Installed;
            }
            var replaced = ExeReplacement.Run(request, startAtPath: false);
            if (NoticeFor(replaced) is { } notice) WriteNotice(notice);
            return replaced == ReplaceOutcome.Replaced;
        }
        catch (Exception ex)
        {
            // Every step that can throw here comes before anything at the path is changed:
            // the replace and the setup's run each answer rather than throw.
            CrashLog.TryWrite(ex);
            WriteNotInstalled();
            return false;
        }
    }

    /// <summary>
    /// Writes the 3007 saying this version was not installed and the copy it was to replace
    /// was left as it was. Never throws.
    /// </summary>
    internal static void WriteNotInstalled() => WriteNotice(() => Format(Strings.Cli_EventLogUpdateNotInstalled));

    /// <summary>
    /// The 3007 for <paramref name="outcome"/>, a replace that was not started at the path, or
    /// null where none is written: the replace put this version there, or found nothing at the
    /// path, which <see cref="ExeReplacement.Run(ReplaceRequest, bool)"/> writes its own notice for.
    /// </summary>
    internal static Func<string>? NoticeFor(ReplaceOutcome outcome) => outcome switch
    {
        ReplaceOutcome.Replaced or ReplaceOutcome.NothingAtPath => null,
        ReplaceOutcome.NotHandedOver or ReplaceOutcome.OldCopyNotSeenToEnd or ReplaceOutcome.Refused =>
            () => Format(Strings.Cli_EventLogUpdateNotInstalled),
        _ => () => Format(Strings.Cli_EventLogUpdateStoppedPartWay),
    };

    /// <summary>
    /// The 3007 for <paramref name="outcome"/> on the setup's copy, with the setup's
    /// <paramref name="exitCode"/> where it was read, or null where the setup put this version
    /// in place. A setup that ran and whose exit code is not known is written as a replace
    /// that stopped part way.
    /// </summary>
    internal static Func<string>? NoticeFor(SetupReplaceOutcome outcome, int? exitCode) => outcome switch
    {
        SetupReplaceOutcome.Installed => null,
        SetupReplaceOutcome.NotThisVersion => () => Format(Strings.Cli_EventLogUpdateSetupNotThisVersion),
        SetupReplaceOutcome.InUse => () => Format(Strings.Cli_EventLogUpdateInUseAfterWait),
        SetupReplaceOutcome.SetupFailed when exitCode is { } code => () => Format(Strings.Cli_EventLogUpdateSetupExitCode, code),
        SetupReplaceOutcome.SetupFailed => () => Format(Strings.Cli_EventLogUpdateStoppedPartWay),
        SetupReplaceOutcome.NotInPlace => () => Format(Strings.Cli_EventLogUpdateSetupNotInPlace),
        _ => () => Format(Strings.Cli_EventLogUpdateNotInstalled),
    };

    /// <summary><paramref name="line"/> with this version and <paramref name="more"/> in its placeholders, in English.</summary>
    private static string Format(string line, params object[] more) =>
        string.Format(CultureInfo.InvariantCulture, line, [SetupReplacement.ThisVersion(), .. more]);

    /// <summary>Writes <paramref name="line"/> to the Application log as a 3007, in English whatever the display language.</summary>
    private static void WriteNotice(Func<string> line) =>
        EventLogWriter.Write(CliEventClass.UpdateNotInstalledNotice, () => MachineCulture.English(line));
}
