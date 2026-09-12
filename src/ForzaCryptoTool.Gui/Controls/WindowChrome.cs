using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ForzaCryptoTool;

internal static class WindowChrome
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int BorderColor = 34;

    public static void ApplyDark(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            int enabled = 1;

            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));

            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int caption = ToColorRef(Resource("SurfaceColor", Color.FromRgb(0x16, 0x18, 0x1D)));
                int text = ToColorRef(Resource("TextPrimaryColor", Color.FromRgb(0xF4, 0xF6, 0xFA)));
                int border = ToColorRef(Resource("BorderColor", Color.FromRgb(0x2C, 0x31, 0x3B)));
                DwmSetWindowAttribute(hwnd, CaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, TextColor, ref text, sizeof(int));
                DwmSetWindowAttribute(hwnd, BorderColor, ref border, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
        }
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static Color Resource(string key, Color fallback)
    {
        try
        {
            return Application.Current.TryFindResource(key) is Color c ? c : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
