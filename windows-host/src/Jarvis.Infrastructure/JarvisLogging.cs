using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Jarvis.Infrastructure;

/// <summary>
/// Serilog setup configured with Compact JSON Formatter and correlation ID enrichment
/// (trace_id, task_id, step_id) as required by DESIGN.md section 10.
/// </summary>
public static class JarvisLogging
{
    public static ILogger CreateLogger(string? logDirectory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var logDir = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "logs");
        try
        {
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            logDir = Path.GetTempPath();
        }

        var logFilePath = Path.Combine(logDir, "host-.jsonl");

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("application", "Jarvis.WindowsHost")
            .WriteTo.File(
                new CompactJsonFormatter(),
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    public static ILogger ForTask(this ILogger logger, string? traceId, string? taskId, string? stepId = null)
    {
        var contextual = logger;
        if (!string.IsNullOrEmpty(traceId))
        {
            contextual = contextual.ForContext("trace_id", traceId);
        }
        if (!string.IsNullOrEmpty(taskId))
        {
            contextual = contextual.ForContext("task_id", taskId);
        }
        if (!string.IsNullOrEmpty(stepId))
        {
            contextual = contextual.ForContext("step_id", stepId);
        }
        return contextual;
    }
}
