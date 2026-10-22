# Changelog

All notable changes to MegaSnip are documented in this file. This project follows the structure of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Tray **Check for updates…** command with a manual, user-initiated GitHub Releases API check. It reports up-to-date, update-available, and unknown-version states, shows release notes, and opens the release page in the browser. Nothing is checked automatically, downloaded, or self-updated.
- Version-tag parsing and numeric comparison, including `v` prefixes and pre-release or build suffixes, with safe fallback URL handling and friendly network, timeout, and rate-limit messages.
- Tests for release parsing, version comparison, and the update dialog's success and failure states.
- README updates documenting manual update checks and the exact outbound request.
- GitHub Actions release workflow triggered by `v*` tags or a manual dispatch of an existing tag. It validates the tag against the project version, builds and tests the solution, publishes `win-x64` and `win-arm64` single-file executables, and creates or updates the GitHub release with generated notes and assets.

## [0.1.0] - 2026-10-21

### Added

- Tray-only startup with a single-instance guard, tray menu (**Snip**, **Last result**, **Settings**, **Quit**), and a configurable global hotkey (default `Ctrl+Alt+T`).
- `RegisterHotKey`-based hotkey registration with a low-level keyboard-hook fallback when the shortcut is already owned by another application.
- Per-monitor, DPI-aware selection overlay with a dimmed screen, bright cut-out, crosshair cursor, live physical-pixel dimensions, and a 5×5 pixel minimum selection.
- Esc and right-click cancellation with no clipboard or preview side effects.
- Physical-pixel screen capture with a DWM flush so the overlay never leaks into the captured image.
- Offline OCR through `Windows.Media.Ocr`, including installed-language enumeration, system-language fallback, small-region upscaling (3×/2×/1×), clamping to the engine's maximum dimension, and whitespace/line normalization. OCR runs after the overlay closes and off the UI thread.
- `IOcrService`, `IScreenCapture`, and `IClipboardService` provider seams.
- Clipboard copying of `CF_UNICODETEXT` only, with ten retries at 100 ms intervals and explicit empty-OCR handling that preserves prior clipboard contents.
- Non-activating success/warning/error toast with character count, **Preview** link, auto-dismiss, and hover pause.
- Optional local history enabled by default: a SQLite index plus one folder per snip under `%APPDATA%\MegaSnip\history\`, storing recognized text and, when enabled, the captured image and thumbnail.
- A **Don't log this** toast action that removes the just-saved entry without affecting the clipboard.
- History window with a virtualized list, search, thumbnail and text detail pane, automatically saved text edits, and Copy text, Copy image, Copy both, Re-run OCR, Open folder, Delete, Clear all, and Export actions.
- Multi-format image clipboard support for history entries: DIB and raw PNG, with alpha flattened to white for DIB and the existing retry behavior; **Ctrl+Shift+C** copies the selected image and **Ctrl+C** copies its text.
- Retention cleanup at startup and every 24 hours with configurable maximum entry count, age in days, and disk usage; `0` disables an individual limit.
- First-use history privacy notice describing what is stored, the storage folder, and that nothing leaves the PC; acknowledgement is persisted in settings.
- Tray **Pause history for 1 hour** (with **Resume history**) and **Open history folder** commands.
- History settings for saving text and images, delete confirmation, default double-click action, storage folder, and retention limits, including a validated move of the existing history to an empty folder.
- Settings schema v2 migration that adds history defaults to existing schema v1 files.
- GitHub repository link in the README and Settings footer.
- Preview/correction window with captured-image thumbnail, editable text, character count, **Copy**, **Copy & Close**, and **Re-run OCR**.
- Settings window and JSON persistence at `%APPDATA%\MegaSnip\settings.json`, covering hotkey, OCR language, line-break mode, auto-preview, sound feedback, and Windows startup.
- Hotkey capture with validation and cancel-only-on-Escape behavior, plus rollback when a new hotkey, startup registration, or settings save fails.
- Current-user startup registration through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- System-sound feedback for success, warning, and error outcomes.
- Single-file, self-contained publish script for `win-x64` and `win-arm64`.
- xUnit test suite covering clipboard retries, line-break formatting, OCR post-processing, settings round-trips and corruption handling, hotkey validation, and OCR against a fixed PNG fixture.
- Project README with usage, privacy, build, test, and publish documentation.

### Fixed

- The selection overlay now forces itself to the foreground (`AttachThreadInput`, `SetForegroundWindow`, `SetFocus`) when opened through the low-level keyboard-hook fallback, so Esc reliably cancels even when another application owned the foreground.
- Escape during hotkey capture cancels the capture only, leaving the settings window open.
- Settings validation errors are exposed to assistive technologies through a live region.

### Known limitations

- Windows OCR is the only OCR provider implemented; other providers can be added behind `IOcrService`.
- Preview images and the last result are kept in memory only.
- Sound feedback uses system sounds and is not adjustable beyond on/off.
