using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Jarvis.Contracts;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.Verification;

namespace Jarvis.Eval;

internal sealed class EvalFakeReminderScheduler(
    string faultId,
    string reminderDirectory,
    string taskId) : IReminderTaskScheduler, IScheduledTaskEvidenceCollector, IEvalReminderPredicateReader
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private ReminderTaskRegistration? _registration;
    private string? _xml;
    private bool _enabled;
    private bool _extraTask;
    private string? _expectedPath;
    private string? _expectedName;

    public string? TaskXml => _xml;
    public bool TaskEnabled => _enabled;
    public bool ExtraTaskExists => _extraTask;

    public void Register(ReminderTaskRegistration registration)
    {
        if (faultId == "F1")
            return;
        _registration = registration;
        _enabled = true;
        _xml = CreateXml(registration);
        if (faultId == "F12")
            _xml = Set(_xml, "StartBoundary", registration.ScheduledAtUtc.AddMinutes(1).ToLocalTime().ToString("s", CultureInfo.InvariantCulture));
        if (faultId == "F13")
            _xml = Set(_xml, "Arguments", "--jarvis-reminder wrong-id");
        if (faultId == "F14")
            _enabled = false;
        if (faultId == "F17a")
            _xml = Set(_xml, "DisallowStartIfOnBatteries", "true");
        if (faultId == "F17b")
            _xml = Set(_xml, "EndBoundary", registration.ScheduledAtUtc.AddMinutes(1).ToLocalTime().ToString("s", CultureInfo.InvariantCulture));
        _extraTask = faultId == "F15";
        if (faultId == "F16")
        {
            var taskIdFromArgs = registration.ActionArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            File.WriteAllText(Path.Combine(reminderDirectory, $"{taskIdFromArgs}.json"), "{\"task_name\":\"altered\",\"message\":\"altered\"}");
        }
    }

    public void Delete(string taskPath) { }

    public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken)
    {
        if (_extraTask)
            return new[] { ExpectedPath + "-extra" };
        if (_registration is null)
            return Array.Empty<string>();
        return new[] { ExpectedPath };
    }

    public IndependentEvidenceObservation Collect(AuthorizationRecordDto authorization, IReadOnlyList<string> beforeTaskPaths, CancellationToken cancellationToken)
    {
        _expectedPath = (string)authorization.Expected["task_path"]!;
        _expectedName = (string)authorization.Expected["scheduler_task_name"]!;
        if (_registration is null || _xml is null)
            return new ScheduledTaskEvidenceCollector().ReadTaskXml(authorization, ExpectedPath, ExpectedName, "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\" />", false, beforeTaskPaths, Array.Empty<string>());
        IReadOnlyList<string> after = _extraTask
            ? new[] { ExpectedPath, ExpectedPath + "-extra" }
            : new[] { ExpectedPath };
        return new ScheduledTaskEvidenceCollector().ReadTaskXml(authorization, ExpectedPath, ExpectedName, _xml, _enabled, beforeTaskPaths, after);
    }

    public Task<IReadOnlyList<string>> FindMismatchesAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default)
    {
        var mismatches = new List<string>();
        if (_registration is null || !_enabled)
            mismatches.Add("Reminder task existence or enabled state does not match.");
        if (_registration is not null && faultId is "F12" or "F13")
            mismatches.Add("Reminder trigger or action arguments do not match.");
        return Task.FromResult<IReadOnlyList<string>>(mismatches);
    }

    private string ExpectedPath => _expectedPath ?? $@"\Jarvis\reminder-{taskId}";
    private string ExpectedName => _expectedName ?? $"reminder-{taskId}";

    private static string CreateXml(ReminderTaskRegistration registration)
    {
        var start = registration.ScheduledAtUtc.ToLocalTime().ToString("s", CultureInfo.InvariantCulture);
        var end = (registration.EndBoundaryUtc ?? registration.ScheduledAtUtc.AddMinutes(10)).ToLocalTime().ToString("s", CultureInfo.InvariantCulture);
        var doc = new XDocument(new XElement(Ns + "Task",
            new XElement(Ns + "Triggers", new XElement(Ns + "TimeTrigger",
                new XElement(Ns + "StartBoundary", start), new XElement(Ns + "EndBoundary", end), new XElement(Ns + "Enabled", "true"))),
            new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XElement(Ns + "LogonType", "InteractiveToken"), new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(Ns + "Settings", new XElement(Ns + "Enabled", "true"), new XElement(Ns + "DisallowStartIfOnBatteries", registration.DisallowStartIfOnBatteries), new XElement(Ns + "StopIfGoingOnBatteries", registration.StopIfGoingOnBatteries), new XElement(Ns + "ExecutionTimeLimit", XmlConvert.ToString(registration.ExecutionTimeLimit ?? TimeSpan.FromMinutes(5))), new XElement(Ns + "StartWhenAvailable", registration.StartWhenAvailable), new XElement(Ns + "MultipleInstancesPolicy", registration.MultipleInstancesPolicy), new XElement(Ns + "DeleteExpiredTaskAfter", XmlConvert.ToString(registration.DeleteExpiredTaskAfter ?? TimeSpan.FromHours(1)))),
            new XElement(Ns + "Actions", new XElement(Ns + "Exec", new XElement(Ns + "Command", registration.ActionPath), new XElement(Ns + "Arguments", registration.ActionArguments)))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static string Set(string xml, string localName, string value)
    {
        var doc = XDocument.Parse(xml);
        doc.Descendants(Ns + localName).Single().Value = value;
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}

internal sealed class EvalReminderDataLocation(string path) : IReminderDataLocation
{
    public string DirectoryPath { get; } = path;
}
