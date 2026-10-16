namespace InstallerClean.Services;

/// <summary>
/// Which part of an update failed, which decides the line the notice shows.
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
