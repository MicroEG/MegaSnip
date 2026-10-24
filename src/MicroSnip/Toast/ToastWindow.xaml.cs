using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MicroSnip.Infrastructure;

namespace MicroSnip.Toast;

internal partial class ToastWindow : Window
{
    private static readonly TimeSpan InitialDismissDelay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan HoverDismissDelay = TimeSpan.FromSeconds(3);

    private static readonly Brush SuccessForeground = CreateBrush(Color.FromRgb(48, 209, 88));
    private static readonly Brush SuccessBackground = CreateBrush(Color.FromArgb(38, 48, 209, 88));
    private static readonly Brush WarningForeground = CreateBrush(Color.FromRgb(255, 159, 10));
    private static readonly Brush WarningBackground = CreateBrush(Color.FromArgb(38, 255, 159, 10));
    private static readonly Brush ErrorForeground = CreateBrush(Color.FromRgb(255, 105, 97));
    private static readonly Brush ErrorBackground = CreateBrush(Color.FromArgb(38, 255, 105, 97));

    private readonly DispatcherTimer _dismissTimer;
    private bool _isLoaded;

    public ToastWindow()
    {
        InitializeComponent();

        _dismissTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = InitialDismissDelay,
        };
        _dismissTimer.Tick += OnDismissTimerTick;

        Loaded += OnLoaded;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
    }

    internal event EventHandler? PreviewRequested;

    internal event EventHandler? DiscardHistoryRequested;

    internal void Configure(
        ToastKind kind,
        string title,
        string? detail,
        bool canPreview,
        bool canDiscardHistory)
    {
        TitleText.Text = title;
        DetailText.Text = detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        PreviewButton.Visibility = canPreview ? Visibility.Visible : Visibility.Collapsed;
        DiscardHistoryButton.Visibility = canDiscardHistory
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (kind)
        {
            case ToastKind.Success:
                ApplyIcon("✓", SuccessBackground, SuccessForeground);
                break;
            case ToastKind.Warning:
                ApplyIcon("!", WarningBackground, WarningForeground);
                break;
            default:
                ApplyIcon("!", ErrorBackground, ErrorForeground);
                break;
        }
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
            exStyle | NativeMethods.WsExToolWindow | (int)NativeMethods.WsExNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        _dismissTimer.Tick -= OnDismissTimerTick;
        base.OnClosed(e);
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void ApplyIcon(string glyph, Brush background, Brush foreground)
    {
        IconText.Text = glyph;
        IconBackground.Background = background;
        IconText.Foreground = foreground;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearTaskbar();
        Opacity = 1;
        _isLoaded = true;
        _dismissTimer.Start();
    }

    private void PositionNearTaskbar()
    {
        UpdateLayout();

        Rect workArea = SystemParameters.WorkArea;
        const double margin = 12;

        Left = Math.Max(workArea.Left + margin, workArea.Right - ActualWidth - margin);
        Top = Math.Max(workArea.Top + margin, workArea.Bottom - ActualHeight - margin);
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isLoaded)
        {
            _dismissTimer.Stop();
        }
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isLoaded || !IsVisible)
        {
            return;
        }

        _dismissTimer.Interval = HoverDismissDelay;
        _dismissTimer.Start();
    }

    private void OnDismissTimerTick(object? sender, EventArgs e)
    {
        _dismissTimer.Stop();
        Close();
    }

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        _dismissTimer.Stop();
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDiscardHistoryClicked(object sender, RoutedEventArgs e)
    {
        _dismissTimer.Stop();
        DiscardHistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
