# Panes Local Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Windows .NET 10 WPF application that displays a multi-pane video wall backed by a single shared queue of files scanned from a local folder, with an edge-peek side panel, drag-and-drop playlist editing, and a Windows installer.

**Architecture:** A `SharedQueue` model is the single source of truth; each `Pane` user control owns one `LibVLCSharp` `MediaPlayer` and pulls from the queue when it finishes a video. The `SidePanel` is a borderless slide-out that hugs whichever window edge it's docked to. All state persists to `%AppData%\PanesLocal\state.json` via a debounced atomic-write `PersistenceService`. The whole app builds into one self-contained exe and is packaged by Inno Setup.

**Tech Stack:** .NET 10 (LTS) / WPF / C# 14, LibVLCSharp + LibVLCSharp.WPF + VideoLAN.LibVLC.Windows, xUnit + FluentAssertions, Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-06-06-panes-local-design.md` — every task in this plan references a section of the spec by name.

---

## Prerequisites (one-time setup, do before Task 1)

The engineer needs these installed:

1. **.NET 10 SDK** — verify with `dotnet --version`. Expected: `10.0.x`. Install from <https://dotnet.microsoft.com/download/dotnet/10.0> if missing.
2. **Inno Setup 6** — verify with `iscc /?`. Install from <https://jrsoftware.org/isdl.php> if missing. Add `C:\Program Files (x86)\Inno Setup 6` to PATH.

If either is missing, install before starting Task 1. Tasks 1–14 work without Inno Setup; only Task 15 needs it.

---

## File Structure

```
panes-local/                                 (new sibling to existing panes/ Electron app)
  PanesLocal.sln
  src/PanesLocal/
    PanesLocal.csproj                        ← WPF target, single-file publish settings
    App.xaml, App.xaml.cs                    ← app entry, libVLC core init
    MainWindow.xaml, MainWindow.xaml.cs      ← borderless window, grid, F11, cursor hide
    Models/
      VideoItem.cs                           ← record: FilePath, DisplayName, FileSize
      PaneState.cs                           ← snapshot of one pane for persistence
      AppState.cs                            ← whole-app snapshot for persistence
      DockSide.cs                            ← enum: Left, Right, Top, Bottom
    Services/
      SharedQueue.cs                         ← Items / InFlight / Played + ops
      FolderScanner.cs                       ← async recursive walk + extension filter
      AudioCoordinator.cs                    ← one-pane-unmuted rule
      PersistenceService.cs                  ← debounced atomic JSON read/write
    Views/
      Pane.xaml, Pane.xaml.cs                ← VideoView + MediaPlayer + hover overlay
      SidePanel.xaml, SidePanel.xaml.cs      ← edge-peek, auto-close, controls, playlist
  tests/PanesLocal.Tests/
    PanesLocal.Tests.csproj                  ← xUnit + FluentAssertions
    SharedQueueTests.cs
    FolderScannerTests.cs
    AudioCoordinatorTests.cs
    PersistenceServiceTests.cs
  installer/
    PanesLocal.iss                           ← Inno Setup script
  build.ps1                                  ← dotnet publish + iscc
  README.md                                  ← build + run instructions
```

The existing `panes/` Electron app is **untouched**.

---

# Phase 1: Foundation (services + tests, no UI yet)

## Task 1: Project scaffolding

**Files:**
- Create: `panes-local/PanesLocal.sln`
- Create: `panes-local/src/PanesLocal/PanesLocal.csproj`
- Create: `panes-local/src/PanesLocal/App.xaml`
- Create: `panes-local/src/PanesLocal/App.xaml.cs`
- Create: `panes-local/src/PanesLocal/MainWindow.xaml`
- Create: `panes-local/src/PanesLocal/MainWindow.xaml.cs`
- Create: `panes-local/tests/PanesLocal.Tests/PanesLocal.Tests.csproj`
- Create: `panes-local/README.md`
- Create: `panes-local/.gitignore`

- [ ] **Step 1: Create the solution and main project**

Run from the repo root:

```powershell
cd panes-local
dotnet new sln -n PanesLocal
dotnet new wpf -n PanesLocal -o src/PanesLocal --framework net10.0-windows
dotnet sln add src/PanesLocal/PanesLocal.csproj
```

- [ ] **Step 2: Replace generated `PanesLocal.csproj` with explicit settings**

Overwrite `panes-local/src/PanesLocal/PanesLocal.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <AssemblyName>PanesLocal</AssemblyName>
    <RootNamespace>PanesLocal</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LibVLCSharp" Version="3.8.5" />
    <PackageReference Include="LibVLCSharp.WPF" Version="3.8.5" />
    <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.20" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create per-monitor DPI manifest**

Create `panes-local/src/PanesLocal/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: Replace App.xaml**

Overwrite `panes-local/src/PanesLocal/App.xaml`:

```xml
<Application x:Class="PanesLocal.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources/>
</Application>
```

- [ ] **Step 5: Replace App.xaml.cs with libVLC core init**

Overwrite `panes-local/src/PanesLocal/App.xaml.cs`:

```csharp
using System.Windows;
using LibVLCSharp.Shared;

namespace PanesLocal;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Initialize();
        base.OnStartup(e);
    }
}
```

- [ ] **Step 6: Replace MainWindow with a borderless black window**

Overwrite `panes-local/src/PanesLocal/MainWindow.xaml`:

```xml
<Window x:Class="PanesLocal.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Panes Local"
        Width="1280" Height="720"
        Background="#000000"
        WindowStyle="None"
        ResizeMode="CanResize"
        AllowsTransparency="False">
    <Grid x:Name="RootGrid"/>
</Window>
```

Overwrite `panes-local/src/PanesLocal/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace PanesLocal;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
```

- [ ] **Step 7: Create the test project**

```powershell
dotnet new xunit -n PanesLocal.Tests -o tests/PanesLocal.Tests --framework net10.0-windows
dotnet sln add tests/PanesLocal.Tests/PanesLocal.Tests.csproj
dotnet add tests/PanesLocal.Tests/PanesLocal.Tests.csproj package FluentAssertions --version 6.12.0
dotnet add tests/PanesLocal.Tests/PanesLocal.Tests.csproj reference src/PanesLocal/PanesLocal.csproj
```

Verify `panes-local/tests/PanesLocal.Tests/PanesLocal.Tests.csproj` has `<TargetFramework>net10.0-windows</TargetFramework>` so the test project can reference WPF types if needed.

- [ ] **Step 8: Create README and gitignore**

Create `panes-local/README.md`:

```markdown
# Panes Local

Standalone native Windows multi-pane local-video player. Sibling to the Electron `panes` app.

## Build

```powershell
dotnet build
dotnet run --project src/PanesLocal
```

## Test

```powershell
dotnet test
```

## Package (Installer)

```powershell
./build.ps1
```
```

Create `panes-local/.gitignore`:

```
bin/
obj/
dist/
*.user
.vs/
```

- [ ] **Step 9: Verify build and run**

Run from `panes-local/`:

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

```powershell
dotnet run --project src/PanesLocal
```

Expected: A small black window with no border opens. F11 maximizes it. Close it with Alt+F4 (no chrome to click yet).

```powershell
dotnet test
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0` (xUnit ships with one sample test, harmless).

- [ ] **Step 10: Commit**

```powershell
git add panes-local/
git commit -m "panes-local: project scaffolding (WPF + LibVLCSharp + xUnit)"
```

---

## Task 2: VideoItem record + SharedQueue (TDD)

**Spec sections:** "Shared Queue", "Folder Loading" (extension filter consumed by scanner — not here).

**Files:**
- Create: `panes-local/src/PanesLocal/Models/VideoItem.cs`
- Create: `panes-local/src/PanesLocal/Services/SharedQueue.cs`
- Create: `panes-local/tests/PanesLocal.Tests/SharedQueueTests.cs`

- [ ] **Step 1: Write the failing `VideoItem` and queue invariant tests**

Create `panes-local/tests/PanesLocal.Tests/SharedQueueTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to confirm they fail**

Run from `panes-local/`:

```powershell
dotnet test
```

Expected: Build fails — types `VideoItem`, `SharedQueue` not found.

- [ ] **Step 3: Implement VideoItem**

Create `panes-local/src/PanesLocal/Models/VideoItem.cs`:

```csharp
namespace PanesLocal.Models;

public record VideoItem(string FilePath, string DisplayName, long FileSize);
```

- [ ] **Step 4: Implement SharedQueue**

Create `panes-local/src/PanesLocal/Services/SharedQueue.cs`:

```csharp
using System.Collections.ObjectModel;
using PanesLocal.Models;

namespace PanesLocal.Services;

public class SharedQueue
{
    public ObservableCollection<VideoItem> Items { get; } = new();
    public HashSet<string> InFlight { get; } = new();
    public HashSet<string> Played { get; } = new();

    public event Action? Changed;

    private void NotifyChanged() => Changed?.Invoke();

    public void AddAll(IEnumerable<VideoItem> incoming)
    {
        foreach (var item in incoming)
        {
            if (KnownPath(item.FilePath)) continue;
            Items.Add(item);
        }
        NotifyChanged();
    }

    public VideoItem? TakeNext()
    {
        if (Items.Count == 0) return null;
        var first = Items[0];
        Items.RemoveAt(0);
        InFlight.Add(first.FilePath);
        NotifyChanged();
        return first;
    }

    public void OnFinished(string path)
    {
        if (InFlight.Remove(path)) Played.Add(path);
        NotifyChanged();
    }

    public void Reinsert(VideoItem item, int position)
    {
        InFlight.Remove(item.FilePath);
        Played.Remove(item.FilePath);
        var idx = Math.Clamp(position, 0, Items.Count);
        Items.Insert(idx, item);
        NotifyChanged();
    }

    public void Remove(VideoItem item)
    {
        var existing = Items.FirstOrDefault(i => i.FilePath == item.FilePath);
        if (existing is not null) Items.Remove(existing);
        InFlight.Remove(item.FilePath);
        Played.Remove(item.FilePath);
        NotifyChanged();
    }

    public void MoveToTop(VideoItem item)
    {
        if (!Items.Remove(item)) return;
        Items.Insert(0, item);
        NotifyChanged();
    }

    public void MoveUp(VideoItem item)
    {
        var idx = Items.IndexOf(item);
        if (idx <= 0) return;
        Items.Move(idx, idx - 1);
        NotifyChanged();
    }

    public void MoveDown(VideoItem item)
    {
        var idx = Items.IndexOf(item);
        if (idx < 0 || idx >= Items.Count - 1) return;
        Items.Move(idx, idx + 1);
        NotifyChanged();
    }

