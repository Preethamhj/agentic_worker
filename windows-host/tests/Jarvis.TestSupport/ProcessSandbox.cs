using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.TestSupport;

/// <summary>Starts test helper processes and only terminates the exact processes it started.</summary>
public sealed class ProcessSandbox : IDisposable
{
    private readonly List<TrackedProcess> _processes = [];
    private bool _disposed;

    /// <summary>Starts Jarvis.TestApp with the supplied arguments and tracks its process identity.</summary>
    public Process Start(params string[] arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(TestApps.Locate())
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Jarvis.TestApp.");
        _processes.Add(new TrackedProcess(process, process.Id, process.StartTime.ToUniversalTime()));
        return process;
    }

    /// <summary>Tracks a process started by an executor if its image is the helper app.</summary>
    public void Track(Process process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);
        process.Refresh();

        var imagePath = process.MainModule?.FileName;
        var helperPath = Path.GetFullPath(TestApps.Locate());
        var expectedProcessName = Path.GetFileNameWithoutExtension(helperPath);
        if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(imagePath)
                && !string.Equals(Path.GetFullPath(imagePath), helperPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ProcessSandbox can track only Jarvis.TestApp processes.");
        }

        if (_processes.Any(tracked => tracked.Id == process.Id))
            throw new InvalidOperationException($"Process {process.Id} is already tracked.");

        _processes.Add(new TrackedProcess(process, process.Id, process.StartTime.ToUniversalTime()));
    }

    /// <summary>Stops a tracked process only when its PID and start time still match.</summary>
    public void Stop(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var tracked = _processes.SingleOrDefault(item => ReferenceEquals(item.Process, process))
            ?? throw new InvalidOperationException("ProcessSandbox can stop only a tracked process.");
        KillIfIdentityMatches(tracked);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        List<Exception>? failures = null;
        foreach (var tracked in _processes)
        {
            try
            {
                KillIfIdentityMatches(tracked);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or Win32Exception
                or TimeoutException)
            {
                (failures ??= []).Add(exception);
            }
            finally
            {
                tracked.Process.Dispose();
            }
        }

        _processes.Clear();
        if (failures is not null)
            throw new AggregateException("One or more tracked test helper processes could not be stopped.", failures);
    }

    private static void KillIfIdentityMatches(TrackedProcess tracked)
    {
        Process current;
        try
        {
            current = Process.GetProcessById(tracked.Id);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (current)
        {
            if (current.Id != tracked.Id
                || current.StartTime.ToUniversalTime() != tracked.StartTime
                || current.HasExited)
            {
                return;
            }

            current.Kill(entireProcessTree: true);
            if (!current.WaitForExit(5_000))
                throw new TimeoutException($"Jarvis.TestApp process {tracked.Id} did not exit within five seconds.");
        }
    }

    private sealed record TrackedProcess(Process Process, int Id, DateTime StartTime);
}
