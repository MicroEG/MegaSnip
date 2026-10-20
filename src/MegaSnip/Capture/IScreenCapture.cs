using System.Drawing;

namespace MegaSnip.Capture;

internal interface IScreenCapture
{
    Bitmap Capture(Rectangle bounds);
}
