using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Verification.Tests;

/// <summary>Tests the deterministic payload-directory boundary without Task Scheduler access.</summary>
public sealed class ReminderDataLocationContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registration_Composition_Shares_One_Injected_Location_Instance()
    {
        var directory = CreateTempDirectory();
        try
        {
            var location = new FixedReminderDataLocation(directory);
            var service = CreateService(location);
            var store = new ReminderDataFileStore(location);
            var executor = new TaskScheduleReminderExecutor(
                service,
                new RecordingScheduler(),
                new FixedClock(),
                store,
                new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"));

            service.ReminderDataLocation.Should().BeSameAs(location);
            store.Location.Should().BeSameAs(location);
            executor.ReminderDataFileStore.Location.Should().BeSameAs(location);
            executor.ReminderDataFileStore.BaseDirectory.Should().Be(Path.GetFullPath(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(OutsideBaseLocations))]
    public async Task Executor_Rejects_Signed_Reminder_Data_Path_Outside_Its_Injected_Base(
        string caseName,
        Func<string, string> outsideDirectory)
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            var signedLocation = new FixedReminderDataLocation(outsideDirectory(baseDirectory));
            var executionLocation = new FixedReminderDataLocation(baseDirectory);
            var service = CreateService(signedLocation);
            var record = IssueReminder(service, caseName);
            var scheduler = new RecordingScheduler();
            var executor = new TaskScheduleReminderExecutor(
                service,
                scheduler,
                new FixedClock(),
                new ReminderDataFileStore(executionLocation),
                new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"));

            var authorization = service.Consume(record, record.StepId);
            var receipt = await executor.ExecuteAsync(authorization);

            receipt.ReportedSuccess.Should().BeFalse();
            receipt.Stderr.Should().Be("Signed reminder task ID, arguments, or data-file path is invalid.");
            scheduler.Registration.Should().BeNull();
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Executor_Accepts_A_Signed_Reminder_Data_Path_Inside_Its_Injected_Base()
    {
        var directory = CreateTempDirectory();
        try
        {
            var location = new FixedReminderDataLocation(directory);
            var service = CreateService(location);
            var store = new ReminderDataFileStore(location);
            var scheduler = new RecordingScheduler();
            var record = IssueReminder(service, "inside-base");
            var taskId = record.Expected["task_id"]!.ToString()!;
            var executor = new TaskScheduleReminderExecutor(service, scheduler, new FixedClock(), store, new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"));

            var receipt = await executor.ExecuteAsync(service.Consume(record, record.StepId));

            receipt.ReportedSuccess.Should().BeTrue();
            scheduler.Registration.Should().NotBeNull();
            File.Exists(store.GetFilePath(taskId)).Should().BeTrue();
            store.Delete(taskId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static IEnumerable<object[]> OutsideBaseLocations()
    {
        yield return ["traversal", new Func<string, string>(baseDirectory => Path.Combine(baseDirectory, "..", "escape"))];
        yield return ["different-drive", new Func<string, string>(_ => @"D:\Jarvis\reminders")];
        yield return ["same-prefix-sibling", new Func<string, string>(baseDirectory => baseDirectory + "-evil")];
    }

    private static AuthorizationService CreateService(IReminderDataLocation location) =>
        new(new InMemoryDpapiKeyStore(), new FixedClock(), hostPathProvider: new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"), reminderDataLocation: location);

    private static AuthorizationRecordDto IssueReminder(AuthorizationService service, string suffix) =>
        service.IssueAuthorization(
            $"task-{suffix}",
            $"step-{suffix}",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Reminder",
                ["time_iso"] = Now.AddMinutes(5).ToString("O"),
                ["message"] = "Reminder message"
            });

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Jarvis.ReminderDataLocationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedReminderDataLocation(string directoryPath) : IReminderDataLocation
    {
        public string DirectoryPath => directoryPath;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }

    private sealed class RecordingScheduler : IReminderTaskScheduler
    {
        public ReminderTaskRegistration? Registration { get; private set; }
        public void Register(ReminderTaskRegistration registration) => Registration = registration;
        public void Delete(string taskPath) { }
    }

}
