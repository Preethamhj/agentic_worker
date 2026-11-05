namespace Jarvis.Tools;

/// <summary>Reports whether the current process is running with an elevated Windows token.</summary>
public interface IElevationChecker
{
    bool IsElevated();
}
