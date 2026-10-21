using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;
using Json.Schema;

namespace Jarvis.Safety;

public sealed record ToolPolicyResult(bool IsValid, string? PolicyRisk, string? ErrorMessage)
{
    public static ToolPolicyResult Success(string risk) => new(true, risk, null);
    public static ToolPolicyResult Failure(string error) => new(false, null, error);
}

public sealed class ToolDefinition
{
    public required string ToolId { get; init; }
    public required string Version { get; init; }
    public required string Risk { get; init; }
    public required bool Enabled { get; init; }
    public required string Description { get; init; }
    public required JsonSchema ArgsSchema { get; init; }
}

public sealed class AllowedAppDefinition
{
    public required string AppId { get; init; }
    public required string Executable { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// ToolPolicy loads tools.registry.json and apps.allowlist.json to independently
/// validate tool availability, argument schema conformance, and application allowlists,
/// computing the authoritative policy risk.
/// </summary>
public sealed class ToolPolicy
{
    public const int MaxReminderMessageLength = 200;
    public const int MaxReminderTaskNameLength = 63;
    private static readonly Regex ReminderTaskIdPattern = new(@"\A[0-9a-fA-F]{32}\z", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AllowedAppDefinition> _allowedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedAppIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;
    public IReadOnlySet<string> AllowedAppIds => _allowedAppIds;
    public IReadOnlyDictionary<string, AllowedAppDefinition> AllowedApps => _allowedApps;

    public ToolPolicy(IEnumerable<ToolDefinition> tools, IEnumerable<string> allowedAppIds)
    {
        foreach (var t in tools)
        {
            _tools[t.ToolId] = t;
        }

        foreach (var appId in allowedAppIds)
        {
            _allowedAppIds.Add(appId);
            _allowedApps[appId] = new AllowedAppDefinition
            {
                AppId = appId,
                Executable = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId : appId + ".exe",
                DisplayName = appId
            };
        }
    }

    public ToolPolicy(IEnumerable<ToolDefinition> tools, IEnumerable<AllowedAppDefinition> allowedApps)
    {
        foreach (var t in tools)
        {
            _tools[t.ToolId] = t;
        }

        foreach (var app in allowedApps)
        {
            _allowedAppIds.Add(app.AppId);
            _allowedApps[app.AppId] = app;
        }
    }

    public bool TryGetAllowedApp(string appId, out AllowedAppDefinition? app)
    {
        return _allowedApps.TryGetValue(appId, out app);
    }

    /// <summary>
    /// Resolves an allowlisted app ID without searching the process PATH.
    /// </summary>
    public bool TryResolveAllowedExecutablePath(string appId, out string? executablePath)
    {
        executablePath = null;
        if (!TryGetAllowedApp(appId, out var app) || app is null || string.IsNullOrWhiteSpace(app.Executable))
            return false;

        if (Path.IsPathRooted(app.Executable) && File.Exists(app.Executable))
        {
            executablePath = Path.GetFullPath(app.Executable);
            return true;
        }

        if (!OperatingSystem.IsWindows())
            return false;

        var systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), app.Executable);
        if (File.Exists(systemPath))
        {
            executablePath = Path.GetFullPath(systemPath);
            return true;
        }

        var windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), app.Executable);
        if (File.Exists(windowsPath))
        {
            executablePath = Path.GetFullPath(windowsPath);
            return true;
        }

