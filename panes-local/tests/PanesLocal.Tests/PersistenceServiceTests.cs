using FluentAssertions;
using PanesLocal.Models;
using PanesLocal.Services;
using Xunit;

namespace PanesLocal.Tests;

public class PersistenceServiceTests : IDisposable
{
    private readonly string _statePath;

    public PersistenceServiceTests()
    {
        _statePath = Path.Combine(Path.GetTempPath(), "panes-local-state-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_statePath)) File.Delete(_statePath);
        if (File.Exists(_statePath + ".tmp")) File.Delete(_statePath + ".tmp");
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAllFields()
    {
        var svc = new PersistenceService(_statePath);
        var state = new AppState
        {
            Grid = new() { Cols = 3, Rows = 4 },
            Zoom = 175,
            Panel = new() { Dock = DockSide.Bottom, Size = 250 },
            Transport = new() { Shuffle = false, RepeatAll = true },
            Audio = new() { UnmutedPaneIndex = 2 },
            Folders = new() { @"D:\Videos" },
            Queue = new() { @"D:\Videos\a.mkv" },
            Played = new() { @"D:\Videos\old.mp4" },
            Panes = new() { new PaneState(0, @"D:\Videos\b.mp4", 42.5) }
        };

        await svc.SaveImmediateAsync(state);
        var loaded = svc.Load();

        loaded.Grid.Cols.Should().Be(3);
        loaded.Grid.Rows.Should().Be(4);
        loaded.Zoom.Should().Be(175);
        loaded.Panel.Dock.Should().Be(DockSide.Bottom);
        loaded.Audio.UnmutedPaneIndex.Should().Be(2);
        loaded.Folders.Should().ContainSingle().Which.Should().Be(@"D:\Videos");
        loaded.Panes.Should().ContainSingle();
        loaded.Panes[0].PositionSeconds.Should().Be(42.5);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var svc = new PersistenceService(_statePath);

        var loaded = svc.Load();

        loaded.Grid.Cols.Should().Be(2);
        loaded.Zoom.Should().Be(100);
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsDefaultsAndReportsFlag()
    {
        File.WriteAllText(_statePath, "{ not valid json");
        var svc = new PersistenceService(_statePath);

        var loaded = svc.Load();

        loaded.Grid.Cols.Should().Be(2);
        svc.LastLoadWasFallback.Should().BeTrue();
    }

    [Fact]
    public async Task SaveImmediate_WritesAtomically_NoLeftoverTmp()
    {
        var svc = new PersistenceService(_statePath);

        await svc.SaveImmediateAsync(new AppState());

        File.Exists(_statePath).Should().BeTrue();
        File.Exists(_statePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task SaveDebounced_CoalescesRapidCalls()
    {
        var svc = new PersistenceService(_statePath, debounceMs: 200);

        for (int i = 0; i < 10; i++)
        {
            svc.SaveDebounced(new AppState { Zoom = 100 + i });
        }

        await Task.Delay(500);

        var loaded = svc.Load();
        loaded.Zoom.Should().Be(109);  // last value wins
    }
}
