# MegaSnip

MegaSnip is a tray-only Windows utility that turns text on your screen into plain text on your clipboard. Press a hotkey, drag a box around the text, and MegaSnip recognizes it locally with Windows OCR and copies it.

Everything runs offline. There are no cloud calls, API keys, telemetry, analytics, or network requests.

## Highlights

- Tray-only: no main window, no taskbar button.
- Configurable global hotkey, default `Ctrl+Alt+T`, plus a tray **Snip** command.
- Dimmed full-screen overlay on every monitor with a 100%-bright selection cut-out, crosshair cursor, and live pixel dimensions.
- Selection is captured in physical pixels and OCR runs after the overlay is already gone.
- Plain Unicode text is copied to the clipboard; no image is placed on the clipboard.
- Empty OCR results show **No text detected** and leave the existing clipboard untouched.
- Optional preview window with the captured thumbnail, editable text, **Copy**, **Copy & Close**, and **Re-run OCR**.
- Per-monitor DPI awareness v2, multi-monitor layouts (including negative coordinates), and a 5×5 pixel minimum selection.
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
5. A toast shows **Text copied (N chars)** with a **Preview** link. The link opens the preview window for corrections.
6. Press `Esc` or right-click at any time before releasing to cancel without copying, previewing, or changing the clipboard.

Tray menu:

| Item | Action |
| --- | --- |
| **Snip** | Starts a selection. The configured shortcut is shown beside it. |
| **Last result** | Reopens the preview for the most recent successful OCR result. Disabled until one exists. |
| **Settings** | Opens the settings window. |
| **Quit** | Exits MegaSnip. |

If OCR finds no text, a **No text detected** toast is shown and the previous clipboard contents are preserved. If the clipboard is locked by another application, MegaSnip retries for about one second before reporting a failure.

Selecting a region smaller than 5×5 pixels is treated as a cancel.

## Settings

Changes are applied when you press **Save**:

- **Hotkey** - click **Change**, then press the new combination. A shortcut needs at least one of Ctrl, Alt, Shift, or Win plus another key. `Esc` cancels the capture without closing the settings window.
- **OCR language** - **System default** uses the first OCR-capable Windows user-profile language; the other entries come from installed Windows OCR recognizers.
- **Line breaks** - preserve the recognized line breaks or join lines with single spaces.
- **Open preview automatically** - open the result window instead of the success toast.
- **Sound feedback** - play Windows system sounds for success, warning, and error outcomes.
- **Start MegaSnip with Windows** - add or remove the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MegaSnip` value.

Settings are validated before they are committed. If a new hotkey cannot be registered, or the startup setting or settings file cannot be updated, the previous values are restored and the error is shown in the window.

## Offline and privacy

- No network capability is used at runtime. Nothing is uploaded, downloaded, or telemetered.
- Screen pixels are captured only after a selection is completed and only for the selected rectangle.
- The capture is held in memory for OCR, the toast preview, and **Last result**. It is discarded when MegaSnip exits.
- Only text is written to the clipboard. No bitmap is copied.
- The only file MegaSnip writes is `%APPDATA%\MegaSnip\settings.json`; startup registration, when enabled, is a single value in the current user's registry hive.

To remove MegaSnip, quit it, turn off **Start MegaSnip with Windows** (or delete the `MegaSnip` Run value), delete the executable, and remove `%APPDATA%\MegaSnip`.

## Build and test

```powershell
dotnet build MegaSnip.sln -c Release
dotnet test MegaSnip.sln -c Release
```

The solution builds with warnings treated as errors. Tests cover clipboard retry behavior, line-break formatting, OCR post-processing, settings round-trips and corruption recovery, hotkey validation, and OCR of a fixed PNG fixture.

The OCR fixture test uses the installed Windows OCR engine with `en-US`; install that language pack if it is missing. The other tests do not touch the real clipboard, the registry, or the user's settings file.

## Publish

```powershell
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

The script publishes a self-contained, single-file executable to:

```text
dist\MegaSnip-<version>-win-x64\MegaSnip.exe
```

Use `-RuntimeIdentifier win-arm64` for Windows on Arm. No installer is produced; the executable is portable. The build is not code-signed, so Windows SmartScreen may show a warning on first launch.

## Project layout

```text
src/MegaSnip/
  App.xaml.cs                  Startup, single instance, capture/OCR/copy orchestration
  Capture/                     Screen capture through GDI
  Clipboard/                   Unicode-text clipboard writer with retry
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

`IOcrService`, `IScreenCapture`, and `IClipboardService` are the provider seams; the current OCR provider is Windows OCR only.

## Limitations

- OCR quality depends on the Windows OCR engine, installed language packs, and image clarity. Very small, low-contrast, stylized, or vertical text may recognize poorly.
- **Last result** and the preview thumbnail live in memory only and are not saved across restarts.
- A hotkey registered by an elevated process may be unavailable to MegaSnip; the hook fallback and tray menu remain available.
- Screen capture of elevated windows and UAC secure-desktop prompts is restricted by Windows.
- The toast appears at the bottom-right of the primary display's work area.
