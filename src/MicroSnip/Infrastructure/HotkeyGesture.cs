using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;

namespace MicroSnip.Infrastructure;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

internal readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, Key Key)
{
    public static HotkeyGesture Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.T);

    public bool IsValid() =>
        Modifiers != HotkeyModifiers.None &&
        Key != Key.None &&
        !IsModifierKey(Key);

    public override string ToString()
    {
        var parts = new List<string>(5);

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyName(Key));
        return string.Join("+", parts);
    }

    internal static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;

    private static string GetKeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num {((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture)}";
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return $"F{(int)(key - Key.F1) + 1}";
        }

        return key switch
        {
            Key.Back => "Backspace",
            Key.Cancel => "Cancel",
            Key.Capital => "Caps Lock",
            Key.Clear => "Clear",
            Key.Delete => "Delete",
            Key.Down => "Down",
            Key.End => "End",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.Home => "Home",
            Key.Insert => "Insert",
            Key.Left => "Left",
            Key.PageDown => "Page Down",
            Key.PageUp => "Page Up",
            Key.Right => "Right",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Up => "Up",
            Key.OemComma => ",",
            Key.OemMinus => "-",
            Key.OemPeriod => ".",
            Key.OemPlus => "=",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemTilde => "`",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemQuotes => "'",
            _ => key.ToString(),
        };
    }
}
