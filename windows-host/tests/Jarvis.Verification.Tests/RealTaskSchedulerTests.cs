using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Microsoft.Win32.TaskScheduler;
using Xunit;

namespace Jarvis.Verification.Tests;

[Trait("Category", "RealScheduler")]
public sealed class RealTaskSchedulerTests
{
    [RealSchedulerFact]
    public void Registered_Task_Xml_Matches_Every_Signed_Scheduler_Setting()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "Jarvis.RealSchedulerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        var dataLocation = new FixedReminderDataLocation(dataDirectory);
        var clock = new FixedClock();
        var hostPath = Path.GetFullPath(Environment.ProcessPath
            ?? throw new InvalidOperationException("The test host process path is unavailable."));
        var service = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            clock,
            hostPathProvider: new FixedHostPathProvider(hostPath),
            reminderDataLocation: dataLocation);
        var dataStore = new ReminderDataFileStore(dataLocation);
        var scheduler = new WindowsReminderTaskScheduler();
        var record = service.IssueAuthorization(
            "real-scheduler-test",
            "real-scheduler-test-step",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Real scheduler contract test",
                ["time_iso"] = clock.UtcNow.AddMinutes(30).ToString("O", CultureInfo.InvariantCulture),
                ["message"] = "This task must not fire during the test."
            });
        var taskPath = (string)record.Expected["task_path"]!;
        var taskId = (string)record.Expected["task_id"]!;
        var registered = false;

        try
        {
            var executor = new TaskScheduleReminderExecutor(
                service,
                scheduler,
                clock,
                dataStore,
                new FixedHostPathProvider(hostPath));
            var receipt = executor.ExecuteAsync(service.Consume(record, record.StepId)).GetAwaiter().GetResult();
            receipt.ReportedSuccess.Should().BeTrue(receipt.Stderr);
            registered = true;

            using var taskService = new TaskService();
            using var task = taskService.GetTask(taskPath);
            task.Should().NotBeNull();
            var xml = XDocument.Parse(task!.Xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var expected = record.Expected;
            var taskName = (string)expected["scheduler_task_name"]!;
            var triggerTime = DateTimeOffset.Parse(
                (string)expected["time_iso"]!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

            task.Path.Should().Be(taskPath);
            task.Name.Should().Be(taskName);
            Element(xml, ns, "StartBoundary").Value.Should().Be(triggerTime.ToLocalTime().ToString("s", CultureInfo.InvariantCulture));
            Element(xml, ns, "EndBoundary").Value.Should().Be(
                triggerTime.AddSeconds((int)expected["end_boundary_offset_seconds"]!)
                    .ToLocalTime().ToString("s", CultureInfo.InvariantCulture));
            Element(xml, ns, "Command").Value.Should().Be((string)expected["action_path"]!);
            Element(xml, ns, "Arguments").Value.Should().Be((string)expected["action_arguments"]!);
            Element(xml, ns, "DisallowStartIfOnBatteries").Value.Should().Be(
                ((bool)expected["disallow_start_if_on_batteries"]!).ToString().ToLowerInvariant());
            Element(xml, ns, "StopIfGoingOnBatteries").Value.Should().Be(
                ((bool)expected["stop_if_going_on_batteries"]!).ToString().ToLowerInvariant());
            Element(xml, ns, "ExecutionTimeLimit").Value.Should().Be(
                XmlConvert.ToString(TimeSpan.FromSeconds((int)expected["execution_time_limit_seconds"]!)));
            Element(xml, ns, "StartWhenAvailable").Value.Should().Be(
                ((bool)expected["start_when_available"]!).ToString().ToLowerInvariant());
            Element(xml, ns, "MultipleInstancesPolicy").Value.Should().Be((string)expected["multiple_instances_policy"]!);
            Element(xml, ns, "LogonType").Value.Should().Be((string)expected["logon_type"]!);
            Element(xml, ns, "RunLevel").Value.Should().Be((string)expected["run_level"]!);
            Element(xml, ns, "DeleteExpiredTaskAfter").Value.Should().Be(
                XmlConvert.ToString(TimeSpan.FromSeconds((int)expected["delete_expired_task_after_seconds"]!)));
        }
        finally
        {
            if (registered)
                scheduler.Delete(taskPath);
            dataStore.Delete(taskId);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static XElement Element(XDocument xml, XNamespace ns, string name) =>
        xml.Descendants(ns + name).Single();

    private sealed class FixedReminderDataLocation(string directoryPath) : IReminderDataLocation
    {
        public string DirectoryPath => directoryPath;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class FixedHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}

public sealed class RealSchedulerFactAttribute : FactAttribute
{
    public RealSchedulerFactAttribute()
    {
        Skip = RealSchedulerFactAttributeHelper.GetSkipReason();
    }
}

public sealed class RealSchedulerTheoryAttribute : TheoryAttribute
{
    public RealSchedulerTheoryAttribute()
    {
        Skip = RealSchedulerFactAttributeHelper.GetSkipReason();
    }
}

internal static class RealSchedulerFactAttributeHelper
{
    public static string? GetSkipReason()
    {
        if (!OperatingSystem.IsWindows())
            return "Real Task Scheduler tests require Windows.";
        if (!string.Equals(Environment.GetEnvironmentVariable("JARVIS_REAL_SCHEDULER"), "1", StringComparison.Ordinal))
            return "Set JARVIS_REAL_SCHEDULER=1 to opt in to real Task Scheduler access.";
        return null;
    }
}
