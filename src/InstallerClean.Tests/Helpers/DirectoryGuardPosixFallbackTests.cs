using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A delete through a handle tries the older disposition only where the POSIX form is
/// answered with a code that says the form is not taken. Every other answer, a refusal
/// of access included, stays a refusal.
/// </summary>
public class DirectoryGuardPosixFallbackTests
{
    [Theory]
    [InlineData(Kernel32.ERROR_INVALID_PARAMETER, true)]
    [InlineData(Kernel32.ERROR_NOT_SUPPORTED, true)]
    [InlineData(Kernel32.ERROR_INVALID_FUNCTION, true)]
    [InlineData(5, false)]    // ERROR_ACCESS_DENIED
    [InlineData(32, false)]   // ERROR_SHARING_VIOLATION
    [InlineData(145, false)]  // ERROR_DIR_NOT_EMPTY
    [InlineData(1224, false)] // ERROR_USER_MAPPED_FILE
    [InlineData(0, false)]
    public void Only_the_codes_that_say_the_POSIX_form_is_not_taken_fall_back(int error, bool fallsBack)
    {
        Assert.Equal(fallsBack, DirectoryGuard.PosixFormIsNotTaken(error));
    }
}
