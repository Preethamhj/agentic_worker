namespace Jarvis.Safety;

/// <summary>Provides the trusted directory for reminder payload files.</summary>
public interface IReminderDataLocation { string DirectoryPath { get; } }

/// <summary>Production reminder payload location.</summary>
public sealed class DefaultReminderDataLocation : IReminderDataLocation
{
    public string DirectoryPath => ReminderDataPaths.DirectoryPath;
}
