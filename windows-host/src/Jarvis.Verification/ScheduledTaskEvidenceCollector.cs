using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Jarvis.Contracts;

namespace Jarvis.Verification;

internal sealed class ScheduledTaskEvidenceCollector : IScheduledTaskEvidenceCollector
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
            var taskName = (string)task.Name;
            var afterTaskPaths = CaptureTaskPaths(cancellationToken);
            return ReadTaskXml(
                authorization,
                taskPath,
                taskName,
                xml,
                enabled,
                beforeTaskPaths,
                afterTaskPaths);
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

    internal IndependentEvidenceObservation ReadTaskXml(
        AuthorizationRecordDto authorization,
        string taskPath,
        string taskName,
        string xml,
        bool taskEnabled,
        IReadOnlyList<string> beforeTaskPaths,
        IReadOnlyList<string> afterTaskPaths)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new IOException("Task Scheduler returned XML without a task root.");
        var triggers = root.Element(TaskNamespace + "Triggers")?.Elements().ToArray() ?? [];
        var actions = root.Element(TaskNamespace + "Actions")?.Elements().ToArray() ?? [];
        var settings = root.Element(TaskNamespace + "Settings");
        var principals = root.Element(TaskNamespace + "Principals")?.Elements(TaskNamespace + "Principal").ToArray() ?? [];
        var principal = principals.Length == 1 ? principals[0] : null;
        var timeTrigger = triggers.Length == 1 && triggers[0].Name == TaskNamespace + "TimeTrigger"
            ? triggers[0]
            : null;
        var execAction = actions.Length == 1 && actions[0].Name == TaskNamespace + "Exec"
            ? actions[0]
            : null;
        var startBoundaryText = timeTrigger?.Element(TaskNamespace + "StartBoundary")?.Value;
        var triggerEnabled = ReadEnabled(timeTrigger?.Element(TaskNamespace + "Enabled")?.Value, defaultValue: true);
        var settingsEnabled = ReadEnabled(settings?.Element(TaskNamespace + "Enabled")?.Value, defaultValue: taskEnabled);
        var command = execAction?.Element(TaskNamespace + "Command")?.Value;
        var arguments = execAction?.Element(TaskNamespace + "Arguments")?.Value;
        var expected = authorization.Expected;
        var expectedTaskName = ReadExpected(authorization, "scheduler_task_name");
        var expectedTaskPath = ReadExpected(authorization, "task_path");
        var taskNameMatches = string.Equals(taskName, expectedTaskName, StringComparison.Ordinal)
            && string.Equals(taskPath, expectedTaskPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(taskPath[(taskPath.LastIndexOf('\\') + 1)..], expectedTaskName, StringComparison.Ordinal);
        var expectedTime = DateTimeOffset.Parse(ReadExpected(authorization, "time_iso"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
        var disallowBatteryMatches = MatchesBoolean(settings?.Element(TaskNamespace + "DisallowStartIfOnBatteries")?.Value, expected, "disallow_start_if_on_batteries");
        var stopBatteryMatches = MatchesBoolean(settings?.Element(TaskNamespace + "StopIfGoingOnBatteries")?.Value, expected, "stop_if_going_on_batteries");
        var executionLimitMatches = MatchesDuration(settings?.Element(TaskNamespace + "ExecutionTimeLimit")?.Value, expected, "execution_time_limit_seconds");
        var startAvailableMatches = MatchesBoolean(settings?.Element(TaskNamespace + "StartWhenAvailable")?.Value, expected, "start_when_available");
        var multipleInstancesMatches = MatchesString(settings?.Element(TaskNamespace + "MultipleInstancesPolicy")?.Value, expected, "multiple_instances_policy");
        var logonTypeMatches = principals.Length == 1 && MatchesString(principal?.Element(TaskNamespace + "LogonType")?.Value, expected, "logon_type");
        var runLevelXml = principal?.Element(TaskNamespace + "RunLevel")?.Value;
        var runLevel = runLevelXml ?? "LeastPrivilege";
        var runLevelMatches = principals.Length == 1 && MatchesString(runLevel, expected, "run_level");
        var endBoundaryMatches = MatchesEndBoundary(timeTrigger?.Element(TaskNamespace + "EndBoundary")?.Value, expectedTime, expected);
        var deleteExpiredMatches = MatchesDuration(settings?.Element(TaskNamespace + "DeleteExpiredTaskAfter")?.Value, expected, "delete_expired_task_after_seconds");
        var dataFilePath = ReadExpected(authorization, "reminder_data_path");
        var dataFileMatches = ReminderDataMatchesAuthorization(authorization, dataFilePath);
        var unexpectedTasks = afterTaskPaths
            .Where(path => !beforeTaskPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(path, expectedTaskPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matches = taskEnabled
            && settingsEnabled
            && triggerEnabled
            && taskNameMatches
            && timeMatches
            && actionMatches
            && disallowBatteryMatches
            && stopBatteryMatches
            && executionLimitMatches
            && startAvailableMatches
            && multipleInstancesMatches
            && logonTypeMatches
            && runLevelMatches
            && endBoundaryMatches
            && deleteExpiredMatches
            && dataFileMatches
            && unexpectedTasks.Length == 0;
        var settingMatches = new Dictionary<string, bool>
        {
            ["task_name"] = taskNameMatches,
            ["trigger_time"] = timeMatches,
            ["action_path_and_arguments"] = actionMatches,
            ["disallow_start_if_on_batteries"] = disallowBatteryMatches,
            ["stop_if_going_on_batteries"] = stopBatteryMatches,
            ["execution_time_limit"] = executionLimitMatches,
            ["start_when_available"] = startAvailableMatches,
            ["multiple_instances_policy"] = multipleInstancesMatches,
            ["logon_type"] = logonTypeMatches,
            ["run_level"] = runLevelMatches,
            ["end_boundary"] = endBoundaryMatches,
            ["delete_expired_task_after"] = deleteExpiredMatches
        };
        var mismatchedSettings = settingMatches.Where(item => !item.Value).Select(item => item.Key).ToArray();
        var observed = new Dictionary<string, object?>
        {
            ["scheduler_method"] = "COM",
            ["task_path"] = taskPath,
            ["task_name"] = taskName,
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
            ["disallow_start_if_on_batteries"] = settings?.Element(TaskNamespace + "DisallowStartIfOnBatteries")?.Value,
            ["stop_if_going_on_batteries"] = settings?.Element(TaskNamespace + "StopIfGoingOnBatteries")?.Value,
            ["execution_time_limit"] = settings?.Element(TaskNamespace + "ExecutionTimeLimit")?.Value,
            ["start_when_available"] = settings?.Element(TaskNamespace + "StartWhenAvailable")?.Value,
            ["multiple_instances_policy"] = settings?.Element(TaskNamespace + "MultipleInstancesPolicy")?.Value,
            ["logon_type"] = principal?.Element(TaskNamespace + "LogonType")?.Value,
            ["run_level"] = runLevel,
            ["run_level_xml"] = runLevelXml,
            ["end_boundary"] = timeTrigger?.Element(TaskNamespace + "EndBoundary")?.Value,
            ["delete_expired_task_after"] = settings?.Element(TaskNamespace + "DeleteExpiredTaskAfter")?.Value,
            ["setting_matches"] = settingMatches,
            ["mismatched_settings"] = mismatchedSettings,
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
                ? "Task Scheduler COM XML, every signed scheduler setting, enabled state, reminder data, and Jarvis-folder side effects match the signed authorization."
                : unexpectedTasks.Length > 0
                    ? "Unauthorized additional tasks appeared in the Jarvis Task Scheduler folder."
                    : $"Scheduled task differs from signed authorization. Mismatched settings: {string.Join(", ", mismatchedSettings)}.");
    }

    private static bool MatchesBoolean(string? actual, IReadOnlyDictionary<string, object?> expected, string key) =>
        bool.TryParse(actual, out var observed)
        && TryReadExpectedBoolean(expected, key, out var signed)
        && observed == signed;

    private static bool MatchesString(string? actual, IReadOnlyDictionary<string, object?> expected, string key) =>
        actual is not null
        && expected.TryGetValue(key, out var value)
        && value is string signed
        && string.Equals(actual, signed, StringComparison.Ordinal);

    private static bool MatchesDuration(string? actual, IReadOnlyDictionary<string, object?> expected, string key) =>
        TryParseDuration(actual, out var observed)
        && TryReadExpectedInteger(expected, key, out var seconds)
        && observed == TimeSpan.FromSeconds(seconds);

    private static bool MatchesEndBoundary(
        string? actual,
        DateTimeOffset expectedStart,
        IReadOnlyDictionary<string, object?> expected)
    {
        if (!DateTimeOffset.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var observed)
            || !TryReadExpectedInteger(expected, "end_boundary_offset_seconds", out var seconds))
            return false;
        return observed.ToUniversalTime() == expectedStart.AddSeconds(seconds).ToUniversalTime();
    }

    private static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        try
        {
            if (value is null)
            {
                duration = default;
                return false;
            }
            duration = System.Xml.XmlConvert.ToTimeSpan(value);
            return true;
        }
        catch (FormatException)
        {
            duration = default;
            return false;
        }
        catch (OverflowException)
        {
            duration = default;
            return false;
        }
    }

    private static bool TryReadExpectedBoolean(IReadOnlyDictionary<string, object?> expected, string key, out bool value)
    {
        value = false;
        return expected.TryGetValue(key, out var item)
            && (item is bool signed ? SetValue(signed, out value) : bool.TryParse(item?.ToString(), out value));
    }

    private static bool TryReadExpectedInteger(IReadOnlyDictionary<string, object?> expected, string key, out int value)
    {
        value = 0;
        return expected.TryGetValue(key, out var item)
            && (item is int signed ? SetValue(signed, out value) : int.TryParse(item?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value));
    }

    private static bool SetValue<T>(T source, out T value)
    {
        value = source;
        return true;
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

    internal static IndependentEvidenceObservation Missing(string taskPath) =>
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
