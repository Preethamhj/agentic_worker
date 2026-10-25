using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class ScheduledTaskEvidenceCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    internal const string RealTaskSchedulerXmlFixture = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Date>2026-10-24T12:00:00.000+00:00</Date>
            <Author>Jarvis</Author>
            <Description>Jarvis reminder</Description>
            <URI>{{TASK_PATH}}</URI>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>REDACTED_TEST_SID</UserId>
              <LogonType>InteractiveToken</LogonType>
            </Principal>
          </Principals>
          <Settings>
            <DeleteExpiredTaskAfter>PT1H</DeleteExpiredTaskAfter>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <StartWhenAvailable>true</StartWhenAvailable>
          </Settings>
          <Triggers>
            <TimeTrigger>
              <StartBoundary>{{START_BOUNDARY}}</StartBoundary>
              <EndBoundary>{{END_BOUNDARY}}</EndBoundary>
            </TimeTrigger>
          </Triggers>
          <Actions Context="Author">
            <Exec>
              <Command>C:\Jarvis\Jarvis.Cli.exe</Command>
              <Arguments>{{ACTION_ARGUMENTS}}</Arguments>
              <WorkingDirectory>C:\Jarvis</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;

    [Theory]
    [MemberData(nameof(SchedulerSettingFaults))]
    public async Task Changed_Signed_Scheduler_Setting_Is_Failed(
        string setting,
        string replacement,
        bool changeTaskName)
    {
        using var fixture = new ReminderFixture();
        var authorization = fixture.IssueAuthorization(setting);
        var taskPath = (string)authorization.Expected["task_path"]!;
        var taskName = (string)authorization.Expected["scheduler_task_name"]!;
        var document = CreateTaskXml(authorization);
        if (changeTaskName)
            taskName = "different-task-name";
        else
            SetElement(document, setting, replacement);

        var evidence = fixture.Collector.ReadTaskXml(
            authorization,
            taskPath,
            taskName,
            document.ToString(SaveOptions.DisableFormatting),
            taskEnabled: true,
            beforeTaskPaths: [],
            afterTaskPaths: [taskPath]);

        evidence.Match.Should().BeFalse(setting);
        var mismatchedSettings = (string[])evidence.Observed["mismatched_settings"]!;
        mismatchedSettings.Should().Contain(ExpectedMismatchName(setting));
        var result = await fixture.VerifyAsync(authorization, evidence);
        result.Status.Should().NotBe("VERIFIED", setting);
        result.Status.Should().Be("FAILED", setting);
        VerificationEngine.Aggregate(
            [new(3, "IndependentEvidenceCollection", false, "collection failed", "ESCALATE"),
             new(5, "AttributeVerification", false, evidence.Detail, "FAILED")])
            .Should().Be("FAILED");
    }

    [Fact]
    public async Task No_Scheduler_Setting_Fault_Aggregates_To_Verified()
    {
        using var fixture = new ReminderFixture();
        var authorization = fixture.IssueAuthorization("clean-control");
        var taskPath = (string)authorization.Expected["task_path"]!;
        var evidence = fixture.Collector.ReadTaskXml(
            authorization,
            taskPath,
            (string)authorization.Expected["scheduler_task_name"]!,
            CreateTaskXml(authorization).ToString(SaveOptions.DisableFormatting),
            taskEnabled: true,
            beforeTaskPaths: [],
            afterTaskPaths: [taskPath]);

        evidence.Match.Should().BeTrue(evidence.Detail);
        var result = await fixture.VerifyAsync(authorization, evidence);
        result.Status.Should().Be("VERIFIED", string.Join("; ", result.Reasons));
        VerificationEngine.Aggregate(
            Enumerable.Range(1, 6)
                .Select(stage => new VerificationStageResultDto(stage, $"stage-{stage}", true, "passed", "PASSED"))
                .ToArray())
            .Should().Be("VERIFIED");
    }

    [Fact]
    public async Task Real_Task_Scheduler_Xml_Defaults_Omitted_RunLevel_To_LeastPrivilege()
    {
        using var fixture = new ReminderFixture();
        var authorization = fixture.IssueAuthorization("real-xml-default-run-level");
        var expected = authorization.Expected;
        var taskPath = (string)expected["task_path"]!;
        var xml = CreateRealTaskSchedulerXml(authorization);

        var evidence = fixture.Collector.ReadTaskXml(
            authorization,
            taskPath,
            (string)expected["scheduler_task_name"]!,
            xml,
            taskEnabled: true,
            beforeTaskPaths: [],
            afterTaskPaths: [taskPath]);

        evidence.Match.Should().BeTrue(evidence.Detail);
        evidence.Observed["run_level"].Should().Be("LeastPrivilege");
        evidence.Observed["run_level_xml"].Should().BeNull();
        var result = await fixture.VerifyAsync(authorization, evidence);
        result.Status.Should().Be("VERIFIED", string.Join("; ", result.Reasons));
    }

    [Fact]
    public async Task Already_Fired_And_Removed_Task_Is_Failed_With_Explicit_Reason()
    {
        var clock = new FixedClock();
        var auditStore = new InMemoryAuditStore();
        var authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), clock, auditStore);
        var collector = new MissingTaskCollector();
        var engine = new VerificationEngine(
            authorizationService,
            auditStore,
            clock,
            10_000,
            TimeSpan.FromSeconds(30),
            null,
            collector);
        var authorization = authorizationService.IssueAuthorization(
            "task-fired-removed",
            "step-fired-removed",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Reminder",
                ["time_iso"] = clock.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture),
                ["message"] = "Reminder message"
            });
        var token = await engine.BeginStepAsync(authorization);
        authorizationService.Consume(authorization, authorization.StepId);
        var receipt = new ExecutionReceiptDto(
            1,
            authorization.StepId,
            authorization.AuthId,
            clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            0,
            true,
            "task fired and was removed",
            string.Empty);

        var result = await engine.VerifyAsync(
            receipt,
            new ExecutionObservation(clock.UtcNow, clock.UtcNow),
            token);

        result.Status.Should().NotBe("VERIFIED");
        result.Status.Should().Be("FAILED");
        result.Reasons.Should().Contain(reason => reason.Contains("does not exist", StringComparison.Ordinal));
        collector.Observation!.Observed["exists"].Should().Be(false);
    }

    public static IEnumerable<object[]> SchedulerSettingFaults()
    {
        yield return ["task_name", "ignored", true];
        yield return ["start_boundary", "2026-10-24T13:02:00", false];
        yield return ["command", @"C:\Windows\System32\where.exe", false];
        yield return ["arguments", "--jarvis-reminder 00000000000000000000000000000000", false];
        yield return ["disallow_start_if_on_batteries", "true", false];
        yield return ["stop_if_going_on_batteries", "true", false];
        yield return ["execution_time_limit", "PT4M", false];
        yield return ["start_when_available", "false", false];
        yield return ["multiple_instances", "Parallel", false];
        yield return ["logon_type", "Password", false];
        yield return ["run_level", "HighestAvailable", false];
        yield return ["end_boundary", "2026-10-24T13:11:00", false];
        yield return ["delete_expired_task_after", "PT2H", false];
    }

    internal static string CreateRealTaskSchedulerXml(AuthorizationRecordDto authorization)
    {
        var expected = authorization.Expected;
        var start = DateTimeOffset.Parse((string)expected["time_iso"]!, CultureInfo.InvariantCulture);
        var end = start.AddSeconds((int)expected["end_boundary_offset_seconds"]!);
        return RealTaskSchedulerXmlFixture
            .Replace("{{TASK_PATH}}", (string)expected["task_path"]!, StringComparison.Ordinal)
            .Replace("{{START_BOUNDARY}}", start.ToLocalTime().ToString("s", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{END_BOUNDARY}}", end.ToLocalTime().ToString("s", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{ACTION_ARGUMENTS}}", (string)expected["action_arguments"]!, StringComparison.Ordinal);
    }

    private static XDocument CreateTaskXml(AuthorizationRecordDto authorization)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var expected = authorization.Expected;
        var start = DateTimeOffset.Parse((string)expected["time_iso"]!, CultureInfo.InvariantCulture);
        var end = start.AddSeconds((int)expected["end_boundary_offset_seconds"]!);
        return new XDocument(
            new XElement(ns + "Task",
                new XElement(ns + "Triggers",
                    new XElement(ns + "TimeTrigger",
                        new XElement(ns + "StartBoundary", start.ToLocalTime().ToString("s", CultureInfo.InvariantCulture)),
                        new XElement(ns + "EndBoundary", end.ToLocalTime().ToString("s", CultureInfo.InvariantCulture)),
                        new XElement(ns + "Enabled", "true"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XElement(ns + "LogonType", expected["logon_type"]),
                        new XElement(ns + "RunLevel", expected["run_level"]))),
                new XElement(ns + "Settings",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "DisallowStartIfOnBatteries", expected["disallow_start_if_on_batteries"]),
                    new XElement(ns + "StopIfGoingOnBatteries", expected["stop_if_going_on_batteries"]),
                    new XElement(ns + "ExecutionTimeLimit", XmlConvert.ToString(TimeSpan.FromSeconds((int)expected["execution_time_limit_seconds"]!))),
                    new XElement(ns + "StartWhenAvailable", expected["start_when_available"]),
                    new XElement(ns + "MultipleInstancesPolicy", expected["multiple_instances_policy"]),
                    new XElement(ns + "DeleteExpiredTaskAfter", XmlConvert.ToString(TimeSpan.FromSeconds((int)expected["delete_expired_task_after_seconds"]!)))),
                new XElement(ns + "Actions",
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", expected["action_path"]),
                        new XElement(ns + "Arguments", expected["action_arguments"])))));
    }

    private static string ExpectedMismatchName(string setting) => setting switch
    {
        "task_name" => "task_name",
        "start_boundary" => "trigger_time",
        "command" or "arguments" => "action_path_and_arguments",
        "disallow_start_if_on_batteries" => "disallow_start_if_on_batteries",
        "stop_if_going_on_batteries" => "stop_if_going_on_batteries",
        "execution_time_limit" => "execution_time_limit",
        "start_when_available" => "start_when_available",
        "multiple_instances" => "multiple_instances_policy",
        "logon_type" => "logon_type",
        "run_level" => "run_level",
        "end_boundary" => "end_boundary",
        "delete_expired_task_after" => "delete_expired_task_after",
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };

    private static void SetElement(XDocument document, string localName, string value)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var xmlName = localName switch
        {
            "start_boundary" => "StartBoundary",
            "command" => "Command",
            "arguments" => "Arguments",
            "disallow_start_if_on_batteries" => "DisallowStartIfOnBatteries",
            "stop_if_going_on_batteries" => "StopIfGoingOnBatteries",
            "execution_time_limit" => "ExecutionTimeLimit",
            "start_when_available" => "StartWhenAvailable",
            "multiple_instances" => "MultipleInstancesPolicy",
            "logon_type" => "LogonType",
            "run_level" => "RunLevel",
            "end_boundary" => "EndBoundary",
            "delete_expired_task_after" => "DeleteExpiredTaskAfter",
            _ => throw new ArgumentOutOfRangeException(nameof(localName))
        };
        document.Descendants(ns + xmlName).Single().Value = value;
    }

    private sealed class ReminderFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Jarvis.SchedulerEvidenceTests", Guid.NewGuid().ToString("N"));
        private readonly FixedClock _clock = new();
        private readonly ReminderDataFileStore _dataStore;
        private readonly AuthorizationService _authorizationService;

        public ReminderFixture()
        {
            Directory.CreateDirectory(_directory);
            var location = new FixedReminderDataLocation(_directory);
            _dataStore = new ReminderDataFileStore(location);
            _authorizationService = new AuthorizationService(
                new InMemoryDpapiKeyStore(),
                _clock,
                hostPathProvider: new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe"),
                reminderDataLocation: location);
        }

        public ScheduledTaskEvidenceCollector Collector { get; } = new();

        public AuthorizationRecordDto IssueAuthorization(string suffix)
        {
            var authorization = _authorizationService.IssueAuthorization(
                $"task-{suffix}",
                $"step-{suffix}",
                "task.schedule_reminder",
                new Dictionary<string, object?>
                {
                    ["task_name"] = "Reminder",
                    ["time_iso"] = _clock.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture),
                    ["message"] = "Reminder message"
                });
            _dataStore.Create(
                (string)authorization.Expected["task_id"]!,
                "Reminder",
                "Reminder message");
            return authorization;
        }

        public async Task<VerificationResultDto> VerifyAsync(
            AuthorizationRecordDto authorization,
            IndependentEvidenceObservation observation)
        {
            var auditStore = new InMemoryAuditStore();
            var engine = new VerificationEngine(
                _authorizationService,
                auditStore,
                _clock,
                10_000,
                TimeSpan.FromSeconds(30),
                null,
                new FixedTaskCollector(observation));
            var token = await engine.BeginStepAsync(authorization);
            _authorizationService.Consume(authorization, authorization.StepId);
            var timestamp = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var receipt = new ExecutionReceiptDto(
                1,
                authorization.StepId,
                authorization.AuthId,
                timestamp,
                timestamp,
                0,
                true,
                "Untrusted receipt claims success.",
                string.Empty);
            return await engine.VerifyAsync(
                receipt,
                new ExecutionObservation(_clock.UtcNow, _clock.UtcNow),
                token);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class MissingTaskCollector : IScheduledTaskEvidenceCollector
    {
        public IndependentEvidenceObservation? Observation { get; private set; }

        public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken) => [];

        public IndependentEvidenceObservation Collect(
            AuthorizationRecordDto authorization,
            IReadOnlyList<string> beforeTaskPaths,
            CancellationToken cancellationToken)
        {
            Observation = ScheduledTaskEvidenceCollector.Missing((string)authorization.Expected["task_path"]!);
            return Observation;
        }

    }

    private sealed class FixedTaskCollector(IndependentEvidenceObservation observation) : IScheduledTaskEvidenceCollector
    {
        public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken) => [];

        public IndependentEvidenceObservation Collect(
            AuthorizationRecordDto authorization,
            IReadOnlyList<string> beforeTaskPaths,
            CancellationToken cancellationToken) => observation;
    }

    private sealed class FixedReminderDataLocation(string directoryPath) : IReminderDataLocation
    {
        public string DirectoryPath => directoryPath;
    }

    private sealed class FixedHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}