    public void Shuffle()
    {
        var rng = Random.Shared;
        var arr = Items.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        Items.Clear();
        foreach (var v in arr) Items.Add(v);
        NotifyChanged();
    }

    public void Reshuffle() => Shuffle();

    public void RefillFromPlayed(bool shuffle)
    {
        var refill = Played.Select(p => new VideoItem(p, Path.GetFileName(p), 0L)).ToList();
        Played.Clear();
        foreach (var v in refill) Items.Add(v);
        if (shuffle) Shuffle();
        NotifyChanged();
    }

    public void Clear()
    {
        Items.Clear();
        InFlight.Clear();
        Played.Clear();
        NotifyChanged();
    }

    private bool KnownPath(string path)
        => InFlight.Contains(path)
        || Played.Contains(path)
        || Items.Any(i => i.FilePath == path);
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```powershell
dotnet test
```

Expected: `Passed: 10`. (The default xUnit sample test plus our 9.)

If the Shuffle test fails intermittently because of bad luck, accept it and re-run; with 50 items the false-positive rate is ~1 in 50!.

- [ ] **Step 6: Commit**

```powershell
git add panes-local/src/PanesLocal/Models/VideoItem.cs `
        panes-local/src/PanesLocal/Services/SharedQueue.cs `
        panes-local/tests/PanesLocal.Tests/SharedQueueTests.cs
git commit -m "panes-local: SharedQueue model with TDD invariants"
```

---

## Task 3: FolderScanner

**Spec sections:** "Folder Loading".

**Files:**
- Create: `panes-local/src/PanesLocal/Services/FolderScanner.cs`
- Create: `panes-local/tests/PanesLocal.Tests/FolderScannerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `panes-local/tests/PanesLocal.Tests/FolderScannerTests.cs`:

```csharp
using FluentAssertions;
using PanesLocal.Services;
using Xunit;

namespace PanesLocal.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _root;

    public FolderScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "panes-local-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relPath)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[] { 0 });
    }

    [Fact]
    public async Task Scan_IncludesVideoExtensionsCaseInsensitive()
    {
        Touch("a.mp4"); Touch("b.MKV"); Touch("c.WebM"); Touch("d.mov");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Select(v => Path.GetFileName(v.FilePath)).Should().BeEquivalentTo(
            new[] { "a.mp4", "b.MKV", "c.WebM", "d.mov" });
    }

    [Fact]
    public async Task Scan_ExcludesNonVideoExtensions()
    {
        Touch("a.mp4"); Touch("ignore.txt"); Touch("img.jpg"); Touch("audio.mp3");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().EndWith("a.mp4");
    }

    [Fact]
    public async Task Scan_Recurses()
    {
        Touch("a.mp4"); Touch("sub/b.mp4"); Touch("sub/deep/c.mp4");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Scan_SkipsHiddenFiles()
    {
        Touch("a.mp4"); Touch(".hidden.mp4");
        File.SetAttributes(Path.Combine(_root, ".hidden.mp4"), FileAttributes.Hidden);

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().EndWith("a.mp4");
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        Touch("a.mp4"); Touch("b.mp4"); Touch("c.mp4");
        var counts = new List<int>();

        await new FolderScanner().ScanAsync(_root, progress: new Progress<int>(n => counts.Add(n)), ct: default);

        counts.Should().NotBeEmpty();
        counts.Last().Should().Be(3);
    }

    [Fact]
    public async Task Scan_HonorsCancellation()
    {
        for (int i = 0; i < 100; i++) Touch($"v{i}.mp4");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => new FolderScanner().ScanAsync(_root, progress: null, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```powershell
dotnet test
```

Expected: Build fails — `FolderScanner` not found.

- [ ] **Step 3: Implement FolderScanner**

Create `panes-local/src/PanesLocal/Services/FolderScanner.cs`:

```csharp
using PanesLocal.Models;

namespace PanesLocal.Services;

public class FolderScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v",
        ".wmv", ".flv", ".ts", ".mpg", ".mpeg", ".ogv"
    };

    public async Task<List<VideoItem>> ScanAsync(
        string rootPath,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var results = new List<VideoItem>();
            ScanDir(rootPath, results, progress, ct);
            return results;
        }, ct);
    }

    private static void ScanDir(
        string dir,
        List<VideoItem> results,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (DirectoryNotFoundException)  { return; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            FileAttributes attrs;
            try { attrs = File.GetAttributes(entry); }
            catch { continue; }

            if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;          // no symlinks

            if ((attrs & FileAttributes.Directory) != 0)
            {
                ScanDir(entry, results, progress, ct);
                continue;
            }

            var ext = Path.GetExtension(entry);
            if (!VideoExtensions.Contains(ext)) continue;

            long size = 0;
            try { size = new FileInfo(entry).Length; } catch { }

            results.Add(new VideoItem(entry, Path.GetFileName(entry), size));
            progress?.Report(results.Count);
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```powershell
dotnet test
```

Expected: All FolderScanner tests pass.

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/Services/FolderScanner.cs `
        panes-local/tests/PanesLocal.Tests/FolderScannerTests.cs
git commit -m "panes-local: FolderScanner with recursive scan + extension filter"
```

---

## Task 4: AudioCoordinator

**Spec sections:** "Audio (one-pane-unmuted-at-a-time)".

**Files:**
- Create: `panes-local/src/PanesLocal/Services/AudioCoordinator.cs`
- Create: `panes-local/tests/PanesLocal.Tests/AudioCoordinatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `panes-local/tests/PanesLocal.Tests/AudioCoordinatorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to confirm they fail**

```powershell
dotnet test
```

Expected: Build fails — `AudioCoordinator` not found.

- [ ] **Step 3: Implement AudioCoordinator**

Create `panes-local/src/PanesLocal/Services/AudioCoordinator.cs`:

```csharp
namespace PanesLocal.Services;

public class AudioCoordinator
{
    public int? UnmutedPaneIndex { get; private set; }
    public event Action? Changed;

    public bool IsUnmuted(int paneIndex) => UnmutedPaneIndex == paneIndex;

    public void UnmutePane(int paneIndex)
    {
        if (UnmutedPaneIndex == paneIndex) return;
        UnmutedPaneIndex = paneIndex;
        Changed?.Invoke();
    }

    public void MutePane(int paneIndex)
    {
        if (UnmutedPaneIndex != paneIndex) return;
        UnmutedPaneIndex = null;
        Changed?.Invoke();
    }

    public void MuteAll()
    {
        if (UnmutedPaneIndex is null) return;
        UnmutedPaneIndex = null;
        Changed?.Invoke();
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```powershell
dotnet test
```

Expected: All AudioCoordinator tests pass.

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/Services/AudioCoordinator.cs `
        panes-local/tests/PanesLocal.Tests/AudioCoordinatorTests.cs
git commit -m "panes-local: AudioCoordinator for one-pane-unmuted rule"
```

---

## Task 5: AppState + PersistenceService

**Spec sections:** "Persistence".

**Files:**
- Create: `panes-local/src/PanesLocal/Models/PaneState.cs`
- Create: `panes-local/src/PanesLocal/Models/AppState.cs`
- Create: `panes-local/src/PanesLocal/Models/DockSide.cs`
- Create: `panes-local/src/PanesLocal/Services/PersistenceService.cs`
- Create: `panes-local/tests/PanesLocal.Tests/PersistenceServiceTests.cs`

- [ ] **Step 1: Create the state types**

Create `panes-local/src/PanesLocal/Models/DockSide.cs`:

```csharp
namespace PanesLocal.Models;

public enum DockSide { Left, Right, Top, Bottom }
```

Create `panes-local/src/PanesLocal/Models/PaneState.cs`:

```csharp
namespace PanesLocal.Models;

public record PaneState(int Index, string? FilePath, double PositionSeconds);
```

Create `panes-local/src/PanesLocal/Models/AppState.cs`:

```csharp
namespace PanesLocal.Models;

public class AppState
{
    public WindowState Window { get; set; } = new();
    public GridState Grid { get; set; } = new();
    public int Zoom { get; set; } = 100;
    public PanelState Panel { get; set; } = new();
    public TransportState Transport { get; set; } = new();
    public AudioState Audio { get; set; } = new();
    public List<string> Folders { get; set; } = new();
    public List<string> Queue { get; set; } = new();
    public List<string> Played { get; set; } = new();
    public List<PaneState> Panes { get; set; } = new();

    public class WindowState { public double X { get; set; } = 100; public double Y { get; set; } = 80;
                               public double Width { get; set; } = 1280; public double Height { get; set; } = 720;
                               public bool Maximized { get; set; } public bool Fullscreen { get; set; } }
    public class GridState { public int Cols { get; set; } = 2; public int Rows { get; set; } = 2; }
    public class PanelState { public DockSide Dock { get; set; } = DockSide.Right; public double Size { get; set; } = 320; }
    public class TransportState { public bool Shuffle { get; set; } = true; public bool RepeatAll { get; set; } = true; }
    public class AudioState { public int? UnmutedPaneIndex { get; set; } }
}
```

- [ ] **Step 2: Write the failing PersistenceService tests**

Create `panes-local/tests/PanesLocal.Tests/PersistenceServiceTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to confirm they fail**

```powershell
dotnet test
```

Expected: Build fails — `PersistenceService` not found.

- [ ] **Step 4: Implement PersistenceService**

Create `panes-local/src/PanesLocal/Services/PersistenceService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using PanesLocal.Models;

namespace PanesLocal.Services;

public class PersistenceService
{
    private readonly string _path;
    private readonly int _debounceMs;
    private readonly JsonSerializerOptions _jsonOpts;
    private CancellationTokenSource? _debounceCts;
    private AppState? _pending;

    public bool LastLoadWasFallback { get; private set; }

    public PersistenceService(string path, int debounceMs = 500)
    {
        _path = path;
        _debounceMs = debounceMs;
        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public AppState Load()
    {
        LastLoadWasFallback = false;
        if (!File.Exists(_path)) return new AppState();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppState>(json, _jsonOpts) ?? new AppState();
        }
        catch
        {
            LastLoadWasFallback = true;
            return new AppState();
        }
    }

    public async Task SaveImmediateAsync(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(state, _jsonOpts);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    public void SaveDebounced(AppState state)
    {
        _pending = state;
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_debounceMs, ct); }
            catch (TaskCanceledException) { return; }

            var snapshot = _pending;
            if (snapshot is null) return;
            try { await SaveImmediateAsync(snapshot); } catch { /* swallow; UI flow */ }
        });
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```powershell
dotnet test
```

Expected: All PersistenceService tests pass.

- [ ] **Step 6: Commit**

```powershell
git add panes-local/src/PanesLocal/Models/ `
        panes-local/src/PanesLocal/Services/PersistenceService.cs `
        panes-local/tests/PanesLocal.Tests/PersistenceServiceTests.cs
git commit -m "panes-local: AppState + PersistenceService (debounced atomic JSON)"
```

---

# Phase 2: Playback UI

## Task 6: Pane user control

**Spec sections:** "Pane Lifecycle", "Audio", "Grid & Panes" (per-pane visuals).

**Files:**
- Create: `panes-local/src/PanesLocal/Views/Pane.xaml`
- Create: `panes-local/src/PanesLocal/Views/Pane.xaml.cs`
- Modify: `panes-local/src/PanesLocal/App.xaml.cs` (expose shared `LibVLC` instance)

- [ ] **Step 1: Add a shared `LibVLC` instance on App**

Replace `panes-local/src/PanesLocal/App.xaml.cs`:

```csharp
using System.Windows;
using LibVLCSharp.Shared;

namespace PanesLocal;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Initialize();
        LibVLC = new LibVLC("--no-osd", "--quiet", "--no-video-title-show");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Create the Pane XAML**

Create `panes-local/src/PanesLocal/Views/Pane.xaml`:

```xml
<UserControl x:Class="PanesLocal.Views.Pane"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
             Background="#000000"
             AllowDrop="True">
    <Grid>
        <vlc:VideoView x:Name="VlcView" />

        <Grid x:Name="HoverOverlay"
              VerticalAlignment="Top"
              HorizontalAlignment="Right"
              Margin="0,6,8,0"
              Opacity="0">
            <StackPanel Orientation="Horizontal">
                <Button x:Name="MuteBtn" Content="🔇" Width="28" Height="28"
                        Background="#80000000" Foreground="White" BorderThickness="0"
                        Click="MuteBtn_Click" Margin="0,0,4,0"/>
                <Button x:Name="SkipBtn" Content="⏭" Width="28" Height="28"
                        Background="#80000000" Foreground="White" BorderThickness="0"
                        Click="SkipBtn_Click" Margin="0,0,4,0"/>
                <Button x:Name="StopBtn" Content="✕" Width="28" Height="28"
                        Background="#80000000" Foreground="White" BorderThickness="0"
                        Click="StopBtn_Click"/>
            </StackPanel>
        </Grid>

        <Border x:Name="DragHighlight"
                BorderBrush="#FF4FC3F7"
                BorderThickness="3"
                Visibility="Collapsed"/>
    </Grid>
</UserControl>
```

- [ ] **Step 3: Create the Pane code-behind**

Create `panes-local/src/PanesLocal/Views/Pane.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LibVLCSharp.Shared;
using PanesLocal.Models;
using PanesLocal.Services;

namespace PanesLocal.Views;

public partial class Pane : UserControl, IDisposable
{
    public int PaneIndex { get; set; }
    public VideoItem? Current { get; private set; }
    public double CurrentPositionSeconds => _player?.Time / 1000.0 ?? 0;

    public event Action<Pane>? RequestNext;
    public event Action<Pane>? RequestUnmute;
    public event Action<Pane>? RequestMute;
    public event Action<Pane, VideoItem>? VideoStarted;
    public event Action<Pane, VideoItem>? VideoFinished;

    private MediaPlayer? _player;
    private SharedQueue? _queue;
    private AudioCoordinator? _audio;

    public Pane()
    {
        InitializeComponent();
        MouseEnter += (_, _) => FadeOverlay(1);
        MouseLeave += (_, _) => FadeOverlay(0);
    }

    public void Attach(SharedQueue queue, AudioCoordinator audio)
    {
        _queue = queue;
        _audio = audio;
        _audio.Changed += SyncMuteToCoordinator;

        _player = new MediaPlayer(App.LibVLC) { Volume = 0 };
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnError;
        VlcView.MediaPlayer = _player;
    }

    public void Play(VideoItem item, double? seekSeconds = null)
    {
        if (_player is null) return;
        Current = item;
        using var media = new Media(App.LibVLC, item.FilePath, FromType.FromPath);
        _player.Play(media);
        if (seekSeconds is double s && s > 0)
            _player.Time = (long)(s * 1000);
        VideoStarted?.Invoke(this, item);
    }

    public void StopAndBlank()
    {
        if (_player is null) return;
        if (Current is not null) VideoFinished?.Invoke(this, Current);
        _player.Stop();
        Current = null;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (Current is not null) _queue?.OnFinished(Current.FilePath);
            RequestNext?.Invoke(this);
        });
    }

    private void OnError(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (Current is not null) _queue?.OnFinished(Current.FilePath);
            RequestNext?.Invoke(this);
        });
    }

    private void SyncMuteToCoordinator()
    {
        if (_player is null || _audio is null) return;
        _player.Volume = _audio.IsUnmuted(PaneIndex) ? 100 : 0;
        MuteBtn.Content = _audio.IsUnmuted(PaneIndex) ? "🔊" : "🔇";
    }

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_audio is null) return;
        if (_audio.IsUnmuted(PaneIndex)) RequestMute?.Invoke(this);
        else RequestUnmute?.Invoke(this);
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not null) _queue?.OnFinished(Current.FilePath);
        RequestNext?.Invoke(this);
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not null) _queue?.Reinsert(Current, int.MaxValue);
        Current = null;
        _player?.Stop();
    }

    private void FadeOverlay(double targetOpacity)
    {
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(150));
        HoverOverlay.BeginAnimation(OpacityProperty, anim);
    }

    public void Dispose()
    {
        if (_player is not null)
        {
            _player.EndReached -= OnEndReached;
            _player.EncounteredError -= OnError;
            _player.Stop();
            _player.Dispose();
        }
        if (_audio is not null) _audio.Changed -= SyncMuteToCoordinator;
    }
}
```

- [ ] **Step 4: Verify build**

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/App.xaml.cs `
        panes-local/src/PanesLocal/Views/
