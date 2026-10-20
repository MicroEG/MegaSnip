using System;

namespace MegaSnip.Toast;

internal sealed class ToastController : IDisposable
{
    private ToastWindow? _current;
    private bool _disposed;

    internal event EventHandler? PreviewRequested;

    internal void ShowCopied(int characterCount) =>
        Show(
            ToastKind.Success,
            $"Text copied ({characterCount} chars)",
            detail: null,
            canPreview: true);

    internal void ShowNoText() =>
        Show(
            ToastKind.Warning,
            "No text detected",
            "Your clipboard was not changed.",
            canPreview: false);

    internal void ShowCopyFailed() =>
        Show(
            ToastKind.Warning,
            "Could not copy the text",
            "The clipboard is busy. Open Preview to copy it again.",
            canPreview: true);

    internal void ShowError(string title, string? detail) =>
        Show(ToastKind.Error, title, detail, canPreview: false);

    internal void Dismiss()
    {
        _current?.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Dismiss();
        _disposed = true;
    }

    private void Show(ToastKind kind, string title, string? detail, bool canPreview)
    {
        if (_disposed)
        {
            return;
        }

        Dismiss();

        var window = new ToastWindow();
        window.Configure(kind, title, detail, canPreview);
        window.PreviewRequested += OnPreviewRequested;
        window.Closed += OnWindowClosed;
        _current = window;
        window.Show();
    }

    private void OnPreviewRequested(object? sender, EventArgs e)
    {
        Dismiss();
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ToastWindow window)
        {
            return;
        }

        window.PreviewRequested -= OnPreviewRequested;
        window.Closed -= OnWindowClosed;

        if (ReferenceEquals(_current, window))
        {
            _current = null;
        }
    }
}
