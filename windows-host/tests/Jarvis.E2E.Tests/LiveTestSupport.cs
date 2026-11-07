using System.Diagnostics;
using System.Net.Http;
using Jarvis.Infrastructure;

namespace Jarvis.E2E.Tests;

internal sealed class LiveTestScope : IDisposable
{
    public string RootPath { get; }
    public string WorkspacePath { get; }

    public LiveTestScope()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("LOCALAPPDATA is required for live-test isolation.");

        var liveParent = Path.Combine(localAppData, "Jarvis", "workspace", "live-tests");
        RootPath = Path.Combine(liveParent, Guid.NewGuid().ToString("N"));
        WorkspacePath = Path.Combine(RootPath, "workspace");
        Directory.CreateDirectory(WorkspacePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}

internal sealed class LiveTestAiServer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly int _processId;
    private readonly DateTime _startTime;
    private bool _disposed;

    public int Port { get; }
    public string Token { get; }

    private LiveTestAiServer(Process process, int port, string token)
    {
        _process = process;
        _processId = process.Id;
        _startTime = process.StartTime;
        Port = port;
        Token = token;
    }

    public static async Task<LiveTestAiServer> StartAsync(string repoRoot, string liveRoot)
    {
        var pythonPath = Path.Combine(repoRoot, "ai-server", ".venv", "Scripts", "python.exe");
        if (!File.Exists(pythonPath))
            throw new FileNotFoundException("The prepared AI-server Python environment is required.", pythonPath);

        var port = AiServerLauncher.GetFreeLoopbackPort();
        var token = AiServerLauncher.GenerateSecureToken();
        var serverInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = Path.Combine(repoRoot, "ai-server"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        serverInfo.ArgumentList.Add("-m");
        serverInfo.ArgumentList.Add("uvicorn");
        serverInfo.ArgumentList.Add("jarvis_ai.api.main:app");
        serverInfo.ArgumentList.Add("--host");
        serverInfo.ArgumentList.Add("127.0.0.1");
        serverInfo.ArgumentList.Add("--port");
        serverInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        serverInfo.ArgumentList.Add("--no-access-log");
        serverInfo.Environment["JARVIS_TOKEN"] = token;
        serverInfo.Environment["JARVIS_SESSION_DB_PATH"] = Path.Combine(liveRoot, "session-memory.db");
        serverInfo.Environment["PYTHONPATH"] = Path.Combine(repoRoot, "ai-server", "src");
        serverInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        serverInfo.Environment["TEMP"] = liveRoot;
        serverInfo.Environment["TMP"] = liveRoot;

        var process = Process.Start(serverInfo)
            ?? throw new InvalidOperationException("Could not start the local AI server process.");
        var server = new LiveTestAiServer(process, port, token);
        try
        {
            await server.WaitUntilHealthyAsync(liveRoot);
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                if (_process.Id != _processId || _process.StartTime != _startTime)
                    throw new InvalidOperationException("Refusing to stop a process that no longer matches the launched AI server.");

                _process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(timeout.Token);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task WaitUntilHealthyAsync(string liveRoot)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Local AI server exited with code {_process.ExitCode}.");

            try
            {
                using var response = await httpClient.GetAsync($"http://127.0.0.1:{Port}/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Local AI server did not become healthy within 30 seconds; live data: {liveRoot}");
    }
}
