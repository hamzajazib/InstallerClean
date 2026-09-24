namespace InstallerClean.Services;

/// <summary>
/// What <see cref="IInstallerInProgressMarker.Read"/> found at the marker's path.
///
/// ONLY <see cref="Absent"/> IS A CLEAR READING. <see cref="Present"/> IS THE ZERO, so
/// a value nobody set, a test double's default included, keeps files back rather
/// than clearing the way.
/// </summary>
public enum InstallerInProgressMarkerReading
{
    /// <summary>
    /// Something is at the marker's path. A file another process holds open without
    /// sharing it, which is what a sharing or lock violation on the read means, is a
    /// file that is there, and reads as this.
    /// </summary>
    Present,

    /// <summary>Nothing is at the marker's path.</summary>
    Absent,

    /// <summary>
    /// Windows refused the read, so whether the marker is there was not established.
    /// </summary>
    AccessRefused,
}

/// <summary>
/// Whether Windows Installer's in-progress marker, <c>inprogressinstallinfo.ipi</c>
/// in the Installer folder, is there.
///
/// WHAT IT IS FOR. Windows Installer writes the file while an installation is under
/// way and removes it at the end. Across a transaction of several packages it stays
/// for the whole transaction, including the stretches between packages when
/// <c>Global\_MSIExecute</c> is free, and those are stretches in which a rollback at
/// the end of the transaction can still put the records back as they were. So the
/// marker being there is a reason not to act on anything the records' present state
/// says.
///
/// NOTHING HERE SAYS WHY THE FILE IS THERE. The file being present is the whole of
/// what is read, and it is Windows Installer's own record of an installation it has
/// not finished.
/// </summary>
public interface IInstallerInProgressMarker
{
    /// <summary>
    /// Reads the marker's path once.
    ///
    /// THROWS ON ANY FAILURE THE THREE READINGS DO NOT NAME, and that is the
    /// contract rather than an omission. A read that failed some other way has
    /// established nothing, and no sentence the app has is true of it, so it goes to
    /// the error path a caller already has for a failure it did not foresee, which
    /// reports the failure and acts on nothing.
    /// </summary>
    InstallerInProgressMarkerReading Read();
}