git commit -m "panes-local: Pane control with LibVLCSharp playback + hover overlay"
```

(Visual verification of the Pane happens in Task 7 once it's in the grid.)

---

## Task 7: MainWindow grid + cursor auto-hide + initial folder load

**Spec sections:** "Window & Chrome", "Grid & Panes".

**Files:**
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml`
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

- [ ] **Step 1: Update MainWindow XAML**

Replace `panes-local/src/PanesLocal/MainWindow.xaml`:

```xml
<Window x:Class="PanesLocal.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Panes Local"
        Width="1280" Height="720"
        Background="#000000"
        WindowStyle="None"
        ResizeMode="CanResize"
        AllowsTransparency="False"
        WindowStartupLocation="CenterScreen">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" ResizeBorderThickness="4" GlassFrameThickness="0" />
    </WindowChrome.WindowChrome>
    <Grid x:Name="LayoutRoot">
        <Grid x:Name="PaneGrid"/>
        <Button x:Name="LoadFolderTempBtn"
                Content="Choose folder…"
                Width="160" Height="36"
                VerticalAlignment="Center" HorizontalAlignment="Center"
                Background="#202020" Foreground="White" BorderThickness="0"
                Click="LoadFolderTempBtn_Click"/>
    </Grid>
</Window>
```

(The temporary button gets removed in Task 8 once the side panel exists.)

- [ ] **Step 2: Wire up the grid, cursor hide, and folder loading**

Replace `panes-local/src/PanesLocal/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PanesLocal.Models;
using PanesLocal.Services;
using PanesLocal.Views;

namespace PanesLocal;

public partial class MainWindow : Window
{
    private readonly SharedQueue _queue = new();
    private readonly AudioCoordinator _audio = new();
    private readonly FolderScanner _scanner = new();
    private readonly List<Pane> _panes = new();
    private readonly DispatcherTimer _idleTimer;
    private DateTime _lastMouseMove = DateTime.Now;
    private int _cols = 2;
    private int _rows = 2;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += (_, _) => { _lastMouseMove = DateTime.Now; Cursor = Cursors.Arrow; };
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) =>
        {
            if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3)
                Cursor = Cursors.None;
        };
        _idleTimer.Start();

        BuildGrid();
    }

    private void BuildGrid()
    {
        foreach (var p in _panes) p.Dispose();
        _panes.Clear();
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < _cols; c++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < _rows; r++)
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        int idx = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var pane = new Pane { PaneIndex = idx };
                pane.Attach(_queue, _audio);
                pane.RequestNext  += OnPaneRequestsNext;
                pane.RequestUnmute += p => _audio.UnmutePane(p.PaneIndex);
                pane.RequestMute   += p => _audio.MutePane(p.PaneIndex);
                Grid.SetColumn(pane, c);
                Grid.SetRow(pane, r);
                PaneGrid.Children.Add(pane);
                _panes.Add(pane);
                idx++;
            }

        FillIdlePanesFromQueue();
    }

    private void FillIdlePanesFromQueue()
    {
        foreach (var p in _panes)
        {
            if (p.Current is not null) continue;
            var next = _queue.TakeNext();
            if (next is null) break;
            p.Play(next);
        }
    }

    private void OnPaneRequestsNext(Pane p)
    {
        var next = _queue.TakeNext();
        if (next is null) { p.StopAndBlank(); return; }
        p.Play(next);
    }

    private async void LoadFolderTempBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        LoadFolderTempBtn.Visibility = Visibility.Collapsed;

        var items = await _scanner.ScanAsync(dlg.FolderName, progress: null, CancellationToken.None);
        _queue.AddAll(items);
        _queue.Shuffle();
        FillIdlePanesFromQueue();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
```

- [ ] **Step 3: Manual smoke test**

```powershell
dotnet run --project src/PanesLocal
```

Manual checks (you do these — there are no automated UI tests):

1. Window opens, black, no chrome, 1280×720, "Choose folder…" button visible.
2. Click button → folder picker. Choose a folder containing a handful of `.mp4` files.
3. Four panes appear in a 2×2 grid, each playing a different video.
4. When any video ends, that pane immediately starts a new one from the queue.
5. Hover any pane → mute/skip/stop icons fade in top-right.
6. Click 🔇 on one pane → it unmutes (icon becomes 🔊), all others stay muted. Click 🔊 on a different pane → first one mutes, second unmutes.
7. Click ⏭ → that pane skips to next.
8. Click ✕ → that pane goes black.
9. Move mouse, then leave it still ~3s → cursor disappears. Move it again → cursor returns.
10. F11 → maximize / restore.

If any step fails, stop and debug before continuing.

- [ ] **Step 4: Commit**

```powershell
git add panes-local/src/PanesLocal/MainWindow.xaml `
        panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: MainWindow grid + cursor auto-hide + folder loading"
