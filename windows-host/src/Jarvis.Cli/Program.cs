using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using System.Windows.Forms;

namespace Jarvis.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length > 0 && args[0] == "--jarvis-reminder")
        {
            try
            {
                return new ReminderDispatcher(
                    new ReminderDataFileStore(),
                    new WindowsReminderNotifier()).Dispatch(args);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine($"Reminder dispatch failed: {exception.Message}");
                return 1;
            }
        }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "run" => await RunCommandAsync(args.Skip(1).ToArray()),
                "history" => await HistoryCommandAsync(),
                "doctor" => await DoctorCommandAsync(),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FATAL] Command execution failed: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Jarvis CLI - Windows Assistant");
        Console.WriteLine("Usage: jarvis <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  run <text>   Execute a natural language task end-to-end");
        Console.WriteLine("  history      Display audit trail of recent tasks and transitions");
        Console.WriteLine("  doctor       Run diagnostic health checks on host environment");
        Console.WriteLine();
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown command '{command}'.");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    public static async Task<int> RunCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Error: 'run' requires a task description text.");
            Console.WriteLine("Example: jarvis run \"create folder my_folder\"");
            return 1;
        }

        var text = string.Join(" ", args);
        Console.WriteLine($"[JARVIS] Initiating task: \"{text}\"");

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var configDir = Path.Combine(repoRoot, "config");
        var toolsRegistryPath = Path.Combine(configDir, "tools.registry.json");
        var appsAllowlistPath = Path.Combine(configDir, "apps.allowlist.json");

        // 1. Launch AI Server child process (Job Object bound)
        Console.WriteLine("[JARVIS] Launching AI server (127.0.0.1 loopback)...");
        await using var serverProcess = await AiServerLauncher.LaunchAsync();
        Console.WriteLine($"[JARVIS] AI server active on port {serverProcess.Port}.");

        // 2. Setup Audit Store & DPAPI
        var dbPath = Path.Combine(repoRoot, "audit.db");
        using var auditStore = new SqliteAuditStore(dbPath);
        IDpapiKeyStore keyStore = OperatingSystem.IsWindows() ? new DpapiKeyStore() : new InMemoryDpapiKeyStore();
        var authService = new AuthorizationService(keyStore, SystemClock.Instance, auditStore);

        // 3. Setup Policies & Safety
        var pathPolicy = new PathPolicy();
        var toolPolicy = ToolPolicy.FromFile(toolsRegistryPath, appsAllowlistPath);
        var safetyGate = new SafetyGate(toolPolicy, authService, pathPolicy: pathPolicy, auditStore: auditStore);

        // 4. Setup Executors
        var executors = new IToolExecutor[]
        {
            new CreateFileExecutor(pathPolicy, authService),
            new CreateFolderExecutor(pathPolicy, authService),
            new SearchFilesExecutor(pathPolicy, authService),
            new AppLaunchExecutor(toolPolicy, authService, new WindowsElevationChecker()),
            new TaskScheduleReminderExecutor(authService, new WindowsReminderTaskScheduler())
        };

        var stepExecutor = new StepExecutor(authService, executors, toolPolicy);
        var approvalHandler = new ConsoleApprovalHandler();
        var stubVerifier = new StubVerifier();

        // 5. Setup ApiClient and Planner
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(95) };
        var apiClient = new ApiClient(httpClient, serverProcess.BaseUrl, serverProcess.Token);
        var planner = new ApiClientPlanner(apiClient);

        // 6. Setup ExecutionQueue and TaskOrchestrator
        await using var queue = new ExecutionQueue();
        var orchestrator = new TaskOrchestrator(
            planner,
            safetyGate,
            approvalHandler,
            queue,
            stepExecutor,
            stubVerifier,
            auditStore);

        // 7. Run Orchestrator
        var result = await orchestrator.RunTaskAsync(text);

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("                     TASK SUMMARY");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Task ID:     {result.TaskId}");
        Console.WriteLine($"Final State: {result.FinalState} (Terminal)");
        Console.WriteLine($"Status:      {result.StatusMessage}");

        foreach (var receipt in result.Receipts)
        {
            Console.WriteLine($"[Receipt] Step: {receipt.StepId}, ExitCode: {receipt.ExitCode}, Success: {receipt.ReportedSuccess}");
            if (!string.IsNullOrEmpty(receipt.Stdout)) Console.WriteLine($"  Stdout: {receipt.Stdout}");
            if (!string.IsNullOrEmpty(receipt.Stderr)) Console.WriteLine($"  Stderr: {receipt.Stderr}");
        }

        foreach (var verification in result.VerificationResults)
        {
            Console.WriteLine($"[Verification] Step: {verification.StepId}, Status: {verification.Status}");
        }

        Console.WriteLine("============================================================");

        // Invariant: StubVerifier always returns ESCALATE, so print "not verified" and never "completed"
        if (result.FinalState == TaskState.Completed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[INVARIANT VIOLATION] Task completed without independent verification!");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[STATUS] Result: not verified (Phase 2b StubVerifier requires escalation)");
        Console.ResetColor();

        return 0;
    }

    public static Task<int> HistoryCommandAsync()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var dbPath = Path.Combine(repoRoot, "audit.db");

        if (!File.Exists(dbPath))
        {
            Console.WriteLine("No history found (audit.db does not exist yet).");
            return Task.FromResult(0);
        }

        using var auditStore = new SqliteAuditStore(dbPath);
        var transitions = auditStore.GetStateTransitions();
        var safetyDecisions = auditStore.GetSafetyDecisions();

        Console.WriteLine("============================================================");
        Console.WriteLine("                   TASK AUDIT HISTORY");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Total State Transitions Recorded: {transitions.Count}");
        Console.WriteLine($"Total Safety Decisions Recorded:   {safetyDecisions.Count}");
        Console.WriteLine();

        Console.WriteLine("Recent State Transitions:");
        foreach (var t in transitions.TakeLast(15))
        {
            Console.WriteLine($"  [{t.Timestamp:yyyy-MM-dd HH:mm:ss}] {t.TaskId,-14} {t.FromState} -> {t.ToState} ({t.Reason ?? "none"})");
        }

        Console.WriteLine();
        Console.WriteLine("Recent Safety Decisions:");
        foreach (var s in safetyDecisions.TakeLast(10))
        {
            Console.WriteLine($"  [{s.Timestamp:yyyy-MM-dd HH:mm:ss}] {s.TaskId,-14} Step: {s.StepId} Tool: {s.ToolId} Risk: {s.FinalRisk} Approved: {s.Approved} ({s.ApprovedBy})");
        }
        Console.WriteLine("============================================================");

        return Task.FromResult(0);
    }

    public static async Task<int> DoctorCommandAsync()
    {
        Console.WriteLine("============================================================");
        Console.WriteLine("                  JARVIS DOCTOR DIAGNOSTIC");
        Console.WriteLine("============================================================");

        bool allOk = true;

        // 1. Operating System
        var osVersion = Environment.OSVersion.VersionString;
        var isWindows = OperatingSystem.IsWindows();
        var isElevated = isWindows && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Console.WriteLine($"[{(isWindows && !isElevated ? "PASS" : "WARN")}] OS: {osVersion} (Elevated: {isElevated})");
        if (isElevated)
        {
            Console.WriteLine("       Warning: Jarvis is designed to run non-elevated.");
        }

        // 2. .NET Runtime
        var dotnetVersion = Environment.Version.ToString();
        Console.WriteLine($"[PASS] .NET Runtime: {dotnetVersion}");

        // 3. Python Virtual Environment
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var venvPython = Path.Combine(repoRoot, "ai-server", ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
        {
            Console.WriteLine($"[PASS] Python venv found at: {venvPython}");
        }
        else
        {
            Console.WriteLine($"[FAIL] Python venv not found at: {venvPython}");
            allOk = false;
        }

        // 4. Ollama Server
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ollamaRes = await http.GetAsync("http://127.0.0.1:11434/api/tags");
            if (ollamaRes.IsSuccessStatusCode)
            {
                Console.WriteLine("[PASS] Ollama server is active on 127.0.0.1:11434");
            }
            else
            {
                Console.WriteLine($"[WARN] Ollama server returned {(int)ollamaRes.StatusCode}");
            }
        }
        catch
        {
            Console.WriteLine("[WARN] Ollama server is not running on 127.0.0.1:11434 (Rule fast-path available)");
        }

        // 5. DPAPI Key Store
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var keyStore = new DpapiKeyStore();
                var key = keyStore.GetOrCreateKey();
                if (key.Length == 32)
                {
                    Console.WriteLine("[PASS] Windows DPAPI key store operational (256-bit key verified)");
                }
                else
                {
                    Console.WriteLine("[FAIL] DPAPI key size mismatch");
                    allOk = false;
                }
            }
            else
            {
                Console.WriteLine("[WARN] Non-Windows OS: Windows DPAPI not available, fallback in-memory store used.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] DPAPI key store error: {ex.Message}");
            allOk = false;
        }

        // 6. Path Policy Allowed Roots
        var pathPolicy = new PathPolicy();
        Console.WriteLine($"[PASS] Allowed roots configured ({pathPolicy.AllowedRoots.Count}):");
        foreach (var r in pathPolicy.AllowedRoots)
        {
            var exists = Directory.Exists(r);
            var inOneDrive = r.Contains($"{Path.DirectorySeparatorChar}OneDrive{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            var status = !exists || inOneDrive ? "WARN" : "PASS";
            Console.WriteLine($"[{status}] Root: {r} (Exists: {exists}, OneDrive: {inOneDrive})");
            if (!exists || inOneDrive)
                Console.WriteLine("       Use a local, non-synced JARVIS_ALLOWED_ROOT for tests and evaluation.");
        }

        // 7. SQLite Audit Store
        try
        {
            var testDb = Path.Combine(Path.GetTempPath(), $"jarvis_doctor_{Guid.NewGuid():N}.db");
            using (var store = new SqliteAuditStore(testDb))
            {
                await store.RecordStateTransitionAsync(new StateTransitionRecord("test-task", "created", "context_resolved", "doctor check", DateTimeOffset.UtcNow));
                var records = store.GetStateTransitions("test-task");
                if (records.Count == 1)
                {
                    Console.WriteLine("[PASS] SQLite audit engine operational");
                }
            }
            if (File.Exists(testDb)) File.Delete(testDb);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SQLite audit test failed: {ex.Message}");
            allOk = false;
        }

        Console.WriteLine("============================================================");
        Console.WriteLine(allOk ? "[RESULT] All core health checks PASSED." : "[RESULT] Some diagnostics reported issues.");
        return allOk ? 0 : 1;
    }
}
