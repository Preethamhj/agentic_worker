using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class ReminderDispatcherTests
{
    [Fact]
    public void Dispatch_Shows_Payload_For_Task_Id_And_Deletes_The_Data_File()
    {
        var store = new ReminderDataFileStore();
        var taskId = Guid.NewGuid().ToString("N");
        const string title = "Reminder \"quoted\" & 100% ಕನ್ನಡ";
        const string message = "Do the thing, ಕನ್ನಡ";
        var notifier = new RecordingNotifier();

        try
        {
            store.Create(taskId, title, message);
            var dataPath = store.GetFilePath(taskId);

            var exitCode = new ReminderDispatcher(store, notifier)
                .Dispatch(["--jarvis-reminder", taskId]);

            exitCode.Should().Be(0);
            notifier.Title.Should().Be(title);
            notifier.Message.Should().Be(message);
            File.Exists(dataPath).Should().BeFalse();
        }
        finally
        {
            store.Delete(taskId);
        }
    }

    [Theory]
    [InlineData("not-a-task-id")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void Dispatch_Refuses_Malformed_Task_Id_Without_Notification(string taskId)
    {
        var notifier = new RecordingNotifier();

        var exitCode = new ReminderDispatcher(new ReminderDataFileStore(), notifier)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(2);
        notifier.ShowCount.Should().Be(0);
    }

    [Fact]
    public void Dispatch_Refuses_Unknown_Task_Id_Without_Notification()
    {
        var store = new ReminderDataFileStore();
        var taskId = Guid.NewGuid().ToString("N");
        var notifier = new RecordingNotifier();

        var exitCode = new ReminderDispatcher(store, notifier)
            .Dispatch(["--jarvis-reminder", taskId]);

        exitCode.Should().Be(2);
        notifier.ShowCount.Should().Be(0);
        File.Exists(store.GetFilePath(taskId)).Should().BeFalse();
    }

    [Fact]
    public void Data_File_Store_Uses_Exclusive_Create_Without_Replacing_An_Existing_Reminder()
    {
        var store = new ReminderDataFileStore();
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

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public string? Title { get; private set; }
        public string? Message { get; private set; }
        public int ShowCount { get; private set; }

        public void Show(string title, string message)
        {
            Title = title;
            Message = message;
            ShowCount++;
        }
    }
}
