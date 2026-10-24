using System.Drawing;

namespace MicroSnip.Capture;

internal interface IScreenCapture
{
    Bitmap Capture(Rectangle bounds);
}
