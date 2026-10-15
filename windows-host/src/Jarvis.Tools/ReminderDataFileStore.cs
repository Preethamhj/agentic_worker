using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>Stores one-time reminder text under the current user's Jarvis data directory.</summary>
public sealed class ReminderDataFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PathPolicy _pathPolicy = new([ReminderDataPaths.DirectoryPath]);

    /// <summary>Creates a new file without replacing an existing reminder payload.</summary>
    public void Create(string taskId, string title, string message)
    {
        if (!ToolPolicy.IsValidReminderTaskId(taskId))
            throw new ArgumentException("Reminder task ID is malformed.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(title) || title.Length > 63 || title.Any(char.IsControl))
            throw new ArgumentException("Reminder title must contain 1-63 non-control characters.", nameof(title));
        if (!ToolPolicy.IsValidReminderMessage(message))
            throw new ArgumentException(
                $"Reminder message must contain 1-{ToolPolicy.MaxReminderMessageLength} characters and no control characters.",
                nameof(message));

        var path = ValidatePath(taskId);
        Directory.CreateDirectory(ReminderDataPaths.DirectoryPath);
        path = ValidatePath(taskId);

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new ReminderDataPayload(title, message), SerializerOptions);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Reads a reminder payload from its task-ID-derived file.</summary>
    public ReminderDataPayload Read(string taskId)
    {
        var path = ValidatePath(taskId);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var payload = JsonSerializer.Deserialize<ReminderDataPayload>(stream, SerializerOptions)
            ?? throw new JsonException("Reminder data file contains no payload.");
        if (string.IsNullOrWhiteSpace(payload.TaskName)
            || payload.TaskName.Length > 63
            || payload.TaskName.Any(char.IsControl)
            || !ToolPolicy.IsValidReminderMessage(payload.Message))
        {
            throw new JsonException("Reminder data file contains invalid title or message text.");
        }
        return payload;
    }

    /// <summary>Deletes the payload identified by a validated task ID.</summary>
    public void Delete(string taskId)
    {
        var path = ValidatePath(taskId);
        File.Delete(path);
    }

    /// <summary>Returns the validated data-file path for diagnostics and tests.</summary>
    public string GetFilePath(string taskId) => ValidatePath(taskId);

    private string ValidatePath(string taskId)
    {
        var validation = _pathPolicy.ValidatePath(ReminderDataPaths.GetFilePath(taskId));
        if (!validation.IsValid || validation.NormalizedPath is null)
            throw new UnauthorizedAccessException(validation.ErrorMessage ?? "Reminder data path is invalid.");
        return validation.NormalizedPath;
    }
}

/// <summary>Text persisted for one reminder until its notification is dispatched.</summary>
public sealed record ReminderDataPayload(
    [property: JsonPropertyName("task_name")] string TaskName,
    [property: JsonPropertyName("message")] string Message);
