namespace InstallerClean.Services;

/// <summary>
/// The PC's record that InstallerClean has had its first run: the value
/// <c>FirstRunRecorded</c> under <c>HKLM\SOFTWARE\NoFaff\InstallerClean</c>, one
/// for the whole PC, so every Windows account on it reads the same answer.
///
/// The window sets it as the PC's first card that writes a report takes the report box
/// (<c>CompletionViewModel.TakeReport</c>), whatever that card shows. It also sets it
/// where a Move or a Delete writes no report: a cancelled or stopped run that moved or
/// deleted a file, and any failure in the Move or Delete that none of the window's own
/// arms foresaw, how far that run got being unknown. The window's start check
/// (<see cref="IEarlierRunCheck"/>) sets it where it finds an earlier run, and where it
/// has not answered within the time a card waits for it.
///
/// The command line writes no report. It sets the mark after a Move or a Delete whose
/// service counts a file moved or deleted, and where the service fails before its count
/// comes back with an exception none of the command line's other catches takes. Nothing
/// removes the mark, and the installer's uninstall leaves it in place.
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
