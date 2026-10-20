# Changelog

All notable changes to MegaSnip are documented in this file. This project follows the structure of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
