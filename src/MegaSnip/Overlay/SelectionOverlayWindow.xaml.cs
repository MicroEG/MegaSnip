using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MegaSnip.Infrastructure;

namespace MegaSnip.Overlay;

internal partial class SelectionOverlayWindow : Window
{
    private readonly Rectangle _monitorBounds;
    private HwndSource? _hwndSource;
    private bool _cancelRaised;
    private bool _isDragging;

    public SelectionOverlayWindow(Rectangle monitorBounds)
    {
        _monitorBounds = monitorBounds;

        InitializeComponent();
        Surface.SetMonitorBounds(monitorBounds);
    }

    internal event EventHandler<ScreenPointEventArgs>? DragStarted;

    internal event EventHandler<ScreenPointEventArgs>? DragMoved;

    internal event EventHandler<ScreenPointEventArgs>? DragFinished;

    internal event EventHandler? CancelRequested;

    internal void Present()
    {
        Show();

        if (!ApplyPhysicalBounds())
        {
            int error = Marshal.GetLastWin32Error();
            Close();
            throw new Win32Exception(error, "Could not position the selection overlay.");
        }

        Opacity = 1;
    }

    internal void ActivateForInput()
    {
        Activate();
        Focus();
        Keyboard.Focus(this);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || NativeMethods.GetForegroundWindow() == handle)
        {
            return;
        }

        ForceForeground(handle);
        Focus();
        Keyboard.Focus(this);
    }

    private static void ForceForeground(IntPtr handle)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThreadId = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThreadId = NativeMethods.GetCurrentThreadId();
        bool attached = foregroundThreadId != 0 &&
            foregroundThreadId != currentThreadId &&
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            NativeMethods.SetForegroundWindow(handle);
            NativeMethods.SetFocus(handle);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    internal void SetSelectionState(Rectangle? selection, ScreenPoint? cursor) =>
        Surface.SetSelection(selection, cursor);

    internal void HideForCapture()
    {
        _isDragging = false;
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(
            handle,
            NativeMethods.GwlExStyle,
            exStyle | NativeMethods.WsExToolWindow);

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        base.OnClosed(e);
    }

    private bool ApplyPhysicalBounds()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            _monitorBounds.Left,
            _monitorBounds.Top,
            _monitorBounds.Width,
            _monitorBounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmDpiChanged)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (IsLoaded)
                    {
                        ApplyPhysicalBounds();
                    }
                }));
        }

        return IntPtr.Zero;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RequestCancel();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_cancelRaised || _isDragging)
        {
            return;
        }

        Activate();
        Focus();
        Keyboard.Focus(this);

        ScreenPoint point = GetPointerScreenPoint(e);

        if (!CaptureMouse())
        {
            return;
        }

        _isDragging = true;
        e.Handled = true;
        DragStarted?.Invoke(this, new ScreenPointEventArgs(point));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        e.Handled = true;
        DragMoved?.Invoke(this, new ScreenPointEventArgs(GetPointerScreenPoint(e)));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        e.Handled = true;
        _isDragging = false;
        ScreenPoint point = GetPointerScreenPoint(e);
        ReleaseMouseCapture();
        DragFinished?.Invoke(this, new ScreenPointEventArgs(point));
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            RequestCancel();
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RequestCancel();
    }

    private void RequestCancel()
    {
        if (_cancelRaised)
        {
            return;
        }

        _cancelRaised = true;
        _isDragging = false;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private ScreenPoint GetPointerScreenPoint(MouseEventArgs e)
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint nativePoint))
        {
            return new ScreenPoint(nativePoint.X, nativePoint.Y);
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var localPoint = e.GetPosition(this);

        return new ScreenPoint(
            _monitorBounds.Left + (int)Math.Round(localPoint.X * dpi.DpiScaleX),
            _monitorBounds.Top + (int)Math.Round(localPoint.Y * dpi.DpiScaleY));
    }
}
