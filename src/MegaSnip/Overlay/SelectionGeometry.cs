using System;
using System.Drawing;

namespace MegaSnip.Overlay;

internal static class SelectionGeometry
{
    internal const int MinimumSideLength = 5;

    internal static Rectangle Normalize(ScreenPoint start, ScreenPoint end)
    {
        int left = Math.Min(start.X, end.X);
        int top = Math.Min(start.Y, end.Y);
        int right = Math.Max(start.X, end.X);
        int bottom = Math.Max(start.Y, end.Y);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    internal static bool IsLargeEnough(Rectangle selection) =>
        selection.Width >= MinimumSideLength &&
        selection.Height >= MinimumSideLength;
}
