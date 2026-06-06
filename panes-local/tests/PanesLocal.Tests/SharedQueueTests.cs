using FluentAssertions;
using PanesLocal.Models;
using PanesLocal.Services;
using Xunit;

namespace PanesLocal.Tests;

public class SharedQueueTests
{
    private static VideoItem V(string path) => new(path, System.IO.Path.GetFileName(path), 0L);

    [Fact]
    public void EmptyQueue_TakeNext_ReturnsNull()
    {
        var q = new SharedQueue();
        q.TakeNext().Should().BeNull();
    }

    [Fact]
    public void AddAll_ThenTakeNext_PopsFirstAndMovesToInFlight()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });

        var taken = q.TakeNext();

        taken!.FilePath.Should().Be("a.mp4");
        q.Items.Should().HaveCount(2);
        q.InFlight.Should().Contain("a.mp4");
        q.Played.Should().BeEmpty();
    }

    [Fact]
    public void OnFinished_MovesPathFromInFlightToPlayed()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4") });
        q.TakeNext();

        q.OnFinished("a.mp4");

        q.InFlight.Should().BeEmpty();
        q.Played.Should().Contain("a.mp4");
    }

    [Fact]
    public void Reinsert_PutsItemAtRequestedPosition()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });
        var a = q.TakeNext()!;

        q.Reinsert(a, position: int.MaxValue);   // bottom

        q.Items.Last().FilePath.Should().Be("a.mp4");
        q.InFlight.Should().NotContain("a.mp4");
    }

    [Fact]
    public void MoveToTop_MovesItemToFrontOfItems()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });

        q.MoveToTop(q.Items[2]);

        q.Items[0].FilePath.Should().Be("c.mp4");
    }

    [Fact]
    public void Remove_RemovesFromItemsOrInFlightOrPlayed()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4") });
        var a = q.TakeNext()!;
        q.OnFinished("a.mp4");

        q.Remove(V("a.mp4"));
        q.Remove(q.Items[0]);

        q.Items.Should().BeEmpty();
        q.InFlight.Should().BeEmpty();
        q.Played.Should().BeEmpty();
    }

    [Fact]
    public void NoDuplicates_PathIsNeverInTwoCollectionsAtOnce()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });
        var a = q.TakeNext()!;
        q.OnFinished("a.mp4");

        var inItems = q.Items.Select(i => i.FilePath).ToHashSet();
        inItems.Overlaps(q.InFlight).Should().BeFalse();
        inItems.Overlaps(q.Played).Should().BeFalse();
        q.InFlight.Overlaps(q.Played).Should().BeFalse();
    }

    [Fact]
    public void AddAll_FiltersOutPathsAlreadyKnown()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4") });
        q.TakeNext();                              // a → InFlight
        q.AddAll(new[] { V("a.mp4"), V("b.mp4") });

        q.InFlight.Should().ContainSingle();
        q.Items.Should().ContainSingle(x => x.FilePath == "b.mp4");
    }

    [Fact]
    public void RefillFromPlayed_MovesPlayedBackIntoItems()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4") });
        q.TakeNext(); q.OnFinished("a.mp4");
        q.TakeNext(); q.OnFinished("b.mp4");

        q.RefillFromPlayed(shuffle: false);

        q.Items.Should().HaveCount(2);
        q.Played.Should().BeEmpty();
    }

    [Fact]
    public void Shuffle_KeepsSameItemsButChangesOrder()
    {
        var q = new SharedQueue();
        var items = Enumerable.Range(0, 50).Select(i => V($"v{i}.mp4")).ToList();
        q.AddAll(items);
        var before = q.Items.Select(i => i.FilePath).ToList();

        q.Shuffle();

        q.Items.Should().HaveCount(50);
        q.Items.Select(i => i.FilePath).Should().BeEquivalentTo(before);
        q.Items.Select(i => i.FilePath).SequenceEqual(before).Should().BeFalse();  // statistically certain for 50 items
    }
}
