namespace InstallerClean.Services;

/// <summary>
/// Which step of the write probe refused a folder, and whether Windows refused it access
/// there, so a caller names a cause only where the probe established one.
/// </summary>
public enum WriteProbeRefusal
{
    /// <summary>The probe passed.</summary>
    None,

    /// <summary>The folder itself could not be opened.</summary>
    FolderOpen,

    /// <summary>Windows refused the account the right to add the probe's file.</summary>
    CreateRefusedAccess,

    /// <summary>Adding the probe's file failed for another cause.</summary>
    CreateFailed,

    /// <summary>The folder took the probe's file and refused access to it when it was read back.</summary>
    ReadBackRefusedAccess,

    /// <summary>The folder took the probe's file and reading it back failed for another cause.</summary>
    ReadBackFailed,
}
