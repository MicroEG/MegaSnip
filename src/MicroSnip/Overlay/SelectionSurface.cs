using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Rectangle = System.Drawing.Rectangle;

namespace MicroSnip.Overlay;

internal sealed class SelectionSurface : FrameworkElement
{
    private static readonly Brush DimBrush = CreateFrozenBrush(Color.FromArgb(102, 0, 0, 0));
    private static readonly Brush AccentBrush = CreateFrozenBrush(Color.FromRgb(10, 132, 255));
    private static readonly Brush LabelBackgroundBrush = CreateFrozenBrush(Color.FromArgb(242, 17, 24, 39));
    private static readonly Brush LabelTextBrush = Brushes.White;
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private Rectangle _monitorBounds;
    private Rectangle? _selection;
    private ScreenPoint? _cursor;
    private bool _hasMonitorBounds;

    public SelectionSurface()
    {
        IsHitTestVisible = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
    }

    internal void SetMonitorBounds(Rectangle monitorBounds)
    {
        _monitorBounds = monitorBounds;
        _hasMonitorBounds = true;
        InvalidateVisual();
    }

    internal void SetSelection(Rectangle? selection, ScreenPoint? cursor)
    {
        if (Nullable.Equals(_selection, selection) && Nullable.Equals(_cursor, cursor))
        {
            return;
        }

        _selection = selection;
        _cursor = cursor;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;

        if (!_hasMonitorBounds || width <= 0 || height <= 0)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY;

        if (scaleX <= 0 || scaleY <= 0)
        {
            return;
        }

        Rectangle? selection = _selection;
        Rectangle selected = default;
        Rectangle intersection = Rectangle.Empty;

        if (selection.HasValue)
        {
            selected = selection.Value;
            intersection = Rectangle.Intersect(selected, _monitorBounds);
        }

        if (!selection.HasValue || intersection.IsEmpty)
        {
            drawingContext.DrawRectangle(DimBrush, null, new Rect(0, 0, width, height));
        }
        else
        {
            Rect hole = PhysicalRectToDip(intersection, scaleX, scaleY);
            DrawDimAroundHole(drawingContext, hole, width, height);
            DrawSelectionBorder(drawingContext, selected, intersection, hole, scaleX, scaleY);
        }

        if (selection is Rectangle currentSelection &&
            currentSelection.Width > 0 &&
            currentSelection.Height > 0 &&
            _cursor is ScreenPoint cursor &&
            _monitorBounds.Contains(cursor.X, cursor.Y))
        {
            DrawDimensions(drawingContext, currentSelection, cursor, dpi);
        }
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Rect PhysicalRectToDip(Rectangle physicalRect, double scaleX, double scaleY)
    {
        return new Rect(
            (physicalRect.Left - _monitorBounds.Left) / scaleX,
            (physicalRect.Top - _monitorBounds.Top) / scaleY,
            physicalRect.Width / scaleX,
            physicalRect.Height / scaleY);
    }

    private static void DrawDimAroundHole(
        DrawingContext drawingContext,
        Rect hole,
        double width,
        double height)
    {
        double left = Math.Clamp(hole.Left, 0, width);
        double top = Math.Clamp(hole.Top, 0, height);
        double right = Math.Clamp(hole.Right, 0, width);
        double bottom = Math.Clamp(hole.Bottom, 0, height);

        if (right <= left || bottom <= top)
        {
            drawingContext.DrawRectangle(DimBrush, null, new Rect(0, 0, width, height));
            return;
        }

        if (top > 0)
        {
            drawingContext.DrawRectangle(DimBrush, null, new Rect(0, 0, width, top));
        }

        if (bottom < height)
        {
            drawingContext.DrawRectangle(DimBrush, null, new Rect(0, bottom, width, height - bottom));
        }

        if (left > 0)
        {
            drawingContext.DrawRectangle(DimBrush, null, new Rect(0, top, left, bottom - top));
        }

        if (right < width)
        {
            drawingContext.DrawRectangle(
                DimBrush,
                null,
                new Rect(right, top, width - right, bottom - top));
        }
    }

    private void DrawSelectionBorder(
        DrawingContext drawingContext,
        Rectangle selection,
        Rectangle intersection,
        Rect hole,
        double scaleX,
        double scaleY)
    {
        double borderWidth = 1 / scaleX;
        double borderHeight = 1 / scaleY;

        if (selection.Top == intersection.Top)
        {
            drawingContext.DrawRectangle(
                AccentBrush,
                null,
                new Rect(hole.Left, hole.Top, hole.Width, borderHeight));
        }

        if (selection.Bottom == intersection.Bottom)
        {
            drawingContext.DrawRectangle(
                AccentBrush,
                null,
                new Rect(hole.Left, hole.Bottom - borderHeight, hole.Width, borderHeight));
        }

        if (selection.Left == intersection.Left)
        {
            drawingContext.DrawRectangle(
                AccentBrush,
                null,
                new Rect(hole.Left, hole.Top, borderWidth, hole.Height));
        }

        if (selection.Right == intersection.Right)
        {
            drawingContext.DrawRectangle(
                AccentBrush,
                null,
                new Rect(hole.Right - borderWidth, hole.Top, borderWidth, hole.Height));
        }
    }

    private void DrawDimensions(
        DrawingContext drawingContext,
        Rectangle selection,
        ScreenPoint cursor,
        DpiScale dpi)
    {
        string text = $"{selection.Width} × {selection.Height}";
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            12,
            LabelTextBrush,
            dpi.PixelsPerDip);

        const double horizontalPadding = 8;
        const double verticalPadding = 4;
        const double cursorGap = 16;
        const double edgeMargin = 8;

        double labelWidth = formattedText.Width + (horizontalPadding * 2);
        double labelHeight = formattedText.Height + (verticalPadding * 2);
        double cursorX = (cursor.X - _monitorBounds.Left) / dpi.DpiScaleX;
        double cursorY = (cursor.Y - _monitorBounds.Top) / dpi.DpiScaleY;

        double labelX = cursorX + cursorGap;
        double labelY = cursorY + cursorGap;

        if (labelX + labelWidth > ActualWidth - edgeMargin)
        {
            labelX = cursorX - cursorGap - labelWidth;
        }

        if (labelY + labelHeight > ActualHeight - edgeMargin)
        {
            labelY = cursorY - cursorGap - labelHeight;
        }

        labelX = Math.Max(edgeMargin, Math.Min(labelX, ActualWidth - labelWidth - edgeMargin));
        labelY = Math.Max(edgeMargin, Math.Min(labelY, ActualHeight - labelHeight - edgeMargin));

        drawingContext.DrawRoundedRectangle(
            LabelBackgroundBrush,
            null,
            new Rect(labelX, labelY, labelWidth, labelHeight),
            4,
            4);

        drawingContext.DrawText(
            formattedText,
            new Point(labelX + horizontalPadding, labelY + verticalPadding));
    }
}
