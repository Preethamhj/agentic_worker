using FluentAssertions;
using Jarvis.Browser.Core;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserTabStateTrackerTests
{
    [Fact]
    public void Navigation_Updates_Url_Title_Loading_And_History_State()
    {
        var tabId = BrowserTabId.New();
        var tracker = new BrowserTabStateTracker(tabId);
        var started = new Uri("https://example.com/start");
        var completed = new Uri("https://example.com/final");

        tracker.NavigationStarted(started);
        tracker.State.IsLoading.Should().BeTrue();
        tracker.State.Address.Should().Be(started);

        tracker.NavigationCompleted(completed, "Example", canGoBack: true, canGoForward: false);
        tracker.State.IsLoading.Should().BeFalse();
        tracker.State.Address.Should().Be(completed);
        tracker.State.Title.Should().Be("Example");
        tracker.State.CanGoBack.Should().BeTrue();
        tracker.State.CanGoForward.Should().BeFalse();
        tracker.State.TabId.Should().Be(tabId);
    }

    [Fact]
    public void Title_And_History_Changes_Preserve_Other_State()
    {
        var tracker = new BrowserTabStateTracker(BrowserTabId.New());
        var address = new Uri("https://example.com/");
        tracker.NavigationCompleted(address, "Initial", canGoBack: false, canGoForward: false);

        tracker.SetTitle("Updated title");
        tracker.SetHistoryState(canGoBack: true, canGoForward: true);

        tracker.State.Address.Should().Be(address);
        tracker.State.Title.Should().Be("Updated title");
        tracker.State.IsLoading.Should().BeFalse();
        tracker.State.CanGoBack.Should().BeTrue();
        tracker.State.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void StateChanged_Fires_Only_When_State_Actually_Changes()
    {
        var tracker = new BrowserTabStateTracker(BrowserTabId.New());
        var changes = 0;
        tracker.StateChanged += (_, _) => changes++;

        tracker.SetTitle("New tab");
        tracker.SetTitle("Example");
        tracker.SetTitle("Example");

        changes.Should().Be(1);
    }
}
