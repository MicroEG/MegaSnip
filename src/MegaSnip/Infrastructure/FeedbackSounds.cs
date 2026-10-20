using System.Media;
using MegaSnip.Toast;

namespace MegaSnip.Infrastructure;

internal static class FeedbackSounds
{
    internal static void Play(ToastKind kind)
    {
        switch (kind)
        {
            case ToastKind.Success:
                SystemSounds.Asterisk.Play();
                break;
            case ToastKind.Warning:
                SystemSounds.Exclamation.Play();
                break;
            default:
                SystemSounds.Hand.Play();
                break;
        }
    }
}
