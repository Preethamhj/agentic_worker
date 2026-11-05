using System.Security.Principal;

namespace Jarvis.Tools;

/// <summary>Checks the current process token using the Windows administrator role.</summary>
public sealed class WindowsElevationChecker : IElevationChecker
{
    public bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
