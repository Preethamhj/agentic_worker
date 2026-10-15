using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class ToolPolicyTests
{
    private static readonly string ConfigDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "config"));
    private readonly ToolPolicy _toolPolicy;

    public ToolPolicyTests()
    {
        var toolsRegistryPath = Path.Combine(ConfigDir, "tools.registry.json");
        var appsAllowlistPath = Path.Combine(ConfigDir, "apps.allowlist.json");
        _toolPolicy = ToolPolicy.FromFile(toolsRegistryPath, appsAllowlistPath);
    }

    [Fact]
    public void Enabled_Tool_With_Valid_Args_Succeeds()
    {
        var args = new JsonObject
        {
            ["path"] = @"C:\Users\test\Documents\Jarvis\test.txt",
            ["content"] = "hello world"
        };

        var result = _toolPolicy.ValidateToolCall("fs.create_file", args);
        result.IsValid.Should().BeTrue();
        result.PolicyRisk.Should().Be("medium");
    }

    [Fact]
    public void Disabled_Tool_Is_Rejected()
    {
        var args = new JsonObject
        {
            ["script"] = "Get-Process"
        };

        var result = _toolPolicy.ValidateToolCall("shell.run_powershell", args);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("disabled");
    }

    [Fact]
    public void Unknown_Tool_Is_Rejected()
    {
        var args = new JsonObject();
        var result = _toolPolicy.ValidateToolCall("malicious.format_drive", args);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not registered");
    }

    [Fact]
    public void Missing_Required_Arg_Is_Rejected()
    {
        var args = new JsonObject
        {
            ["path"] = @"C:\Users\test\Documents\Jarvis\folder"
            // Missing "content" required by fs.create_file
        };

        var result = _toolPolicy.ValidateToolCall("fs.create_file", args);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema");
    }

    [Fact]
    public void Unexpected_Extra_Arg_Is_Rejected()
    {
        var args = new JsonObject
        {
            ["path"] = @"C:\Users\test\Documents\Jarvis\folder",
            ["extra_arg"] = "disallowed"
        };

        var result = _toolPolicy.ValidateToolCall("fs.create_folder", args);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema");
    }

    [Fact]
    public void Wrong_Arg_Type_Is_Rejected()
    {
        var args = new JsonObject
        {
            ["path"] = 12345 // should be string
        };

        var result = _toolPolicy.ValidateToolCall("fs.create_folder", args);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema");
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("calc")]
    [InlineData("mspaint")]
    public void Allowlisted_App_Is_Accepted(string appId)
    {
        var args = new JsonObject
        {
            ["app_id"] = appId
        };

        var result = _toolPolicy.ValidateToolCall("app.launch", args);
        result.IsValid.Should().BeTrue();
        result.PolicyRisk.Should().Be("medium");
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("powershell")]
    [InlineData("malicious.exe")]
    [InlineData("unknown_app")]
    public void Non_Allowlisted_App_Is_Rejected(string appId)
    {
        var args = new JsonObject
        {
            ["app_id"] = appId
        };

        var result = _toolPolicy.ValidateToolCall("app.launch", args);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("allowlist");
    }

    [Theory]
    [InlineData("line one\nline two")]
    [InlineData("line one\rline two")]
    [InlineData("column\tvalue")]
    [InlineData("null\0byte")]
    public void Reminder_Message_With_Control_Characters_Is_Rejected(string message)
    {
        var args = ReminderArgs(message);

        var result = _toolPolicy.ValidateToolCall("task.schedule_reminder", args);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema");
    }

    [Fact]
    public void Reminder_Message_Over_200_Characters_Is_Rejected()
    {
        var result = _toolPolicy.ValidateToolCall(
            "task.schedule_reminder",
            ReminderArgs(new string('x', ToolPolicy.MaxReminderMessageLength + 1)));

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema");
    }

    [Fact]
    public void Reminder_Message_Accepts_Punctuation_And_Kannada_Within_Limit()
    {
        var message = "Quote \" & 100% ಕನ್ನಡ ನಮಸ್ಕಾರ";

        var result = _toolPolicy.ValidateToolCall("task.schedule_reminder", ReminderArgs(message));

        result.IsValid.Should().BeTrue();
        ToolPolicy.IsValidReminderMessage(message).Should().BeTrue();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef0", false)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]
    public void Reminder_Task_Id_Requires_Exactly_32_Hex_Characters(string taskId, bool expected)
    {
        ToolPolicy.IsValidReminderTaskId(taskId).Should().Be(expected);
    }

    [Theory]
    [InlineData("fs.search_files", "low")]
    [InlineData("fs.create_folder", "medium")]
    [InlineData("fs.create_file", "medium")]
    [InlineData("app.launch", "medium")]
    [InlineData("task.schedule_reminder", "medium")]
    [InlineData("shell.run_powershell", "high")]
    public void GetPolicyRisk_Returns_Correct_Registered_Risk(string toolId, string expectedRisk)
    {
        var risk = _toolPolicy.GetPolicyRisk(toolId);
        risk.Should().Be(expectedRisk);
    }

    private static JsonObject ReminderArgs(string message) =>
        new()
        {
            ["task_name"] = "Review",
            ["time_iso"] = "2026-10-16T09:00:00Z",
            ["message"] = message
        };
}
