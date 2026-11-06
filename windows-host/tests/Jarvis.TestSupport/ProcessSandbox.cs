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
