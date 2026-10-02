using Godot;

namespace slpp;

internal enum Shortcut { Undo, Redo, PreviousTurn, NextTurn, RestartRoom, RestartSeed, RandomSeed, Timeline }

internal sealed record KeyChord(Key Key, bool Ctrl = false, bool Shift = false, bool Alt = false, bool Meta = false)
{
    internal static KeyChord From(InputEventKey key) => new(key.Keycode, key.CtrlPressed, key.ShiftPressed, key.AltPressed, key.MetaPressed);

    internal static bool TryParse(string text, out KeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Length ||
            !Enum.TryParse<Key>(parts[^1], true, out var key) || !Enum.IsDefined(key) ||
            key is Key.None or Key.Ctrl or Key.Shift or Key.Alt or Key.Meta or Key.Escape or Key.Backspace) return false;
        bool ctrl = false, shift = false, alt = false, meta = false;
        foreach (string part in parts[..^1])
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                case "meta": meta = true; break;
                default: return false;
            }
        chord = new(key, ctrl, shift, alt, meta);
        return true;
    }

    public override string ToString() => string.Join("+", new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null,
        Alt ? "Alt" : null, Meta ? "Meta" : null, Key.ToString() }.OfType<string>());
}
