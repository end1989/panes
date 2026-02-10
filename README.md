# Panes

Watch multiple streams side by side with zero UI getting in the way. One HTML file, no install, no dependencies.

Paste YouTube, Twitch, Vimeo, or Kick links and they just work. Resize the grid from a single panel to a 6x6 wall. Everything auto-hides so all you see is video.

## Quick Start

**Option A** — Open `panes.html` in your browser. Press F11.

**Option B** — Double-click `launch.bat` and pick a mode.

**Option C** — Host it anywhere. It's a single static HTML file. Works on GitHub Pages.

## Features

- **Custom grid** — Adjust columns and rows from 1x1 up to 6x6 (36 streams)
- **Smart URLs** — Paste a regular YouTube/Twitch/Vimeo/Kick link, it auto-converts to an embed
- **Zoom slider** — Scale all videos at once to crop out player chrome and black bars
- **Keyboard-first** — Press `1`-`9` to enter URLs, `M` for menu, `F` for fullscreen
- **Remembers everything** — Grid size, URLs, and zoom persist across sessions
- **Invisible UI** — Controls and cursor auto-hide after 3 seconds

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `M` | Toggle control panel |
| `F` | Toggle fullscreen |
| `1`-`9` | Open panel and focus URL field |
| `Escape` | Close panel |
| `Enter` | Load video (when typing a URL) |

## Supported Platforms

| Service | Example URL |
|---------|-------------|
| YouTube | `youtube.com/watch?v=...` `youtu.be/...` `youtube.com/shorts/...` `youtube.com/live/...` |
| Twitch | `twitch.tv/channelname` `twitch.tv/videos/123` |
| Vimeo | `vimeo.com/123456` |
| Dailymotion | `dailymotion.com/video/...` |
| Kick | `kick.com/channelname` |

Any URL that supports iframe embedding will work — these are just the ones with auto-conversion.

## Launch Modes (Windows)

| Mode | What it does |
|------|-------------|
| `launch.bat` option 1 | Opens in default browser. Press F11 for fullscreen. |
| `launch.bat` option 2 | Chrome App Mode — no tabs, no address bar. |
| `launch.bat` option 3 | Chrome Kiosk — true fullscreen, zero UI. Exit with Alt+F4. |
| `quick_launch.bat` | Straight to Chrome App Mode. |

## License

MIT
