using System.Windows.Media;

namespace CoPilotVoiceHost.Ui;

/// <summary>Frozen brushes for the ~2 Hz Status refresh (no per-tick SolidColorBrush alloc).</summary>
internal static class StatusBrushes
{
    public static readonly SolidColorBrush LiveGreen = Freeze(0x2E, 0xCC, 0x71);
    public static readonly SolidColorBrush OfflineAmber = Freeze(0xF3, 0x9C, 0x12);
    public static readonly SolidColorBrush DisconnectedRed = Freeze(0xE7, 0x4C, 0x3C);
    public static readonly SolidColorBrush MicIdle = Freeze(0x3A, 0x42, 0x50);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }
}
