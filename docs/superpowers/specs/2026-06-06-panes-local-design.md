# Panes Local — Design Spec

**Date:** 2026-06-06
**Status:** Approved (pending user spec review)
**Scope:** New standalone Windows application for a folder-backed multi-pane video wall, sibling to the existing Electron-based Panes stream viewer.

## Goals

Build a lightweight, high-quality, chrome-less multi-pane local video player. Pick a folder (recursive), get a single shared playlist, and watch N panes pull from it independently with no duplicates. Same minimalist aesthetic as the existing Panes app: just video, hidden controls, edge-peek panel.

## Non-Goals

- URL/streaming support — that's what the existing Electron `panes` app is for.
- Cross-platform — Windows-only.
- Online/cloud features.
- Per-pane independent playlists — explicitly rejected in favor of the shared queue model.
- "Mode" switches for shuffle vs ordered vs custom — replaced with actions (shuffle button, reshuffle button, drag-to-reorder) on a single queue.

## Stack

- **.NET 8 + WPF** — native Windows UI, per-monitor DPI, mature.
- **LibVLCSharp** (NuGet) + **VideoLAN.LibVLC.Windows** — playback. One `MediaPlayer` per pane bound to a `VideoView`. Plays every common video format.
- **Inno Setup** — installer (`setup.exe`, per-user install, Start Menu + desktop shortcuts).
- No web tech, no Electron, no Chromium.

## Repo Layout

Sibling folder in the existing `panes` repo. The existing Electron app is untouched.

```
panes/                          ← existing Electron app, unchanged
panes-local/                    ← new
  PanesLocal.sln
  src/PanesLocal/
    PanesLocal.csproj
    App.xaml, App.xaml.cs
    MainWindow.xaml, MainWindow.xaml.cs
    Models/                     ← VideoItem, SharedQueue, PaneState, AppState
    Views/                      ← Pane.xaml, SidePanel.xaml
    Services/                   ← FolderScanner, PersistenceService, AudioCoordinator
  installer/
    PanesLocal.iss              ← Inno Setup script
  build.ps1                     ← dotnet publish + iscc
```

**Build:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces one ~80MB exe with .NET runtime and libvlc DLLs bundled. `build.ps1` then runs Inno Setup to wrap it as `dist/PanesLocal-Setup.exe`.

## Window & Chrome

Single borderless WPF window — no title bar, no menu, no system chrome.

- `WindowStyle="None"`, `WindowChrome` for native Windows snap (Win+Arrow, Aero snap) without showing a title bar.
- 1px invisible resize hit zone around edges.
- F11 toggles between bordered window and true fullscreen (covers taskbar).
- Background `#000`. Window position/size/state restored on launch.
- Cursor and edge-peek arrow auto-hide after 3 seconds of mouse idle. Any movement brings them back.

## Grid & Panes

