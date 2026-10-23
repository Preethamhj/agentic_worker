using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;
using Jarvis.Safety;

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
        const string argumentsPrefix = "--jarvis-reminder ";
        var taskId = registration.ActionArguments.StartsWith(argumentsPrefix, StringComparison.Ordinal)
            ? registration.ActionArguments[argumentsPrefix.Length..]
            : string.Empty;
        if (!ToolPolicy.IsValidReminderTaskId(taskId)
            || !string.Equals(
                registration.ActionArguments,
                $"{argumentsPrefix}{taskId}",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Reminder action arguments must contain only --jarvis-reminder and one valid task ID.",
                nameof(registration));
        }
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
            definition.Principal.LogonType = registration.LogonType;
            definition.Principal.RunLevel = registration.RunLevel;
            definition.Settings.Enabled = true;
            definition.Settings.DisallowStartIfOnBatteries = registration.DisallowStartIfOnBatteries;
            definition.Settings.StopIfGoingOnBatteries = registration.StopIfGoingOnBatteries;
            definition.Settings.ExecutionTimeLimit = registration.ExecutionTimeLimit ?? ReminderTaskPolicy.ExecutionTimeLimit;
            definition.Settings.StartWhenAvailable = registration.StartWhenAvailable;
            definition.Settings.MultipleInstances = registration.MultipleInstancesPolicy;
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
                registration.LogonType);
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
