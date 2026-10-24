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
        q.Played.Should().ContainKey("a.mp4");
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
        inItems.Overlaps(q.Played.Keys).Should().BeFalse();
        q.InFlight.Overlaps(q.Played.Keys).Should().BeFalse();
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

    [Fact]
    public void MoveUp_MovesItemOneSlotUp_AndStopsAtTop()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });

        q.MoveUp(q.Items[2]);   // c moves to slot 1
        q.Items[1].FilePath.Should().Be("c.mp4");

        var top = q.Items[0];
        q.MoveUp(top);          // already at top → no-op
        q.Items[0].Should().BeSameAs(top);
    }

    [Fact]
    public void MoveDown_MovesItemOneSlotDown_AndStopsAtBottom()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });

        q.MoveDown(q.Items[0]);  // a moves to slot 1
        q.Items[1].FilePath.Should().Be("a.mp4");

        var bottom = q.Items[2];
        q.MoveDown(bottom);      // already at bottom → no-op
        q.Items[2].Should().BeSameAs(bottom);
    }

    [Fact]
    public void Move_ReordersItems_AndRaisesChangedSoTheNewOrderIsSaved()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4"), V("c.mp4") });
        var changed = 0;
        q.Changed += () => changed++;

        q.Move(0, 2);   // drag a onto c

        q.Items.Select(i => i.FilePath).Should().Equal("b.mp4", "c.mp4", "a.mp4");
        changed.Should().Be(1);
    }

    [Fact]
    public void Clear_EmptiesAllThreeCollections()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4") });
        q.TakeNext();
        q.OnFinished("a.mp4");

        q.Clear();

        q.Items.Should().BeEmpty();
        q.InFlight.Should().BeEmpty();
        q.Played.Should().BeEmpty();
    }

    [Fact]
    public void RefillFromPlayed_PreservesOriginalVideoItemMetadata()
    {
        var q = new SharedQueue();
        var original = new VideoItem("a.mp4", "Custom Name", 12345L);
        q.AddAll(new[] { original });
        q.TakeNext();
        q.OnFinished("a.mp4");

        q.RefillFromPlayed(shuffle: false);

        q.Items.Should().ContainSingle();
        q.Items[0].FilePath.Should().Be("a.mp4");
        q.Items[0].DisplayName.Should().Be("Custom Name");
        q.Items[0].FileSize.Should().Be(12345L);
    }

    [Fact]
    public void Reinsert_OnPathAlreadyInItems_DoesNotDuplicate()
    {
        var q = new SharedQueue();
        q.AddAll(new[] { V("a.mp4"), V("b.mp4") });
        // a is in Items; force a buggy reinsert
        q.Reinsert(V("a.mp4"), position: 0);

        q.Items.Should().HaveCount(2);
        q.Items.Select(i => i.FilePath).Should().BeEquivalentTo(new[] { "a.mp4", "b.mp4" });
    }

    [Fact]
    public void MarkInFlight_RecordsItemSoOnFinishedPopulatesPlayed()
    {
        var q = new SharedQueue();
        var item = new VideoItem("a.mp4", "Custom Name", 99L);

        q.MarkInFlight(item);
        q.OnFinished("a.mp4");

        q.InFlight.Should().BeEmpty();
        q.Played.Should().ContainKey("a.mp4");
        q.Played["a.mp4"].DisplayName.Should().Be("Custom Name");
        q.Played["a.mp4"].FileSize.Should().Be(99L);
    }
}
