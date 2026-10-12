using FluentAssertions;
using Jarvis.Core;
using Xunit;

namespace Jarvis.Core.Tests;

public class CoreTests
{
    [Fact]
    public void SystemClock_Returns_Recent_Utc_Time()
    {
        var clock = SystemClock.Instance;
        var now = clock.UtcNow;

        now.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(TaskState.Created, "created")]
    [InlineData(TaskState.ContextResolved, "context_resolved")]
    [InlineData(TaskState.Parsed, "parsed")]
    [InlineData(TaskState.Planned, "planned")]
    [InlineData(TaskState.Validated, "validated")]
    [InlineData(TaskState.AwaitingApproval, "awaiting_approval")]
    [InlineData(TaskState.Approved, "approved")]
    [InlineData(TaskState.Queued, "queued")]
    [InlineData(TaskState.Executing, "executing")]
    [InlineData(TaskState.Verifying, "verifying")]
    [InlineData(TaskState.Completed, "completed")]
    [InlineData(TaskState.Failed, "failed")]
    [InlineData(TaskState.Cancelled, "cancelled")]
    [InlineData(TaskState.Timeout, "timeout")]
    public void TaskState_ToWireString_Matches_Design_Specification(TaskState state, string expectedWireString)
    {
        state.ToWireString().Should().Be(expectedWireString);
    }
}
