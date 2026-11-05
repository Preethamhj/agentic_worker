using Jarvis.Tools;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class NonElevatedFactAttribute : FactAttribute
{
    public NonElevatedFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows process-token and application-launch evidence.";
        }
        else if (new WindowsElevationChecker().IsElevated())
        {
            Skip = "Requires a non-elevated host to verify a normal-integrity child process.";
        }
    }
}

public sealed class ElevatedFactAttribute : FactAttribute
{
    public ElevatedFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !new WindowsElevationChecker().IsElevated())
            Skip = "Requires an elevated host so the launched child has an elevated process token.";
    }
}
