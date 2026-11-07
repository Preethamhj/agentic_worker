using Jarvis.Composition;

namespace Jarvis.UI.ViewModels;

/// <summary>Read-only settings and diagnostic information for the settings tab.</summary>
public sealed class SettingsViewModel
{
    /// <summary>Copies the displayed configuration and doctor results into immutable collections.</summary>
    public SettingsViewModel(IEnumerable<string> allowedRoots, string modelName, IEnumerable<string> doctorResults)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(doctorResults);
        AllowedRoots = Array.AsReadOnly(allowedRoots.ToArray());
        ModelName = modelName;
        DoctorResults = Array.AsReadOnly(doctorResults.ToArray());
    }

    /// <summary>Configured allowed roots; the settings UI cannot change them.</summary>
    public IReadOnlyList<string> AllowedRoots { get; }

    /// <summary>Configured model name; the settings UI cannot change it.</summary>
    public string ModelName { get; }

    /// <summary>Configured planner order; rule parsing is first and Ollama handles unmatched requests.</summary>
    public string PlannerDescription => JarvisSettingsSnapshot.PlannerDescription;

    /// <summary>Doctor diagnostics captured by the host composition.</summary>
    public IReadOnlyList<string> DoctorResults { get; }
}
