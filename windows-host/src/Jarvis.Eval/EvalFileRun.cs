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
                .Where(task => task.ToolId == "fs.create_file")
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

    private static async Task<EvalRunResult> RunCaseAsync(
        EvalCase evalCase,
        string sandbox,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(sandbox, evalCase.Id);
        Directory.CreateDirectory(root);
        var auditPath = Path.Combine(root, "audit.db");
        using var auditStore = new SqliteAuditStore(auditPath);
        var keyStore = new InMemoryDpapiKeyStore();
        var authService = new AuthorizationService(
            keyStore, SystemClock.Instance, auditStore,
            new EvalHostPathProvider(Path.Combine(root, "Jarvis.Eval.exe")));
        var policy = LoadToolPolicy();
        var target = Path.Combine(root, evalCase.TaskId + ".txt");
        var inner = new CreateFileExecutor(new PathPolicy([root]), authService);
        IToolExecutor executor = evalCase.FaultId == "F0" || evalCase.FaultId == "F18"
            ? inner
            : new EvalFaultInjectingExecutor(inner, evalCase.FaultId, Path.Combine(root, "unauthorized-extra.txt"));
        var stepExecutor = new StepExecutor(authService, [executor], policy);
        var authorization = stepExecutor.PrepareAuthorization(
            evalCase.TaskId, "step-1", "fs.create_file",
            new Dictionary<string, object?> { ["path"] = target, ["content"] = "evaluation" }, "low", "auto-low");
        var engine = new VerificationEngine(authService, auditStore);
        var token = await engine.BeginStepAsync(authorization, cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        var receipt = await stepExecutor.ExecuteStepAsync(authorization, cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;
        if (evalCase.FaultId == "F6")
            File.Delete(target);
        var context = new EvalExecutionContext(
            authorization, receipt, new ExecutionObservation(startedAt, completedAt), token, root,
            _ => ValueTask.FromResult(File.Exists(target) && File.ReadAllText(target) == "evaluation"), []);
        var verifiers = new IEvalVerifier[]
        {
            new ReceiptEvalVerifier(),
            new SelfCheckEvalVerifier(),
            new PredicateOnlyEvalVerifier(new FileSystemEvalPredicateChecker()),
            new VerificationEngineEvalVerifier(engine)
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
