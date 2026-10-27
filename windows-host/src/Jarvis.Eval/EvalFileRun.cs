using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.Verification;

namespace Jarvis.Eval;

public sealed class EvalFileRunOptions
{
    public int Seed { get; init; } = EvalTaskCatalog.DefaultSeed;
    public int Repetitions { get; init; } = 1;
    public bool Smoke { get; init; }
    public string OutputDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "eval-output");
    public string? ToolFilter { get; init; }
    public string Scheduler { get; init; } = "fake";
    public bool AllowPreexistingProcesses { get; init; }
}

public sealed record EvalPreexistingProcess(
    string ProcessName,
    int ProcessId,
    DateTimeOffset StartTime,
    string WindowTitle);

public sealed class EvalFileRunner
{
    private readonly List<(Process Process, DateTime StartTime)> _startedProcesses = [];

    public static IReadOnlyList<EvalPreexistingProcess> PreflightAppProcesses(bool allowPreexistingProcesses)
    {
        var processes = EnumeratePreexistingProcesses();
        if (processes.Count > 0 && !allowPreexistingProcesses)
        {
            var details = string.Join(
                "; ",
                processes.Select(process =>
                    $"{process.ProcessName} PID={process.ProcessId} StartTime={process.StartTime:o} WindowTitle={process.WindowTitle}"));
            throw new InvalidOperationException(
                $"App evaluation aborted because pre-existing Notepad/ping processes were found: {details}. " +
                "Close only processes you own, or pass --allow-preexisting-processes to record them explicitly.");
        }
        return processes;
    }

