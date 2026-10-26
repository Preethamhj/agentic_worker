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
}

public sealed class EvalFileRunner
{
    private readonly List<int> _startedProcessIds = [];

    public async Task<int> RunAsync(EvalFileRunOptions options, CancellationToken cancellationToken = default)
    {
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
                row_type = "metadata", run_id = runId, os_build = Environment.OSVersion.VersionString,
                dotnet_version = Environment.Version.ToString(), git_commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown",
                seed = options.Seed, timestamp = DateTimeOffset.UtcNow
            })).ConfigureAwait(false);

            var tasks = EvalTaskCatalog.Generate(options.Seed)
                .Where(task => options.ToolFilter is null || task.ToolId == options.ToolFilter)
                .Take(options.Smoke ? 2 : int.MaxValue)
                .ToArray();
            var repetitions = options.Smoke ? 1 : options.Repetitions;
            foreach (var repetition in Enumerable.Range(1, repetitions))
            foreach (var evalCase in EvalTaskCatalog.ExpandCases(tasks, repetition, options.Seed))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await RunCaseAsync(evalCase, sandbox, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                foreach (var verdict in result.Verdicts)
                {
                    if (completed.Contains($"{evalCase.Id}:{verdict.VerifierId}"))
                        continue;
                    await output.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        run_id = runId, case_id = evalCase.Id, task_id = evalCase.TaskId,
                        tool = evalCase.ToolId, fault = evalCase.FaultId, ground_truth = evalCase.GroundTruth.ToString().ToUpperInvariant(),
                        rep = evalCase.Repetition, verifier = verdict.VerifierId, status = verdict.Status.ToString().ToUpperInvariant(),
                        reasons = verdict.Reasons, latency_ms = verdict.Latency.TotalMilliseconds
                    })).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                }
            }
            return 0;
        }
        finally
        {
            foreach (var processId in _startedProcessIds)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
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
            var result = await RunCaseAsync(evalCase, sandbox, cancellationToken, diagnostics: true).ConfigureAwait(false);
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

    private async Task<EvalRunResult> RunCaseAsync(
        EvalCase evalCase,
        string sandbox,
        CancellationToken cancellationToken,
        bool diagnostics = false)
    {
        var root = Path.Combine(sandbox, evalCase.Id);
        Directory.CreateDirectory(root);
        var auditStore = new InMemoryAuditStore();
        var keyStore = new InMemoryDpapiKeyStore();
        var authService = new AuthorizationService(
            keyStore, SystemClock.Instance, auditStore,
            new EvalHostPathProvider(Path.Combine(root, "Jarvis.Eval.exe")));
        var policy = LoadToolPolicy();
        var target = Path.Combine(root, evalCase.ToolId == "fs.create_folder" ? "folder" : evalCase.TaskId + ".txt");
        IToolExecutor inner = evalCase.ToolId switch
        {
            "fs.create_file" => new CreateFileExecutor(new PathPolicy([root]), authService),
            "fs.create_folder" => new CreateFolderExecutor(new PathPolicy([root]), authService),
            "fs.search_files" => new SearchFilesExecutor(new PathPolicy([root]), authService),
            "app.launch" => new AppLaunchExecutor(policy, authService, new WindowsElevationChecker()),
            _ => throw new ArgumentException($"Unsupported W1-W3 tool '{evalCase.ToolId}'.", nameof(evalCase))
        };
        IToolExecutor executor = (evalCase.ToolId == "app.launch" && evalCase.FaultId != "F18") || (evalCase.FaultId == "F0" || evalCase.FaultId == "F18")
            ? inner
            : new EvalFaultInjectingExecutor(inner, evalCase.FaultId, Path.Combine(root, "unauthorized-extra.txt"), processId => _startedProcessIds.Add(processId));
        if (evalCase.ToolId == "app.launch" && evalCase.FaultId != "F18")
            executor = new EvalFaultInjectingExecutor(inner, evalCase.FaultId, Path.Combine(root, "unused"), processId => _startedProcessIds.Add(processId));
        var stepExecutor = new StepExecutor(authService, [executor], policy);
        var args = evalCase.ToolId switch
        {
            "fs.create_file" => new Dictionary<string, object?> { ["path"] = target, ["content"] = "evaluation" },
            "fs.create_folder" => new Dictionary<string, object?> { ["path"] = target },
            "fs.search_files" => new Dictionary<string, object?>
            {
                ["root_path"] = Path.Combine(root, "search"), ["pattern"] = "*.txt", ["max_depth"] = 5, ["max_results"] = 100
            },
            "app.launch" => new Dictionary<string, object?> { ["app_id"] = "notepad" },
            _ => throw new ArgumentException($"Unsupported W1-W3 tool '{evalCase.ToolId}'.", nameof(evalCase))
        };
        if (evalCase.ToolId == "fs.search_files")
        {
            Directory.CreateDirectory((string)args["root_path"]!);
            await File.WriteAllTextAsync(Path.Combine((string)args["root_path"]!, "match.txt"), "search", cancellationToken).ConfigureAwait(false);
        }
        var authorization = stepExecutor.PrepareAuthorization(
            evalCase.TaskId, "step-1", evalCase.ToolId, args, "low", "auto-low");
        var engine = new VerificationEngine(authService, auditStore);
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
                ? JsonSerializer.Deserialize<string[]>(receipt.Stdout) ?? []
                : []);
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

    private sealed class EvalHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}
