namespace InstallerClean.Services;

/// <summary>
/// The PC's record that InstallerClean has had its first run: the value
/// <c>FirstRunRecorded</c> under <c>HKLM\SOFTWARE\NoFaff\InstallerClean</c>, one
/// for the whole PC, so every Windows account on it reads the same answer.
///
/// The window claims it as the PC's first card that writes a report takes the report box
/// (<c>CompletionViewModel.TakeReport</c>), whatever that card shows. It also sets it
/// where a Move or a Delete writes no report: a cancelled or stopped run that moved or
/// deleted a file, a finished run that did after the window was asked to close, and any
/// failure in the Move or Delete that none of the window's own arms foresaw, how far
/// that run got being unknown. The window's start check
/// (<see cref="IEarlierRunCheck"/>) sets it where it finds an earlier run, and where it
/// has not answered within the time a card waits for it.
///
/// The command line sets the mark after a Move or a Delete whose service counts a file
/// moved or deleted, and where the service fails before its count comes back with an
/// exception none of the command line's other catches takes. A run at a console that
/// finishes on a PC with no earlier run claims it as it carries its report to the question
/// (<see cref="ConsoleReportOffer"/>), whatever it moved or deleted. Nothing removes the
/// mark, and the installer's uninstall leaves it in place.
/// </summary>
public interface IFirstRunMark
{
    /// <summary>
    /// Whether the mark is set. Any value under the name answers
    /// <see cref="FirstRunMarkState.Set"/>, whatever its type or number: the app
    /// writes only a DWORD of 1, and a value somebody wrote by hand, a 0 or a string
    /// included, still reads as a first run that has happened.
    /// </summary>
    FirstRunMarkState Read();

    /// <summary>
    /// Sets the mark. A write that fails is logged to crash.log and nothing is
    /// thrown, so a caller needs no guard of its own.
    /// </summary>
    void Set();

    /// <summary>
    /// Takes the PC's one report: true for the one caller that finds the mark clear and
    /// sets it, false for every other. The mark is read afresh, never from the answer the
    /// start check gave, under a short-held machine-wide lock, so a window and a command
    /// line run that both found the PC clear at their starts cannot both take the report.
    /// A mark this same instance created earlier counts as clear, once: a command-line
    /// Delete or Move that moved or deleted a file has set it by the time it claims the
    /// report of that run. False where the mark was created by anything else, where the
    /// lock cannot be had within a short wait, where the mark cannot be read, where the
    /// write does not read back as set, and on any call after a claim was granted: no
    /// report is carried then. Never throws.
    /// </summary>
    bool TryClaim();
}

/// <summary>What <see cref="IFirstRunMark.Read"/> found.</summary>
public enum FirstRunMarkState
{
    /// <summary>
    /// The read itself failed, so nothing was established. First member so that it is
    /// what the type's own zero carries, for the reason
    /// <see cref="RegistryKeyPresence.Unreadable"/> is first.
    /// </summary>
    Unreadable,

    /// <summary>A value is there under the name.</summary>
    Set,

    /// <summary>The key or the value is not there.</summary>
    NotSet,
}
