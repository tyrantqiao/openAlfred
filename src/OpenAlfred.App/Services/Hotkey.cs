using System.Text;
using System.Windows.Input;

namespace OpenAlfred.App.Services;

/// <summary>
/// 全局唤起热键：修饰键 + 主键。负责与字符串（"Alt+Space"）及 Win32 MOD_/VK_ 互转。
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public const string DefaultString = "Alt+Space";

    public static Hotkey Default() => Parse(DefaultString);

    /// <summary>从 "Ctrl+Alt+K" 之类的字符串解析；无法识别时回退默认。</summary>
    public static Hotkey Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default();

        ModifierKeys mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "alt": mods |= ModifierKeys.Alt; continue;
                case "ctrl":
                case "control": mods |= ModifierKeys.Control; continue;
                case "shift": mods |= ModifierKeys.Shift; continue;
                case "win":
                case "windows":
                case "cmd": mods |= ModifierKeys.Windows; continue;
            }
            key = ParseKey(raw);
        }

        return new Hotkey(mods, key ?? Key.Space);
    }

    private static Key ParseKey(string token)
    {
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
            return Key.D0 + (token[0] - '0');
        return Enum.TryParse<Key>(token, ignoreCase: true, out var k) ? k : Key.Space;
    }

    /// <summary>Win32 RegisterHotKey 的 fsModifiers。</summary>
    public uint ToWin32Modifiers()
    {
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
        uint m = 0;
        if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= MOD_ALT;
        if (Modifiers.HasFlag(ModifierKeys.Control)) m |= MOD_CONTROL;
        if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= MOD_SHIFT;
        if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= MOD_WIN;
        return m;
    }

    /// <summary>Win32 RegisterHotKey 的 vk。</summary>
    public uint ToVirtualKey() => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(KeyName(Key));
        return sb.ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => key.ToString(),
    };
}
