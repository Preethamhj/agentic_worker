using FluentAssertions;
using Jarvis.Eval;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class EvalFaultInjectorTests
{
    [Fact] public void Folder_F1_Real_State_And_Ground_Truth() => AssertDeclaredFault("F1", "fs.create_folder");
    [Fact] public void Folder_F4_Real_State_And_Ground_Truth() => AssertDeclaredFault("F4", "fs.create_folder");
    [Fact] public void Folder_F5_Real_State_And_Ground_Truth() => AssertDeclaredFault("F5", "fs.create_folder");
    [Fact] public void Folder_F6_Real_State_And_Ground_Truth() => AssertDeclaredFault("F6", "fs.create_folder");
    [Fact] public void Search_F1_Real_State_And_Ground_Truth() => AssertDeclaredFault("F1", "fs.search_files");
    [Fact] public void Search_F7_Real_State_And_Ground_Truth() => AssertDeclaredFault("F7", "fs.search_files");
    [Fact] public void Search_F8_Real_State_And_Ground_Truth() => AssertDeclaredFault("F8", "fs.search_files");
    [Fact] public void App_F1_Real_State_And_Ground_Truth() => AssertDeclaredFault("F1", "app.launch");
    [Fact] public void App_F9_Real_State_And_Ground_Truth() => AssertDeclaredFault("F9", "app.launch");
    [Fact] public void App_F10_Real_State_And_Ground_Truth() => AssertDeclaredFault("F10", "app.launch");
    [Fact] public void App_F11_Real_State_And_Ground_Truth() => AssertDeclaredFault("F11", "app.launch");
    [Fact] public void Reminder_F1_Real_State_And_Ground_Truth() => AssertDeclaredFault("F1", "task.schedule_reminder");
    [Fact] public void Reminder_F12_Real_State_And_Ground_Truth() => AssertDeclaredFault("F12", "task.schedule_reminder");
    [Fact] public void Reminder_F13_Real_State_And_Ground_Truth() => AssertDeclaredFault("F13", "task.schedule_reminder");
    [Fact] public void Reminder_F14_Real_State_And_Ground_Truth() => AssertDeclaredFault("F14", "task.schedule_reminder");
    [Fact] public void Reminder_F15_Real_State_And_Ground_Truth() => AssertDeclaredFault("F15", "task.schedule_reminder");
    [Fact] public void Reminder_F16_Real_State_And_Ground_Truth() => AssertDeclaredFault("F16", "task.schedule_reminder");
    [Fact] public void Reminder_F17a_Real_State_And_Ground_Truth() => AssertDeclaredFault("F17a", "task.schedule_reminder");
    [Fact] public void Reminder_F17b_Real_State_And_Ground_Truth() => AssertDeclaredFault("F17b", "task.schedule_reminder");

    [Theory]
    [InlineData("F0", "fs.create_file")]
    [InlineData("F1", "fs.create_file")]
    [InlineData("F2", "fs.create_file")]
    [InlineData("F3", "fs.create_file")]
    [InlineData("F4", "fs.create_file")]
    [InlineData("F5", "fs.create_folder")]
    [InlineData("F6", "fs.create_file")]
    [InlineData("F7", "fs.search_files")]
    [InlineData("F8", "fs.search_files")]
    [InlineData("F9", "app.launch")]
    [InlineData("F10", "app.launch")]
    [InlineData("F11", "app.launch")]
    [InlineData("F12", "task.schedule_reminder")]
    [InlineData("F13", "task.schedule_reminder")]
    [InlineData("F14", "task.schedule_reminder")]
    [InlineData("F15", "task.schedule_reminder")]
    [InlineData("F16", "task.schedule_reminder")]
    [InlineData("F17a", "task.schedule_reminder")]
    [InlineData("F17b", "task.schedule_reminder")]
    [InlineData("F18", "fs.create_file")]
    public void Injector_Produces_Declared_Fault_State_And_Ground_Truth(string faultId, string toolId)
    {
        var initial = EvalFaultState.CreateGood(toolId);

        var result = new EvalFaultInjector().Inject(faultId, initial);

        result.GroundTruth.Should().Be(faultId == "F0" ? EvalGroundTruth.Good : EvalGroundTruth.Bad);
        switch (faultId)
        {
            case "F0":
                result.Should().BeSameAs(initial);
                break;
            case "F1":
                result.ExecutorRan.Should().BeFalse();
                result.ReceiptSuccess.Should().BeTrue();
                break;
            case "F2":
                result.Content.Should().NotEqual(initial.Content);
                break;
            case "F3":
                result.Content.Length.Should().Be(initial.Content.Length / 2);
                break;
            case "F4":
                result.TargetExists.Should().BeFalse();
                result.SiblingTargetExists.Should().BeTrue();
                break;
            case "F5":
                result.TargetExists.Should().BeTrue();
                result.ExtraFileExists.Should().BeTrue();
                break;
            case "F6":
                result.TargetExists.Should().BeFalse();
                break;
            case "F7":
                result.ReportedMatches.Should().NotContain("match-1");
                result.ExistingMatches.Should().Contain("match-1");
                break;
            case "F8":
                result.ReportedMatches.Should().Contain("phantom.txt");
                result.ExistingMatches.Should().NotContain("phantom.txt");
                break;
            case "F9":
                result.AuthorizedProcessRunning.Should().BeFalse();
                result.WrongProcessRunning.Should().BeTrue();
                break;
            case "F10":
                result.AuthorizedProcessRunning.Should().BeFalse();
                break;
            case "F11":
                result.AuthorizedProcessRunning.Should().BeTrue();
                result.ExtraProcessRunning.Should().BeTrue();
                break;
            case "F12":
                result.TriggerOffsetSeconds.Should().Be(60);
                break;
            case "F13":
                result.ActionMatches.Should().BeFalse();
                break;
            case "F14":
                result.TaskEnabled.Should().BeFalse();
                break;
            case "F15":
                result.ExtraTaskExists.Should().BeTrue();
                break;
            case "F16":
                result.PayloadMatches.Should().BeFalse();
                break;
            case "F17a":
                result.BatterySettingMatches.Should().BeFalse();
                break;
            case "F17b":
                result.ExpirySettingMatches.Should().BeFalse();
                break;
            case "F18":
                result.EvidenceFresh.Should().BeFalse();
                break;
            default:
                throw new InvalidOperationException($"Unexpected fault ID {faultId}.");
        }
    }

    [Fact]
    public void Injector_Rejects_A_Fault_That_Does_Not_Apply_To_The_Tool()
    {
        var act = () => new EvalFaultInjector().Inject(
            "F12",
            EvalFaultState.CreateGood("fs.create_file"));

        act.Should().Throw<ArgumentException>();
    }

    private static void AssertDeclaredFault(string faultId, string toolId)
    {
        var result = new EvalFaultInjector().Inject(faultId, EvalFaultState.CreateGood(toolId));
        result.GroundTruth.Should().Be(EvalGroundTruth.Bad);
        result.ToolId.Should().Be(toolId);
    }
}
