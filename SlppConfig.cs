using System.Reflection;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;

namespace slpp;

public sealed class SlppConfig : SimpleModConfig
{
    private const string KeyPattern = @"(?i:(?:(?:Ctrl|Shift|Alt|Meta)\+)*(?:[A-Z]|Key[0-9]|F[1-9]|F[12][0-9]|F3[0-5]|Tab|Space|Enter|KpEnter|Insert|Delete|Home|End|Pageup|Pagedown|Left|Right|Up|Down|Bracketleft|Bracketright|Semicolon|Apostrophe|Comma|Period|Slash|Backslash|Minus|Equal|Quoteleft|Kp[0-9]|KpAdd|KpSubtract|KpMultiply|KpDivide|KpPeriod))?";

    [ConfigSection("Components")]
    public static bool Toolbar { get; set; } = true;
    public static bool Actions { get; set; } = true;
    public static bool Turns { get; set; } = true;
    public static bool RoomRestart { get; set; } = true;
    public static bool QuickRestart { get; set; } = true;
    public static bool Timeline { get; set; } = true;
    public static bool Status { get; set; } = true;
    public static bool Hotkeys { get; set; } = true;

    [ConfigSection("Display")]
    [ConfigSlider(80, 140, 5)]
    public static int Scale { get; set; } = 100;
    public static bool AlignRight { get; set; }

    [ConfigSection("Shortcuts")]
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string UndoKey { get; set; } = "Ctrl+Z";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string RedoKey { get; set; } = "Ctrl+Y";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string PreviousTurnKey { get; set; } = "Ctrl+Shift+Z";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string NextTurnKey { get; set; } = "Ctrl+Shift+Y";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string RestartRoomKey { get; set; } = "Ctrl+R";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string RestartSeedKey { get; set; } = "";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string RandomSeedKey { get; set; } = "";
    [ConfigTextInput(KeyPattern, MaxLength = 64)] public static string TimelineKey { get; set; } = "F8";

    internal static SlppConfig Instance { get; private set; } = null!;
    private static readonly Dictionary<Shortcut, PropertyInfo> KeyProperties = Enum.GetValues<Shortcut>()
        .ToDictionary(s => s, s => typeof(SlppConfig).GetProperty(s + "Key")!);
    private static readonly Dictionary<string, string> Labels = new()
    {
        [nameof(Actions)] = "Action controls",
        [nameof(Turns)] = "Turn controls",
        [nameof(RoomRestart)] = "Room restart",
        [nameof(QuickRestart)] = "Quick restart",
        [nameof(Hotkeys)] = "Keyboard shortcuts",
        [nameof(Scale)] = "UI scale (%)",
        [nameof(AlignRight)] = "Align toolbar right"
    };
    private static Control? _options;
    internal static bool IsOpen => GodotObject.IsInstanceValid(_options) && _options!.IsVisibleInTree();

    internal static void Register()
    {
        Instance = new SlppConfig();
        ModConfigRegistry.Register("slpp", Instance);
    }

    internal static string Name(Shortcut shortcut) => shortcut switch
    {
        Shortcut.Undo => "Undo action",
        Shortcut.Redo => "Redo action",
        Shortcut.PreviousTurn => "Previous turn",
        Shortcut.NextTurn => "Next turn",
        Shortcut.RestartRoom => "Restart room",
        Shortcut.RestartSeed => "Same seed",
        Shortcut.RandomSeed => "Random seed",
        _ => "Timeline"
    };

    internal static bool Enabled(Shortcut shortcut) => shortcut switch
    {
        Shortcut.Undo or Shortcut.Redo => Actions,
        Shortcut.PreviousTurn or Shortcut.NextTurn => Turns,
        Shortcut.RestartRoom => RoomRestart,
        Shortcut.RestartSeed or Shortcut.RandomSeed => QuickRestart,
        Shortcut.Timeline => Timeline,
        _ => false
    };

    internal static KeyChord? Binding(Shortcut shortcut) =>
        KeyChord.TryParse((string?)KeyProperties[shortcut].GetValue(null) ?? "", out var chord) ? chord : null;

    internal static void InstallLabels()
    {
        UpdateLabels();
        LocManager.Instance.SubscribeToLocaleChange(UpdateLabels);
    }

    private static void UpdateLabels()
    {
        string prefix = Instance.ModPrefix;
        var labels = Labels.ToDictionary(p => prefix + StringHelper.Slugify(p.Key) + ".title", p => p.Value);
        labels[prefix[..^1] + ".mod_title"] = "Save & Load ++";
        foreach (var pair in KeyProperties)
        {
            string key = prefix + StringHelper.Slugify(pair.Value.Name);
            labels[key + ".title"] = Name(pair.Key);
            labels[key + ".placeholder"] = "Unbound";
        }
        LocManager.Instance.GetTable("settings_ui").MergeWith(labels);
    }

    public override void SetupConfigUI(Control optionContainer)
    {
        _options = optionContainer;
        base.SetupConfigUI(optionContainer);
    }
}
