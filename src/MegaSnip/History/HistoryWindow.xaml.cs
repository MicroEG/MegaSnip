using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MegaSnip.Clipboard;
using MegaSnip.Infrastructure;
using MegaSnip.Ocr;
using MegaSnip.Settings;

namespace MegaSnip.History;

internal partial class HistoryWindow : Window
{
    private static readonly string AppVersion =
        typeof(HistoryWindow).Assembly.GetName().Version?.ToString() ?? "unknown";

    private readonly HistoryService _historyService;
    private readonly IOcrService _ocrService;
    private readonly IClipboardService _clipboardService;
    private readonly Func<bool, string?> _setAskBeforeDelete;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HistoryViewModel _viewModel;

    private HistoryListItem? _displayedItem;
    private LineBreakMode _lineBreakMode;
    private string? _languageTag;
    private bool _askBeforeDelete;
    private bool _suppressEditorChange;
    private bool _syncingSelection;
    private bool _selectionChangeInProgress;
    private bool _operationInProgress;
    private bool _reloadPending;
    private bool _isReloading;
    private bool _closeInProgress;
    private bool _closeReady;
    private bool _cleanedUp;
    private int _searchGeneration;

    internal HistoryWindow(
        HistoryService historyService,
        IOcrService ocrService,
        IClipboardService clipboardService,
        string? languageTag,
        LineBreakMode lineBreakMode,
        bool askBeforeDelete,
        string hotkeyDisplay,
        Func<bool, string?> setAskBeforeDelete)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(ocrService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(hotkeyDisplay);
        ArgumentNullException.ThrowIfNull(setAskBeforeDelete);

        _historyService = historyService;
        _ocrService = ocrService;
        _clipboardService = clipboardService;
        _languageTag = languageTag;
        _lineBreakMode = lineBreakMode;
        _askBeforeDelete = askBeforeDelete;
        _setAskBeforeDelete = setAskBeforeDelete;
        _viewModel = new HistoryViewModel(historyService, hotkeyDisplay);

        InitializeComponent();

        DataContext = _viewModel;
        _viewModel.SelectedItemChanged += OnViewModelSelectedItemChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _historyService.Changed += OnHistoryChanged;

        Loaded += OnLoaded;
    }

    internal string? SelectedEntryId => _viewModel.SelectedEntryId;

    internal void ApplySettings(string? languageTag, LineBreakMode lineBreakMode)
    {
        _languageTag = languageTag;
        _lineBreakMode = lineBreakMode;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeReady && _viewModel.HasPendingEditorChanges)
        {
            e.Cancel = true;

            if (!_closeInProgress)
            {
                _closeInProgress = true;
                _ = SaveBeforeCloseAsync();
            }

            return;
        }

