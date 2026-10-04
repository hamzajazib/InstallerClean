namespace InstallerClean.Services;

/// <summary>
/// The command line's own record of its runs, read from the entries it writes to the
/// Application log under its own source: whether any of them is a Move or a Delete
/// that moved or deleted at least one file.
/// </summary>
public interface ICommandLineRunRecord
{
    /// <summary>
    /// Reads the log, newest entry first, and stops at the first Move or Delete that
    /// acted on a file. Never throws: a log that cannot be read answers
    /// <see cref="CommandLineRunRecordState.Unreadable"/>.
    /// </summary>
    CommandLineRunRecordState Read();
}

/// <summary>What <see cref="ICommandLineRunRecord.Read"/> found.</summary>
public enum CommandLineRunRecordState
{
    /// <summary>
    /// The log could not be read, so nothing was established. First member so that it
    /// is what the type's own zero carries, for the reason
    /// <see cref="RegistryKeyPresence.Unreadable"/> is first.
    /// </summary>
    Unreadable,

    /// <summary>An entry records a Move or a Delete that moved or deleted at least one file.</summary>
    ActedOnFiles,

    /// <summary>The log was read and no entry records one.</summary>
    NothingActedOn,
}
