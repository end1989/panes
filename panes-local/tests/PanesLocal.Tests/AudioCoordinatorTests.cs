using FluentAssertions;
using PanesLocal.Services;
using Xunit;

namespace PanesLocal.Tests;

public class AudioCoordinatorTests
{
    [Fact]
    public void Initial_State_AllMuted()
    {
        var c = new AudioCoordinator();
        c.UnmutedPaneIndex.Should().BeNull();
    }

    [Fact]
    public void UnmutePane_SetsUnmutedIndex()
    {
        var c = new AudioCoordinator();

        c.UnmutePane(2);

        c.UnmutedPaneIndex.Should().Be(2);
    }

    [Fact]
    public void UnmutingNewPane_TransfersUnmute()
    {
        var c = new AudioCoordinator();
        c.UnmutePane(1);

        c.UnmutePane(3);

        c.UnmutedPaneIndex.Should().Be(3);
    }

    [Fact]
    public void MutePane_OnUnmutedPane_GoesToAllMuted()
    {
        var c = new AudioCoordinator();
        c.UnmutePane(1);

        c.MutePane(1);

        c.UnmutedPaneIndex.Should().BeNull();
    }

    [Fact]
    public void Raises_Changed_OnEachTransition()
    {
        var c = new AudioCoordinator();
        int events = 0;
        c.Changed += () => events++;

        c.UnmutePane(0);
        c.UnmutePane(1);
        c.MutePane(1);

        events.Should().Be(3);
    }

    [Fact]
    public void IsUnmuted_ReturnsTrueOnlyForUnmutedIndex()
    {
        var c = new AudioCoordinator();
        c.UnmutePane(2);

        c.IsUnmuted(2).Should().BeTrue();
        c.IsUnmuted(0).Should().BeFalse();
    }
}