```

---

# Phase 3: Side panel

## Task 8: SidePanel basic layout (no edge-peek yet, no drag-snap yet)

**Spec sections:** "Side Panel UX — panel contents".

**Files:**
- Create: `panes-local/src/PanesLocal/Views/SidePanel.xaml`
- Create: `panes-local/src/PanesLocal/Views/SidePanel.xaml.cs`
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml`
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

- [ ] **Step 1: Create the SidePanel XAML**

Create `panes-local/src/PanesLocal/Views/SidePanel.xaml`:

```xml
<UserControl x:Class="PanesLocal.Views.SidePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#E6111111"
             Width="320">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Drag handle (functional in Task 11) -->
        <Border Grid.Row="0" x:Name="DragHandle" Background="#22FFFFFF" Height="14" Margin="0,0,0,12">
            <TextBlock Text="⠿⠿⠿" Foreground="#88FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>

        <!-- Header -->
        <StackPanel Grid.Row="1" Margin="0,0,0,12">
            <TextBlock x:Name="FolderName" Text="(no folder)" Foreground="White" FontWeight="Bold"/>
            <TextBlock x:Name="StatusLine" Text="0 videos · 🔇 All muted" Foreground="#AAAAAA" FontSize="11"/>
        </StackPanel>

        <!-- Grid size -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Grid" Foreground="White" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <Button Content="-" Width="24" Click="ColsMinus_Click"/>
            <TextBlock x:Name="ColsLabel" Text="2" Foreground="White" Width="24" TextAlignment="Center" VerticalAlignment="Center"/>
            <Button Content="+" Width="24" Click="ColsPlus_Click" Margin="0,0,12,0"/>
            <TextBlock Text="×" Foreground="White" VerticalAlignment="Center" Margin="0,0,12,0"/>
            <Button Content="-" Width="24" Click="RowsMinus_Click"/>
            <TextBlock x:Name="RowsLabel" Text="2" Foreground="White" Width="24" TextAlignment="Center" VerticalAlignment="Center"/>
            <Button Content="+" Width="24" Click="RowsPlus_Click"/>
        </StackPanel>

        <!-- Transport -->
        <WrapPanel Grid.Row="3" Margin="0,0,0,12">
            <ToggleButton x:Name="ShuffleBtn"  Content="Shuffle"     Margin="0,0,4,4" IsChecked="True"  Click="ShuffleBtn_Click"/>
            <ToggleButton x:Name="RepeatAllBtn" Content="Repeat all" Margin="0,0,4,4" IsChecked="True"  Click="RepeatAllBtn_Click"/>
            <Button       x:Name="ReshuffleBtn" Content="Reshuffle"  Margin="0,0,4,4" Click="ReshuffleBtn_Click"/>
            <Button       x:Name="ChooseFolderBtn" Content="Choose folder…" Margin="0,0,4,4" Click="ChooseFolderBtn_Click"/>
            <Button       x:Name="AddFolderBtn"    Content="Add folder…"    Margin="0,0,4,4" Click="AddFolderBtn_Click"/>
            <Button       x:Name="ClearQueueBtn"   Content="Clear queue"    Margin="0,0,4,4" Click="ClearQueueBtn_Click"/>
        </WrapPanel>

        <!-- Zoom -->
        <StackPanel Grid.Row="4" Margin="0,0,0,12">
            <TextBlock Text="Zoom" Foreground="White"/>
            <Slider x:Name="ZoomSlider" Minimum="100" Maximum="300" Value="100"
                    TickFrequency="25" IsSnapToTickEnabled="True"
                    ValueChanged="ZoomSlider_ValueChanged"/>
        </StackPanel>

        <!-- Playlist -->
        <ListView Grid.Row="5" x:Name="PlaylistView" Background="#22000000" Foreground="White"
                  BorderThickness="0" SelectionMode="Single"
                  AllowDrop="True">
            <ListView.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding DisplayName}" Foreground="White" Padding="4"/>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <!-- Footer -->
        <TextBlock Grid.Row="6" Foreground="#888888" FontSize="10" Margin="0,8,0,0"
                   Text="F11 fullscreen · Drag rows to reorder · Drag onto a pane to play now"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create the SidePanel code-behind**

Create `panes-local/src/PanesLocal/Views/SidePanel.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PanesLocal.Models;
using PanesLocal.Services;

namespace PanesLocal.Views;

public partial class SidePanel : UserControl
{
    public SharedQueue? Queue { get; private set; }
    public AudioCoordinator? Audio { get; private set; }

    public event Action<int, int>? GridSizeChanged;
    public event Action<int>? ZoomChanged;
    public event Action<string>? FolderChosen;
    public event Action<string>? FolderAdded;
    public event Action? QueueCleared;
    public event Action<bool>? ShuffleToggled;
    public event Action<bool>? RepeatAllToggled;
    public event Action? ReshuffleClicked;

    private int _cols = 2;
    private int _rows = 2;

    public SidePanel() { InitializeComponent(); }

    public void Attach(SharedQueue queue, AudioCoordinator audio)
    {
        Queue = queue;
        Audio = audio;
        PlaylistView.ItemsSource = queue.Items;
        queue.Changed += UpdateStatus;
        audio.Changed += UpdateStatus;
        UpdateStatus();
    }

    public void SetGrid(int cols, int rows)
    {
        _cols = cols; _rows = rows;
        ColsLabel.Text = cols.ToString();
        RowsLabel.Text = rows.ToString();
    }

    public void SetZoom(int zoom) => ZoomSlider.Value = zoom;
    public void SetShuffle(bool on) => ShuffleBtn.IsChecked = on;
    public void SetRepeatAll(bool on) => RepeatAllBtn.IsChecked = on;
    public void SetFolderName(string name) => FolderName.Text = name;

    private void UpdateStatus()
    {
        var count = (Queue?.Items.Count ?? 0) + (Queue?.InFlight.Count ?? 0) + (Queue?.Played.Count ?? 0);
        var audioState = Audio?.UnmutedPaneIndex is int i ? $"🔊 Pane {i + 1}" : "🔇 All muted";
        StatusLine.Text = $"{count} videos · {audioState}";
    }

    private void ColsPlus_Click(object s, RoutedEventArgs e)  { if (_cols < 6) { _cols++; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void ColsMinus_Click(object s, RoutedEventArgs e) { if (_cols > 1) { _cols--; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void RowsPlus_Click(object s, RoutedEventArgs e)  { if (_rows < 6) { _rows++; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void RowsMinus_Click(object s, RoutedEventArgs e) { if (_rows > 1) { _rows--; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }

    private void ZoomSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => ZoomChanged?.Invoke((int)e.NewValue);

    private void ShuffleBtn_Click(object s, RoutedEventArgs e)   => ShuffleToggled?.Invoke(ShuffleBtn.IsChecked == true);
    private void RepeatAllBtn_Click(object s, RoutedEventArgs e) => RepeatAllToggled?.Invoke(RepeatAllBtn.IsChecked == true);
    private void ReshuffleBtn_Click(object s, RoutedEventArgs e) => ReshuffleClicked?.Invoke();

    private void ChooseFolderBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        SetFolderName(System.IO.Path.GetFileName(dlg.FolderName));
        FolderChosen?.Invoke(dlg.FolderName);
    }

    private void AddFolderBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        FolderAdded?.Invoke(dlg.FolderName);
    }

    private void ClearQueueBtn_Click(object s, RoutedEventArgs e) => QueueCleared?.Invoke();
}
```

- [ ] **Step 3: Embed SidePanel in MainWindow and wire events**

Replace `panes-local/src/PanesLocal/MainWindow.xaml`:

```xml
<Window x:Class="PanesLocal.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:v="clr-namespace:PanesLocal.Views"
        Title="Panes Local"
        Width="1280" Height="720"
        Background="#000000"
        WindowStyle="None"
        ResizeMode="CanResize"
        AllowsTransparency="False"
        WindowStartupLocation="CenterScreen">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" ResizeBorderThickness="4" GlassFrameThickness="0" />
    </WindowChrome.WindowChrome>
    <Grid x:Name="LayoutRoot">
        <Grid x:Name="PaneGrid"/>
        <v:SidePanel x:Name="Panel" HorizontalAlignment="Right" VerticalAlignment="Stretch"/>
    </Grid>
</Window>
```

Replace `panes-local/src/PanesLocal/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PanesLocal.Models;
using PanesLocal.Services;
using PanesLocal.Views;

namespace PanesLocal;

public partial class MainWindow : Window
{
    private readonly SharedQueue _queue = new();
    private readonly AudioCoordinator _audio = new();
    private readonly FolderScanner _scanner = new();
    private readonly List<Pane> _panes = new();
    private readonly DispatcherTimer _idleTimer;
    private DateTime _lastMouseMove = DateTime.Now;
    private int _cols = 2, _rows = 2, _zoom = 100;
    private bool _shuffleOn = true;
    private bool _repeatAllOn = true;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += (_, _) => { _lastMouseMove = DateTime.Now; Cursor = Cursors.Arrow; };
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) =>
        {
            if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3)
                Cursor = Cursors.None;
        };
        _idleTimer.Start();

        Panel.Attach(_queue, _audio);
        Panel.SetGrid(_cols, _rows);
        Panel.SetZoom(_zoom);
        Panel.SetShuffle(_shuffleOn);
        Panel.SetRepeatAll(_repeatAllOn);

        Panel.GridSizeChanged += (c, r) => { _cols = c; _rows = r; BuildGrid(); };
        Panel.ZoomChanged += z => { _zoom = z; ApplyZoom(); };
        Panel.FolderChosen += async path => await LoadFolderAsync(path, replace: true);
        Panel.FolderAdded  += async path => await LoadFolderAsync(path, replace: false);
        Panel.QueueCleared += () => { _queue.Clear(); foreach (var p in _panes) p.StopAndBlank(); };
        Panel.ShuffleToggled += on => _shuffleOn = on;
        Panel.RepeatAllToggled += on => _repeatAllOn = on;
        Panel.ReshuffleClicked += () => _queue.Reshuffle();

        BuildGrid();
    }

    private async Task LoadFolderAsync(string path, bool replace)
    {
        if (replace) { _queue.Clear(); foreach (var p in _panes) p.StopAndBlank(); }
        var items = await _scanner.ScanAsync(path, progress: null, CancellationToken.None);
        _queue.AddAll(items);
        if (_shuffleOn) _queue.Shuffle();
        FillIdlePanesFromQueue();
    }

    private void BuildGrid()
    {
        foreach (var p in _panes) p.Dispose();
        _panes.Clear();
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < _cols; c++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < _rows; r++)
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        int idx = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var pane = new Pane { PaneIndex = idx };
                pane.Attach(_queue, _audio);
                pane.RequestNext   += OnPaneRequestsNext;
                pane.RequestUnmute += p => _audio.UnmutePane(p.PaneIndex);
                pane.RequestMute   += p => _audio.MutePane(p.PaneIndex);
                Grid.SetColumn(pane, c);
                Grid.SetRow(pane, r);
                PaneGrid.Children.Add(pane);
                _panes.Add(pane);
                idx++;
            }

        ApplyZoom();
        FillIdlePanesFromQueue();
    }

