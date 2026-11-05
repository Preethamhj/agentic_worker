using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Xunit;

namespace Jarvis.Core.Tests;

public class StateMachineTests
{
    private class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void InitialState_Is_Created()
    {
        var clock = new TestClock();
        var sm = new StateMachine("task-123", clock);

        sm.TaskId.Should().Be("task-123");
        sm.CurrentState.Should().Be(TaskState.Created);
        sm.CreatedAt.Should().Be(clock.UtcNow);
        sm.UpdatedAt.Should().Be(clock.UtcNow);
    }

    [Fact]
    public void Legal_Full_Happy_Path_Reaches_Completed_With_VERIFIED()
    {
        var clock = new TestClock();
        var sm = new StateMachine("task-1", clock);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.ContextResolved);
        sm.CurrentState.Should().Be(TaskState.ContextResolved);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Parsed);
        sm.CurrentState.Should().Be(TaskState.Parsed);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Planned);
        sm.CurrentState.Should().Be(TaskState.Planned);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Validated);
        sm.CurrentState.Should().Be(TaskState.Validated);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.AwaitingApproval);
        sm.CurrentState.Should().Be(TaskState.AwaitingApproval);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Approved);
        sm.CurrentState.Should().Be(TaskState.Approved);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Queued);
        sm.CurrentState.Should().Be(TaskState.Queued);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Executing);
        sm.CurrentState.Should().Be(TaskState.Executing);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.Transition(TaskState.Verifying);
        sm.CurrentState.Should().Be(TaskState.Verifying);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        sm.TransitionToCompleted("VERIFIED");
        sm.CurrentState.Should().Be(TaskState.Completed);
    }

    [Theory]
    [InlineData("FAILED")]
    [InlineData("ESCALATE")]
    [InlineData("PARTIAL")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    [InlineData(null)]
    public void Completed_Throws_If_Verification_Not_VERIFIED(string? verificationStatus)
    {
        var sm = CreateAtState(TaskState.Verifying);

        Action act = () => sm.TransitionToCompleted(verificationStatus!);
        act.Should().Throw<IllegalStateTransitionException>();
        sm.CurrentState.Should().Be(TaskState.Verifying);
    }

    [Theory]
    [InlineData(TaskState.Created)]
    [InlineData(TaskState.ContextResolved)]
    [InlineData(TaskState.Parsed)]
    [InlineData(TaskState.Planned)]
    [InlineData(TaskState.Validated)]
    [InlineData(TaskState.AwaitingApproval)]
    [InlineData(TaskState.Approved)]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Executing)]
    public void Completed_Cannot_Be_Reached_From_States_Other_Than_Verifying(TaskState fromState)
    {
        var sm = CreateAtState(fromState);

        Action act1 = () => sm.Transition(TaskState.Completed);
        act1.Should().Throw<IllegalStateTransitionException>();

        Action act2 = () => sm.TransitionToCompleted("VERIFIED");
        act2.Should().Throw<IllegalStateTransitionException>();
    }

    [Theory]
    [InlineData(TaskState.Created)]
    [InlineData(TaskState.ContextResolved)]
    [InlineData(TaskState.Parsed)]
    [InlineData(TaskState.Planned)]
    [InlineData(TaskState.Validated)]
    [InlineData(TaskState.AwaitingApproval)]
    [InlineData(TaskState.Approved)]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Executing)]
    [InlineData(TaskState.Verifying)]
    public void NonTerminal_States_Can_Exit_To_Failed_Cancelled_Timeout(TaskState fromState)
    {
        // Test transition to Failed
        var smFailed = CreateAtState(fromState);
        smFailed.Transition(TaskState.Failed, "failure test");
        smFailed.CurrentState.Should().Be(TaskState.Failed);

        // Test transition to Cancelled
        var smCancelled = CreateAtState(fromState);
        smCancelled.Transition(TaskState.Cancelled, "cancellation test");
        smCancelled.CurrentState.Should().Be(TaskState.Cancelled);

        // Test transition to Timeout
        var smTimeout = CreateAtState(fromState);
        smTimeout.Transition(TaskState.Timeout, "timeout test");
        smTimeout.CurrentState.Should().Be(TaskState.Timeout);
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Timeout)]
    public void Terminal_States_Have_Zero_Exits(TaskState terminalState)
    {
        foreach (TaskState targetState in Enum.GetValues<TaskState>())
        {
            var sm = CreateAtState(terminalState);
            Action act = () => sm.Transition(targetState);
            act.Should().Throw<IllegalStateTransitionException>(
                $"terminal state {terminalState} must not allow transition to {targetState}");
        }
    }

    [Fact]
    public void Every_Illegal_Transition_Throws()
    {
        // Legal non-terminal transitions
        var legalStepTransitions = new Dictionary<TaskState, TaskState>
        {
            [TaskState.Created] = TaskState.ContextResolved,
            [TaskState.ContextResolved] = TaskState.Parsed,
            [TaskState.Parsed] = TaskState.Planned,
            [TaskState.Planned] = TaskState.Validated,
            [TaskState.Validated] = TaskState.AwaitingApproval,
            [TaskState.AwaitingApproval] = TaskState.Approved,
            [TaskState.Approved] = TaskState.Queued,
            [TaskState.Queued] = TaskState.Executing,
            [TaskState.Executing] = TaskState.Verifying
        };

        var exitStates = new HashSet<TaskState> { TaskState.Failed, TaskState.Cancelled, TaskState.Timeout };

        foreach (TaskState from in Enum.GetValues<TaskState>())
        {
            foreach (TaskState to in Enum.GetValues<TaskState>())
            {
                bool isLegal = false;
                if (!StateMachine.IsTerminal(from))
                {
                    if (exitStates.Contains(to))
                    {
                        isLegal = true;
                    }
                    else if (legalStepTransitions.TryGetValue(from, out var legalNext) && legalNext == to)
                    {
                        isLegal = true;
                    }
                }

                if (!isLegal)
                {
                    var sm = CreateAtState(from);
                    Action act = () => sm.Transition(to);
                    act.Should().Throw<IllegalStateTransitionException>(
                        $"Transition from {from} to {to} should be illegal and throw");
                }
            }
        }
    }

    private static StateMachine CreateAtState(TaskState target)
    {
        var clock = new TestClock();
        var sm = new StateMachine("test-task", clock);

        if (target == TaskState.Created) return sm;

        var sequence = new[]
        {
            TaskState.ContextResolved,
            TaskState.Parsed,
            TaskState.Planned,
            TaskState.Validated,
            TaskState.AwaitingApproval,
            TaskState.Approved,
            TaskState.Queued,
            TaskState.Executing,
            TaskState.Verifying
        };

        foreach (var s in sequence)
        {
            sm.Transition(s);
            if (s == target) return sm;
        }

        if (target == TaskState.Completed)
        {
            sm.TransitionToCompleted("VERIFIED");
            return sm;
        }

        if (target is TaskState.Failed or TaskState.Cancelled or TaskState.Timeout)
        {
            sm.Transition(target);
            return sm;
        }

        throw new ArgumentException($"Unknown state: {target}");
    }
}
