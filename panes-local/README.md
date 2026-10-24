# Panes Local

Native Windows multi-pane player for folders of local video files (.NET 10, WPF, LibVLCSharp). Sibling to the Electron `panes` app.

How to use it, and the full update and install steps, are in the main [README](../README.md#using-panes-local).

## Quick reference

Run from this folder:

```powershell
dotnet test                            # unit tests
dotnet run --project src/PanesLocal    # run from source (uses the same saved session as the installed app)
./build.ps1                            # tests, publish, installer -> dist\PanesLocal-Setup.exe (needs ISCC.exe on PATH)
```

## Layout

| Path | What it is |
|------|------------|
| `src/PanesLocal/MainWindow.*` | Window, grid of panes, fullscreen, session save and restore |
| `src/PanesLocal/Views/` | `Pane` (one video) and `SidePanel` (all controls and the playlist) |
| `src/PanesLocal/Services/` | `SharedQueue`, `FolderScanner`, `AudioCoordinator`, `PersistenceService` |
| `src/PanesLocal/Models/` | The saved state (`AppState`, `PaneState`) and `VideoItem` |
| `tests/PanesLocal.Tests/` | xUnit tests for the queue, scanner, audio and persistence |
| `installer/PanesLocal.iss` | Inno Setup script (per-user install, Start Menu and desktop shortcuts) |

The saved session is `%APPDATA%\PanesLocal\state.json`. The design spec and implementation plan are in `../docs/superpowers/`.