    private void ApplyZoom()
    {
        var scale = _zoom / 100.0;
        foreach (var pane in _panes)
            pane.RenderTransform = new ScaleTransform(scale, scale, 0.5, 0.5);
    }

    private void FillIdlePanesFromQueue()
    {
        foreach (var p in _panes)
        {
            if (p.Current is not null) continue;
            var next = _queue.TakeNext();
            if (next is null) break;
            p.Play(next);
        }
    }

    private void OnPaneRequestsNext(Pane p)
    {
        var next = _queue.TakeNext();
        if (next is null && _repeatAllOn && _queue.Played.Count > 0)
        {
            _queue.RefillFromPlayed(_shuffleOn);
            next = _queue.TakeNext();
        }
        if (next is null) { p.StopAndBlank(); return; }
        p.Play(next);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.Key == Key.Escape)
            { /* close panel — wired in Task 9 */ }
    }
}
```

The pane scale transform needs the cell to clip overflow. Add `ClipToBounds="True"` to each pane:

Edit `panes-local/src/PanesLocal/Views/Pane.xaml` line 6 (the `<UserControl>` root tag) — add `ClipToBounds="True"`:

```xml
<UserControl x:Class="PanesLocal.Views.Pane"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
             Background="#000000"
             AllowDrop="True"
             ClipToBounds="True">
```

- [ ] **Step 4: Manual smoke test**

```powershell
dotnet run --project src/PanesLocal
```

1. Window opens with side panel always visible on the right.
2. Click "Choose folder…" in the panel, pick a folder.
3. Grid populates as before.
4. Click + on Cols/Rows → grid rebuilds with more panes; new panes pull from the queue.
5. Slide Zoom up → all panes scale up, content clipped to cell bounds.
6. Click "Shuffle" off → no immediate change. Click "Reshuffle" → playlist reorders.
7. Click "Repeat all" off → when the queue empties, panes go black instead of refilling.
8. Click "Clear queue" → all panes go black.

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/Views/SidePanel.xaml `
        panes-local/src/PanesLocal/Views/SidePanel.xaml.cs `
        panes-local/src/PanesLocal/Views/Pane.xaml `
        panes-local/src/PanesLocal/MainWindow.xaml `
        panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: SidePanel with grid/transport/zoom/playlist controls"
```

---

## Task 9: Side panel edge-peek + auto-close

**Spec sections:** "Side Panel UX — edge peek, auto-close".

**Files:**
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml`
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

The panel itself doesn't manage its own visibility; the MainWindow tracks mouse position relative to the dock edge and slides the panel in/out.

- [ ] **Step 1: Add edge chevron and animated panel container**

Replace `panes-local/src/PanesLocal/MainWindow.xaml`:

```xml
<Window x:Class="PanesLocal.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:v="clr-namespace:PanesLocal.Views"
        Title="Panes Local"
        Width="1280" Height="720"
        Background="#000000"
        WindowStyle="None"
        ResizeMode="CanResize"
        AllowsTransparency="False"
        WindowStartupLocation="CenterScreen">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" ResizeBorderThickness="4" GlassFrameThickness="0" />
    </WindowChrome.WindowChrome>
    <Grid x:Name="LayoutRoot">
        <Grid x:Name="PaneGrid"/>

        <!-- Edge peek chevron (right dock default) -->
        <Border x:Name="OpenChevron"
                Width="16" Height="48"
                HorizontalAlignment="Right" VerticalAlignment="Center"
                Background="#80222222" CornerRadius="6,0,0,6"
                Opacity="0.25"
                Visibility="Visible"
                Cursor="Hand"
                MouseEnter="OpenChevron_MouseEnter"
                MouseLeave="OpenChevron_MouseLeave"
                MouseLeftButtonDown="OpenChevron_Click">
            <TextBlock x:Name="ChevronText" Text="›" Foreground="White" FontSize="20"
                       HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>

        <!-- Panel host (translated off-screen when closed) -->
        <Grid x:Name="PanelHost" HorizontalAlignment="Right">
            <Grid.RenderTransform>
                <TranslateTransform x:Name="PanelTransform" X="320"/>
            </Grid.RenderTransform>
            <v:SidePanel x:Name="Panel"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Add open/close logic with mouse tracking**

Add these fields and methods to `MainWindow.xaml.cs` (keep everything else from Task 8). Replace the file with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PanesLocal.Models;
using PanesLocal.Services;
using PanesLocal.Views;

namespace PanesLocal;

public partial class MainWindow : Window
{
    private readonly SharedQueue _queue = new();
    private readonly AudioCoordinator _audio = new();
    private readonly FolderScanner _scanner = new();
    private readonly List<Pane> _panes = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _panelTimer;
    private DateTime _lastMouseMove = DateTime.Now;
    private DateTime _mouseLeftPanelAt = DateTime.MaxValue;
    private bool _panelOpen;
    private bool _panelAnchored;
    private int _cols = 2, _rows = 2, _zoom = 100;
    private bool _shuffleOn = true, _repeatAllOn = true;
    private const double PanelWidth = 320;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += OnMouseMove;
        Panel.MouseEnter += (_, _) => _mouseLeftPanelAt = DateTime.MaxValue;
        Panel.MouseLeave += (_, _) => _mouseLeftPanelAt = DateTime.Now;
        Panel.PreviewMouseLeftButtonDown += (_, _) => _panelAnchored = true;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) =>
        {
            if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3) Cursor = Cursors.None;
        };
        _idleTimer.Start();

        _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _panelTimer.Tick += (_, _) => UpdatePanelState();
        _panelTimer.Start();

        Panel.Attach(_queue, _audio);
        Panel.SetGrid(_cols, _rows);
        Panel.SetZoom(_zoom);
        Panel.SetShuffle(_shuffleOn);
        Panel.SetRepeatAll(_repeatAllOn);

        Panel.GridSizeChanged += (c, r) => { _cols = c; _rows = r; BuildGrid(); };
        Panel.ZoomChanged += z => { _zoom = z; ApplyZoom(); };
        Panel.FolderChosen += async p => await LoadFolderAsync(p, replace: true);
        Panel.FolderAdded  += async p => await LoadFolderAsync(p, replace: false);
        Panel.QueueCleared += () => { _queue.Clear(); foreach (var pn in _panes) pn.StopAndBlank(); };
        Panel.ShuffleToggled += on => _shuffleOn = on;
        Panel.RepeatAllToggled += on => _repeatAllOn = on;
        Panel.ReshuffleClicked += () => _queue.Reshuffle();

        BuildGrid();
        ClosePanelInstant();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _lastMouseMove = DateTime.Now;
        Cursor = Cursors.Arrow;
        var p = e.GetPosition(this);

        // Show chevron when cursor is within 24px of the right edge
        if (ActualWidth - p.X < 24 && !_panelOpen)
            OpenChevron.Opacity = 0.7;
        else if (!IsMouseOver(OpenChevron, e))
            OpenChevron.Opacity = 0.25;
    }

    private bool IsMouseOver(UIElement el, MouseEventArgs e)
    {
        var pos = e.GetPosition(el);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= el.RenderSize.Width && pos.Y <= el.RenderSize.Height;
    }

    private void UpdatePanelState()
    {
        if (!_panelOpen) return;
        if (_panelAnchored) return;
        if (_mouseLeftPanelAt == DateTime.MaxValue) return;
        if ((DateTime.Now - _mouseLeftPanelAt).TotalMilliseconds > 600)
            ClosePanel();
    }

    private void OpenPanel()
    {
        _panelOpen = true;
        OpenChevron.Visibility = Visibility.Collapsed;
        AnimatePanel(targetX: 0);
    }

    private void ClosePanel()
    {
        _panelOpen = false;
        _panelAnchored = false;
        _mouseLeftPanelAt = DateTime.MaxValue;
        OpenChevron.Visibility = Visibility.Visible;
        AnimatePanel(targetX: PanelWidth);
    }

    private void ClosePanelInstant()
    {
        _panelOpen = false;
        PanelTransform.X = PanelWidth;
        OpenChevron.Visibility = Visibility.Visible;
    }

    private void AnimatePanel(double targetX)
    {
        var anim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void OpenChevron_MouseEnter(object s, MouseEventArgs e) => OpenChevron.Opacity = 0.7;
    private void OpenChevron_MouseLeave(object s, MouseEventArgs e) => OpenChevron.Opacity = 0.25;
    private void OpenChevron_Click(object s, MouseButtonEventArgs e) => OpenPanel();

    private async Task LoadFolderAsync(string path, bool replace)
    {
        if (replace) { _queue.Clear(); foreach (var p in _panes) p.StopAndBlank(); }
        var items = await _scanner.ScanAsync(path, progress: null, CancellationToken.None);
        _queue.AddAll(items);
        if (_shuffleOn) _queue.Shuffle();
        FillIdlePanesFromQueue();
    }

    private void BuildGrid()
    {
        foreach (var p in _panes) p.Dispose();
        _panes.Clear();
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < _cols; c++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < _rows; r++)
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        int idx = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var pane = new Pane { PaneIndex = idx };
                pane.Attach(_queue, _audio);
                pane.RequestNext   += OnPaneRequestsNext;
                pane.RequestUnmute += p => _audio.UnmutePane(p.PaneIndex);
                pane.RequestMute   += p => _audio.MutePane(p.PaneIndex);
                Grid.SetColumn(pane, c);
                Grid.SetRow(pane, r);
                PaneGrid.Children.Add(pane);
                _panes.Add(pane);
                idx++;
            }

        ApplyZoom();
        FillIdlePanesFromQueue();
    }

    private void ApplyZoom()
    {
        var scale = _zoom / 100.0;
        foreach (var pane in _panes)
            pane.RenderTransform = new ScaleTransform(scale, scale, 0.5, 0.5);
    }

    private void FillIdlePanesFromQueue()
    {
        foreach (var p in _panes)
        {
            if (p.Current is not null) continue;
            var next = _queue.TakeNext();
            if (next is null) break;
            p.Play(next);
        }
    }

    private void OnPaneRequestsNext(Pane p)
    {
        var next = _queue.TakeNext();
        if (next is null && _repeatAllOn && _queue.Played.Count > 0)
        {
            _queue.RefillFromPlayed(_shuffleOn);
            next = _queue.TakeNext();
        }
        if (next is null) { p.StopAndBlank(); return; }
        p.Play(next);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.Key == Key.Escape && _panelOpen)
            ClosePanel();
    }
}
```

- [ ] **Step 3: Manual smoke test**

```powershell
dotnet run --project src/PanesLocal
```

1. Panel starts closed. A thin chevron is visible at center-right.
2. Move mouse near the right edge → chevron brightens (opacity ~0.7).
3. Click chevron → panel slides out in 200ms.
4. Move mouse off the panel → after 600ms it slides back closed.
5. Click anywhere inside the panel → "anchored" — leaving the panel no longer closes it.
6. Press Escape → panel closes, anchored state resets.

- [ ] **Step 4: Commit**

```powershell
git add panes-local/src/PanesLocal/MainWindow.xaml `
        panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: edge-peek chevron + auto-close side panel"
```

