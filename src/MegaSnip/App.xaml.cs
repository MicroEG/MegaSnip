using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MegaSnip.Capture;
using MegaSnip.Clipboard;
using MegaSnip.History;
using MegaSnip.Infrastructure;
using MegaSnip.Ocr;
using MegaSnip.Overlay;
using MegaSnip.Preview;
using MegaSnip.Settings;
using MegaSnip.Toast;
using MegaSnip.Tray;

namespace MegaSnip;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "MegaSnip.SingleInstance";

    private static readonly TimeSpan HistoryPruneInterval = TimeSpan.FromHours(24);
    private static readonly string AppVersion =
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

    private readonly IScreenCapture _screenCapture = new ScreenCapture();
    private readonly IOcrService _ocrService = new WindowsOcrService();
    private readonly IClipboardService _clipboardService = new ClipboardService();
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private Mutex? _singleInstanceMutex;
    private Bitmap? _lastCapture;
    private string? _lastText;
    private TrayController? _trayController;
    private ToastController? _toastController;
    private HotkeyManager? _hotkeyManager;
    private SelectionOverlayController? _overlayController;
    private PreviewWindow? _previewWindow;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private HistoryService? _historyService;
    private HistoryLogOperation? _activeHistoryLog;
    private DispatcherTimer? _historyPruneTimer;
    private CancellationTokenSource? _ocrCancellation;
    private Task _ocrWarmUpTask = Task.CompletedTask;
    private int _captureGeneration;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        _settings = _settingsStore.Load();
        ValidateOcrLanguage();

        _toastController = new ToastController();
        _toastController.PreviewRequested += OnPreviewRequested;
        _toastController.DiscardHistoryRequested += OnDiscardHistoryRequested;

        _trayController = new TrayController();
        _trayController.SnipRequested += OnSnipRequested;
        _trayController.LastResultRequested += OnLastResultRequested;
        _trayController.HistoryRequested += OnHistoryRequested;
        _trayController.SettingsRequested += OnSettingsRequested;
        _trayController.ExitRequested += OnExitRequested;
        _trayController.SetHotkeyDisplay(_settings.Hotkey.ToString());
        _trayController.SetLastResultAvailable(false);

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.Pressed += OnHotkeyPressed;

        if (!_hotkeyManager.TryRegister(_settings.Hotkey, out string? error))
        {
            _trayController.ShowBalloon(
                "Hotkey unavailable",
                $"{error} Open Settings from the tray menu to choose another shortcut.");
        }

        SyncStartupRegistration();
        InitializeHistory();
        _ocrWarmUpTask = WarmUpOcrAsync(_settings.OcrLanguageTag);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        _ocrCancellation?.Cancel();
        CloseOverlay();
        ClosePreview();
        CloseHistory();
        CloseSettings();

        if (_historyPruneTimer is not null)
        {
            _historyPruneTimer.Stop();
            _historyPruneTimer.Tick -= OnHistoryPruneTimerTick;
            _historyPruneTimer = null;
        }

        _activeHistoryLog?.Completion.Wait(TimeSpan.FromSeconds(2));

        if (_toastController is not null)
        {
            _toastController.PreviewRequested -= OnPreviewRequested;
            _toastController.DiscardHistoryRequested -= OnDiscardHistoryRequested;
            _toastController.Dispose();
            _toastController = null;
        }

        _hotkeyManager?.Dispose();
        _hotkeyManager = null;
        _trayController?.Dispose();
        _trayController = null;

        _lastCapture?.Dispose();
        _lastCapture = null;

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) => BeginSnip();

    private void OnSnipRequested(object? sender, EventArgs e) => BeginSnip();

    private void OnLastResultRequested(object? sender, EventArgs e) => OpenPreview();

    private void OnHistoryRequested(object? sender, EventArgs e) => OpenHistory();

    private void OnSettingsRequested(object? sender, EventArgs e) => OpenSettings();

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    private void BeginSnip()
    {
        if (_isShuttingDown || _overlayController is not null)
        {
            return;
        }

        _toastController?.Dismiss();
        _ocrCancellation?.Cancel();

        try
        {
            var overlay = new SelectionOverlayController();
            overlay.Closed += OnOverlayClosed;
            overlay.SelectionCompleted += OnSelectionCompleted;
            _overlayController = overlay;
            overlay.Show();
        }
        catch (Exception ex)
        {
            CloseOverlay();
            PlayFeedback(ToastKind.Error);
            _toastController?.ShowError("MegaSnip could not start a snip", ex.Message);
        }
    }

    private async void OnSelectionCompleted(object? sender, SelectionCompletedEventArgs e)
    {
        Bitmap capture;

        try
        {
            capture = _screenCapture.Capture(e.Bounds);
        }
        catch (Exception ex)
        {
            PlayFeedback(ToastKind.Error);
            _toastController?.ShowError("MegaSnip could not capture the screen", ex.Message);
            return;
        }

        ReplaceLastCapture(capture);
        _lastText = null;
        _trayController?.SetLastResultAvailable(false);
        _ocrCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _ocrCancellation = cancellation;
        int generation = ++_captureGeneration;
        string monitorId = System.Windows.Forms.Screen.FromRectangle(e.Bounds).DeviceName;

        await Task.Yield();

        await RecognizeAndCopyAsync(capture, monitorId, generation, cancellation);
    }

    private async Task RecognizeAndCopyAsync(
        Bitmap capture,
        string monitorId,
        int generation,
        CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;

        try
        {
            await _ocrWarmUpTask.ConfigureAwait(true);

            if (!IsCurrentResult(generation))
            {
                return;
            }

            using Bitmap ocrInput = (Bitmap)capture.Clone();
            string? languageTag = _settings.OcrLanguageTag;

            string text = await Task.Run(
                    () => _ocrService.RecognizeAsync(ocrInput, languageTag, token),
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (!IsCurrentResult(generation))
            {
                return;
            }

            _lastText = text;
            _trayController?.SetLastResultAvailable(text.Length > 0);

            if (text.Length == 0)
            {
                PlayFeedback(ToastKind.Warning);
                _toastController?.ShowNoText();
                return;
            }

            bool historyLogStarted = TryBeginHistoryLog(capture, monitorId, text);
            string clipboardText = ClipboardTextFormatter.Format(text, _settings.LineBreakMode);
            bool copied;

            try
            {
                copied = await _clipboardService
                    .TrySetTextAsync(clipboardText, token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsCurrentResult(generation))
                {
                    PlayFeedback(ToastKind.Error);
                    _toastController?.ShowError(
                        "MegaSnip could not copy the text",
                        ex.Message);
                }

                return;
            }

            if (!IsCurrentResult(generation))
            {
                return;
            }

            if (copied)
            {
                PlayFeedback(ToastKind.Success);

                if (_settings.AutoPreview)
                {
                    OpenPreview();
                }
                else
                {
                    _toastController?.ShowCopied(clipboardText.Length, historyLogStarted);
                }
            }
            else
            {
                PlayFeedback(ToastKind.Warning);
                _toastController?.ShowCopyFailed(historyLogStarted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentResult(generation))
            {
                PlayFeedback(ToastKind.Error);
                _toastController?.ShowError("OCR failed", ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_ocrCancellation, cancellation))
            {
                _ocrCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void OnPreviewRequested(object? sender, EventArgs e) => OpenPreview();

    private async void OnDiscardHistoryRequested(object? sender, EventArgs e)
    {
        HistoryLogOperation? operation = _activeHistoryLog;
        if (operation is null)
        {
            return;
        }

        try
        {
            await operation.DiscardAsync();
        }
        catch (Exception ex)
        {
            ShowHistoryError("History could not be updated", ex.Message);
        }

        if (ReferenceEquals(_activeHistoryLog, operation))
        {
            _activeHistoryLog = null;
        }
    }

    private void OpenPreview()
    {
        if (_isShuttingDown ||
            _lastCapture is null ||
            string.IsNullOrEmpty(_lastText))
        {
            return;
        }

        PreviewWindow? preview = null;

        try
        {
            ClosePreview();
            _toastController?.Dismiss();

            Bitmap snapshot = (Bitmap)_lastCapture.Clone();
            preview = new PreviewWindow(
                snapshot,
                _lastText,
                _settings.OcrLanguageTag,
                _settings.LineBreakMode,
                _ocrService,
                _clipboardService);
            preview.Closed += OnPreviewClosed;
            _previewWindow = preview;
            preview.Show();
            preview.Activate();
        }
        catch (Exception ex)
        {
            if (preview is not null)
            {
                preview.Closed -= OnPreviewClosed;

                if (ReferenceEquals(_previewWindow, preview))
                {
                    _previewWindow = null;
                }

                preview.Close();
            }

            PlayFeedback(ToastKind.Error);
            _toastController?.ShowError("MegaSnip could not open the preview", ex.Message);
        }
    }

    private void OpenHistory()
    {
        if (_isShuttingDown)
        {
            return;
        }

        HistoryService? service = _historyService;
        if (service is null)
        {
            ShowHistoryError(
                "History is unavailable",
                "MegaSnip could not open its history storage.");
            return;
        }

        if (_historyWindow is not null)
        {
            _historyWindow.Activate();
            return;
        }

        HistoryWindow? window = null;

        try
        {
            _toastController?.Dismiss();
            window = new HistoryWindow(
                service,
                _ocrService,
                _clipboardService,
                _settings.OcrLanguageTag,
                _settings.LineBreakMode,
                _settings.AskBeforeDelete,
                _settings.Hotkey.ToString(),
                SetAskBeforeDelete);
            window.Closed += OnHistoryClosed;
            _historyWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            if (window is not null)
            {
                window.Closed -= OnHistoryClosed;

                if (ReferenceEquals(_historyWindow, window))
                {
                    _historyWindow = null;
                }

                window.Close();
            }

            PlayFeedback(ToastKind.Error);
            ShowHistoryError("MegaSnip could not open history", ex.Message);
        }
    }

    private void OpenSettings()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        SettingsWindow? window = null;

        try
        {
            IReadOnlyList<OcrLanguage> languages;

            try
            {
                languages = _ocrService.AvailableLanguages;
            }
            catch (Exception)
            {
                languages = Array.Empty<OcrLanguage>();
            }

            _toastController?.Dismiss();
            window = new SettingsWindow(
                _settings,
                languages,
                ApplySettings,
                OnHotkeyCaptureActiveChanged);
            window.Closed += OnSettingsClosed;
            _settingsWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            if (window is not null)
            {
                window.Closed -= OnSettingsClosed;

                if (ReferenceEquals(_settingsWindow, window))
                {
                    _settingsWindow = null;
                }

                window.Close();
            }

            PlayFeedback(ToastKind.Error);
            _toastController?.ShowError("MegaSnip could not open settings", ex.Message);
        }
    }

    private void OnHotkeyCaptureActiveChanged(bool capturing)
    {
        if (_isShuttingDown || _hotkeyManager is null)
        {
            return;
        }

        if (capturing)
        {
            _hotkeyManager.Suspend();
            return;
        }

        _hotkeyManager.TryRegister(_settings.Hotkey, out _);
    }

    private string? ApplySettings(AppSettings candidate)
    {
        if (_isShuttingDown)
        {
            return "MegaSnip is closing.";
        }

        if (!candidate.Hotkey.IsValid())
        {
            return "Choose a hotkey with at least one modifier key and another key.";
        }

        if (!Enum.IsDefined(typeof(LineBreakMode), candidate.LineBreakMode))
        {
            return "Choose how copied text should handle line breaks.";
        }

        HotkeyGesture previousHotkey = _settings.Hotkey;
        string? previousLanguageTag = _settings.OcrLanguageTag;
        bool previousStartWithWindows = _settings.StartWithWindows;
        bool hotkeyChanged = candidate.Hotkey != previousHotkey;
        bool startupChanged = candidate.StartWithWindows != previousStartWithWindows;

        if (hotkeyChanged &&
            _hotkeyManager is not null &&
            !_hotkeyManager.TryRegister(candidate.Hotkey, out string? hotkeyError))
        {
            _hotkeyManager.TryRegister(previousHotkey, out _);
            return hotkeyError ?? "The hotkey could not be registered.";
        }

        try
        {
            if (startupChanged)
            {
                StartupRegistration.SetEnabled(candidate.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            if (hotkeyChanged)
            {
                _hotkeyManager?.TryRegister(previousHotkey, out _);
            }

            return "Windows startup could not be updated: " + ex.Message;
        }

        try
        {
            _settingsStore.Save(candidate);
        }
        catch (Exception ex)
        {
            if (hotkeyChanged)
            {
                _hotkeyManager?.TryRegister(previousHotkey, out _);
            }

            if (startupChanged)
            {
                try
                {
                    StartupRegistration.SetEnabled(previousStartWithWindows);
                }
                catch (Exception)
                {
                }
            }

            return "Settings could not be saved: " + ex.Message;
        }

        _settings = candidate;
        _trayController?.SetHotkeyDisplay(candidate.Hotkey.ToString());
        _previewWindow?.ApplySettings(candidate.OcrLanguageTag, candidate.LineBreakMode);
        _historyWindow?.ApplySettings(candidate.OcrLanguageTag, candidate.LineBreakMode);

        if (!string.Equals(
                candidate.OcrLanguageTag,
                previousLanguageTag,
                StringComparison.OrdinalIgnoreCase))
        {
            _ocrWarmUpTask = WarmUpOcrAsync(candidate.OcrLanguageTag);
        }

        return null;
    }

    private string? SetAskBeforeDelete(bool ask)
    {
        if (_isShuttingDown)
        {
            return "MegaSnip is closing.";
        }

        if (_settings.AskBeforeDelete == ask)
        {
            return null;
        }

        AppSettings candidate = _settings.Clone();
        candidate.AskBeforeDelete = ask;

        try
        {
            _settingsStore.Save(candidate);
        }
        catch (Exception ex)
        {
            return "Settings could not be saved: " + ex.Message;
        }

        _settings = candidate;
        return null;
    }

    private void InitializeHistory()
    {
        try
        {
            string directory = HistoryPaths.Resolve(_settings.HistoryDirectory);
            _historyService = new HistoryService(new HistoryRepository(directory));
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            ShowHistoryError("History is unavailable", ex.Message);
            return;
        }

        _ = InitializeHistoryAsync();
    }

    private async Task InitializeHistoryAsync()
    {
        HistoryService? service = _historyService;
        if (service is null)
        {
            return;
        }

        try
        {
            await service.InitializeAsync();
        }
        catch (Exception ex)
        {
            if (!_isShuttingDown)
            {
                ShowHistoryError("History is unavailable", ex.Message);
            }

            return;
        }

        if (_isShuttingDown)
        {
            return;
        }

        StartHistoryPruneTimer();
        await PruneHistoryAsync();
    }

    private void StartHistoryPruneTimer()
    {
        if (_historyPruneTimer is not null)
        {
            return;
        }

        _historyPruneTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = HistoryPruneInterval,
        };
        _historyPruneTimer.Tick += OnHistoryPruneTimerTick;
        _historyPruneTimer.Start();
    }

    private async void OnHistoryPruneTimerTick(object? sender, EventArgs e)
    {
        await PruneHistoryAsync();
    }

    private async Task PruneHistoryAsync()
    {
        HistoryService? service = _historyService;
        if (service is null || _isShuttingDown)
        {
            return;
        }

        try
        {
            await service.PruneAsync(
                _settings.HistoryRetention,
                _historyWindow?.SelectedEntryId);
        }
        catch (Exception ex)
        {
            if (!_isShuttingDown)
            {
                ShowHistoryError("History cleanup failed", ex.Message);
            }
        }
    }

    private bool TryBeginHistoryLog(Bitmap capture, string monitorId, string text)
    {
        HistoryService? service = _historyService;
        if (service is null || !_settings.SaveHistory || text.Length == 0)
        {
            return false;
        }

        Bitmap? image;

        try
        {
            image = _settings.SaveHistoryImages ? (Bitmap)capture.Clone() : null;
        }
        catch (Exception ex)
        {
            ShowHistoryError("History could not be saved", ex.Message);
            return false;
        }

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var request = new HistorySaveRequest
        {
            Id = HistoryEntryId.Create(createdAt),
            CreatedAtUtc = createdAt,
            Text = text,
            Width = capture.Width,
            Height = capture.Height,
            MonitorId = monitorId,
            OcrLanguage = _settings.OcrLanguageTag,
            AppVersion = AppVersion,
            Image = image,
            SaveImage = image is not null,
        };

        var operation = new HistoryLogOperation(service, request);
        operation.Failed += OnHistoryLogFailed;
        _activeHistoryLog = operation;
        operation.Start();
        return true;
    }

    private void OnHistoryLogFailed(object? sender, HistoryStorageException e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (!_isShuttingDown)
                {
                    ShowHistoryError("History could not be updated", e.Message);
                }
            }));
    }

    private void ShowHistoryError(string title, string message) =>
        _trayController?.ShowBalloon(title, message);

    private void PlayFeedback(ToastKind kind)
    {
        if (_settings.SoundFeedback)
        {
            FeedbackSounds.Play(kind);
        }
    }

    private void SyncStartupRegistration()
    {
        try
        {
            StartupRegistration.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.Security.SecurityException)
        {
        }
    }

    private void ValidateOcrLanguage()
    {
        if (string.IsNullOrEmpty(_settings.OcrLanguageTag))
        {
            return;
        }

        try
        {
            foreach (OcrLanguage language in _ocrService.AvailableLanguages)
            {
                if (string.Equals(
                        language.Tag,
                        _settings.OcrLanguageTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _settings.OcrLanguageTag = null;
        }
        catch (Exception)
        {
        }
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: false,
                SingleInstanceMutexName,
                out bool createdNew);
            return createdNew;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private async Task WarmUpOcrAsync(string? languageTag)
    {
        try
        {
            await Task.Run(() => _ocrService.WarmUpAsync(languageTag))
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private bool IsCurrentResult(int generation) =>
        !_isShuttingDown &&
        generation == _captureGeneration &&
        _overlayController is null;

    private void ReplaceLastCapture(Bitmap capture)
    {
        Bitmap? previous = _lastCapture;
        _lastCapture = capture;
        previous?.Dispose();
    }

    private void OnOverlayClosed(object? sender, EventArgs e) => CloseOverlay();

    private void OnPreviewClosed(object? sender, EventArgs e)
    {
        if (sender is not PreviewWindow preview)
        {
            return;
        }

        preview.Closed -= OnPreviewClosed;

        if (ReferenceEquals(_previewWindow, preview))
        {
            _previewWindow = null;
        }
    }

    private void OnSettingsClosed(object? sender, EventArgs e)
    {
        if (sender is not SettingsWindow window)
        {
            return;
        }

        window.Closed -= OnSettingsClosed;

        if (ReferenceEquals(_settingsWindow, window))
        {
            _settingsWindow = null;
        }
    }

    private void OnHistoryClosed(object? sender, EventArgs e)
    {
        if (sender is not HistoryWindow window)
        {
            return;
        }

        window.Closed -= OnHistoryClosed;

        if (ReferenceEquals(_historyWindow, window))
        {
            _historyWindow = null;
        }
    }

    private void CloseOverlay()
    {
        SelectionOverlayController? overlay = _overlayController;
        if (overlay is null)
        {
            return;
        }

        _overlayController = null;
        overlay.Closed -= OnOverlayClosed;
        overlay.SelectionCompleted -= OnSelectionCompleted;
        overlay.Dispose();
    }

    private void ClosePreview()
    {
        PreviewWindow? preview = _previewWindow;
        if (preview is null)
        {
            return;
        }

        _previewWindow = null;
        preview.Closed -= OnPreviewClosed;
        preview.Close();
    }

    private void CloseHistory()
    {
        HistoryWindow? window = _historyWindow;
        if (window is null)
        {
            return;
        }

        _historyWindow = null;
        window.Closed -= OnHistoryClosed;
        window.Close();
    }

    private void CloseSettings()
    {
        SettingsWindow? window = _settingsWindow;
        if (window is null)
        {
            return;
        }

        _settingsWindow = null;
        window.Closed -= OnSettingsClosed;
        window.Close();
    }
}
