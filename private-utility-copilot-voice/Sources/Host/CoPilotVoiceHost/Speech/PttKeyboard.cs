using System.Runtime.InteropServices;

namespace CoPilotVoiceHost.Speech;

/// <summary>Optional PTT key state via Win32 GetAsyncKeyState (Windows host only).</summary>
public static class PttKeyboard
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsKeyDown(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            return false;

        if (!TryMapVirtualKey(keyName.Trim(), out var vk))
            return false;

        try
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryMapVirtualKey(string keyName, out int virtualKey)
    {
        virtualKey = 0;
        var k = keyName.Trim().ToUpperInvariant();
        switch (k)
        {
            case "F1": virtualKey = 0x70; return true;
            case "F2": virtualKey = 0x71; return true;
            case "F3": virtualKey = 0x72; return true;
            case "F4": virtualKey = 0x73; return true;
            case "F5": virtualKey = 0x74; return true;
            case "F6": virtualKey = 0x75; return true;
            case "F7": virtualKey = 0x76; return true;
            case "F8": virtualKey = 0x77; return true;
            case "F9": virtualKey = 0x78; return true;
            case "F10": virtualKey = 0x79; return true;
            case "F11": virtualKey = 0x7A; return true;
            case "F12": virtualKey = 0x7B; return true;
            case "SCROLL":
            case "SCROLL LOCK": virtualKey = 0x91; return true;
            case "PAUSE": virtualKey = 0x13; return true;
            default:
                if (k.Length == 1 && k[0] is >= 'A' and <= 'Z')
                {
                    virtualKey = k[0];
                    return true;
                }
                return false;
        }
    }
}
