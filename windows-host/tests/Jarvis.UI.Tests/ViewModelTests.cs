using FluentAssertions;
using System.Collections.Generic;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.UI.ViewModels;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public void ApprovalViewModel_Preserves_Canonical_Arguments_And_Displays_Risk_And_Image_Path()
    {
        IReadOnlyDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["app_id"] = "notepad",
            ["image_path"] = @"C:\Windows\System32\notepad.exe"
        };
        var viewModel = new ApprovalViewModel(
            new ApprovalRequest("task-1", "step-1", "app.launch", args, "medium"),
            resolvedImagePath: @"C:\Windows\System32\notepad.exe",
            userName: "alice");

        viewModel.CanonicalArguments.Should().BeSameAs(args);
        viewModel.ToolId.Should().Be("app.launch");
        viewModel.RiskBadge.Should().Be("MEDIUM");
        viewModel.ResolvedImagePath.Should().Be(@"C:\Windows\System32\notepad.exe");
    }

    [Fact]
    public void ApprovalViewModel_Displays_Reminder_Local_Time_Iso_And_Countdown()
    {
        var instant = new DateTimeOffset(2026, 11, 5, 16, 0, 0, TimeSpan.Zero);
        var iso = instant.ToString("O");
        var viewModel = new ApprovalViewModel(
            new ApprovalRequest(
                "task-1",
                "step-1",
                "task.schedule_reminder",
                new Dictionary<string, object?> { ["time_iso"] = iso },
                "medium"),
            clock: new FixedClock(instant.AddMinutes(-15)));

        viewModel.ReminderTimeLocal.Should().Be(instant.ToLocalTime());
        viewModel.ReminderTimeIso.Should().Be(iso);
        viewModel.ReminderCountdown.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void ApprovalViewModel_Default_And_Close_Are_Deny()
    {
        var viewModel = CreateApprovalViewModel(userName: "alice");

        viewModel.Decision.Approved.Should().BeFalse();
        viewModel.Close();
        viewModel.Decision.Should().Be(new ApprovalDecision(false, "alice", "User denied execution."));
    }

    [Fact]
    public void ApprovalViewModel_Approve_And_Deny_Commands_Record_Human_Decisions()
    {
        var approve = CreateApprovalViewModel(userName: "alice");
        approve.ApproveCommand.Execute(null);
        approve.Decision.Should().Be(new ApprovalDecision(true, "alice"));

        var deny = CreateApprovalViewModel(userName: "bob");
        deny.DenyCommand.Execute(null);
        deny.Decision.Should().Be(new ApprovalDecision(false, "bob", "User denied execution."));
    }

    [Fact]
    public void ApprovalViewModel_Timeout_Uses_Timeout_Approver()
    {
        var viewModel = CreateApprovalViewModel(userName: "alice");

        viewModel.Timeout();

        viewModel.Decision.Should().Be(new ApprovalDecision(false, "timeout", "Approval timed out."));
    }

    [Fact]
    public void EscalationViewModel_Exposes_Evidence_And_Retry_Only_Decision()
    {
        var viewModel = new EscalationViewModel(CreateVerification("ESCALATE", "ambiguous evidence"));

        viewModel.EvidenceSummary.Should().Contain("ambiguous evidence");
        viewModel.RetryCommand.Execute(null);
        viewModel.Decision.Action.Should().Be(EscalationAction.RetryVerification);
    }

    [Fact]
    public void EscalationViewModel_Abort_And_Timeout_Are_Not_Retry()
    {
        var abort = new EscalationViewModel(CreateVerification("ESCALATE", "review"));
        abort.AbortCommand.Execute(null);
        abort.Decision.Action.Should().Be(EscalationAction.Abort);

        var timeout = new EscalationViewModel(CreateVerification("ESCALATE", "review"));
        timeout.Timeout();
        timeout.Decision.Action.Should().Be(EscalationAction.TimedOut);
    }

    [Fact]
    public void TaskViewModel_Shows_Timeline_Stages_And_Not_Verified_Unless_Verification_Passed()
    {
        var time = DateTimeOffset.UtcNow;
        var transitions = new[]
        {
            new StateTransitionRecord("task-1", "created", "planned", null, time.AddSeconds(1)),
            new StateTransitionRecord("task-1", "planned", "failed", "verification failed", time.AddSeconds(2))
        };
        var result = new TaskOrchestratorResult(
            "task-1",
            TaskState.Failed,
            [],
            [CreateVerification("FAILED", "bad evidence", ("authorization", "FAILED"), ("execution", "NOT_RUN"))],
            "Task did not verify.");

        var viewModel = new TaskViewModel(result, transitions);

        viewModel.StateTimeline.Should().HaveCount(2);
        viewModel.StateTimeline[0].ToState.Should().Be("planned");
        viewModel.VerificationStages.Select(stage => stage.Status)
            .Should().Equal("FAILED", "NOT_RUN");
        viewModel.VerificationStages[0].Reason.Should().Be("authorization detail");
        viewModel.FinalMessage.Should().Be("not verified");
    }

    [Fact]
    public void TaskViewModel_Only_Reports_Verified_For_Completed_Verified_Result()
    {
        var result = new TaskOrchestratorResult(
            "task-1",
            TaskState.Completed,
            [],
            [CreateVerification("VERIFIED", "all checks passed", ("authorization", "PASSED"))],
            "All steps completed.");

        new TaskViewModel(result).FinalMessage.Should().Be("verified");
    }

    [Theory]
    [InlineData("FAILED")]
    [InlineData("ESCALATE")]
    [InlineData("ERROR")]
    public void TaskViewModel_Never_Reports_Verified_For_NonVerified_Result(string status)
    {
        var result = new TaskOrchestratorResult(
            "task-1",
            TaskState.Completed,
            [],
            [CreateVerification(status, "not verified")],
            "Task did not verify.");

        new TaskViewModel(result).FinalMessage.Should().Be("not verified");
    }

    [Fact]
    public void HistoryViewModel_Combines_Audit_Events_And_Renders_Legacy_Safety_Rows()
    {
        var time = DateTimeOffset.UtcNow;
        var viewModel = new HistoryViewModel(
            [new StateTransitionRecord("task-1", "planned", "validated", null, time)],
            [new PolicyValidationRecord("task-1", "step-1", "fs.create_file", "medium", "medium", "medium", true, null, time)],
            [new ApprovalDecisionRecord("task-1", "step-1", true, "alice", null, time)],
            [new VerificationAuditRecord("step-1", "VERIFIED", "passed", time, "task-1")],
            [new SafetyDecisionRecord("old-task", "old-step", "fs.create_file", "medium", "medium", "medium", "system", true, null, time)]);

        viewModel.Events.Should().HaveCount(5);
        viewModel.Events.Should().ContainSingle(entry => entry.Kind == "Approval" && entry.Summary.Contains("alice", StringComparison.Ordinal));
        viewModel.Events.Should().ContainSingle(entry => entry.Kind == "Legacy safety decision");
    }

    [Fact]
    public void SettingsViewModel_Exposes_Read_Only_Roots_Model_And_Doctor_Results()
    {
        var roots = new[] { @"C:\Jarvis\workspace" };
        var doctor = new[] { "OS: PASS", "Ollama: WARN" };
        var viewModel = new SettingsViewModel(roots, "model-from-config", doctor);

        viewModel.AllowedRoots.Should().Equal(roots);
        viewModel.ModelName.Should().Be("model-from-config");
        viewModel.PlannerDescription.Should().Contain("Rule-based fast path");
        viewModel.PlannerDescription.Should().Contain("Ollama model from config is the fallback");
        viewModel.DoctorResults.Should().Equal(doctor);
        ((IList<string>)viewModel.AllowedRoots).IsReadOnly.Should().BeTrue();
        ((IList<string>)viewModel.DoctorResults).IsReadOnly.Should().BeTrue();
    }

    private static ApprovalViewModel CreateApprovalViewModel(string userName) =>
        new(
            new ApprovalRequest("task-1", "step-1", "fs.create_file", new Dictionary<string, object?> { ["path"] = @"C:\Jarvis\file.txt" }, "medium"),
            userName: userName);

    private static VerificationResultDto CreateVerification(
        string status,
        string reason,
        params (string Name, string Outcome)[] stages) =>
        new(
            1,
            "step-1",
            status,
            stages.Select((stage, index) => new VerificationStageResultDto(
                index + 1,
                stage.Name,
                stage.Outcome == "PASSED",
                stage.Name + " detail",
                stage.Outcome)).ToList(),
            [],
            [],
            [reason],
            1);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