        return false;
    }

    public static ToolPolicy FromFile(string toolsRegistryPath, string appsAllowlistPath)
    {
        if (!File.Exists(toolsRegistryPath))
            throw new FileNotFoundException($"Tools registry not found at '{toolsRegistryPath}'", toolsRegistryPath);
        if (!File.Exists(appsAllowlistPath))
            throw new FileNotFoundException($"Apps allowlist not found at '{appsAllowlistPath}'", appsAllowlistPath);

        var registryJson = File.ReadAllText(toolsRegistryPath);
        var appsJson = File.ReadAllText(appsAllowlistPath);

        return FromJson(registryJson, appsJson);
    }

    public static ToolPolicy FromJson(string registryJson, string appsJson)
    {
        var registryNode = JsonNode.Parse(registryJson)
            ?? throw new JsonException("Failed to parse tools registry JSON.");
        var appsNode = JsonNode.Parse(appsJson)
            ?? throw new JsonException("Failed to parse apps allowlist JSON.");

        var toolsList = new List<ToolDefinition>();
        if (registryNode["tools"] is JsonArray toolsArray)
        {
            foreach (var item in toolsArray)
            {
                if (item is null) continue;

                var toolId = item["tool_id"]?.GetValue<string>()
                    ?? throw new JsonException("Missing tool_id in tool definition.");
                var version = item["version"]?.GetValue<string>() ?? "1.0";
                var risk = item["risk"]?.GetValue<string>() ?? "medium";
                var enabled = item["enabled"]?.GetValue<bool>() ?? false;
                var description = item["description"]?.GetValue<string>() ?? "";

                var argsSchemaNode = item["args_schema"];
                var argsSchema = argsSchemaNode != null
                    ? JsonSchema.FromText(argsSchemaNode.ToJsonString())
                    : JsonSchema.Empty;

                toolsList.Add(new ToolDefinition
                {
                    ToolId = toolId,
                    Version = version,
                    Risk = risk,
                    Enabled = enabled,
                    Description = description,
                    ArgsSchema = argsSchema
                });
            }
        }

        var allowedApps = new List<AllowedAppDefinition>();
        if (appsNode["allowed_apps"] is JsonArray appsArray)
        {
            foreach (var app in appsArray)
            {
                if (app?["app_id"]?.GetValue<string>() is { } appId)
                {
                    var executable = app["executable"]?.GetValue<string>() ?? (appId + ".exe");
                    var displayName = app["display_name"]?.GetValue<string>() ?? appId;
                    allowedApps.Add(new AllowedAppDefinition
                    {
                        AppId = appId,
                        Executable = executable,
                        DisplayName = displayName
                    });
                }
            }
        }

        return new ToolPolicy(toolsList, allowedApps);
    }

    public string? GetPolicyRisk(string toolId)
    {
        return _tools.TryGetValue(toolId, out var tool) ? tool.Risk : null;
    }

    public ToolPolicyResult ValidateToolCall(string toolId, JsonNode? args)
    {
        if (!_tools.TryGetValue(toolId, out var tool))
        {
            return ToolPolicyResult.Failure($"Tool '{toolId}' is not registered.");
        }

        if (!tool.Enabled)
        {
            return ToolPolicyResult.Failure($"Tool '{toolId}' is disabled by policy.");
        }

        // Schema validation
        var argsNode = args ?? new JsonObject();
        var evalOptions = new EvaluationOptions { OutputFormat = OutputFormat.List };
        var evalResult = tool.ArgsSchema.Evaluate(argsNode, evalOptions);
        if (!evalResult.IsValid)
        {
            var errors = evalResult.Details
                .Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Values);
            var errorMessage = string.Join("; ", errors);
            return ToolPolicyResult.Failure($"Tool arguments failed schema validation: {errorMessage}");
        }

        if (string.Equals(toolId, "task.schedule_reminder", StringComparison.OrdinalIgnoreCase))
        {
            var message = argsNode["message"]?.GetValue<string>();
            if (message is null || !IsValidReminderMessage(message))
            {
                return ToolPolicyResult.Failure(
                    $"Reminder message must contain 1-{MaxReminderMessageLength} characters and no control characters.");
            }
            var taskName = argsNode["task_name"]?.GetValue<string>();
            if (taskName is null)
            {
                // Title is optional at the planning boundary.  Canonicalize it before signing.
                argsNode["task_name"] = TruncateReminderTitle(message);
            }
            else if (string.IsNullOrWhiteSpace(taskName)
                || CountUtf16CodeUnits(taskName) > MaxReminderTaskNameLength
                || taskName.Any(char.IsControl))
            {
                return ToolPolicyResult.Failure(
                    $"Reminder title must contain 1-{MaxReminderTaskNameLength} non-control characters.");
            }
        }

        // Specific application allowlist check for app.launch
        if (string.Equals(toolId, "app.launch", StringComparison.OrdinalIgnoreCase))
        {
            var appId = argsNode["app_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(appId) || !_allowedAppIds.Contains(appId))
            {
                return ToolPolicyResult.Failure($"App ID '{appId}' is not present in the apps allowlist.");
            }
        }

        return ToolPolicyResult.Success(tool.Risk);
    }

    /// <summary>Checks the opaque task identifier embedded in the scheduled action.</summary>
    public static bool IsValidReminderTaskId(string? taskId) =>
        taskId is not null && ReminderTaskIdPattern.IsMatch(taskId);

    /// <summary>Checks the reminder text length and excludes control characters.</summary>
    public static bool IsValidReminderMessage(string? message) =>
        !string.IsNullOrEmpty(message)
        && CountUtf16CodeUnits(message) <= MaxReminderMessageLength
        && !message.Any(char.IsControl);

    /// <summary>Counts UTF-16 code units, the shared reminder length contract with Python.</summary>
    public static int CountUtf16CodeUnits(string value) => value.Length;

    /// <summary>Truncates at text-element boundaries while preserving the UTF-16 title limit.</summary>
    public static string TruncateReminderTitle(string message)
    {
        var builder = new System.Text.StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(message);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (builder.Length + element.Length > MaxReminderTaskNameLength)
                break;
            builder.Append(element);
        }
        return builder.ToString();
    }
}