    public static IReadOnlyList<EvalPreexistingProcess> EnumeratePreexistingProcesses()
    {
        var result = new List<EvalPreexistingProcess>();
        foreach (var processName in new[] { "notepad", "ping" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        result.Add(new EvalPreexistingProcess(
                            process.ProcessName,
                            process.Id,
                            new DateTimeOffset(process.StartTime),
                            process.MainWindowTitle));
                    }
                    catch (InvalidOperationException)
                    {
                        // A process can exit between enumeration and inspection; it is not a preflight blocker.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // An inaccessible process is not treated as an owned evaluation process.
                    }
                }
            }
        }
        return result;
    }

    public async Task<int> RunAsync(EvalFileRunOptions options, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(options.Scheduler, "fake", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Scheduler, "real", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scheduler must be fake or real.", nameof(options));
        if (string.Equals(options.Scheduler, "real", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Environment.GetEnvironmentVariable("JARVIS_REAL_SCHEDULER"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("Real scheduler evaluation requires JARVIS_REAL_SCHEDULER=1.");
        var realScheduler = string.Equals(options.Scheduler, "real", StringComparison.OrdinalIgnoreCase);
        if (realScheduler && options.ToolFilter != "task.schedule_reminder")
            throw new InvalidOperationException("Real scheduler evaluation is limited to task.schedule_reminder.");
        if (realScheduler && options.Repetitions != 1)
            throw new InvalidOperationException("Real scheduler evaluation requires exactly one repetition.");
        var preexistingProcesses = options.ToolFilter == "app.launch"
            ? PreflightAppProcesses(options.AllowPreexistingProcesses)
            : Array.Empty<EvalPreexistingProcess>();
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var sandbox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis", "eval-sandbox", runId);
        Directory.CreateDirectory(sandbox);
        Directory.CreateDirectory(options.OutputDirectory);
        var rawPath = Path.Combine(options.OutputDirectory, "raw.jsonl");
        var completed = LoadCompleted(rawPath);
        try
        {
            await using var output = new StreamWriter(rawPath, append: true, Encoding.UTF8);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                row_type = "metadata",
                run_id = runId,
                os_build = Environment.OSVersion.VersionString,
                dotnet_version = Environment.Version.ToString(),
                git_commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown",
                seed = options.Seed,
                timestamp = DateTimeOffset.UtcNow,
                preexisting_processes = preexistingProcesses
            })).ConfigureAwait(false);

            var tasks = EvalTaskCatalog.Generate(options.Seed)
                .Where(task => options.ToolFilter is null || task.ToolId == options.ToolFilter)
                .Take(options.Smoke ? 2 : int.MaxValue)
                .ToArray();
            var repetitions = options.Smoke ? 1 : options.Repetitions;
            foreach (var repetition in Enumerable.Range(1, repetitions))
                foreach (var evalCase in EvalTaskCatalog.ExpandCases(tasks, repetition, options.Seed)
                    .Where(evalCase => !realScheduler || evalCase.FaultId is "F0" or "F12" or "F14" or "F15"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await RunCaseAsync(evalCase, sandbox, cancellationToken, realScheduler)
                        .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    foreach (var verdict in result.Verdicts)
                    {
                        if (completed.Contains($"{evalCase.Id}:{verdict.VerifierId}"))
                            continue;
                        await output.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            run_id = runId,
                            case_id = evalCase.Id,
                            task_id = evalCase.TaskId,
                            tool = evalCase.ToolId,
                            fault = evalCase.FaultId,
                            ground_truth = evalCase.GroundTruth.ToString().ToUpperInvariant(),
                            rep = evalCase.Repetition,
                            verifier = verdict.VerifierId,
                            status = verdict.Status.ToString().ToUpperInvariant(),
                            reasons = verdict.Reasons.Select(reason => SanitizeReason(reason, sandbox)),
                            latency_ms = verdict.Latency.TotalMilliseconds
                        })).ConfigureAwait(false);
                        await output.FlushAsync().ConfigureAwait(false);
                    }
                }
            return 0;
        }
        finally
        {
            foreach (var tracked in _startedProcesses)
            {
                try
                {
                    using var current = Process.GetProcessById(tracked.Process.Id);
                    if (current.StartTime == tracked.StartTime && !current.HasExited)
                        current.Kill(entireProcessTree: true);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    public async Task DiagnoseAsync(string caseId, CancellationToken cancellationToken = default)
    {
        var evalCase = EvalTaskCatalog.ExpandCases(
                EvalTaskCatalog.Generate(EvalTaskCatalog.DefaultSeed).Where(task => task.ToolId == "fs.create_file").ToArray(),
                1,
                EvalTaskCatalog.DefaultSeed)
            .Single(evalCase => evalCase.Id == caseId);
        var runId = $"diagnose-{Guid.NewGuid():N}";
        var sandbox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis", "eval-sandbox", runId);
        Directory.CreateDirectory(sandbox);
        try
        {
            var result = await RunCaseAsync(evalCase, sandbox, cancellationToken, realScheduler: false, diagnostics: true).ConfigureAwait(false);
            foreach (var verdict in result.Verdicts)
            {
                Console.WriteLine($"verifier={verdict.VerifierId} status={verdict.Status}");
                foreach (var reason in verdict.Reasons)
                    Console.WriteLine(reason);
            }
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    private static HashSet<string> LoadCompleted(string rawPath)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(rawPath))
            return completed;
        foreach (var line in File.ReadLines(rawPath))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("case_id", out var caseId) && root.TryGetProperty("verifier", out var verifier))
                completed.Add($"{caseId.GetString()}:{verifier.GetString()}");
        }
        return completed;
    }

    private static string SanitizeReason(string reason, string sandbox) =>
        reason.Replace(sandbox, "<sandbox>", StringComparison.OrdinalIgnoreCase);

    private async Task<EvalRunResult> RunCaseAsync(
        EvalCase evalCase,
        string sandbox,
        CancellationToken cancellationToken,
        bool realScheduler = false,
        bool diagnostics = false)
    {
        var root = Path.Combine(sandbox, evalCase.Id);
        Directory.CreateDirectory(root);
        var auditStore = new InMemoryAuditStore();
        var keyStore = new InMemoryDpapiKeyStore();
        var reminderLocation = new EvalReminderDataLocation(Path.Combine(root, "reminders"));
        var reminderStore = new ReminderDataFileStore(reminderLocation);
        var authService = new AuthorizationService(
            keyStore, SystemClock.Instance, auditStore,
            new EvalHostPathProvider(Path.Combine(root, "Jarvis.Cli.exe")),
            reminderLocation);
        var policy = LoadToolPolicy();
        var target = Path.Combine(root, evalCase.ToolId == "fs.create_folder" ? "folder" : evalCase.TaskId + ".txt");
        EvalFakeReminderScheduler? fakeScheduler = evalCase.ToolId == "task.schedule_reminder" && !realScheduler
            ? new EvalFakeReminderScheduler(evalCase.FaultId, reminderStore.BaseDirectory, evalCase.TaskId)
            : null;
        IReminderTaskScheduler? scheduler = fakeScheduler is not null
            ? fakeScheduler
            : evalCase.ToolId == "task.schedule_reminder" ? new WindowsReminderTaskScheduler() : null;
        IToolExecutor inner = evalCase.ToolId switch
        {
            "fs.create_file" => new CreateFileExecutor(new PathPolicy([root]), authService),
            "fs.create_folder" => new CreateFolderExecutor(new PathPolicy([root]), authService),
            "fs.search_files" => new SearchFilesExecutor(new PathPolicy([root]), authService),
            "app.launch" => new AppLaunchExecutor(policy, authService, new WindowsElevationChecker()),
            "task.schedule_reminder" => new TaskScheduleReminderExecutor(authService, scheduler!, dataFileStore: reminderStore, hostPathProvider: new EvalHostPathProvider(Path.Combine(root, "Jarvis.Cli.exe"))),
            _ => throw new ArgumentException($"Unsupported W1-W3 tool '{evalCase.ToolId}'.", nameof(evalCase))
        };
        IToolExecutor executor = evalCase.ToolId == "task.schedule_reminder"
            || (evalCase.ToolId == "app.launch" && evalCase.FaultId != "F18") || (evalCase.FaultId == "F0" || evalCase.FaultId == "F18")
            ? inner
            : new EvalFaultInjectingExecutor(inner, evalCase.FaultId, Path.Combine(root, "unauthorized-extra.txt"), process => _startedProcesses.Add((process, process.StartTime)));
        if (evalCase.ToolId == "app.launch" && evalCase.FaultId != "F18")
            executor = new EvalFaultInjectingExecutor(inner, evalCase.FaultId, Path.Combine(root, "unused"), process => _startedProcesses.Add((process, process.StartTime)));
        var stepExecutor = new StepExecutor(authService, [executor], policy);
        var args = evalCase.ToolId switch
        {
            "fs.create_file" => new Dictionary<string, object?> { ["path"] = target, ["content"] = "evaluation" },
            "fs.create_folder" => new Dictionary<string, object?> { ["path"] = target },
            "fs.search_files" => new Dictionary<string, object?>
            {
                ["root_path"] = Path.Combine(root, "search"),
                ["pattern"] = "*.txt",
                ["max_depth"] = 5,
                ["max_results"] = 100
            },
            "app.launch" => new Dictionary<string, object?> { ["app_id"] = "notepad" },
            "task.schedule_reminder" => new Dictionary<string, object?> { ["task_name"] = $"Evaluation reminder {evalCase.TaskId}", ["time_iso"] = ReminderTime().ToString("O"), ["message"] = $"Evaluation reminder {evalCase.TaskId}" },
            _ => throw new ArgumentException($"Unsupported W1-W3 tool '{evalCase.ToolId}'.", nameof(evalCase))
        };
        if (evalCase.ToolId == "fs.search_files")
        {
            Directory.CreateDirectory((string)args["root_path"]!);
            await File.WriteAllTextAsync(Path.Combine((string)args["root_path"]!, "match.txt"), "search", cancellationToken).ConfigureAwait(false);
        }
        var authorization = stepExecutor.PrepareAuthorization(
            evalCase.TaskId, "step-1", evalCase.ToolId, args, "low", "auto-low");
        var engine = fakeScheduler is null
            ? new VerificationEngine(authService, auditStore)
            : new VerificationEngine(authService, auditStore, SystemClock.Instance, 10_000, null, null, fakeScheduler);
        var token = await engine.BeginStepAsync(authorization, cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        var receipt = await stepExecutor.ExecuteStepAsync(authorization, cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;
        if (evalCase.FaultId == "F6")
            File.Delete(target);
        var context = new EvalExecutionContext(
            authorization, receipt, new ExecutionObservation(startedAt, completedAt), token, root,
            _ => ValueTask.FromResult(evalCase.ToolId == "fs.create_folder"
                ? Directory.Exists(target)
                : evalCase.ToolId == "fs.create_file"
                    ? File.Exists(target) && File.ReadAllText(target) == "evaluation"
                    : true),
            evalCase.ToolId == "fs.search_files"
                ? (string.IsNullOrWhiteSpace(receipt.Stdout)
                    ? []
                    : JsonSerializer.Deserialize<string[]>(receipt.Stdout) ?? [])
                : [],
            fakeScheduler);
        var verifiers = new IEvalVerifier[]
        {
            new ReceiptEvalVerifier(),
            new SelfCheckEvalVerifier(),
            new PredicateOnlyEvalVerifier(new FileSystemEvalPredicateChecker()),
            new VerificationEngineEvalVerifier(engine, diagnostics)
        };
        return await new EvalRunner(evalCase.Seed)
            .RunCaseAsync(evalCase, _ => Task.FromResult(context), verifiers, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ToolPolicy LoadToolPolicy()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "config", "tools.registry.json")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Cannot locate the repository tool registry.");
        return ToolPolicy.FromFile(
            Path.Combine(directory.FullName, "config", "tools.registry.json"),
            Path.Combine(directory.FullName, "config", "apps.allowlist.json"));
    }

    private static DateTimeOffset ReminderTime()
    {
        var now = DateTimeOffset.UtcNow.AddHours(1);
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero);
    }

    private sealed class EvalHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}
