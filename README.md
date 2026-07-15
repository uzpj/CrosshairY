<div align="center">

# CrosshairY

**A lightweight crosshair overlay for Windows. Always on top, fully customizable, zero bloat.**

![Platform](https://img.shields.io/badge/platform-Windows-blue?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)
[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/tvJpwkyBmN)
![Language](https://img.shields.io/badge/language-C%23-green?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-orange?style=flat-square)

</div>

---

## What is it

CrosshairY draws a persistent crosshair directly on your screen as a transparent overlay. It works in any game or app, sits on top of everything, and is completely click-through so it never gets in the way. No injection, no drivers, just a window.

---

## Features

- **22 crosshair templates** - Dot, Ring, Square, Thin Cross, Thick Cross, Cross·, T-Shape, Circle+, Small Plus, Large Plus, Sniper, X Cross, X·, Arrows, Chevrons, Triangle, Diamond, Dot Ring, 2 Rings, Plus·, Corners, X Thick
- **Image import** - import your own PNG/JPG/BMP/GIF as a crosshair, position it anywhere on screen with drag controls, resize and recolor it with the existing sliders, and save it in your profiles
- **8 color swatches** with live preview plus a full **custom color picker** - square saturation/value field, hue bar and free hex input
- **Crosshair builder** - draw your own crosshair on a 15x15, 30x30 or 60x60 grid with a 16-color palette and a full tool set: **pencil, eraser, fill (bucket), line, rectangle and ellipse**
  - **Undo / redo** with Ctrl+Z / Ctrl+Y
  - **Mirror symmetry** - left/right, up/down or 4-way mirroring while you draw
  - **Save as crosshair** - pin your drawing into the "My Crosshairs" grid for one-click reuse
- **Crosshair keybinds** - bind any template, drawn, or image crosshair to any key for instant switching on press. Each binding stores its own full configuration with a live preview thumbnail. Bound keys persist globally across all profiles.
- **Randomize button** - picks a random template and color instantly
- **Outline toggle** with adjustable thickness (1-5)
- **Size slider** from 50% to 200%
- **Opacity slider** from 10% to 100%
- **Center gap slider** - control the gap between crosshair arms (0-20)
- **Position offset** - nudge the crosshair off-center on X and Y with `−`/`+` buttons (hold to repeat) or by typing an exact value, clamped to the screen bounds
- **Follow cursor mode** - replaces the Windows cursor with your crosshair so it tracks the pointer with zero lag instead of sitting in the center. Restores the original cursors the moment it is turned off. Works with any template, drawn, or image crosshair
- **Multi-monitor** - pick which display the crosshair appears on
- **Profile system** - save, load, overwrite, duplicate and delete configs stored locally in `%APPDATA%\CrosshairY\Configs`, each shown with a live crosshair thumbnail. Drop a friend's `.json` in the folder and hit reload
- **Auto game-profiles** - the **Games** tab maps a saved profile to a game, then loads it automatically when that game takes focus. Ships with a list of popular titles (Blood Strike, Valorant, CS2, Apex, Fortnite, Overwatch 2, Rainbow Six Siege, PUBG, Call of Duty, Marvel Rivals, The Finals, Destiny 2, Battlefield 2042) and lets you add any other game by its process name. Optional **auto-revert** restores your previous profile the moment you alt-tab out
- **Share codes** - export the current config to the clipboard as a compact code and import a friend's code in one click
- **Last used config** auto-loads on startup
- **Proof mode** - hides the window from screen capture software with a single keypress
- **Global hotkeys** - bind keys to cycle profiles, toggle the overlay on/off, toggle follow-cursor mode, trigger proof mode, or switch to any bound crosshair slot, all without opening the UI. Press **ESC** while binding to clear a key back to NONE
- **Auto-update** - on launch CrosshairY checks GitHub for a newer release and shows a small notification. One click downloads it and silently swaps the running executable, then relaunches. Toggle the notifications off in Settings, or check manually any time
- **Start with Windows** - optional auto-launch on sign-in, toggled in Settings (writes a per-user `Run` key, no admin needed)
- **System tray** - double-click the tray icon to open; right-click to toggle the overlay, follow-cursor or proof mode on/off, switch between saved profiles, or exit
- **No taskbar icon**, borderless, transparent, runs silently in the background
- **Launch surveys** - occasional single-question popups at launch milestones, never more than once each. Picking **Other** reveals a free-text field so you can type your own answer

---

## Requirements

- Windows 10 or 11 (x64)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

---

## Building from source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) or [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **.NET Desktop Development** workload

### Steps

1. Clone the repo
2. Drop the two font files into the `Fonts/` folder (see below)
3. Run `build.bat`

```
build.bat
```

Output lands at `bin\publish\CrosshairY.exe` as a single self-contained executable.

### Fonts needed

Both are free and available on [Google Fonts](https://fonts.google.com):

| File | Font |
|------|------|
| `Fonts/BebasNeue-Regular.ttf` | Bebas Neue |
| `Fonts/IBMPlexMono-Regular.ttf` | IBM Plex Mono |

---

## Profiles

Configs are plain `.json` files stored in `%APPDATA%\CrosshairY\Configs\`.

- **Save** - type a name in the Profiles tab and hit Save
- **Load** - click Load next to any config to apply it instantly
- **Overwrite** - hit Save next to an existing config to update it with your current settings
- **Duplicate** - hit Dup to copy a config to a new `name (2).json`
- **Thumbnail** - every saved config shows a live preview of its crosshair
- **Share file** - paste a friend's `.json` into the folder and hit Reload to see it appear
- **Export code** - copies the current config to the clipboard as a `CY1:` share code
- **Import code** - reads a `CY1:` code from the clipboard and saves it as a profile (named from the box, or `imported`)

A profile stores the template, color, outline, size, opacity, gap, position offset, follow-cursor mode, any custom builder crosshair, and any imported image path. The last loaded config is remembered and auto-applied on the next launch.

Hotkeys (proof key, profile cycle key, toggle-overlay key, follow-toggle key) and the update-notification preference are global settings stored separately in `%APPDATA%\CrosshairY\settings.dat`. They are never overwritten by loading a profile. Press **ESC** while binding a key to clear it back to NONE.

---

## Auto game-profiles

The **Games** tab assigns one of your saved profiles to each game. With **auto-switch** enabled, CrosshairY polls the focused window once a second, reads its process name, and loads the mapped profile when a known game comes to the front. Each game's button toggles `OFF` / `ON` (the assigned profile name shows on hover); enable **auto-revert** to fall back to the profile you had before, the moment no game is focused.

A built-in list covers popular titles, and any other game can be added by its process executable name (for example `EscapeFromTarkov.exe`). The mappings, toggles and custom games are stored in `%APPDATA%\CrosshairY\settings.dat`.

---

## Proof mode

Pressing the configured proof key calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the overlay window, hiding it from OBS, Discord screenshare, and similar capture software. Press again to restore. Configurable under Settings.

---

## Updates

On launch CrosshairY asks GitHub for the latest published release. If it is newer than the running version, a small notification slides in. Clicking **Download & Install** fetches the release's `CrosshairY.exe` asset, verifies its size, then launches a hidden script that waits for CrosshairY to close, swaps the executable in place and relaunches it - no visible terminal. You can turn the notifications off or run a manual check under **Settings → Update notifications**.

For the updater to work, each release must be published as a GitHub Release with `CrosshairY.exe` attached as an asset.

---

## Surveys

At launch counts 3, 7, 15, and 30 a small popup appears with a single question before the main UI loads. Each survey shows at most once. Closing without answering defers it to the next launch. The launch counter is stored in `%APPDATA%\CrosshairY\launches.dat`.

Survey responses are sent to a **Supabase Edge Function**, which forwards them to a Discord webhook stored as a server-side secret. The real webhook never ships in the client - the app only knows the public Supabase endpoint and publishable key. To point it at your own backend, change `SurveyEndpoint` and `SupabaseAnonKey` in `SurveyWindow.xaml.cs` and set the `DISCORD_WEBHOOK_URL` secret on your Supabase project.

---

## Project structure

```
CrosshairY/
|
+-- src/
|   +-- AppState.cs                 - runtime state
|   +-- GlobalKeyboardHook.cs       - low-level keyboard hook
|   +-- MainWindow.xaml             - main UI layout
|   +-- MainWindow.xaml.cs          - UI logic, color picker, builder, profiles
|   +-- CrosshairOverlay.xaml       - transparent overlay window
|   +-- CrosshairOverlay.xaml.cs    - crosshair draw logic (CrDraw), offset and follow-cursor rendering
|   +-- CursorReplacer.cs           - swaps the system cursor for the crosshair and restores it
|   +-- Updater.cs                  - GitHub release check, download and silent self-replace
|   +-- SurveyWindow.xaml           - survey popup layout
|   +-- SurveyWindow.xaml.cs        - survey logic, posts to the Supabase endpoint
|
+-- Fonts/                          - Bebas Neue + IBM Plex Mono
+-- App.xaml                        - styles and resources
+-- App.xaml.cs                     - global exception handling
+-- GlobalUsings.cs                 - shared global using directives
+-- CrosshairY.csproj
+-- app.manifest                    - DPI awareness
+-- build.bat
+-- clean.bat
```

---

## Virus Scan Results

*Last updated: July 15, 2026 - v1.0.8*

| Scanner | Result | Link |
|---------|--------|------|
| VirusTotal | 0/70 flagged | [View](https://www.virustotal.com/gui/file/3d99a00f35a260318997824f1356002b83ad5444cea5d5815ef3d502f43b2ed1) |
| Dr.Web | CLEAN | [View](https://online273.drweb.com/cache/?i=ce6804eb3cc6cb3e8c8f3022aa5e4f76) |
| Kaspersky OpenTIP | CLEAN | [View](https://opentip.kaspersky.com/3D99A00F35A260318997824F1356002B83AD5444CEA5D5815EF3D502F43B2ED1/results?tab=upload) |
| Jotti's Virus Scan | 0/13 flagged | [View](https://virusscan.jotti.org/en-US/filescanjob/g5hsub6cwf) |
| FileScan.io | NO THREAT DETECTED | [View](https://www.filescan.io/uploads/6a5776216f57653c1689951b/reports/b9e2fec5-f538-4661-97cc-d56cff94cf5c/overview) |

---

## License

MIT - do whatever you want with it.
