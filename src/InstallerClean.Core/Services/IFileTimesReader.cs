namespace InstallerClean.Services;

/// <summary>
/// What <see cref="IFileTimesReader.ReadOutcome"/> established, with each way it can
/// fall short kept apart rather than collapsed into one <c>false</c>.
///
/// ONLY <see cref="Read"/> LETS A FILE THROUGH, and it means more than that the times
/// were read: the file is a plain file on a local, fixed NTFS volume. Every other
/// member keeps the file back. Nothing branches on which one it is; they are named
/// apart because they are different facts about a file, and a caller that ever
/// counts them should be able to.
/// </summary>
public enum FileTimesRead
{
    /// <summary>
    /// The times were read off a plain file on a local, fixed NTFS volume. The only
    /// member that fills the out value, and the only one a caller treats as an
    /// answer.
    /// </summary>
    Read,

    /// <summary>There was no string to open.</summary>
    NotAPath,

    /// <summary>
    /// Nothing is at the path: the file went between the walk and this read.
    /// </summary>
    NamesNothing,

    /// <summary>
    /// Something is at the path and no handle could be opened on it.
    /// </summary>
    OpenRefused,

    /// <summary>
    /// The handle landed on a reparse point or a directory rather than on a file.
    /// </summary>
    NotAPlainFile,

    /// <summary>
    /// The file system would not give the times, or gave a change time that is not
    /// a point in time.
    /// </summary>
    TimesUnavailable,

    /// <summary>
    /// The volume holding the file could not be named by a volume GUID path, or would
    /// not say which file system it carries.
    /// </summary>
    VolumeUnestablished,

    /// <summary>
    /// The volume answered, and it is not a local, fixed volume.
    /// </summary>
    NotAFixedVolume,

    /// <summary>
    /// The volume answered, and its file system is not NTFS.
    /// </summary>
    NotNtfs,

    /// <summary>
    /// The attempt threw. Distinct from every member above, each of which is the
    /// system answering rather than the call failing to complete.
    /// </summary>
    Faulted,
}

/// <summary>
/// The three times of a file that <see cref="CachedFileAge"/> reads, in UTC.
///
/// LAST ACCESS IS NOT AMONG THEM. On a volume that updates it, any read of the file
/// moves it, this app's own scan included, so it says nothing about when the file
/// was made.
/// </summary>
public readonly record struct FileTimes(
    DateTime CreationUtc,
    DateTime LastWriteUtc,
    DateTime ChangeUtc);

/// <summary>
/// Reads a file's times, and establishes that they are kept by NTFS on a local,
/// fixed volume, for <see cref="CachedFileAge"/> to judge the file's age by.
///
/// THE FILE SYSTEM IS PART OF THE ANSWER AND NOT A SEPARATE QUESTION. NTFS keeps a
/// change time of its own, set when the file's contents or attributes change; FAT32
/// and exFAT keep none. A time read off any volume but a local NTFS one is not taken
/// as an answer, and the reader says so rather than handing it back.
/// </summary>
public interface IFileTimesReader
{
    /// <summary>
    /// The times of the file <paramref name="path"/> names, with the answer named
    /// rather than collapsed. <see cref="FileTimesRead.Read"/> is the only outcome
    /// that fills <paramref name="times"/>.
    ///
    /// Links are NOT followed. A candidate is a file the walk found directly in the
    /// folder and the containment guard has already refused a reparse point, so a
    /// link here is one that appeared since, and it is reported as
    /// <see cref="FileTimesRead.NotAPlainFile"/> rather than read through.
    /// </summary>
    FileTimesRead ReadOutcome(string path, out FileTimes times);
}

/// <summary>
/// Whether a cached file has been shown to be at least a day old, which a file the
/// folder walk found must be before the scan offers it.
///
/// WHAT IT IS FOR. Windows Installer puts a new copy of a package into the folder
/// before any of its records names that copy, and while no record names it the copy
/// looks exactly like a spare. So a file is kept back unless its times show it was
/// created, written and changed a day or more before the scan.
///
/// THE AGE IS TAKEN FROM THE LATEST OF THREE TIMES, because a copy can take some of
/// its times from its source. Given its source's creation and last-write times, a
/// copy has a change time from when they were set; given its source's last-write and
/// change times, it has a creation time from when it was made. No one of the three is
/// the copy's own in both cases, and the latest of them is.
///
/// EVERYTHING BUT A POSITIVE ANSWER KEEPS THE FILE, written as the one case that
/// lets it through: a time that could not be read, a volume that is not local NTFS,
/// a time later than the scan's own clock and a time less than a day before it.
/// </summary>
public static class CachedFileAge
{
    /// <summary>How long ago a file must last have been created, written or changed.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

    /// <summary>
    /// The latest of the three times, which is the one the age is taken from.
    /// </summary>
    public static DateTime Latest(FileTimes times)
    {
        var latest = times.CreationUtc;
        if (times.LastWriteUtc > latest) latest = times.LastWriteUtc;
        if (times.ChangeUtc > latest) latest = times.ChangeUtc;
        return latest;
    }

    /// <summary>
    /// True only where <paramref name="outcome"/> is <see cref="FileTimesRead.Read"/>
    /// and the latest of the file's times is at least <see cref="MinimumAge"/> before
    /// <paramref name="scanClock"/>. A latest time after the clock is less than a day
    /// before it, so it keeps the file on the same test.
    /// </summary>
    public static bool ShownADayOld(FileTimesRead outcome, FileTimes times, DateTimeOffset scanClock) =>
        outcome == FileTimesRead.Read
        && scanClock.UtcDateTime - Latest(times) >= MinimumAge;
}
