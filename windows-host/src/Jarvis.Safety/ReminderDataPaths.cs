namespace Jarvis.Safety;

/// <summary>Defines the per-user storage location for one-time reminder payloads.</summary>
public static class ReminderDataPaths
{
    public static string DirectoryPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
                throw new InvalidOperationException("The current user's local application data directory is unavailable.");

            return Path.GetFullPath(Path.Combine(localAppData, "Jarvis", "reminders"));
        }
    }

    /// <summary>Builds a data-file path using only a validated 32-character task ID.</summary>
    public static string GetFilePath(string taskId, string? baseDirectory = null)
    {
        if (!ToolPolicy.IsValidReminderTaskId(taskId))
            throw new ArgumentException("Reminder task ID must contain exactly 32 hexadecimal characters.", nameof(taskId));

        var directory = baseDirectory ?? DirectoryPath;
        return Path.GetFullPath(Path.Combine(directory, $"{taskId}.json"));
    }
}