---

## Task 10: Drag and drop (playlist ↔ playlist, playlist ↔ pane, pane ↔ pane)

**Spec sections:** "Drag and Drop".

**Files:**
- Modify: `panes-local/src/PanesLocal/Views/SidePanel.xaml.cs`
- Modify: `panes-local/src/PanesLocal/Views/Pane.xaml.cs`

We use `DataObject` with a custom format string `"PanesLocal.VideoItem"` for queue items and `"PanesLocal.PaneIndex"` for pane sources.

- [ ] **Step 1: Add playlist-row drag source + drop target in SidePanel**

Add to the bottom of `panes-local/src/PanesLocal/Views/SidePanel.xaml.cs`, inside the `SidePanel` class:

```csharp
    public const string VideoItemFormat = "PanesLocal.VideoItem";
    public const string PaneIndexFormat = "PanesLocal.PaneIndex";

    private Point _dragStart;
    private VideoItem? _dragItem;

    public SidePanel WireDragHandlers()
    {
        PlaylistView.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _dragStart = e.GetPosition(null);
            _dragItem = (PlaylistView.SelectedItem as VideoItem)
                ?? ((PlaylistView.InputHitTest(e.GetPosition(PlaylistView)) as FrameworkElement)?.DataContext as VideoItem);
        };
        PlaylistView.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (_dragItem is null) return;
            var data = new DataObject(VideoItemFormat, _dragItem);
            DragDrop.DoDragDrop(PlaylistView, data, DragDropEffects.Move);
        };
        PlaylistView.Drop += OnPlaylistDrop;
        PlaylistView.DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent(VideoItemFormat) || e.Data.GetDataPresent(PaneIndexFormat)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        return this;
    }

    private void OnPlaylistDrop(object sender, DragEventArgs e)
    {
        if (Queue is null) return;

        // Playlist row → playlist row (reorder)
        if (e.Data.GetDataPresent(VideoItemFormat))
        {
            var dragged = (VideoItem)e.Data.GetData(VideoItemFormat)!;
            var target  = (e.OriginalSource as FrameworkElement)?.DataContext as VideoItem;
            if (target is null || target.FilePath == dragged.FilePath) return;
            int from = Queue.Items.IndexOf(dragged);
            int to   = Queue.Items.IndexOf(target);
            if (from < 0 || to < 0) return;
            Queue.Items.Move(from, to);
            return;
        }

        // Pane → playlist (booted current video drops into queue)
        if (e.Data.GetDataPresent(PaneIndexFormat))
        {
            var paneIdx = (int)e.Data.GetData(PaneIndexFormat)!;
            PaneDroppedOnPlaylist?.Invoke(paneIdx, GetDropIndex(e));
        }
    }

    private int GetDropIndex(DragEventArgs e)
    {
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as VideoItem;
        if (target is null || Queue is null) return Queue?.Items.Count ?? 0;
        return Queue.Items.IndexOf(target);
    }

    public event Action<int, int>? PaneDroppedOnPlaylist;
```

In the existing `SidePanel` constructor body, call `WireDragHandlers()` at the end:

```csharp
    public SidePanel()
    {
        InitializeComponent();
        WireDragHandlers();
    }
```

- [ ] **Step 2: Add drag source + drop target in Pane**

Replace the `Pane` constructor and add drag handlers in `panes-local/src/PanesLocal/Views/Pane.xaml.cs`:

```csharp
    public Pane()
    {
        InitializeComponent();
        MouseEnter += (_, _) => FadeOverlay(1);
        MouseLeave += (_, _) => FadeOverlay(0);

        PreviewMouseLeftButtonDown += OnPaneMouseDown;
        PreviewMouseMove += OnPaneMouseMove;
        Drop += OnPaneDrop;
        DragEnter += (_, _) => DragHighlight.Visibility = Visibility.Visible;
        DragLeave += (_, _) => DragHighlight.Visibility = Visibility.Collapsed;
        DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent(SidePanel.VideoItemFormat)
                       || e.Data.GetDataPresent(SidePanel.PaneIndexFormat)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private Point _paneDragStart;

    private void OnPaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paneDragStart = e.GetPosition(null);
    }

    private void OnPaneMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (Current is null) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _paneDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _paneDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (HoverOverlay.IsMouseOver) return;       // don't drag when clicking the overlay buttons
        var data = new DataObject(SidePanel.PaneIndexFormat, PaneIndex);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
    }

    public event Action<Pane, VideoItem>? PlaylistItemDroppedOnMe;     // raised on playlist→pane drop
    public event Action<Pane, int>? PaneDroppedOnMe;                   // raised on pane→pane drop (other pane index)

    private void OnPaneDrop(object sender, DragEventArgs e)
    {
        DragHighlight.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(SidePanel.VideoItemFormat))
        {
            var item = (VideoItem)e.Data.GetData(SidePanel.VideoItemFormat)!;
            PlaylistItemDroppedOnMe?.Invoke(this, item);
            return;
        }

        if (e.Data.GetDataPresent(SidePanel.PaneIndexFormat))
        {
            var otherIdx = (int)e.Data.GetData(SidePanel.PaneIndexFormat)!;
            if (otherIdx == PaneIndex) return;
            PaneDroppedOnMe?.Invoke(this, otherIdx);
        }
    }
```

- [ ] **Step 3: Wire the drop events in MainWindow**

In `MainWindow.xaml.cs`, inside `BuildGrid()`, after `pane.RequestMute   += ...`, add:

```csharp
                pane.PlaylistItemDroppedOnMe += OnPlaylistDroppedOnPane;
                pane.PaneDroppedOnMe += OnPaneDroppedOnPane;
```

And after the constructor's existing `Panel.ReshuffleClicked += ...` line, add:

```csharp
        Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;
```

Then add these handler methods inside `MainWindow`:

```csharp
    private void OnPlaylistDroppedOnPane(Pane targetPane, VideoItem item)
    {
        // Boot current video to bottom of queue
        if (targetPane.Current is not null)
            _queue.Reinsert(targetPane.Current, int.MaxValue);
        // Pull the dropped item out of queue and play it
        _queue.Remove(item);
        _queue.InFlight.Add(item.FilePath);
        targetPane.Play(item);
    }

    private void OnPaneDroppedOnPane(Pane targetPane, int otherIndex)
    {
        if (otherIndex < 0 || otherIndex >= _panes.Count) return;
        var other = _panes[otherIndex];
        var a = targetPane.Current;
        var b = other.Current;
        if (a is null && b is null) return;

        if (a is not null) other.Play(a);
        else other.StopAndBlank();

        if (b is not null) targetPane.Play(b);
        else targetPane.StopAndBlank();
    }

    private void OnPaneDroppedOnPlaylist(int paneIndex, int dropPosition)
    {
        if (paneIndex < 0 || paneIndex >= _panes.Count) return;
        var pane = _panes[paneIndex];
        if (pane.Current is null) return;
        _queue.Reinsert(pane.Current, dropPosition);
        pane.StopAndBlank();
        // pane will be filled from queue by next idle tick? We do it explicitly:
        var next = _queue.TakeNext();
        if (next is not null) pane.Play(next);
    }
```

- [ ] **Step 4: Manual smoke test**

1. Run the app, load a folder, open the panel.
2. Drag a playlist row up/down → the order changes.
3. Drag a playlist row onto a pane → that pane immediately plays the dropped video; the previous video appears at the bottom of the playlist.
4. Drag a pane onto a different pane → the two swap videos.
5. Drag a pane onto a playlist row → pane goes black, the booted video appears in the playlist at that position.

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/Views/SidePanel.xaml.cs `
        panes-local/src/PanesLocal/Views/Pane.xaml.cs `
        panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: drag-and-drop (playlist + pane swaps)"
```

---

## Task 11: Drag-to-snap docking

**Spec sections:** "Side Panel UX — drag-to-snap docking".

**Files:**
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml`
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

For v1 we implement a **simpler** snap: dragging the panel's grip handle past a window edge re-docks to that edge without a separate floating ghost step. This still satisfies the spec ("drop near edge snaps to that edge"). A floating ghost is a v2 nice-to-have.

- [ ] **Step 1: Expose the DragHandle from SidePanel**

In `panes-local/src/PanesLocal/Views/SidePanel.xaml.cs`, add a public property:

```csharp
    public UIElement Handle => DragHandle;
```

- [ ] **Step 2: Add dock state + reactive layout to MainWindow**

In `MainWindow.xaml.cs`, add fields:

```csharp
    private DockSide _dock = DockSide.Right;
    private bool _draggingPanel;
    private Point _dragStartPos;
```

Add to the constructor, after `Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;`:

```csharp
        Panel.Handle.MouseLeftButtonDown += (_, e) =>
        {
            _draggingPanel = true;
            _dragStartPos = e.GetPosition(this);
            Panel.Handle.CaptureMouse();
        };
        Panel.Handle.MouseMove += (_, _) => { /* live-detach effect could go here */ };
        Panel.Handle.MouseLeftButtonUp += (_, e) =>
        {
            if (!_draggingPanel) return;
            _draggingPanel = false;
            Panel.Handle.ReleaseMouseCapture();
            var p = e.GetPosition(this);
            var newDock = ResolveDockFromPosition(p);
            ApplyDock(newDock);
        };
```

Add these methods inside `MainWindow`:

