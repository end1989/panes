# Panes

Minimal multi-video walls for Windows. Pick a grid, fill it with videos, and the controls get out of the way so all you see is video.

| App | Plays | Open it with |
|-----|-------|--------------|
| **Panes** | Streams and web videos: YouTube, Twitch, Vimeo, Dailymotion, Kick, or any page that can be embedded | the **Panes** desktop shortcut |
| **Panes Online** | The same app, hosted. Nothing to install | **[end1989.github.io/panes](https://end1989.github.io/panes/)**, or `panes_online.bat` |
| **Panes Local** | Video files from folders on your PC | the **Panes Local** desktop shortcut |

- [Using Panes](#using-panes) (and Panes Online)
- [Using Panes Local](#using-panes-local)
- [Updating the apps](#updating-the-apps)
- [Troubleshooting](#troubleshooting)

---

## Using Panes

Panes and Panes Online are the same page (`index.html`). The desktop app wraps it in its own window. Panes Online serves the copy on the `master` branch from GitHub Pages, so it can be a version behind the desktop app.

### Basics

1. The control panel opens on launch. Later, press `M` or click the faint gear in the bottom-right corner.
2. Set the grid with the `-` / `+` buttons: 1 to 6 columns by 1 to 6 rows (up to 36 videos).
3. Paste a link into a numbered row and press `Enter` (or **Go**). Row 1 is the top-left cell, numbered left to right, top to bottom.
4. Press `F` (or **Enter Fullscreen**) to go fullscreen. In the desktop app, `F11` also works.

The gear and the cursor hide after 3 seconds without mouse movement, unless the panel is open.

### Zoom and pan

- **Zoom** (100 to 300%) enlarges every video at once to crop off player controls and black bars. Use the slider, or type an exact value in the box.
- **Pan** moves the visible area over the zoomed videos: drag inside the pan pad. Double-click the pad or press the ◎ button to re-center.

### Presets

Type a name and press **Save** to store the current grid, links, zoom and pan. Click a preset to load it exactly as saved; click the **×** next to it to delete it.

### Keyboard

| Key | Action |
|-----|--------|
| `M` | Show or hide the control panel |
| `F` | Fullscreen on or off (`F11` also works in the desktop app) |
| `1` to `9` | Open the panel and jump to that cell's link box |
| `Enter` | Load the link you're typing |
| `Esc` | Close the panel (and leave the link box you're in) |

These control every YouTube cell at once. They only work when Panes is served over http(s), which means Panes Online or `serve.bat`:

| Key | Action |
|-----|--------|
| `Space` or `K` | Play or pause all |
| `←` / `→` | Back or forward 5 seconds |
| `J` / `L` | Back or forward 10 seconds |
| `[` / `]` | Previous or next video in a playlist |

### Supported links

Regular links are converted to embeddable players automatically:

| Service | Links |
|---------|-------|
| YouTube | `youtube.com/watch?v=…`, `youtu.be/…`, `youtube.com/shorts/…`, `youtube.com/live/…`, `youtube.com/playlist?list=…` (a `list=` on a video link is kept too) |
| Twitch | `twitch.tv/channelname`, `twitch.tv/videos/123` |
| Vimeo | `vimeo.com/123456` |
| Dailymotion | `dailymotion.com/video/…` |
| Kick | `kick.com/channelname` |

Any other link loads as-is, so any page that allows embedding works. Links without `http` get `https://` added.

### What Panes remembers

The grid size, every cell's link, zoom, pan and your presets are saved as you go and come back the next time you open it.

The desktop app, Panes Online, `serve.bat`, and opening the file from disk in a browser each keep their **own** separate copy of these. Presets saved in one don't appear in the others.

### Other ways to run it

| Launcher | What it does |
|----------|--------------|
| `serve.bat` | Serves this folder at `http://localhost:8080` and opens it in a Chrome app window. Gives you the YouTube keyboard controls without going online. Needs Python 3. |
| `launch.bat` | Menu: open `index.html` in your default browser, in Chrome app mode, or in Chrome kiosk mode (exit kiosk with `Alt+F4`). |
| `quick_launch.bat` | Opens `index.html` straight in Chrome app mode. |
| `panes_online.bat` | Opens Panes Online. |

The desktop app adds a few things of its own: it remembers its window size and position, only one copy runs at a time, and pressing `Alt` shows a menu (Reload, Developer Tools, Full Screen, Quit).

---

## Using Panes Local

Panes Local plays the video files in your folders across a grid of panes. All panes pull from one shared queue, so the same video never plays in two panes at once.

### Getting started

1. Open **Panes Local**. The side panel opens on launch.
2. Click **Choose folder…** and pick a folder. Subfolders are included.
3. Videos start filling the panes from the queue. When a video ends, its pane takes the next one.

The window has no title bar. **Close it with `Alt+F4`.** Resize it from its edges, or snap it with `Win`+arrow keys.

### The side panel

| Control | What it does |
|---------|--------------|
| ⠿⠿⠿ handle | Drag it to the left, right, top or bottom edge of the window to dock the panel there |
| Header | The folder, how many videos are loaded, and which pane has sound |
| **Grid** `-` / `+` | 1 to 6 columns by 1 to 6 rows |
| **Shuffle** | On: the queue is put in random order when a folder loads and when Repeat all refills it |
| **Repeat all** | On: when the queue runs out, everything that played goes back in. Off: panes go dark with "(queue empty)" |
| **Reshuffle** | Shuffles the upcoming queue now |
| **Choose folder…** | Replaces the queue with a folder's videos |
| **Add folder…** | Adds another folder's videos to the queue |
| **Clear queue** | Empties the queue and stops every pane |
| **Now Playing** | One row per pane: 🔇/🔊 sound, ⏭ skip to the next video, ✕ stop the pane (its video goes back to the end of the queue) |
| Playlist | The upcoming queue. Drag a row up or down to reorder it, or onto a pane to play it there right now |
| **Reset to defaults** | Forgets everything (folders, queue, layout) and restarts the app |

Only one pane plays sound at a time. To hide the panel, press `M` or `Esc`. To bring it back, press `M` or click the small tab on the panel's edge of the window. The cursor hides after 3 seconds without mouse movement.

### Keyboard

| Key | Action |
|-----|--------|
| `F11` | Fullscreen on or off. Fullscreen covers the taskbar; pressing it again puts the window back where it was |
| `M` | Show or hide the side panel |
| `Esc` | Hide the side panel |
| `Space` | Mute everything, or if everything is muted, unmute pane 1 |
| `S` | Shuffle on or off |
| `R` | Reshuffle the queue |

### Supported files

`.mp4` `.mkv` `.avi` `.mov` `.webm` `.m4v` `.wmv` `.flv` `.ts` `.mpg` `.mpeg` `.ogv`

Hidden files, system files and symbolic links are skipped.

### What Panes Local remembers

Everything is saved as you go and restored the next time you open it:

- window position and size, and whether it was fullscreen
- grid size, which edge the panel is docked to, Shuffle and Repeat all
- your folders, the queue in its current order, and what has already played
- what each pane was playing and how far in, and which pane had sound

On launch it rescans your folders first. Videos that were deleted are dropped, and new ones are added to the end of the queue (in random order if Shuffle is on). The rest of the queue keeps your order.

---

## Updating the apps

### What lives where

| Path | What it is |
|------|------------|
| `index.html` | The whole Panes app in one file. Used by the desktop app, Panes Online and the `.bat` launchers |
| `main.js`, `preload.js`, `package.json` | The Electron wrapper that makes Panes a desktop app |
| `build/icon.ico`, `build/icon.png` | App icons, used by both installers |
| `panes-local/` | Panes Local (.NET 10, WPF and libVLC): `src/`, `tests/`, `installer/`, `build.ps1` |
| `docs/` | Panes Local design spec and implementation plan |
| `*.bat` | The launchers described above |

### Tools you need

- **Node.js 22+** for the Panes desktop app. Run `npm install` in the repo root once, and again whenever `package.json` changes.
- **.NET 10 SDK** for Panes Local.
- **Inno Setup 6** for the Panes Local installer. `build.ps1` needs `ISCC.exe` on your PATH; the per-user install puts it in `%LOCALAPPDATA%\Programs\Inno Setup 6`. To add it for the current PowerShell session:

  ```powershell
  $env:PATH += ";$env:LOCALAPPDATA\Programs\Inno Setup 6"
  ```

- **Python 3**, only for `serve.bat`.

### Panes (desktop app)

1. Edit `index.html` (the app) or `main.js` (the window around it).
2. Try your change from source:

   ```powershell
   npm start
   ```

   This uses the same saved links and presets as the installed app.
3. Build the installer:

   ```powershell
   npm run dist:nsis
   ```

   This creates `dist\Panes Setup 1.0.0.exe`.
4. Close Panes, then run the installer. It installs over the old version for your user only, and keeps your links, presets and window position. For a silent install:

   ```powershell
   & "dist\Panes Setup 1.0.0.exe" /S
   ```

### Panes Online

Panes Online is GitHub Pages serving `index.html` from the root of the `master` branch. To update it, commit the change to `master` and push:

```powershell
git push origin master
```

The new version is live about a minute later. Browsers can keep the old page for up to 10 minutes; press `Ctrl+F5` to fetch it now. Changes on other branches don't go live until they're merged into `master`.

### Panes Local

Run these from the `panes-local` folder.

1. Edit the code under `src/PanesLocal`.
2. Run the tests:

   ```powershell
   dotnet test
   ```

3. Try your change from source:

   ```powershell
   dotnet run --project src/PanesLocal
   ```

   This uses the same saved session as the installed app.
4. Build the installer:

   ```powershell
   ./build.ps1
   ```

   It runs the tests, publishes the app, and creates `dist\PanesLocal-Setup.exe`.
5. Close Panes Local (`Alt+F4`), then run the installer. It installs over the old version for your user only, and keeps your saved session. For a silent install:

   ```powershell
   dist\PanesLocal-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER
   ```

### Version numbers

Both installers are version 1.0.0 and reinstall over themselves fine. To label a new release, bump `version` in `package.json` (this also changes the Panes installer's file name) and `AppVersion` in `panes-local/installer/PanesLocal.iss`.

### Where settings are stored

| App | Location |
|-----|----------|
| Panes (desktop app) | `%APPDATA%\Panes` (browser storage inside the app) |
| Panes Online | your browser's site data for `end1989.github.io` |
| Panes Local | `%APPDATA%\PanesLocal\state.json` |

Reinstalling or updating keeps these. Uninstall either app from **Settings → Apps**.

---

## Troubleshooting

**A YouTube video won't play.** YouTube's player can refuse to play on a page opened straight from disk (`file://`), which is how the desktop app and the `.bat` launchers load Panes. Use Panes Online or `serve.bat`, which run over http(s).

**The YouTube keyboard shortcuts do nothing.** They need http(s) as well: use Panes Online or `serve.bat`.

**My presets are missing.** Each way of running Panes has its own storage (see [What Panes remembers](#what-panes-remembers)). Check the one you saved them in.

**Panes Local leaves the taskbar showing.** The window is maximized (for example with `Win`+`↑`), not fullscreen. Press `F11`.

**Panes Local says "Could not restore last session — starting fresh."** Its saved session file couldn't be read, so it started with defaults. Choose your folder again.

**Panes Local shows a libVLC error on startup.** Its video engine didn't load. Reinstall Panes Local.

## License

MIT
