using InstallerClean.Models;

namespace InstallerClean.Services;

/// <summary>
/// Reads and writes <see cref="AppSettings"/>. Persistence uses
/// write-temp-then-rename so a crash mid-save can't leave a half-
/// written settings.json. A corrupt file detected during
/// <see cref="Load"/> is renamed to <c>settings.json.bad</c> before
/// the loader returns defaults. Both Load and TrySave open via
/// <c>StorageHelpers.OpenAtomic</c> so a symlink at the settings
/// file path can't redirect the read or write under elevation.
/// </summary>
public interface ISettingsService
{
    /// <summary>Read settings.json. Returns defaults on failure; never throws.</summary>
    AppSettings Load();

    /// <summary>
    /// Reads settings.json and says whether it was read. True with defaults where
    /// there is no file; false with defaults where a file is there and could not be
    /// read, which is not a fresh account and must not be written back as one. Takes
    /// the same lock as <see cref="Update"/>, so it never lands while this process
    /// is saving. Never throws.
    /// </summary>
    bool TryLoad(out AppSettings settings);

    /// <summary>Persist settings. Returns true on success; never throws.</summary>
    bool TrySave(AppSettings settings);

    /// <summary>
    /// Serialises a read-modify-write under a private lock so concurrent
    /// writers cannot lose each other's change to the last-writer-wins rename:
    /// the debounced destination save and the report's saves run on thread-pool
    /// threads while the language pick runs on the dispatcher.
    /// Loads, applies <paramref name="mutate"/>, saves. Returns the TrySave
    /// result; never throws.
    /// </summary>
    bool Update(Action<AppSettings> mutate);
}