        Cleanup();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await _viewModel.LoadAsync();
            SyncSelectionToList();
            HistoryList.Focus();
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("Could not load history: " + ex.Message, StatusKind.Error);
        }
    }

    private async Task SaveBeforeCloseAsync()
    {
        try
        {
            await _viewModel.FlushPendingSaveAsync();
        }
        catch (Exception)
        {
        }

        _closeReady = true;
        Close();
    }

    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _lifetime.Cancel();
        _historyService.Changed -= OnHistoryChanged;
        _viewModel.SelectedItemChanged -= OnViewModelSelectedItemChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.Entries))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(SyncSelectionToList));
        }
    }

    private void OnViewModelSelectedItemChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(SyncSelectionToList));
    }

    private void SyncSelectionToList()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            HistoryList.SelectedItem = _viewModel.SelectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }

        ShowSelectedItem();
        UpdateActionButtons();
    }

    private void ShowSelectedItem()
    {
        HistoryListItem? item = _viewModel.SelectedItem;

        if (!ReferenceEquals(_displayedItem, item))
        {
            _displayedItem = item;
            _suppressEditorChange = true;

            try
            {
                TextEditor.Text = item?.Text ?? string.Empty;
                TextEditor.CaretIndex = 0;

                if (TextEditor.Text.Length > 0)
                {
                    TextEditor.ScrollToHome();
                }
            }
            finally
            {
                _suppressEditorChange = false;
            }
        }

        UpdateEditorCharacterCount();
        UpdateActionButtons();
    }

    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtons();

        if (_syncingSelection || _selectionChangeInProgress)
        {
            return;
        }

        if (HistoryList.SelectedItem is not HistoryListItem item)
        {
            return;
        }

        if (ReferenceEquals(item, _viewModel.SelectedItem))
        {
            ShowSelectedItem();
            return;
        }

        _selectionChangeInProgress = true;

        try
        {
            await _viewModel.FlushPendingSaveAsync();
            _viewModel.SelectItem(item);
        }
        finally
        {
            _selectionChangeInProgress = false;
            UpdateActionButtons();
        }
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        int generation = ++_searchGeneration;

        try
        {
            await _viewModel.FlushPendingSaveAsync();
        }
        catch (Exception)
        {
        }

        if (generation != _searchGeneration || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _viewModel.SearchText = SearchBox.Text;
        UpdateActionButtons();
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditorCharacterCount();

        if (_suppressEditorChange || _viewModel.SelectedItem is null)
        {
            return;
        }

        _viewModel.NoteEditorText(TextEditor.Text);
    }

    private async void OnEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await FlushEditorAsync();
    }

    private async Task FlushEditorAsync()
    {
        try
        {
            await _viewModel.FlushPendingSaveAsync();
        }
        catch (Exception)
        {
        }

        if (_reloadPending && !_viewModel.HasPendingEditorChanges)
        {
            _reloadPending = false;
            await ReloadAsync();
        }
    }

    private void UpdateEditorCharacterCount()
    {
        int count = TextEditor.Text.Length;
        EditorCharacterCountText.Text = count == 1
            ? "1 character"
            : count.ToString("N0") + " characters";
    }

    private void UpdateActionButtons()
    {
        bool busy = _operationInProgress;
        bool hasSelection = _viewModel.SelectedItem is not null;
        bool hasImage = _viewModel.SelectedItem?.HasImage == true;
        int selectedCount = HistoryList.SelectedItems.Count;
        bool hasHistory = _viewModel.TotalCount > 0;

        ExportButton.IsEnabled = !busy && hasHistory;
        DeleteSelectedButton.IsEnabled = !busy && selectedCount > 0;
        DeleteSelectedButton.Content = selectedCount > 1
            ? $"Delete {selectedCount} selected"
            : "Delete selected";
        ClearAllButton.IsEnabled = !busy && hasHistory;
        CopyTextButton.IsEnabled = !busy && hasSelection;
        CopyImageButton.IsEnabled = !busy && hasImage;
        CopyBothButton.IsEnabled = !busy && hasImage;
        RerunButton.IsEnabled = !busy && hasImage;
        OpenFolderButton.IsEnabled = !busy && hasSelection;
        DeleteButton.IsEnabled = !busy && hasSelection;
        TextEditor.IsReadOnly = busy;
        SearchBox.IsEnabled = !busy;
    }

    private void SetBusy(bool busy)
    {
        _operationInProgress = busy;
        UpdateActionButtons();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = CopySelectedImageAsync();
        }
        else if (modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
        {
            e.Handled = true;
            _ = CopySelectedTextAsync();
        }
    }

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            _ = DeleteSelectedAsync();
        }
    }

    private async void OnCopyTextClicked(object sender, RoutedEventArgs e)
    {
        await CopySelectedTextAsync();
    }

    private async Task CopySelectedTextAsync()
    {
        if (_operationInProgress || _viewModel.SelectedItem is null)
        {
            return;
        }

        string text = TextEditor.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            _viewModel.SetStatus("There is no text to copy.", StatusKind.Error);
            return;
        }

        SetBusy(true);
        _viewModel.SetStatus("Copying text…", StatusKind.Info);

        try
        {
            string formatted = ClipboardTextFormatter.Format(text, _lineBreakMode);
            bool copied = await _clipboardService.TrySetTextAsync(formatted, _lifetime.Token);

            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            _viewModel.SetStatus(
                copied
                    ? _lineBreakMode == LineBreakMode.JoinWithSpaces
                        ? "Copied as a single line."
                        : "Copied to the clipboard."
                    : "Could not copy — the clipboard is busy.",
                copied ? StatusKind.Success : StatusKind.Error);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _viewModel.SetStatus("Could not copy: " + ex.Message, StatusKind.Error);
            }
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private async void OnCopyImageClicked(object sender, RoutedEventArgs e)
    {
        await CopySelectedImageAsync();
    }

    private async Task CopySelectedImageAsync()
    {
        if (_operationInProgress || _viewModel.SelectedItem is null)
        {
            return;
        }

        string? imagePath = GetSelectedImagePath();

        if (imagePath is null)
        {
            return;
        }

        SetBusy(true);
        _viewModel.SetStatus("Copying image…", StatusKind.Info);

        try
        {
            bool copied = await _clipboardService.TrySetImageAsync(imagePath, _lifetime.Token);

            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            _viewModel.SetStatus(
                copied
                    ? "Image copied to the clipboard."
                    : "Could not copy — the clipboard is busy.",
                copied ? StatusKind.Success : StatusKind.Error);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _viewModel.SetStatus("Could not copy image: " + ex.Message, StatusKind.Error);
            }
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private async void OnCopyBothClicked(object sender, RoutedEventArgs e)
    {
        await CopySelectedTextAndImageAsync();
    }

    private async Task CopySelectedTextAndImageAsync()
    {
        if (_operationInProgress || _viewModel.SelectedItem is null)
        {
            return;
        }

        string? imagePath = GetSelectedImagePath();

        if (imagePath is null)
        {
            return;
        }

        string text = TextEditor.Text;
        bool hasText = !string.IsNullOrWhiteSpace(text);

        SetBusy(true);
        _viewModel.SetStatus(
            hasText ? "Copying text and image…" : "Copying image…",
            StatusKind.Info);

        try
        {
            bool copied = hasText
                ? await _clipboardService.TrySetTextAndImageAsync(
                    ClipboardTextFormatter.Format(text, _lineBreakMode),
                    imagePath,
                    _lifetime.Token)
                : await _clipboardService.TrySetImageAsync(imagePath, _lifetime.Token);

            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (!copied)
            {
                _viewModel.SetStatus(
                    "Could not copy — the clipboard is busy.",
                    StatusKind.Error);
                return;
            }

            _viewModel.SetStatus(
                hasText
                    ? "Text and image copied to the clipboard."
                    : "Image copied. This snip has no text.",
                StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _viewModel.SetStatus(
                    "Could not copy text and image: " + ex.Message,
                    StatusKind.Error);
            }
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private string? GetSelectedImagePath()
    {
        HistoryListItem? item = _viewModel.SelectedItem;
        string? imagePath = item?.Entry.ImagePath;

        if (item?.HasImage != true || imagePath is null)
        {
            _viewModel.SetStatus("This snip has no saved image.", StatusKind.Error);
            return null;
        }

        if (!File.Exists(imagePath))
        {
            _viewModel.SetStatus("The saved image is missing.", StatusKind.Error);
            return null;
        }

        return imagePath;
    }

    private async void OnRerunClicked(object sender, RoutedEventArgs e)
    {
        await RerunOcrAsync();
    }

    private async Task RerunOcrAsync()
    {
        HistoryListItem? item = _viewModel.SelectedItem;
        string? imagePath = item?.Entry.ImagePath;

        if (_operationInProgress || item is null || imagePath is null)
        {
            return;
        }

        if (!File.Exists(imagePath))
        {
            _viewModel.SetStatus("The saved image is missing.", StatusKind.Error);
            return;
        }

        SetBusy(true);
        _viewModel.SetStatus("Running OCR…", StatusKind.Info);

        try
        {
            string text = await Task.Run(
                async () =>
                {
                    using var bitmap = new System.Drawing.Bitmap(imagePath);
                    return await _ocrService
                        .RecognizeAsync(bitmap, _languageTag, _lifetime.Token)
                        .ConfigureAwait(false);
                },
                CancellationToken.None);

            if (_lifetime.IsCancellationRequested ||
                !ReferenceEquals(_viewModel.SelectedItem, item))
            {
                return;
            }

            TextEditor.Text = text;
            _viewModel.SetStatus(
                text.Length == 0
                    ? "OCR found no text. The saved text was cleared."
                    : "OCR complete. The updated text saves automatically.",
                text.Length == 0 ? StatusKind.Error : StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _viewModel.SetStatus("OCR failed: " + ex.Message, StatusKind.Error);
            }
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        HistoryListItem? item = _viewModel.SelectedItem;

        if (item is null)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(item.Entry.EntryDirectory))
            {
                _viewModel.SetStatus("The snip folder no longer exists.", StatusKind.Error);
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + item.Entry.EntryDirectory + "\"",
                    UseShellExecute = true,
                });
        }
        catch (Exception ex) when (
            ex is Win32Exception
                or IOException
                or InvalidOperationException
                or UnauthorizedAccessException)
        {
            _viewModel.SetStatus("Could not open the folder: " + ex.Message, StatusKind.Error);
        }
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is { } item)
        {
            _ = DeleteItemsAsync([item]);
        }
    }

    private async void OnDeleteSelectedClicked(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        List<HistoryListItem> selected = HistoryList.SelectedItems
            .OfType<HistoryListItem>()
            .ToList();

        if (selected.Count == 0 && _viewModel.SelectedItem is { } current)
        {
            selected.Add(current);
        }

        await DeleteItemsAsync(selected);
    }

    private async Task DeleteItemsAsync(IReadOnlyList<HistoryListItem> items)
    {
        if (_operationInProgress || items.Count == 0)
        {
            return;
        }

        if (!ConfirmDelete(items.Count))
        {
            return;
        }

        SetBusy(true);
        _viewModel.SetStatus("Deleting…", StatusKind.Info);

        try
        {
            await _viewModel.FlushPendingSaveAsync();
            HistoryDeleteResult result = await _historyService.DeleteAsync(
                items.Select(item => item.Id));
            await ReloadAsync();

            if (result.Failures.Count > 0)
            {
                _viewModel.SetStatus(
                    $"Deleted {result.DeletedIds.Count:N0} of {items.Count:N0} snips. " +
                    result.Failures[0].Message,
                    StatusKind.Error);
            }
            else
            {
                _viewModel.SetStatus(
                    result.DeletedIds.Count == 1
                        ? "Snip deleted."
                        : $"Deleted {result.DeletedIds.Count:N0} snips.",
                    StatusKind.Success);
            }
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("Could not delete history: " + ex.Message, StatusKind.Error);
            await ReloadAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool ConfirmDelete(int count)
    {
        if (!_askBeforeDelete)
        {
            return true;
        }

        string title = count == 1 ? "Delete this snip?" : $"Delete {count:N0} snips?";
        string message =
            "This permanently removes the selected text and saved images from this PC.";

        var dialog = new HistoryConfirmDialog(
            title,
            message,
            count == 1 ? "Delete" : "Delete all selected",
            showDontAskAgain: true)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.DontAskAgain)
        {
            string? error = _setAskBeforeDelete(false);

            if (error is null)
            {
                _askBeforeDelete = false;
            }
            else
            {
                _viewModel.SetStatus(error, StatusKind.Error);
            }
        }

        return true;
    }

    private async void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        await ClearAllAsync();
    }

    private async Task ClearAllAsync()
    {
        if (_operationInProgress)
        {
            return;
        }

        int count = _viewModel.TotalCount;

        if (count == 0)
        {
            return;
        }

        var dialog = new HistoryConfirmDialog(
            "Clear all history?",
            $"This permanently deletes all {count:N0} snips, including their text and images, from this PC.",
            "Clear all history",
            requiredPhrase: "DELETE")
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetBusy(true);
        _viewModel.SetStatus("Clearing history…", StatusKind.Info);

        try
        {
            await _viewModel.FlushPendingSaveAsync();
            HistoryDeleteResult result = await _historyService.DeleteAllAsync();
            await ReloadAsync();

            _viewModel.SetStatus(
                result.Failures.Count > 0
                    ? $"Removed {result.DeletedIds.Count:N0} of {count:N0} snips. " +
                      result.Failures[0].Message
                    : "History cleared.",
                result.Failures.Count > 0 ? StatusKind.Error : StatusKind.Success);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("Could not clear history: " + ex.Message, StatusKind.Error);
            await ReloadAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        await ExportAsync();
    }

    private async Task ExportAsync()
    {
        if (_operationInProgress || _viewModel.TotalCount == 0)
        {
            return;
        }

        try
        {
            await _viewModel.FlushPendingSaveAsync();
        }
        catch (Exception)
        {
        }

        List<HistoryListItem> selected = HistoryList.SelectedItems
            .OfType<HistoryListItem>()
            .ToList();
        IReadOnlyList<HistoryListItem> items = selected.Count > 0
            ? selected
            : _viewModel.AllItems;

        if (items.Count == 0)
        {
            return;
        }

        string destination;

        using (var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where to save the history export",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        })
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(documents))
            {
                dialog.SelectedPath = documents;
            }

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
                string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            destination = dialog.SelectedPath;
        }

        SetBusy(true);
        _viewModel.SetStatus("Exporting history…", StatusKind.Info);

        try
        {
            var exportItems = items
                .Select(item => new HistoryExportItem(
                    item.Id,
                    item.Entry.CreatedAtUtc,
                    item.Text,
                    item.HasImage ? item.Entry.ImagePath : null,
                    item.Entry.Width,
                    item.Entry.Height))
                .ToList();

            HistoryExportResult result = await Task.Run(
                () => HistoryExportService.Export(
                    exportItems,
                    destination,
                    DateTimeOffset.Now,
                    AppVersion));

            string message =
                (result.ExportedCount == 1
                    ? "Exported 1 snip"
                    : $"Exported {result.ExportedCount:N0} snips") +
                " to " + result.Directory + ".";

            if (result.Warnings.Count > 0)
            {
                message += " " + result.Warnings[0];
            }

            _viewModel.SetStatus(
                message,
                result.Warnings.Count > 0 ? StatusKind.Info : StatusKind.Success);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("Export failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_lifetime.IsCancellationRequested ||
            _operationInProgress ||
            _viewModel.IsMutating)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(RefreshAfterExternalChange));
    }

    private async void RefreshAfterExternalChange()
    {
        if (_lifetime.IsCancellationRequested ||
            _operationInProgress ||
            _viewModel.IsMutating ||
            _isReloading)
        {
            return;
        }

        if (TextEditor.IsKeyboardFocusWithin || _viewModel.HasPendingEditorChanges)
        {
            _reloadPending = true;
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_isReloading || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _isReloading = true;

        try
        {
            await _viewModel.LoadAsync(_viewModel.SelectedEntryId);
            SyncSelectionToList();
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("Could not refresh history: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            _isReloading = false;
        }
    }
}
