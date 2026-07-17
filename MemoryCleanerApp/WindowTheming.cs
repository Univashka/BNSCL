using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Bnscl;

internal static class WindowTheming
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void ApplyDark(Window window)
    {
        try
        {
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == 0) return;

            int dark = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            int caption = 0x001B130D;
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));

            int text = 0x00EEE4DB;
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
        }
        catch
        {
            // Старые версии Windows могут не поддерживать цветовые атрибуты DWM.
        }
    }
}
