using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace Jarvis.Tools;

/// <summary>
/// Registers per-user Task Scheduler reminders without elevation.
/// </summary>
public sealed class WindowsReminderTaskScheduler : IReminderTaskScheduler
{
    /// <inheritdoc />
    public void Register(ReminderTaskRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler is available only on Windows.");

        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var actionPath = Path.GetFullPath(registration.ActionPath);
        if (!File.Exists(actionPath))
            throw new FileNotFoundException("The signed reminder action executable does not exist.", actionPath);
        if (!string.Equals(
                registration.TaskPath,
                $@"\Jarvis\{registration.TaskName}",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Reminder task path must be a direct child of the Jarvis task folder.", nameof(registration));
        }

        try
        {
            using var service = new TaskService();
            var folder = GetOrCreateJarvisFolder(service);
            using var definition = service.NewTask();
            definition.RegistrationInfo.Author = "Jarvis";
            definition.RegistrationInfo.Description = registration.Description;
            definition.Principal.UserId = userSid;
            definition.Principal.LogonType = TaskLogonType.InteractiveToken;
            definition.Principal.RunLevel = TaskRunLevel.LUA;
            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = true;
            definition.Triggers.Add(new TimeTrigger(registration.ScheduledAtUtc.ToLocalTime().DateTime)
            {
                Enabled = true
            });
            definition.Actions.Add(new ExecAction(
                actionPath,
                registration.ActionArguments,
                registration.WorkingDirectory));
            folder.RegisterTaskDefinition(
                registration.TaskName,
                definition,
                TaskCreation.CreateOrUpdate,
                userSid,
                null,
                TaskLogonType.InteractiveToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new IOException("Windows Task Scheduler could not register the reminder task.", exception);
        }
        catch (Win32Exception exception)
        {
            throw new IOException("Windows Task Scheduler could not register the reminder task.", exception);
        }
    }

    /// <inheritdoc />
    public void Delete(string taskPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler is available only on Windows.");

        var separator = taskPath.LastIndexOf('\\');
        if (separator <= 0 || separator == taskPath.Length - 1
            || separator != @"\Jarvis".Length
            || !taskPath.StartsWith(@"\Jarvis\", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Task path must be inside the Jarvis Task Scheduler folder.", nameof(taskPath));
        }

        using var service = new TaskService();
        service.GetFolder(taskPath[..separator]).DeleteTask(taskPath[(separator + 1)..]);
    }

    private static TaskFolder GetOrCreateJarvisFolder(TaskService service)
    {
        try
        {
            var folder = service.GetFolder(@"\Jarvis");
            return folder ?? service.RootFolder.CreateFolder("Jarvis");
        }
        catch (FileNotFoundException)
        {
            return service.RootFolder.CreateFolder("Jarvis");
        }
    }
}
