using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class ReminderDispatcherTests
{
    [Fact]
    public void Dispatcher_And_Default_Authorization_Resolve_The_Same_Directory_Value()
    {
        var location = new DefaultReminderDataLocation();
        var service = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            new FixedClock(),
            hostPathProvider: new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"),
            reminderDataLocation: location);
        var record = service.IssueAuthorization(
            "task-default-location",
            "step-default-location",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Reminder",
                ["time_iso"] = "2026-10-09T10:05:00.0000000+00:00",
                ["message"] = "Reminder message"
            });
        var dispatcher = new ReminderDispatcher(
            new ReminderDataFileStore(),
            new RecordingNotifier(),
            new RecordingScheduler());

        dispatcher.ReminderDataDirectory.Should().Be(location.DirectoryPath);
        Path.GetDirectoryName(record.Expected["reminder_data_path"]!.ToString()).Should().Be(location.DirectoryPath);
    }

    [Fact]
    public void Dispatch_Displays_Then_Consumes_Payload_And_Unregisters_Its_Task()
    {
        using var temporaryStore = new TemporaryReminderDataStore();
        var store = temporaryStore.Store;
        var taskId = Guid.NewGuid().ToString("N");
        const string title = "Reminder \"quoted\" & 100% ಕನ್ನಡ";
        const string message = "Do the thing, ಕನ್ನಡ";
        var dataPath = store.GetFilePath(taskId);
        var notifier = new RecordingNotifier(() => File.Exists(dataPath).Should().BeTrue());
        var scheduler = new RecordingScheduler();
        store.Create(taskId, title, message);

        var exitCode = new ReminderDispatcher(store, notifier, scheduler)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(0);
        notifier.Title.Should().Be(title);
        notifier.Message.Should().Be(message);
        File.Exists(dataPath).Should().BeFalse();
        scheduler.DeletedTaskPath.Should().Be($@"\Jarvis\reminder-{taskId}");
    }

    [Fact]
    public void Dispatch_Tolerates_Task_Unregister_Failure_After_Display()
    {
        using var temporaryStore = new TemporaryReminderDataStore();
        var store = temporaryStore.Store;
        var taskId = Guid.NewGuid().ToString("N");
        var dataPath = store.GetFilePath(taskId);
        var notifier = new RecordingNotifier();
        var scheduler = new RecordingScheduler { ThrowOnDelete = true };
        store.Create(taskId, "Reminder", "Message");

        var exitCode = new ReminderDispatcher(store, notifier, scheduler)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(0);
        notifier.ShowCount.Should().Be(1);
        File.Exists(dataPath).Should().BeFalse();
        scheduler.DeletedTaskPath.Should().Be($@"\Jarvis\reminder-{taskId}");
    }

    [Theory]
    [InlineData("not-a-task-id")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void Dispatch_Refuses_Malformed_Task_Id_Without_Notification(string taskId)
    {
        var notifier = new RecordingNotifier();
        var scheduler = new RecordingScheduler();

        var exitCode = new ReminderDispatcher(new ReminderDataFileStore(), notifier, scheduler)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(2);
        notifier.ShowCount.Should().Be(0);
        scheduler.DeletedTaskPath.Should().BeNull();
    }

    [Fact]
    public void Dispatch_Refuses_Unknown_Task_Id_Without_Notification()
    {
        using var temporaryStore = new TemporaryReminderDataStore();
        var store = temporaryStore.Store;
        var taskId = Guid.NewGuid().ToString("N");
        var notifier = new RecordingNotifier();
        var scheduler = new RecordingScheduler();

        var exitCode = new ReminderDispatcher(store, notifier, scheduler)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(2);
        notifier.ShowCount.Should().Be(0);
        scheduler.DeletedTaskPath.Should().BeNull();
        File.Exists(store.GetFilePath(taskId)).Should().BeFalse();
    }

    [Fact]
    public void Data_File_Store_Uses_Exclusive_Create_Without_Replacing_An_Existing_Reminder()
    {
        using var temporaryStore = new TemporaryReminderDataStore();
        var store = temporaryStore.Store;
        var taskId = Guid.NewGuid().ToString("N");
        try
        {
            store.Create(taskId, "Original", "Original message");
            Action duplicateCreate = () => store.Create(taskId, "Replacement", "Replacement message");
            duplicateCreate.Should().Throw<IOException>();
            store.Read(taskId).Should().Be(new ReminderDataPayload("Original", "Original message"));
        }
        finally
        {
            store.Delete(taskId);
        }
    }

    private sealed class RecordingNotifier(Action? onShow = null) : IReminderNotifier
    {
        public string? Title { get; private set; }
        public string? Message { get; private set; }
        public int ShowCount { get; private set; }

        public void Show(string title, string message)
        {
            onShow?.Invoke();
            Title = title;
            Message = message;
            ShowCount++;
        }
    }

    private sealed class RecordingScheduler : IReminderTaskScheduler
    {
        public string? DeletedTaskPath { get; private set; }
        public bool ThrowOnDelete { get; init; }

        public void Register(ReminderTaskRegistration registration) => throw new NotSupportedException();

        public void Delete(string taskPath)
        {
            DeletedTaskPath = taskPath;
            if (ThrowOnDelete)
                throw new IOException("simulated scheduler failure");
        }
    }

    private sealed class TemporaryReminderDataStore : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Jarvis.DispatcherTests", Guid.NewGuid().ToString("N"));

        public TemporaryReminderDataStore()
        {
            Directory.CreateDirectory(_directory);
            Store = new ReminderDataFileStore(new FixedReminderDataLocation(_directory));
        }

        public ReminderDataFileStore Store { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class FixedReminderDataLocation(string directoryPath) : IReminderDataLocation
    {
        public string DirectoryPath => directoryPath;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}
