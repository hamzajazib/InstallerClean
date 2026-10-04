namespace InstallerClean.Services;

/// <summary>
/// The Country or region set in Windows' settings for the account running the app.
/// </summary>
public interface IWindowsRegion
{
    /// <summary>
    /// The setting as Windows gives it: an ISO 3166-1 two-letter code, a UN M.49
    /// number where the location has no such code, or null where it could not be
    /// read. Never throws.
    /// </summary>
    string? Read();
}
