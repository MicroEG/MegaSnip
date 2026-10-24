using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroSnip.Overlay;

internal sealed class SelectionOverlayController : IDisposable
{
    private readonly List<SelectionOverlayWindow> _windows = new();
    private ScreenPoint _dragStart;
    private ScreenPoint _dragCurrent;
    private bool _dragActive;
    private bool _closing;
    private bool _disposed;

    public event EventHandler? Closed;

    public event EventHandler<SelectionCompletedEventArgs>? SelectionCompleted;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_windows.Count > 0)
        {
            throw new InvalidOperationException("The selection overlay is already visible.");
        }

        SelectionOverlayWindow? primaryWindow = null;

        try
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                var window = new SelectionOverlayWindow(screen.Bounds);
                window.CancelRequested += OnCancelRequested;
                window.DragStarted += OnDragStarted;
                window.DragMoved += OnDragMoved;
                window.DragFinished += OnDragFinished;
                window.Closed += OnWindowClosed;
                _windows.Add(window);

                if (screen.Primary)
                {
                    primaryWindow = window;
                }
            }

            if (_windows.Count == 0)
            {
                Close();
                return;
            }

            foreach (SelectionOverlayWindow window in _windows)
            {
                window.Present();
            }

            (primaryWindow ?? _windows[0]).ActivateForInput();
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        foreach (SelectionOverlayWindow window in _windows.ToArray())
        {
            window.CancelRequested -= OnCancelRequested;
            window.DragStarted -= OnDragStarted;
            window.DragMoved -= OnDragMoved;
            window.DragFinished -= OnDragFinished;
            window.Closed -= OnWindowClosed;
            window.Close();
        }

        _windows.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    private void OnCancelRequested(object? sender, EventArgs e) => Close();

    private void OnDragStarted(object? sender, ScreenPointEventArgs e)
    {
        if (_closing || _dragActive)
        {
            return;
        }

        _dragActive = true;
        _dragStart = e.Point;
        _dragCurrent = e.Point;
        PublishSelection();
    }

    private void OnDragMoved(object? sender, ScreenPointEventArgs e)
    {
        if (!_dragActive)
        {
            return;
        }

        _dragCurrent = e.Point;
        PublishSelection();
    }

    private void OnDragFinished(object? sender, ScreenPointEventArgs e)
    {
        if (!_dragActive)
        {
            return;
        }

        _dragCurrent = e.Point;
        Rectangle bounds = SelectionGeometry.Normalize(_dragStart, _dragCurrent);
        _dragActive = false;

        if (!SelectionGeometry.IsLargeEnough(bounds))
        {
            PublishSelection();
            return;
        }

        foreach (SelectionOverlayWindow window in _windows.ToArray())
        {
            window.HideForCapture();
        }

        try
        {
            SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(bounds));
        }
        finally
        {
            Close();
        }
    }

    private void PublishSelection()
    {
        Rectangle? selection = null;
        ScreenPoint? cursor = null;

        if (_dragActive)
        {
            selection = SelectionGeometry.Normalize(_dragStart, _dragCurrent);
            cursor = _dragCurrent;
        }

        foreach (SelectionOverlayWindow window in _windows)
        {
            window.SetSelectionState(selection, cursor);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SelectionOverlayWindow window)
        {
            window.CancelRequested -= OnCancelRequested;
            window.DragStarted -= OnDragStarted;
            window.DragMoved -= OnDragMoved;
            window.DragFinished -= OnDragFinished;
            window.Closed -= OnWindowClosed;
            _windows.Remove(window);
        }

        if (!_closing)
        {
            Close();
        }
    }
}
