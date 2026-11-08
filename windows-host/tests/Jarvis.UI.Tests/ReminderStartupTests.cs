using FluentAssertions;
using Jarvis.UI;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class ReminderStartupTests
{
    [Fact]
    public void Reminder_Startup_Routes_Arguments_To_Dispatcher()
    {
        string[]? received = null;
        var args = new[] { "--jarvis-reminder", "0123456789abcdef0123456789abcdef" };

        var handled = ReminderStartup.TryDispatch(args, values =>
        {
            received = values;
            return 0;
        }, out var exitCode);

        handled.Should().BeTrue();
        received.Should().Equal(args);
        exitCode.Should().Be(0);
    }

    [Fact]
    public void Normal_Startup_Does_Not_Dispatch()
    {
        var invoked = false;
        var handled = ReminderStartup.TryDispatch([], _ =>
        {
            invoked = true;
            return 0;
        }, out _);

        handled.Should().BeFalse();
        invoked.Should().BeFalse();
    }
}
