using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MicroSnip.Infrastructure;

internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    internal static void ApplyDarkTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int enabled = 1;
        int result = DwmSetWindowAttribute(
            handle,
            DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));

        if (result != 0)
        {
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
