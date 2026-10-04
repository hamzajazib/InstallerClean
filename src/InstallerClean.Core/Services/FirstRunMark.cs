using System.Security;
using InstallerClean.Helpers;
using Microsoft.Win32;

namespace InstallerClean.Services;

/// <summary>
/// Production <see cref="IFirstRunMark"/>: a DWORD under HKLM in the 64-bit view.
/// A read that fails in one of the four ways <see cref="RegistryReader"/> reports
/// answers <see cref="FirstRunMarkState.Unreadable"/>.
/// </summary>
internal sealed class FirstRunMark : IFirstRunMark
{
    internal const string KeyPath = @"SOFTWARE\NoFaff\InstallerClean";
    internal const string ValueName = "FirstRunRecorded";

    private readonly RegistryHive _hive;
    private readonly string _keyPath;

    public FirstRunMark() : this(RegistryHive.LocalMachine, KeyPath) { }

    /// <summary>
    /// Test seam: points the read and the write at another hive and key, so a test
    /// can work on a scratch key under HKCU, which needs no administrator rights,
    /// and delete it afterwards.
    /// </summary>
    internal FirstRunMark(RegistryHive hive, string keyPath)
    {
        _hive = hive;
        _keyPath = keyPath;
    }

    public FirstRunMarkState Read()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var key = hive.OpenSubKey(_keyPath);
            if (key is null) return FirstRunMarkState.NotSet;

            // Not expanded and not converted: whatever is stored under the name, of
            // any type, is enough to answer.
            var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null ? FirstRunMarkState.NotSet : FirstRunMarkState.Set;
        }
        catch (SecurityException) { return FirstRunMarkState.Unreadable; }
        catch (IOException) { return FirstRunMarkState.Unreadable; }
        catch (UnauthorizedAccessException) { return FirstRunMarkState.Unreadable; }
        catch (ObjectDisposedException) { return FirstRunMarkState.Unreadable; }
    }

    public void Set()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            // Creates the key where it is missing and opens it for writing where it is
            // there. A refusal throws; null comes back only where Windows reports
            // success and hands back no key.
            using var key = hive.CreateSubKey(_keyPath, writable: true);
            if (key is null)
            {
                CrashLog.TryWrite(new IOException(
                    "The key for the first-run mark could not be created or opened."));
                return;
            }
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            // Nothing is thrown from here. The command line sets the mark after its
            // files have gone and before it writes the run's summary entry, and a throw
            // would reach its catch-all and write a failure entry in that one's place.
            CrashLog.TryWrite(ex);
        }
    }
}
