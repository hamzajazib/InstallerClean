namespace InstallerClean.Services;

/// <summary>
/// Which part of an update failed, or what kept it from being tried, which decides the
/// line shown.
/// </summary>
public enum UpdateFailureStage
{
    /// <summary>
    /// Fetching the newer version: no answer, an error from the server, an answer that
    /// stopped coming, one too large, or one that is not a Windows program.
    /// </summary>
    Download,

    /// <summary>
    /// Putting the newer version in place: making the folder it is saved in, saving
    /// it, moving it into place or starting it.
    /// </summary>
    Install,

    /// <summary>
    /// Nothing was put in place because InstallerClean is in use: the single-instance
    /// mutex is held, or another process is running from the command line's own file.
    /// Only the command line's update gives this answer.
    /// </summary>
    InUse,
}

/// <summary>
/// An update that could not go ahead, and which part of it failed. Nothing has changed
/// on disk when one is thrown. The cause rides along as the inner exception for
/// crash.log; the screen shows only the line for the stage.
/// </summary>
public sealed class UpdateFailedException : Exception
{
    public UpdateFailedException(UpdateFailureStage stage, string message, Exception? inner = null)
        : base(message, inner)
    {
        Stage = stage;
    }

    public UpdateFailureStage Stage { get; }
}

/// <summary>
/// An update its release rules out for this copy (<see cref="UpdateManifest"/>), thrown
/// before anything is downloaded. Not a failure: a host says why with the line for
/// <see cref="Refusal"/> and gives the release page, and writes nothing to crash.log.
/// </summary>
public sealed class UpdateRefusedException : Exception
{
    public UpdateRefusedException(UpdateRefusal refusal, string version)
        : base($"The release of {version} rules this copy out: {refusal}.")
    {
        Refusal = refusal;
    }

    public UpdateRefusal Refusal { get; }
}
