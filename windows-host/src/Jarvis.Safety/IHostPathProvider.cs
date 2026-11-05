namespace Jarvis.Safety;

/// <summary>Supplies the absolute executable path of the trusted Jarvis host.</summary>
public interface IHostPathProvider
{
    /// <summary>Gets the absolute path of the current host executable.</summary>
    string GetHostPath();
}

/// <summary>Reads the host executable path from the current process environment.</summary>
public sealed class EnvironmentHostPathProvider : IHostPathProvider
{
    /// <inheritdoc />
    public string GetHostPath() => Path.GetFullPath(
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The host process path is unavailable for reminder authorization."));
}
