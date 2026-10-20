<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="MegaSnip app icon">
</p>

# MegaSnip

MegaSnip is a tray-only Windows utility that turns text on your screen into plain text on your clipboard. Press a hotkey, drag a box around the text, and MegaSnip recognizes it locally with Windows OCR and copies it.

Everything runs offline. There are no cloud calls, API keys, telemetry, analytics, or network requests.

Repository: [github.com/MicroEG/MegaSnip](https://github.com/MicroEG/MegaSnip)

## Highlights

- Tray-only: no main window, no taskbar button.
- Configurable global hotkey, default `Ctrl+Alt+T`, plus a tray **Snip** command.
- Dimmed full-screen overlay on every monitor with a 100%-bright selection cut-out, crosshair cursor, and live pixel dimensions.
- Selection is captured in physical pixels and OCR runs after the overlay is already gone.
- Plain Unicode text is copied to the clipboard by default; no image is placed on the clipboard.
- Empty OCR results show **No text detected** and leave the existing clipboard untouched.
- Optional preview window with the captured thumbnail, editable text, **Copy**, **Copy & Close**, and **Re-run OCR**.
- Per-monitor DPI awareness v2, multi-monitor layouts (including negative coordinates), and a 5×5 pixel minimum selection.
- Optional local history with text and captured images, search, editing, copy, export, retention limits, and one-hour pause.
- Single instance; launching a second copy exits immediately.
- Settings persist to `%APPDATA%\MegaSnip\settings.json`.
- Optional start with Windows through the current user's `Run` key; no administrator rights required.
- Best-effort low-level keyboard-hook fallback if another application already owns the configured hotkey.

## Requirements

- Windows 10 version 1809 or later, or Windows 11.
- At least one Windows OCR language pack. English (United States) is present on most systems; other languages are installed through Windows Settings under **Time & language > Language & region > Language options > Optical character recognition**.
- The published build is self-contained and does not require a separate .NET installation.
- Building from source requires the .NET 8 SDK or later.

MegaSnip runs with the current user's privileges and never requests elevation.

## Usage

1. Start `MegaSnip.exe`. It appears in the notification area, possibly under the hidden-icons chevron.
2. Press `Ctrl+Alt+T` or choose **Snip** from the tray menu.
3. Drag a rectangle around the text. The size label shows the selection in physical pixels.
4. Release the mouse. The overlay closes, OCR runs, and the text is copied.
5. A toast shows **Text copied (N chars)** with a **Preview** link and, when history is enabled, a **Don't log this** action. The link opens the preview window for corrections.
6. Press `Esc` or right-click at any time before releasing to cancel without copying, previewing, or changing the clipboard.

Tray menu:

| Item | Action |
| --- | --- |
| **Snip** | Starts a selection. The configured shortcut is shown beside it. |
| **Last result** | Reopens the preview for the most recent successful OCR result. Disabled until one exists. |
| **History** | Opens the history window. |
| **Pause history for 1 hour** | Stops saving new snips to history for one hour. While paused the item becomes **Resume history**. |
| **Open history folder** | Opens the current history folder in File Explorer. |
| **Settings** | Opens the settings window. |
| **Quit** | Exits MegaSnip. |

If OCR finds no text, a **No text detected** toast is shown and the previous clipboard contents are preserved. If the clipboard is locked by another application, MegaSnip retries for about one second before reporting a failure.

Selecting a region smaller than 5×5 pixels is treated as a cancel.

## History

History is enabled by default. The first time it is active, a privacy notice explains what is saved and where it is stored; acknowledging the notice is remembered in the settings file.

- **Storage:** by default `%APPDATA%\MegaSnip\history\`. The folder contains `history.db` (a SQLite index) and one folder per snip with `meta.json` plus `image.png` and `thumb.png` when images are saved.
- **What is saved:** recognized text always; the captured screenshot only when **Save captured images** is enabled. Empty OCR results are not logged.
- **Nothing leaves the PC:** history is never uploaded, synced, or analyzed.
- **History window:** a virtualized list with search, a thumbnail and text detail pane, and automatic saving of text edits. Actions include Copy text, Copy image, Copy both, Re-run OCR on the saved image, Open folder, Delete, Clear all history, and Export.
- **Default double-click action:** open the details, copy the text, or copy the saved image. This is configurable in Settings.
- **Deletion:** deleting a snip removes its database row and its folder, including the saved image. Clear all requires typing `DELETE` and permanently removes every entry.
- **Export:** writes an `export-<timestamp>` folder with `index.md`, one `.txt` per snip, and a `.png` when an image exists.
- **Retention:** automatic cleanup runs at startup and every 24 hours. You can cap the number of snips, their age in days, and total disk usage; `0` disables an individual limit.
- **Pause:** the tray pause stops new history writes for one hour. It is in memory only and resets when MegaSnip restarts.
- **Storage folder change:** choose **Change…** in Settings. The entire history is copied to an empty folder, the new location is verified, and the old folder is removed. If the old folder cannot be removed, the move still completes and a warning is shown.

### Schema migration

Settings files written by earlier versions (schema v1) are upgraded to schema v2 on load. Missing history fields receive their defaults, so existing settings keep working without manual edits. The SQLite schema is created automatically on first use, and snips saved by earlier history builds remain readable.

## Settings

Changes are applied when you press **Save**:

- **Hotkey** - click **Change**, then press the new combination. A shortcut needs at least one of Ctrl, Alt, Shift, or Win plus another key. `Esc` cancels the capture without closing the settings window.
- **OCR language** - **System default** uses the first OCR-capable Windows user-profile language; the other entries come from installed Windows OCR recognizers.
- **Line breaks** - preserve the recognized line breaks or join lines with single spaces.
- **Open preview automatically** - open the result window instead of the success toast.
- **Sound feedback** - play Windows system sounds for success, warning, and error outcomes.
- **Start MegaSnip with Windows** - add or remove the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MegaSnip` value.
- **Save snips to history** - turn local history on or off.
- **Save captured images with each snip** - include the screenshot in new history entries; text is saved either way.
- **Ask before deleting snips** - show a confirmation dialog for single and bulk deletions.
- **Double-click action** - open details, copy text, or copy image.
- **Storage folder** - view and change the history location. **Change…** moves the existing history to an empty folder immediately.
- **Delete old snips automatically** - enable the retention limits for count, age, and disk usage.

Settings are validated before they are committed. If a new hotkey cannot be registered, or the startup setting or settings file cannot be updated, the previous values are restored and the error is shown in the window. A failed history folder move restores the previous history location and leaves the original data untouched.

## Offline and privacy

- No network capability is used at runtime. Nothing is uploaded, downloaded, or telemetered.
- Screen pixels are captured only after a selection is completed and only for the selected rectangle.
- The latest capture is held in memory for the toast preview and **Last result**. It is discarded when MegaSnip exits.
- During a normal snip, only text is written to the clipboard. The history window can explicitly copy a saved image or text and image together.
- MegaSnip writes only:
  - `%APPDATA%\MegaSnip\settings.json`
  - the history folder (`%APPDATA%\MegaSnip\history\` by default), when history is enabled
  - the current user's `MegaSnip` Run value, when **Start with Windows** is enabled
  - export folders that you explicitly choose
- Text edits, OCR results, and saved images remain in history until you delete them or retention removes them.

To remove MegaSnip, quit it, turn off **Start MegaSnip with Windows** (or delete the `MegaSnip` Run value), delete the executable, and remove `%APPDATA%\MegaSnip`.

## Build and test

```powershell
dotnet build MegaSnip.sln -c Release
dotnet test MegaSnip.sln -c Release
```

The solution builds with warnings treated as errors. Tests cover clipboard retry behavior and image formats, line-break formatting, OCR post-processing, settings round-trips, corruption recovery, and schema migration, hotkey validation, history storage and retention, history directory migration, pause state, export, and OCR of a fixed PNG fixture.

The OCR fixture test uses the installed Windows OCR engine with `en-US`; install that language pack if it is missing. The other tests use temporary folders and do not touch the real clipboard, the registry, or the user's settings or history.

## Publish

```powershell
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

The script publishes a self-contained, single-file executable to:

```text
dist\MegaSnip-<version>-win-x64\MegaSnip.exe
```

Use `-RuntimeIdentifier win-arm64` for Windows on Arm. The SQLite native library is embedded for self-extraction, so the executable remains a single file. No installer is produced; the executable is portable. The build is not code-signed, so Windows SmartScreen may show a warning on first launch.

## Project layout

```text
src/MegaSnip/
  App.xaml.cs                  Startup, single instance, capture/OCR/copy orchestration
  Capture/                     Screen capture through GDI
  Clipboard/                   Unicode-text and multi-format image clipboard writers
  History/                     SQLite repository, history window, export, privacy notice, migration
  Infrastructure/              Hotkey manager, keyboard-hook fallback, theme, interop
  Ocr/                         IOcrService seam, Windows.Media.Ocr provider, post-processing
  Overlay/                     Per-monitor selection windows and physical-pixel geometry
  Preview/                     Result preview and correction window
  Settings/                    Settings model, JSON store, startup registration, settings window
  Toast/                       Non-activating result toast
  Tray/                        NotifyIcon and context menu
tests/MegaSnip.Tests/          xUnit tests and fixed OCR fixture
build/                         Icon generation and publish scripts
```

## How it works

1. `HotkeyManager` registers the shortcut with `RegisterHotKey`. If that fails because the combination is taken, `LowLevelKeyboardHook` watches for it instead.
2. `SelectionOverlayController` creates one overlay window per monitor. The overlay draws a dim layer with a transparent cut-out and publishes the selection rectangle in physical screen coordinates.
3. On mouse-up the overlays are hidden; `ScreenCapture` waits for DWM to commit that change and captures the rectangle with `CopyFromScreen`.
4. `WindowsOcrService` converts the image to a `SoftwareBitmap`, upscales small selections (3× up to 400 px, 2× up to 1600 px, clamped to the OCR engine's maximum), and invokes `Windows.Media.Ocr` off the UI thread.
5. Recognized lines are whitespace-normalized, then formatted according to the line-break setting.
6. `ClipboardService` writes `CF_UNICODETEXT` with up to ten attempts at 100 ms intervals. A toast or the preview window reports the result.
7. When history is enabled and not paused, `HistoryLogOperation` saves the text and optional image in the background. The **Don't log this** toast action removes the entry after the save completes.

`IOcrService`, `IScreenCapture`, and `IClipboardService` are the provider seams; the current OCR provider is Windows OCR only.

## Limitations

- OCR quality depends on the Windows OCR engine, installed language packs, and image clarity. Very small, low-contrast, stylized, or vertical text may recognize poorly.
- **Last result** and the preview thumbnail live in memory only and are not saved across restarts. History entries persist until deleted.
- A hotkey registered by an elevated process may be unavailable to MegaSnip; the hook fallback and tray menu remain available.
- Screen capture of elevated windows and UAC secure-desktop prompts is restricted by Windows.
- The toast appears at the bottom-right of the primary display's work area.
- History migrations require an empty destination folder; target folders containing other files are rejected.