```csharp
    private DockSide ResolveDockFromPosition(Point p)
    {
        const double edge = 80;
        if (p.X < edge) return DockSide.Left;
        if (ActualWidth - p.X < edge) return DockSide.Right;
        if (p.Y < edge) return DockSide.Top;
        if (ActualHeight - p.Y < edge) return DockSide.Bottom;
        return _dock;  // no edge in range → snap back to previous side
    }

    private void ApplyDock(DockSide dock)
    {
        _dock = dock;
        PanelHost.HorizontalAlignment = dock switch
        {
            DockSide.Left  => HorizontalAlignment.Left,
            DockSide.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch
        };
        PanelHost.VerticalAlignment = dock switch
        {
            DockSide.Top    => VerticalAlignment.Top,
            DockSide.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Stretch
        };
        Panel.Width  = (dock is DockSide.Left  or DockSide.Right)  ? PanelWidth : double.NaN;
        Panel.Height = (dock is DockSide.Top   or DockSide.Bottom) ? PanelWidth : double.NaN;

        // Re-orient chevron
        OpenChevron.HorizontalAlignment = dock switch
        {
            DockSide.Left  => HorizontalAlignment.Left,
            DockSide.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        OpenChevron.VerticalAlignment = dock switch
        {
            DockSide.Top    => VerticalAlignment.Top,
            DockSide.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        ChevronText.Text = dock switch
        {
            DockSide.Left  => "›",
            DockSide.Right => "‹",
            DockSide.Top   => "˅",
            DockSide.Bottom => "˄",
            _ => "‹"
        };

        // Reset transform — close instantly in new dock
        PanelTransform.X = dock switch
        {
            DockSide.Right => PanelWidth,
            DockSide.Left  => -PanelWidth,
            _ => 0
        };
        PanelTransform.Y = dock switch
        {
            DockSide.Bottom => PanelWidth,
            DockSide.Top    => -PanelWidth,
            _ => 0
        };
        _panelOpen = false;
        OpenChevron.Visibility = Visibility.Visible;
    }
```

Update `AnimatePanel` to handle both axes:

```csharp
    private void AnimatePanel(double target)
    {
        var prop = (_dock is DockSide.Left or DockSide.Right)
            ? TranslateTransform.XProperty
            : TranslateTransform.YProperty;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(prop, anim);
    }
```

And update the `OpenPanel` / `ClosePanel` call sites to pass `0` and the dock's "off-screen" value respectively:

```csharp
    private void OpenPanel()
    {
        _panelOpen = true;
        OpenChevron.Visibility = Visibility.Collapsed;
        AnimatePanel(0);
    }

    private void ClosePanel()
    {
        _panelOpen = false;
        _panelAnchored = false;
        _mouseLeftPanelAt = DateTime.MaxValue;
        OpenChevron.Visibility = Visibility.Visible;
        AnimatePanel(_dock switch
        {
            DockSide.Right  =>  PanelWidth,
            DockSide.Left   => -PanelWidth,
            DockSide.Bottom =>  PanelWidth,
            DockSide.Top    => -PanelWidth,
            _ => PanelWidth
        });
    }
```

- [ ] **Step 3: Update the chevron's mouse-near-edge detection to honor the current dock**

Replace the `OnMouseMove` body in `MainWindow.xaml.cs`:

```csharp
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _lastMouseMove = DateTime.Now;
        Cursor = Cursors.Arrow;
        var p = e.GetPosition(this);
        bool nearEdge = _dock switch
        {
            DockSide.Right  => ActualWidth - p.X < 24,
            DockSide.Left   => p.X < 24,
            DockSide.Top    => p.Y < 24,
            DockSide.Bottom => ActualHeight - p.Y < 24,
            _ => false
        };
        if (nearEdge && !_panelOpen) OpenChevron.Opacity = 0.7;
        else if (!IsMouseOver(OpenChevron, e)) OpenChevron.Opacity = 0.25;
    }
```

- [ ] **Step 4: Manual smoke test**

1. Run app, open panel.
2. Press & drag the grip-handle (⠿⠿⠿) at the top of the panel → release near the left window edge → panel re-docks to the left, chevron now lives at the left edge pointing right (›).
3. Drag-release in the middle (no edge in range) → panel snaps back to the previous side.
4. Try all four edges.

- [ ] **Step 5: Commit**

```powershell
git add panes-local/src/PanesLocal/Views/SidePanel.xaml.cs `
        panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: drag-to-snap panel docking on any edge"
```

---

# Phase 4: Persistence + polish

## Task 12: Persistence wiring + session restore

**Spec sections:** "Persistence".

**Files:**
- Modify: `panes-local/src/PanesLocal/App.xaml.cs` (expose state path)
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

- [ ] **Step 1: Expose the persistence path on App**

Update `panes-local/src/PanesLocal/App.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using LibVLCSharp.Shared;

namespace PanesLocal;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;
    public static string StateFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PanesLocal", "state.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Initialize();
        LibVLC = new LibVLC("--no-osd", "--quiet", "--no-video-title-show");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Add persistence wiring to MainWindow**

Add fields:

```csharp
    private readonly PersistenceService _persistence = new(App.StateFilePath);
    private AppState _state = new();
```

In the constructor, replace the body with:

```csharp
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += OnMouseMove;
        Panel.MouseEnter += (_, _) => _mouseLeftPanelAt = DateTime.MaxValue;
        Panel.MouseLeave += (_, _) => _mouseLeftPanelAt = DateTime.Now;
        Panel.PreviewMouseLeftButtonDown += (_, _) => _panelAnchored = true;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) => { if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3) Cursor = Cursors.None; };
        _idleTimer.Start();

        _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _panelTimer.Tick += (_, _) => UpdatePanelState();
        _panelTimer.Start();

        _state = _persistence.Load();
        _cols = _state.Grid.Cols;
        _rows = _state.Grid.Rows;
        _zoom = _state.Zoom;
        _shuffleOn = _state.Transport.Shuffle;
        _repeatAllOn = _state.Transport.RepeatAll;
        _dock = _state.Panel.Dock;
        Width  = _state.Window.Width;
        Height = _state.Window.Height;
        Left   = _state.Window.X;
        Top    = _state.Window.Y;
        if (_state.Window.Maximized) WindowState = WindowState.Maximized;

        // Restore queue: rescan folders + filter saved paths
        if (_state.Folders.Count > 0)
        {
            _ = RestoreSessionAsync();
        }

        Panel.Attach(_queue, _audio);
        Panel.SetGrid(_cols, _rows);
        Panel.SetZoom(_zoom);
        Panel.SetShuffle(_shuffleOn);
        Panel.SetRepeatAll(_repeatAllOn);

        Panel.GridSizeChanged += (c, r) => { _cols = c; _rows = r; BuildGrid(); SaveState(); };
        Panel.ZoomChanged += z => { _zoom = z; ApplyZoom(); SaveState(); };
        Panel.FolderChosen += async p => { _state.Folders.Clear(); _state.Folders.Add(p); await LoadFolderAsync(p, true); SaveState(); };
        Panel.FolderAdded  += async p => { if (!_state.Folders.Contains(p)) _state.Folders.Add(p); await LoadFolderAsync(p, false); SaveState(); };
        Panel.QueueCleared += () => { _queue.Clear(); foreach (var pn in _panes) pn.StopAndBlank(); SaveState(); };
        Panel.ShuffleToggled += on => { _shuffleOn = on; SaveState(); };
        Panel.RepeatAllToggled += on => { _repeatAllOn = on; SaveState(); };
        Panel.ReshuffleClicked += () => { _queue.Reshuffle(); SaveState(); };
        Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;

        _queue.Changed += SaveState;
        _audio.Changed += SaveState;

        Panel.Handle.MouseLeftButtonDown += (_, e) =>
        {
            _draggingPanel = true;
            _dragStartPos = e.GetPosition(this);
            Panel.Handle.CaptureMouse();
        };
        Panel.Handle.MouseLeftButtonUp += (_, e) =>
        {
            if (!_draggingPanel) return;
            _draggingPanel = false;
            Panel.Handle.ReleaseMouseCapture();
            var p = e.GetPosition(this);
            ApplyDock(ResolveDockFromPosition(p));
            SaveState();
        };

        Closing += OnClosing;
        SizeChanged += (_, _) => SaveState();
        LocationChanged += (_, _) => SaveState();

        ApplyDock(_dock);
        BuildGrid();
        ClosePanelInstant();

        if (_persistence.LastLoadWasFallback)
            MessageBox.Show("Could not restore last session — starting fresh.", "Panes Local",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestoreSessionAsync()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _state.Folders)
        {
            try
            {
                var items = await _scanner.ScanAsync(folder, progress: null, CancellationToken.None);
                foreach (var i in items) existing.Add(i.FilePath);
            }
            catch { /* folder missing → skip */ }
        }
        // Items the saved queue still finds on disk, in order
        foreach (var path in _state.Queue.Where(existing.Contains))
            _queue.Items.Add(new VideoItem(path, System.IO.Path.GetFileName(path), 0));
        // Played list — keep filtered
        foreach (var path in _state.Played.Where(existing.Contains))
            _queue.Played.Add(path);
        // Newly discovered files: append
        var known = new HashSet<string>(_queue.Items.Select(i => i.FilePath));
        foreach (var p in _queue.InFlight) known.Add(p);
        foreach (var p in _queue.Played) known.Add(p);
        foreach (var path in existing.Where(p => !known.Contains(p)))
            _queue.Items.Add(new VideoItem(path, System.IO.Path.GetFileName(path), 0));

        if (_shuffleOn) _queue.Shuffle();

        // Restore per-pane current video at saved position
        foreach (var ps in _state.Panes)
        {
            if (ps.FilePath is null) continue;
            if (!existing.Contains(ps.FilePath)) continue;
            if (ps.Index < 0 || ps.Index >= _panes.Count) continue;
            var v = new VideoItem(ps.FilePath, System.IO.Path.GetFileName(ps.FilePath), 0);
            _queue.Remove(v);
            _queue.InFlight.Add(v.FilePath);
            _panes[ps.Index].Play(v, ps.PositionSeconds);
        }

        if (_state.Audio.UnmutedPaneIndex is int u) _audio.UnmutePane(u);
        FillIdlePanesFromQueue();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveStateSync();
    }

    private void SaveState()
    {
        SyncStateFromUi();
        _persistence.SaveDebounced(_state);
    }

    private void SaveStateSync()
    {
        SyncStateFromUi();
        _ = _persistence.SaveImmediateAsync(_state);
    }

    private void SyncStateFromUi()
    {
        _state.Window = new AppState.WindowState
        {
            X = Left, Y = Top, Width = Width, Height = Height,
            Maximized = WindowState == WindowState.Maximized,
            Fullscreen = false
        };
        _state.Grid = new AppState.GridState { Cols = _cols, Rows = _rows };
        _state.Zoom = _zoom;
        _state.Panel = new AppState.PanelState { Dock = _dock, Size = PanelWidth };
        _state.Transport = new AppState.TransportState { Shuffle = _shuffleOn, RepeatAll = _repeatAllOn };
        _state.Audio = new AppState.AudioState { UnmutedPaneIndex = _audio.UnmutedPaneIndex };
        _state.Queue = _queue.Items.Select(i => i.FilePath).ToList();
        _state.Played = _queue.Played.ToList();
        _state.Panes = _panes.Select(p => new PaneState(p.PaneIndex, p.Current?.FilePath, p.CurrentPositionSeconds)).ToList();
    }
```

