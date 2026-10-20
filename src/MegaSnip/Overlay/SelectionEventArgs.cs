using System;
using System.Drawing;

namespace MegaSnip.Overlay;

internal sealed class ScreenPointEventArgs : EventArgs
{
    internal ScreenPointEventArgs(ScreenPoint point)
    {
        Point = point;
    }

    internal ScreenPoint Point { get; }
}

internal sealed class SelectionCompletedEventArgs : EventArgs
{
    internal SelectionCompletedEventArgs(Rectangle bounds)
    {
        Bounds = bounds;
    }

    internal Rectangle Bounds { get; }
}
