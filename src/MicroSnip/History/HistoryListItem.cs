using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MicroSnip.History;

internal sealed class HistoryListItem : INotifyPropertyChanged
{
    private const int MaxFirstLineLength = 100;
    private const int ThumbnailDecodeWidth = 96;

    private string _text;
    private int _charCount;
    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    internal HistoryListItem(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _text = entry.Text;
        _charCount = entry.CharCount;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoryEntry Entry { get; }

    public string Id => Entry.Id;

    public string Text => _text;

    public int CharCount => _charCount;

    public string FirstLine => CreateFirstLine(_text);

    public string DisplayTime =>
        Entry.LocalCreatedAt.ToString("g", CultureInfo.CurrentCulture);

    public string CharacterCountText =>
        _charCount == 1
            ? "1 char"
            : _charCount.ToString("N0", CultureInfo.CurrentCulture) + " chars";

    public bool HasImage => Entry.HasImage;

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailLoaded)
            {
                _thumbnailLoaded = true;
                _thumbnail = LoadThumbnail(Entry.ThumbnailPath);
            }

            return _thumbnail;
        }
    }

    internal void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return;
        }

        _text = text;
        _charCount = text.Length;
        RaisePropertyChanged(nameof(Text));
        RaisePropertyChanged(nameof(FirstLine));
        RaisePropertyChanged(nameof(CharCount));
        RaisePropertyChanged(nameof(CharacterCountText));
    }

    private static string CreateFirstLine(string text)
    {
        int breakIndex = text.IndexOfAny(['\r', '\n']);
        string line = (breakIndex >= 0 ? text[..breakIndex] : text).Trim();

        if (line.Length == 0)
        {
            return "No text";
        }

        return line.Length <= MaxFirstLineLength
            ? line
            : line[..MaxFirstLineLength] + "…";
    }

    private static ImageSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = ThumbnailDecodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