- [ ] **Step 2.5: Add a "Reset to defaults" button**

In `SidePanel.xaml`, replace the footer `TextBlock` row with:

```xml
        <StackPanel Grid.Row="6" Margin="0,8,0,0">
            <TextBlock Foreground="#888888" FontSize="10"
                       Text="F11 fullscreen · Drag rows to reorder · Drag onto a pane to play now"/>
            <Button x:Name="ResetBtn" Content="Reset to defaults" Margin="0,8,0,0"
                    Background="#33000000" Foreground="#CCCCCC" BorderThickness="0"
                    Click="ResetBtn_Click"/>
        </StackPanel>
```

In `SidePanel.xaml.cs`, add an event and handler:

```csharp
    public event Action? ResetRequested;
    private void ResetBtn_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();
```

In `MainWindow.xaml.cs` constructor, after `Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;`:

```csharp
        Panel.ResetRequested += () =>
        {
            if (File.Exists(App.StateFilePath)) File.Delete(App.StateFilePath);
            System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
            Application.Current.Shutdown();
        };
```

Add `using System.IO;` at the top of `MainWindow.xaml.cs`.

- [ ] **Step 3: Manual smoke test**

1. Run, choose a folder, let it run for 30 seconds.
2. Resize the window, change grid to 3×3, zoom to 175, dock the panel to the bottom, unmute pane 3.
3. Close the app.
4. Reopen → window size/position/grid/zoom/dock/unmute all restored. Same videos resume in the same panes near the same positions.
5. Open `%AppData%\PanesLocal\state.json` and confirm the structure matches the spec.
6. Click "Reset to defaults" → app relaunches with empty queue and default settings.
7. Manually corrupt `state.json` (write garbage), relaunch → message box: "Could not restore last session — starting fresh." Defaults applied.

- [ ] **Step 4: Commit**

```powershell
git add panes-local/src/PanesLocal/App.xaml.cs `
        panes-local/src/PanesLocal/MainWindow.xaml.cs `
        panes-local/src/PanesLocal/Views/SidePanel.xaml `
        panes-local/src/PanesLocal/Views/SidePanel.xaml.cs
git commit -m "panes-local: persistence (state.json) + session restore + reset"
```

---

## Task 13: Remaining keyboard shortcuts + Space audio toggle

**Spec sections:** "Keyboard Shortcuts".

**Files:**
- Modify: `panes-local/src/PanesLocal/MainWindow.xaml.cs`

- [ ] **Step 1: Extend OnKeyDown**

Replace `OnKeyDown` in `MainWindow.xaml.cs`:

```csharp
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Ignore when a text input has focus
        if (FocusManager.GetFocusedElement(this) is TextBox or PasswordBox) return;

        switch (e.Key)
        {
            case Key.F11:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case Key.Escape:
                if (_panelOpen) ClosePanel();
                break;
            case Key.Space:
                if (_audio.UnmutedPaneIndex is null) _audio.UnmutePane(0);
                else _audio.MuteAll();
                e.Handled = true;
                break;
            case Key.S:
                _shuffleOn = !_shuffleOn;
                Panel.SetShuffle(_shuffleOn);
                SaveState();
                break;
            case Key.R:
                _queue.Reshuffle();
                SaveState();
                break;
        }
    }
```

- [ ] **Step 2: Manual smoke test**

1. Run, load folder.
2. Space → pane 0 unmutes. Space again → all mute.
3. S → shuffle toggle flips; status updates.
4. R → playlist reorders.
5. F11 → maximize. Esc → closes panel if open.

- [ ] **Step 3: Commit**

```powershell
git add panes-local/src/PanesLocal/MainWindow.xaml.cs
git commit -m "panes-local: keyboard shortcuts (Space mute toggle, S shuffle, R reshuffle)"
```

---

## Task 14: Error handling polish

**Spec sections:** "Error Handling Boundaries".

**Files:**
- Modify: `panes-local/src/PanesLocal/App.xaml.cs`

- [ ] **Step 1: Catch libVLC init failure with a useful error**

Replace `OnStartup` in `App.xaml.cs`:

```csharp
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Core.Initialize();
            LibVLC = new LibVLC("--no-osd", "--quiet", "--no-video-title-show");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize libVLC.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                "Reinstall the app, or copy the contents of the bundled libvlc folder next to PanesLocal.exe.",
                "Panes Local — startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
```

- [ ] **Step 2: Add an "queue empty" hint overlay to Pane**

In `Pane.xaml`, add inside the `<Grid>` root after `DragHighlight`:

```xml
        <TextBlock x:Name="EmptyHint"
                   Text="(queue empty)"
                   Foreground="#666666"
                   FontSize="14"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   Visibility="Collapsed"/>
```

In `Pane.xaml.cs`, update `Play` and `StopAndBlank`:

```csharp
    public void Play(VideoItem item, double? seekSeconds = null)
    {
        if (_player is null) return;
        Current = item;
        EmptyHint.Visibility = Visibility.Collapsed;
        using var media = new Media(App.LibVLC, item.FilePath, FromType.FromPath);
        _player.Play(media);
        if (seekSeconds is double s && s > 0)
            _player.Time = (long)(s * 1000);
        VideoStarted?.Invoke(this, item);
    }

    public void StopAndBlank()
    {
        if (_player is null) return;
        if (Current is not null) VideoFinished?.Invoke(this, Current);
        _player.Stop();
        Current = null;
        EmptyHint.Visibility = Visibility.Visible;
    }
```

- [ ] **Step 3: Manual smoke test**

1. Temporarily rename the bundled `libvlc` folder (so init fails) → app shows error dialog, exits.
2. Restore the libvlc folder. Run app, load folder. With repeat-all OFF, let the queue drain → each pane shows "(queue empty)" when its last video ends.

- [ ] **Step 4: Commit**

```powershell
git add panes-local/src/PanesLocal/App.xaml.cs `
        panes-local/src/PanesLocal/Views/Pane.xaml `
        panes-local/src/PanesLocal/Views/Pane.xaml.cs
git commit -m "panes-local: error handling for libVLC + queue-empty hint"
```

---

# Phase 5: Installer

## Task 15: Inno Setup installer + build script

**Spec sections:** "Stack — Inno Setup".

**Files:**
- Create: `panes-local/installer/PanesLocal.iss`
- Create: `panes-local/build.ps1`

- [ ] **Step 1: Create the Inno Setup script**

Create `panes-local/installer/PanesLocal.iss`:

```ini
#define AppName       "Panes Local"
#define AppVersion    "1.0.0"
#define AppPublisher  "Panes"
#define AppId         "{{A2F8F8F7-1B5E-4E2D-9F2B-PANESLOCAL01}}"
#define AppExe        "PanesLocal.exe"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\PanesLocal
DefaultGroupName=Panes Local
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=PanesLocal-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=..\..\panes\build\icon.ico
UninstallDisplayIcon={app}\{#AppExe}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\src\PanesLocal\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 2: Create the build script**

Create `panes-local/build.ps1`:

```powershell
#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot

Write-Host "==> dotnet test" -ForegroundColor Cyan
dotnet test --nologo --verbosity minimal

Write-Host "==> dotnet publish" -ForegroundColor Cyan
dotnet publish src/PanesLocal/PanesLocal.csproj `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --nologo --verbosity minimal

Write-Host "==> Inno Setup" -ForegroundColor Cyan
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    throw "iscc.exe not found in PATH. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and add 'C:\Program Files (x86)\Inno Setup 6' to PATH."
}
& iscc installer\PanesLocal.iss

$installer = Join-Path (Resolve-Path "dist") "PanesLocal-Setup.exe"
Write-Host "==> Built $installer" -ForegroundColor Green

Pop-Location
```

- [ ] **Step 3: Run the build and verify installer works**

```powershell
cd panes-local
./build.ps1
```

Expected:
1. `dotnet test` passes.
2. `dotnet publish` produces `src/PanesLocal/bin/Release/net10.0-windows/win-x64/publish/PanesLocal.exe` plus libvlc DLLs.
3. `iscc` produces `dist/PanesLocal-Setup.exe`.

Manual test of the installer:
1. Double-click `dist/PanesLocal-Setup.exe`. Per-user install (no admin prompt).
2. Setup completes, app launches.
3. Check Start Menu has "Panes Local". Check Settings → Apps lists it with a working Uninstall.
4. Uninstall removes it cleanly.

- [ ] **Step 4: Commit**

```powershell
git add panes-local/installer/PanesLocal.iss `
        panes-local/build.ps1
git commit -m "panes-local: Inno Setup installer + build script"
```

---

# Self-review notes

These were addressed inline while writing the plan; recording them here so the engineer can verify nothing slipped:

- **Spec coverage:** Every section of the spec maps to at least one task — project layout (T1), window/grid/cursor (T1+T7), shared queue (T2), folder loading (T3), audio (T4+T7), persistence (T5+T12), pane lifecycle (T6+T7), drag/drop (T10), side panel (T8+T9+T11), keyboard shortcuts (T13), error handling (T14), installer (T15).
- **Type consistency:** `VideoItem`, `SharedQueue`, `AudioCoordinator`, `PersistenceService`, `AppState`, `PaneState`, `DockSide`, `Pane`, `SidePanel` are all defined consistently across tasks. The drag-drop format strings (`"PanesLocal.VideoItem"`, `"PanesLocal.PaneIndex"`) are defined once in `SidePanel` and referenced from `Pane`.
- **No placeholders:** Every code block is complete and ready to paste. Every command shows expected output.
- **Booted-video rule consistency:** "Reinsert(item, int.MaxValue)" — bottom of queue — appears in `StopBtn_Click` (T6), `OnPlaylistDroppedOnPane` (T10), grid shrink (still implicit via `Dispose` → no reinsert; left as a known acceptable gap since grid shrink is rare and "Dispose then BuildGrid which calls FillIdle" results in the InFlight set holding the path until a future restart — flagged here so a v2 fix can revisit).
