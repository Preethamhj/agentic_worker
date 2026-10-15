using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Jarvis.Contracts;

namespace Jarvis.Verification;

internal sealed class ScheduledTaskEvidenceCollector
{
    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorPathNotFound = unchecked((int)0x80070003);
    private const int SchedulerTaskNotFound = unchecked((int)0x8004130F);
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Scheduled-task evidence is available only on Windows.");

        object? serviceObject = null;
        object? folderObject = null;
        object? tasksObject = null;
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("Windows Task Scheduler COM service is unavailable.");
            serviceObject = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("Could not create the Task Scheduler COM service.");
            dynamic service = serviceObject;
            service.Connect();
            try
            {
                folderObject = service.GetFolder(@"\Jarvis");
            }
            catch (COMException exception) when (IsNotFound(exception.HResult))
            {
                return [];
            }
            catch (FileNotFoundException)
            {
                return [];
            }

            dynamic folder = folderObject!;
            tasksObject = folder.GetTasks(1);
            dynamic tasks = tasksObject;
            var paths = new List<string>();
            for (var index = 1; index <= (int)tasks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? taskObject = null;
                try
                {
                    taskObject = tasks.Item(index);
                    dynamic task = taskObject;
                    paths.Add($@"\Jarvis\{(string)task.Name}");
                }
                finally
                {
                    Release(taskObject);
                }
            }
            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (COMException exception)
        {
            throw new IOException("Could not enumerate the Jarvis Task Scheduler folder through COM.", exception);
        }
        finally
        {
            Release(tasksObject);
            Release(folderObject);
            Release(serviceObject);
        }
    }

    public IndependentEvidenceObservation Collect(
        AuthorizationRecordDto authorization,
        IReadOnlyList<string> beforeTaskPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Scheduled-task evidence is available only on Windows.");

        var taskPath = ReadExpected(authorization, "task_path");
        var separator = taskPath.LastIndexOf('\\');
        if (separator <= 0 || separator == taskPath.Length - 1)
            throw new ArgumentException("Signed scheduled-task path is malformed.", nameof(authorization));

        object? serviceObject = null;
        object? folderObject = null;
        object? taskObject = null;
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("Windows Task Scheduler COM service is unavailable.");
            serviceObject = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("Could not create the Task Scheduler COM service.");
            dynamic service = serviceObject;
            service.Connect();
            try
            {
                folderObject = service.GetFolder(taskPath[..separator]);
                dynamic folder = folderObject;
                try
                {
                    taskObject = folder.GetTask(taskPath[(separator + 1)..]);
                }
                catch (COMException exception) when (IsNotFound(exception.HResult))
                {
                    return Missing(taskPath);
                }
                catch (FileNotFoundException)
                {
                    return Missing(taskPath);
                }
                catch (DirectoryNotFoundException)
                {
                    return Missing(taskPath);
                }
            }
            catch (COMException exception) when (IsNotFound(exception.HResult))
            {
                return Missing(taskPath);
            }
            catch (FileNotFoundException)
            {
                return Missing(taskPath);
            }
            catch (DirectoryNotFoundException)
            {
                return Missing(taskPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            dynamic task = taskObject!;
            var xml = (string)task.Xml;
            var enabled = (bool)task.Enabled;
            return ReadTaskXml(authorization, taskPath, xml, enabled, beforeTaskPaths, cancellationToken);
        }
        catch (COMException exception)
        {
            throw new IOException("Could not read the scheduled task through the Windows Task Scheduler COM API.", exception);
        }
        finally
        {
            Release(taskObject);
            Release(folderObject);
            Release(serviceObject);
        }
    }

    private IndependentEvidenceObservation ReadTaskXml(
        AuthorizationRecordDto authorization,
        string taskPath,
        string xml,
        bool taskEnabled,
        IReadOnlyList<string> beforeTaskPaths,
        CancellationToken cancellationToken)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new IOException("Task Scheduler returned XML without a task root.");
        var triggers = root.Element(TaskNamespace + "Triggers")?.Elements().ToArray() ?? [];
        var actions = root.Element(TaskNamespace + "Actions")?.Elements().ToArray() ?? [];
        var timeTrigger = triggers.Length == 1 && triggers[0].Name == TaskNamespace + "TimeTrigger"
            ? triggers[0]
            : null;
        var execAction = actions.Length == 1 && actions[0].Name == TaskNamespace + "Exec"
            ? actions[0]
            : null;
        var startBoundaryText = timeTrigger?.Element(TaskNamespace + "StartBoundary")?.Value;
        var triggerEnabled = ReadEnabled(timeTrigger?.Element(TaskNamespace + "Enabled")?.Value, defaultValue: true);
        var settingsEnabled = ReadEnabled(
            root.Element(TaskNamespace + "Settings")?.Element(TaskNamespace + "Enabled")?.Value,
            defaultValue: taskEnabled);
        var command = execAction?.Element(TaskNamespace + "Command")?.Value;
        var arguments = execAction?.Element(TaskNamespace + "Arguments")?.Value;
        var expectedTime = DateTimeOffset.Parse(
            ReadCanonical(authorization, "time_iso"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var hasTime = DateTimeOffset.TryParse(
            startBoundaryText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var actualTime);
        var expectedAction = Path.GetFullPath(ReadExpected(authorization, "action_path"));
        var expectedArguments = ReadExpected(authorization, "action_arguments");
        var normalizedArguments = NormalizeReminderArguments(arguments);
        var normalizedExpectedArguments = NormalizeReminderArguments(expectedArguments);
        var actionMatches = command is not null
            && string.Equals(Path.GetFullPath(command), expectedAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedArguments, normalizedExpectedArguments, StringComparison.Ordinal);
        var timeMatches = hasTime && actualTime.ToUniversalTime() == expectedTime.ToUniversalTime();
        var dataFilePath = ReadExpected(authorization, "reminder_data_path");
        var dataFileMatches = ReminderDataMatchesAuthorization(authorization, dataFilePath);
        var unexpectedTasks = CaptureTaskPaths(cancellationToken)
            .Where(path => !beforeTaskPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(path, taskPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matches = taskEnabled
            && settingsEnabled
            && triggerEnabled
            && timeMatches
            && actionMatches
            && dataFileMatches
            && unexpectedTasks.Length == 0;
        var observed = new Dictionary<string, object?>
        {
            ["scheduler_method"] = "COM",
            ["task_path"] = taskPath,
            ["task_enabled"] = taskEnabled,
            ["settings_enabled"] = settingsEnabled,
            ["trigger_count"] = triggers.Length,
            ["trigger_is_time"] = timeTrigger is not null,
            ["trigger_enabled"] = triggerEnabled,
            ["start_boundary"] = startBoundaryText,
            ["action_count"] = actions.Length,
            ["action_command"] = command,
            ["action_arguments"] = arguments,
            ["exec_action_xml"] = execAction?.ToString(SaveOptions.DisableFormatting),
            ["time_matches"] = timeMatches,
            ["action_matches"] = actionMatches,
            ["reminder_data_path"] = dataFilePath,
            ["reminder_data_exists"] = File.Exists(dataFilePath),
            ["reminder_data_matches_authorization"] = dataFileMatches,
            ["unexpected_new_tasks"] = unexpectedTasks,
            ["xml_valid"] = true
        };
        return new IndependentEvidenceObservation(
            "task_exists",
            nameof(ScheduledTaskEvidenceCollector),
            observed,
            matches,
            matches
                ? "Task Scheduler COM XML, one-time trigger, action, enabled state, and Jarvis-folder side effects match the signed authorization."
                : unexpectedTasks.Length > 0
                    ? "Unauthorized additional tasks appeared in the Jarvis Task Scheduler folder."
                    : "Scheduled task XML, trigger time, action, enabled state, or reminder data differs from the signed authorization.");
    }

    private static bool ReminderDataMatchesAuthorization(
        AuthorizationRecordDto authorization,
        string dataFilePath)
    {
        if (!Path.IsPathRooted(dataFilePath)
            || !File.Exists(dataFilePath)
            || (File.GetAttributes(dataFilePath) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(dataFilePath));
            var payload = document.RootElement;
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("task_name", out var taskName)
                && taskName.ValueKind == JsonValueKind.String
                && string.Equals(taskName.GetString(), ReadCanonical(authorization, "task_name"), StringComparison.Ordinal)
                && payload.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && string.Equals(message.GetString(), ReadCanonical(authorization, "message"), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IndependentEvidenceObservation Missing(string taskPath) =>
        new(
            "task_exists",
            nameof(ScheduledTaskEvidenceCollector),
            new Dictionary<string, object?>
            {
                ["task_path"] = taskPath,
                ["scheduler_method"] = "COM",
                ["exists"] = false,
                ["task_enabled"] = false
            },
            false,
            "The authorized scheduled task does not exist in the OS Task Scheduler.");

    private static string NormalizeReminderArguments(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string ReadCanonical(AuthorizationRecordDto authorization, string key) =>
        authorization.CanonicalArgs.TryGetValue(key, out var value) && value is string text
            ? text
            : throw new ArgumentException($"Authorization is missing canonical '{key}'.", nameof(authorization));

    private static string ReadExpected(AuthorizationRecordDto authorization, string key) =>
        authorization.Expected.TryGetValue(key, out var value) && value is string text
            ? text
            : throw new ArgumentException($"Authorization is missing signed expected '{key}'.", nameof(authorization));

    private static bool IsNotFound(int hresult) =>
        hresult is ErrorFileNotFound or ErrorPathNotFound or SchedulerTaskNotFound;

    private static bool ReadEnabled(string? value, bool defaultValue) =>
        value is null
            ? defaultValue
            : bool.TryParse(value, out var enabled) && enabled;

    private static void Release(object? value)
    {
        if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
