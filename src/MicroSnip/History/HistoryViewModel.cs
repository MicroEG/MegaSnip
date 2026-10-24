using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace MicroSnip.History;

internal enum StatusKind
{
    Info,
    Success,
    Error,
}

internal sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(600);
    private static readonly Brush MutedBrush = CreateBrush(Color.FromRgb(138, 148, 166));
    private static readonly Brush SuccessBrush = CreateBrush(Color.FromRgb(110, 231, 168));
    private static readonly Brush ErrorBrush = CreateBrush(Color.FromRgb(255, 135, 128));

    private readonly HistoryService _historyService;
    private readonly string _hotkeyDisplay;
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private List<HistoryListItem> _allItems = [];
    private IReadOnlyList<HistoryListItem> _entries = Array.Empty<HistoryListItem>();
    private HistoryListItem? _selectedItem;
    private ImageSource? _selectedImage;
    private CancellationTokenSource? _imageCancellation;
    private string _searchText = string.Empty;
    private string _statusMessage = "Ready";
    private Brush _statusBrush = MutedBrush;
    private string? _pendingText;
    private string? _pendingItemId;
    private bool _disposed;

    internal HistoryViewModel(HistoryService historyService, string hotkeyDisplay)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _hotkeyDisplay = hotkeyDisplay;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutoSaveDelay,
        };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? SelectedItemChanged;

    public IReadOnlyList<HistoryListItem> Entries
    {
        get => _entries;
        private set
        {
            _entries = value;
            RaisePropertyChanged();
        }
    }

    internal IReadOnlyList<HistoryListItem> AllItems => _allItems;

    internal int TotalCount => _allItems.Count;

    internal int FilteredCount => Entries.Count;

    internal string SearchText
    {
        get => _searchText;
        set
        {
            string next = value ?? string.Empty;

            if (string.Equals(_searchText, next, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = next;
            RaisePropertyChanged();
            ApplyFilter();
        }
    }

    internal HistoryListItem? SelectedItem => _selectedItem;

    internal string? SelectedEntryId => _selectedItem?.Id;

    internal bool IsMutating { get; private set; }

    internal bool HasPendingEditorChanges => _pendingText is not null;

    public string StatusMessage => _statusMessage;

    public Brush StatusBrush => _statusBrush;

    public string CountText
    {
        get
        {
            int total = TotalCount;

            if (total == 0)
            {
                return "No snips";
            }

            if (_searchText.Trim().Length > 0)
            {
                return $"{FilteredCount:N0} of {total:N0} snips";
            }

            return total == 1 ? "1 snip" : $"{total:N0} snips";
        }
    }

    public string EmptyStateText =>
        TotalCount == 0
            ? $"No snips yet — press {_hotkeyDisplay} to capture one."
            : "No snips match your search.";

    public Visibility EmptyStateVisibility =>
        Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetailVisibility =>
        _selectedItem is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DetailEmptyVisibility =>
        _selectedItem is null ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedTimeText =>
        _selectedItem?.Entry.LocalCreatedAt.ToString(
            "dddd, MMMM d, yyyy · HH:mm:ss",
            CultureInfo.CurrentCulture) ?? string.Empty;

    public string SelectedDimensionsText =>
        _selectedItem is null
            ? string.Empty
            : $"{_selectedItem.Entry.Width:N0} × {_selectedItem.Entry.Height:N0} px";

    public string SelectedCharacterCountText =>
        _selectedItem?.CharacterCountText ?? string.Empty;

    public string SelectedLanguageText =>
        _selectedItem?.Entry.OcrLanguage ?? "System default";

    public string SelectedMonitorText =>
        _selectedItem?.Entry.MonitorId ?? string.Empty;

    public string SelectedAppVersionText =>
        _selectedItem?.Entry.AppVersion ?? string.Empty;

    public ImageSource? SelectedImage
    {
        get => _selectedImage;
        private set
        {
            _selectedImage = value;
            RaisePropertyChanged();
        }
    }

    public string ImageMessage { get; private set; } = string.Empty;

    public Visibility ImageMessageVisibility =>
        DetailVisibility == Visibility.Visible &&
        (SelectedImage is null || _selectedItem?.HasImage != true)
            ? Visibility.Visible
            : Visibility.Collapsed;

    internal async Task LoadAsync(string? preferredSelectionId = null)
    {
        string? selectionId = preferredSelectionId ?? SelectedEntryId;
        IReadOnlyList<HistoryEntry> entries = await _historyService.LoadAllAsync();

        _allItems = entries.Select(entry => new HistoryListItem(entry)).ToList();
        ApplyFilter(selectionId);
        SetStatus("Ready", StatusKind.Info);
    }

    internal void SelectItem(HistoryListItem? item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            return;
        }

        _selectedItem = item;

        if (item is null)
        {
            _imageCancellation?.Cancel();
            _imageCancellation?.Dispose();
            _imageCancellation = null;
            SelectedImage = null;
            ImageMessage = string.Empty;
        }
        else
        {
            LoadSelectedImage(item);
        }

        RaisePropertyChanged(nameof(SelectedItem));
        RaisePropertyChanged(nameof(SelectedEntryId));
        RaisePropertyChanged(nameof(DetailVisibility));
        RaisePropertyChanged(nameof(DetailEmptyVisibility));
        RaisePropertyChanged(nameof(SelectedTimeText));
        RaisePropertyChanged(nameof(SelectedDimensionsText));
        RaisePropertyChanged(nameof(SelectedCharacterCountText));
        RaisePropertyChanged(nameof(SelectedLanguageText));
        RaisePropertyChanged(nameof(SelectedMonitorText));
        RaisePropertyChanged(nameof(SelectedAppVersionText));
        RaisePropertyChanged(nameof(ImageMessageVisibility));
        SelectedItemChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void NoteEditorText(string text)
    {
        if (_selectedItem is null)
        {
            return;
        }

        if (string.Equals(text, _selectedItem.Text, StringComparison.Ordinal))
        {
            _pendingText = null;
            _pendingItemId = null;
            _saveTimer.Stop();
            RaisePropertyChanged(nameof(HasPendingEditorChanges));
            return;
        }

        _pendingText = text;
        _pendingItemId = _selectedItem.Id;
        _saveTimer.Stop();
        _saveTimer.Start();
        SetStatus("Editing…", StatusKind.Info);
        RaisePropertyChanged(nameof(HasPendingEditorChanges));
    }

    internal async Task<bool> FlushPendingSaveAsync()
    {
        _saveTimer.Stop();
        return await SavePendingAsync();
    }

    internal void SetStatus(string message, StatusKind kind)
    {
        _statusMessage = message;
        _statusBrush = kind switch
        {
            StatusKind.Success => SuccessBrush,
            StatusKind.Error => ErrorBrush,
            _ => MutedBrush,
        };
        RaisePropertyChanged(nameof(StatusMessage));
        RaisePropertyChanged(nameof(StatusBrush));
    }

    internal void RefreshCounts()
    {
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(FilteredCount));
        RaisePropertyChanged(nameof(CountText));
        RaisePropertyChanged(nameof(EmptyStateVisibility));
        RaisePropertyChanged(nameof(EmptyStateText));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _imageCancellation?.Cancel();
        _imageCancellation?.Dispose();
        _imageCancellation = null;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void ApplyFilter(string? preferredSelectionId = null)
    {
        string term = _searchText.Trim();
        string? preferredId = preferredSelectionId ?? SelectedEntryId;

        IReadOnlyList<HistoryListItem> filtered = term.Length == 0
            ? _allItems.ToList()
            : _allItems
                .Where(item => item.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Entries = filtered;

        HistoryListItem? selection = preferredId is null
            ? null
            : filtered.FirstOrDefault(item => item.Id == preferredId);

        if (selection is null)
        {
            selection = filtered.Count > 0 ? filtered[0] : null;
        }

        SelectItem(selection);
        RefreshCounts();
    }

    private async Task<bool> SavePendingAsync()
    {
        await _saveGate.WaitAsync();

        try
        {
            string? text = _pendingText;
            string? id = _pendingItemId;

            if (text is null || id is null)
            {
                return true;
            }

            HistoryListItem? item = _allItems.FirstOrDefault(candidate => candidate.Id == id);

            if (item is null)
            {
                _pendingText = null;
                _pendingItemId = null;
                RaisePropertyChanged(nameof(HasPendingEditorChanges));
                return true;
            }

            if (string.Equals(text, item.Text, StringComparison.Ordinal))
            {
                ClearPendingIfCurrent(text);
                return true;
            }

            IsMutating = true;
            SetStatus("Saving…", StatusKind.Info);

            try
            {
                bool saved = await _historyService.UpdateTextAsync(id, text);

                if (!saved)
                {
                    SetStatus(
                        "This snip was removed before its edits could be saved.",
                        StatusKind.Error);
                    return false;
                }

                item.SetText(text);

                if (ReferenceEquals(_selectedItem, item))
                {
                    RaisePropertyChanged(nameof(SelectedCharacterCountText));
                }

                ClearPendingIfCurrent(text);

                SetStatus(
                    HasPendingEditorChanges ? "Editing…" : "Saved",
                    HasPendingEditorChanges ? StatusKind.Info : StatusKind.Success);
                RefreshCounts();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Could not save changes: " + ex.Message, StatusKind.Error);
                return false;
            }
            finally
            {
                IsMutating = false;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ClearPendingIfCurrent(string savedText)
    {
        if (!string.Equals(_pendingText, savedText, StringComparison.Ordinal))
        {
            return;
        }

        _pendingText = null;
        _pendingItemId = null;
        RaisePropertyChanged(nameof(HasPendingEditorChanges));
    }

    private async void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();

        try
        {
            await SavePendingAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void LoadSelectedImage(HistoryListItem item)
    {
        _imageCancellation?.Cancel();
        _imageCancellation?.Dispose();
        _imageCancellation = null;

        SelectedImage = null;
        ImageMessage = string.Empty;
        RaisePropertyChanged(nameof(ImageMessageVisibility));

        if (!item.HasImage)
        {
            ImageMessage = "This snip was saved without an image.";
            RaisePropertyChanged(nameof(ImageMessage));
            RaisePropertyChanged(nameof(ImageMessageVisibility));
            return;
        }

        string? path = item.Entry.ImagePath;
        if (string.IsNullOrEmpty(path))
        {
            ImageMessage = "The saved image is unavailable.";
            RaisePropertyChanged(nameof(ImageMessage));
            RaisePropertyChanged(nameof(ImageMessageVisibility));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _imageCancellation = cancellation;
        _ = LoadSelectedImageAsync(item, path, cancellation);
    }

    private async Task LoadSelectedImageAsync(
        HistoryListItem item,
        string path,
        CancellationTokenSource cancellation)
    {
        try
        {
            BitmapSource image = await Task.Run(
                () => LoadPreviewImage(path, cancellation.Token),
                cancellation.Token);

            if (_disposed ||
                cancellation.IsCancellationRequested ||
                !ReferenceEquals(_selectedItem, item))
            {
                return;
            }

            SelectedImage = image;
            RaisePropertyChanged(nameof(ImageMessageVisibility));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_selectedItem, item))
            {
                ImageMessage = "The saved image could not be opened.";
                RaisePropertyChanged(nameof(ImageMessage));
                RaisePropertyChanged(nameof(ImageMessageVisibility));
            }
        }
    }

    private static BitmapSource LoadPreviewImage(string path, CancellationToken cancellationToken)
    {
        const int maxWidth = 1440;
        const int maxHeight = 960;

        cancellationToken.ThrowIfCancellationRequested();

        using var source = new DrawingBitmap(path);
        double scale = Math.Min(
            1d,
            Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var scaled = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppPArgb);
        using (DrawingGraphics graphics = DrawingGraphics.FromImage(scaled))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, width, height));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryStream();
        scaled.Save(stream, DrawingImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