`Grid` with `cols × rows` cells, 1–6 each (matches existing app's range). Cells are `Pane` user controls. Grid rebuilds when cols/rows change.

When pane count shrinks (e.g., 3×3 → 2×2), surplus panes' current videos go back to the bottom of the queue per the booted-video rule.

**Per-pane visuals:**
- `VideoView` fills the cell.
- Hover overlay (top-right): ⏭ Skip, 🔇/🔊 Mute, ✕ Stop. Fades in on mouse over the pane.
- Soft highlight border during drag-over.

**Zoom:** Side-panel slider applies a `ScaleTransform` to each `VideoView` (`RenderTransformOrigin=0.5,0.5`, cell clips the overflow). Range 100–300%. If WPF transform scaling looks rough, fall back to libVLC's native crop/scale.

## Shared Queue (the core data model)

Single source of truth across all panes.

```csharp
public record VideoItem(string FilePath, string DisplayName, long FileSize);

public class SharedQueue {
    ObservableCollection<VideoItem> Items;   // upcoming, in order
    HashSet<string> InFlight;                // paths currently playing on a pane
    HashSet<string> Played;                  // paths that finished naturally

    VideoItem? TakeNext();                   // pops from Items, adds to InFlight
    void Reinsert(VideoItem item, int position);
    void Remove(VideoItem item);
    void MoveToTop(VideoItem item);
    void MoveUp(VideoItem item);
    void MoveDown(VideoItem item);
    void Shuffle();                          // shuffles Items
    void Reshuffle();                        // alias — explicit action button
    void OnFinished(string path);            // InFlight → Played; refill if needed
}
```

**Invariants:**
- Every path is in exactly one of `{Items, InFlight, Played}` at any moment. This is what gives us "no duplicates" for free.
- `TakeNext()` returns `null` when `Items` is empty.

**Shuffle toggle ON:** `Shuffle()` runs on every folder load and every refill. `Reshuffle` button = manual `Shuffle()` of current `Items`.

**Repeat-all toggle ON + queue empty + a pane asks for next:** `Played` is moved back into `Items` (shuffled if shuffle is on), pane gets a video. Loops forever.

**Repeat-all OFF + queue empty:** Pane goes black with a small "queue empty" hint. Other panes keep playing until they finish.

**Booted video (drag-replace on a pane):** Re-inserted at the **bottom** of `Items`.

## Folder Loading

- "Choose Folder…" button in the side panel → `Microsoft.Win32.OpenFolderDialog` (.NET 8 built-in).
- `FolderScanner.ScanAsync(path, ct)` walks recursively on a background thread.
- Extension filter (initial): `.mp4 .mkv .avi .mov .webm .m4v .wmv .flv .ts .mpg .mpeg .ogv`. Easy to extend.
- Hidden/system files skipped. Symlinks not followed.
- Side-panel header shows live scan progress: "Scanning… 312 videos".
- "Add Folder…" appends to the queue instead of replacing.
- "Clear queue" empties Items/InFlight/Played and stops all panes.

## Pane Lifecycle

- On creation, each pane calls `queue.TakeNext()` and starts playback: `new Media(libVLC, path, FromType.FromPath)` → `MediaPlayer.Play(media)`.
- `MediaPlayer.EndReached` → `queue.OnFinished(path)`, then `TakeNext()` and play next. 200ms opacity crossfade between videos.
- `MediaPlayer.EncounteredError` → log to debug output, skip to next. Broken files never freeze a pane.
- On disposal (grid resize, app close) → `MediaPlayer.Dispose()`, current video reinserted at bottom of queue.

## Drag and Drop

| From | To | Effect |
|--|--|--|
| Playlist row | Playlist row | Reorder. Insert-line adorner shows the drop position. |
| Playlist row | Pane | Play this *now* on that pane. Pane's current video → bottom of queue. Dropped item: Items → InFlight. |
| Pane | Playlist | Stop with placement. Current video reinserted at the drop position. |
| Pane | Pane | Swap. Both panes exchange currently-playing videos. Queue untouched. |

While any drag is in progress, the side panel does NOT auto-close.

## Context Menus

**Playlist row:** Play next (move to top), Move up, Move down, Move to bottom, Remove, Open file location.

**Pane:** Skip, Stop, Mute / Unmute, Open file location.

## Audio (one-pane-unmuted-at-a-time)

- All panes start muted.
- Clicking 🔊 on a pane: that pane's `MediaPlayer.Volume = 100`, every other pane → 0.
- Clicking 🔇 on the unmuted pane: that pane → 0 (return to all-muted state).
- An `AudioCoordinator` service owns this state so the side panel header can show "🔊 Pane 3" or "🔇 All muted".
- State persists.

## Side Panel UX

**Resting state:** invisible. Just panes.

**Edge peek:**
- When cursor moves within 24px of the dock edge, a small chevron (›) fades in at vertical center. Opacity 0.25 default, 0.7 on hover.
- Click — or 400ms hover on the chevron — slides the panel out (200ms ease).
- A matching close chevron (‹) appears at the panel's inner edge while open.

**Auto-close:**
- If cursor leaves the panel's bounds + 12px hysteresis and stays out for 600ms, panel slides closed.
- Suspended during any drag operation.
- Clicking inside the panel "anchors" it open until you click the close chevron — useful for long playlist editing.

**Drag-to-snap docking:**
- 8px grip handle (⠿) at the top of the panel.
- Press-drag detaches the panel (semi-transparent ghost follows cursor).
- Window edges glow as targets when cursor is within 80px.
- Release → snap to that edge. Vertical layout for left/right, horizontal layout for top/bottom.
- If released without any edge in range, snap back to the previous dock side (no floating panels).
- Dock side persists.

**Panel contents (vertical dock, top to bottom):**
1. Drag handle (⠿).
2. Header: folder name, video count, audio status, scan progress.
3. Grid size: cols × rows with +/- buttons.
4. Transport: **Shuffle** toggle, **Repeat all** toggle, **Reshuffle** button, **Choose Folder…**, **Add Folder…**, **Clear queue**.
5. Zoom slider (100–300%).
6. "Now Playing" — N rows showing what's on each pane, with pane index.
7. Playlist `ListView` — upcoming items, drag handle per row, ⋮ menu per row.
8. Footer: "F11 fullscreen · Drag rows to reorder · Drag onto a pane to play now".

**Horizontal dock variant:** same controls in a row; playlist becomes a horizontal strip with smaller thumbnails. Vertical is the primary design.

## Persistence

Single file: `%AppData%\PanesLocal\state.json`. Written debounced 500ms after the last change.

```json
{
  "window": { "x": 100, "y": 80, "width": 1920, "height": 1080,
              "maximized": false, "fullscreen": false },
  "grid":   { "cols": 2, "rows": 2 },
  "zoom":   150,
  "panel":  { "dock": "right", "size": 320 },
  "transport": { "shuffle": true, "repeatAll": true },
  "audio":  { "unmutedPaneIndex": null },
  "folders": ["D:\\Videos\\Wall"],
  "queue":  ["D:\\Videos\\Wall\\a.mkv", "D:\\Videos\\Wall\\b.mp4"],
  "played": ["D:\\Videos\\Wall\\old.mkv"],
  "panes":  [
    { "index": 0, "filePath": "D:\\Videos\\Wall\\c.mkv", "positionSeconds": 412.5 },
    { "index": 1, "filePath": "D:\\Videos\\Wall\\d.mp4", "positionSeconds":  88.2 }
  ]
}
```

**Rehydrate on launch:**
1. Restore window, grid, zoom, panel dock, transport toggles, audio state.
2. Rescan saved folders in the background.
3. Reconstruct the queue: for each saved `queue` path, keep if file still exists, drop silently otherwise. Append newly discovered files (folder grew since last session) to the end — shuffled in if shuffle is on. Filter `played` the same way.
4. For each pane: if saved `filePath` exists, resume playback and seek to `positionSeconds`. Otherwise call `TakeNext()` like a fresh start.
5. Restore unmuted pane.

**Crash safety:** Write to `state.json.tmp` then atomic rename. On corrupt-file load, fall back to defaults and show a one-time toast: "Could not restore last session — starting fresh."

**Reset:** "Reset to defaults" button in panel footer wipes `state.json` and relaunches with empty queue.

## Components Summary

| Component | Responsibility | Depends On |
|--|--|--|
| `MainWindow` | Window chrome, F11, mouse-idle hide, layout root | — |
| `Pane` (UserControl) | `VideoView`, `MediaPlayer`, hover overlay, drag source/target | `SharedQueue`, `AudioCoordinator` |
| `SidePanel` (UserControl) | Edge peek, auto-close, drag-snap docking, all controls, playlist | `SharedQueue`, `AppState` |
| `SharedQueue` | The Items/InFlight/Played model and its operations | — |
| `FolderScanner` | Background recursive walk, extension filter | — |
| `AudioCoordinator` | One-unmuted-pane rule | — |
| `PersistenceService` | Debounced JSON read/write, atomic file swap | `AppState` |
| `AppState` | In-memory snapshot of everything saved to JSON | — |

## Keyboard Shortcuts

- `F11` — toggle fullscreen.
- `Space` — if any pane is unmuted, mute all. If all muted, unmute pane index 0.
- `Esc` — close side panel.
- `S` — toggle shuffle.
- `R` — reshuffle.
- All shortcuts ignored while a text input has focus.

## Error Handling Boundaries

- **libVLC unavailable / native DLL load failed:** Show a modal error at startup with a copy-button error message. Don't crash silently.
- **Corrupt video file:** `EncounteredError` → log → skip. Pane keeps going.
- **Folder permission denied during scan:** Skip the subfolder, continue scanning, report count in panel header.
- **State file corrupt:** Fall back to defaults, one-time toast.
- **Drive disconnect mid-playback:** `EncounteredError` fires → skip; on next rehydrate, missing paths are filtered.

## Testing Approach

- Manual smoke test on a real folder of mixed-format videos (.mp4, .mkv, .avi at minimum).
- Stress test: 6×6 grid (36 simultaneous players) on a folder of 100+ videos to verify shared queue + dedup invariants and CPU/GPU behavior.
- Unit tests for `SharedQueue` operations (the invariants — never duplicates across Items/InFlight/Played — are the most important thing to test).
- Persistence round-trip test: write → reload → assert state matches, with one missing file in the queue.

No automated UI tests in initial scope.

## Open Items (deferred, not blocking v1)

- Thumbnail generation for playlist rows (libVLC can produce thumbnails; deferred for perf).
- Integration with the Electron launcher (the launcher.html idea was raised then deferred — eventually a single startup chooser may launch either app).
- Hotkey customization.
- Multiple saved "queue presets" (the existing Panes app has presets; we may want them here too later).
