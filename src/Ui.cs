using Godot;

namespace slpp;

internal static class Ui
{
    private static Theme? _theme;
    internal static Theme Theme => _theme ??= CreateTheme();
    internal const string BackIcon = "res://images/atlases/compressed.sprites/back_button_arrow.tres";
    internal const string CloseIcon = "res://images/atlases/compressed.sprites/back_button_x.tres";
    internal const string KeysIcon = "res://images/ui/keyboard_icon_ninepatch.png";
    internal const string PositionIcon = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_map.tres";
    internal const string MenuIcon = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres";

    internal static Label Text(string text, int size = 20)
    {
        var label = new Label { Text = text, Theme = Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    internal static Button Button(string label, Action action, string name, bool flip = false)
    {
        var button = new Button { Name = name };
        Icon(button, BackIcon, label, flip);
        button.Pressed += action;
        return button;
    }

    internal static void Icon(Button button, string path, string label, bool flip = false)
    {
        button.Theme = Theme;
        button.Flat = false;
        button.FocusMode = Control.FocusModeEnum.None;
        button.CustomMinimumSize = new Vector2(40, 40);
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.TooltipText = label;
        button.MouseEntered += () => { if (!button.Disabled) button.SelfModulate = new Color("fff2cd"); };
        button.MouseExited += () => button.SelfModulate = Colors.White;
        if (!ResourceLoader.Exists(path)) { button.Text = label; return; }
        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = Texture(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FlipH = flip
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 6);
    }

    internal static string ShortcutIcon(Shortcut shortcut) => shortcut switch
    {
        Shortcut.PreviousTurn => "res://images/atlases/ui_atlas.sprites/settings_tiny_left_arrow.tres",
        Shortcut.NextTurn => "res://images/atlases/ui_atlas.sprites/settings_tiny_right_arrow.tres",
        Shortcut.RestartSeed => "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_floor.tres",
        Shortcut.RandomSeed => "res://images/packed/statistics_screen/stats_questionmark.png",
        Shortcut.Timeline => "res://images/atlases/ui_atlas.sprites/top_bar/timer_icon.tres",
        _ => BackIcon
    };

    internal static string RoomIcon(string label) => "res://images/atlases/ui_atlas.sprites/map/icons/map_" + (label switch
    {
        "Combat" => "monster",
        "Elite" => "elite",
        "Boss" => "elite",
        "Rest site" => "rest",
        "Shop" => "shop",
        "Treasure" => "chest",
        _ => "unknown"
    }) + ".tres";

    private static Texture2D Texture(string path)
    {
        var texture = GD.Load<Texture2D>(path);
        return texture is AtlasTexture atlas ? new AtlasTexture { Atlas = atlas.Atlas, Region = atlas.Region, FilterClip = true } : texture;
    }

    internal static void TreeIcon(TreeItem item, int column, string path)
    {
        if (!ResourceLoader.Exists(path)) return;
        item.SetIcon(column, Texture(path));
        item.SetIconMaxWidth(column, 22);
    }

    internal static Button TextButton(string text, Action action, string name)
    {
        var button = new Button { Text = text, Name = name, Theme = Theme, CustomMinimumSize = new Vector2(88, 36), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.Pressed += action;
        return button;
    }

    internal static (Button Button, Label Key) MenuRow(string text, string path, Action action, string name)
    {
        var button = new Button { Name = name, Theme = Theme, ThemeTypeVariation = "SlppMenuRow", CustomMinimumSize = new Vector2(0, 36), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var content = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        content.AddThemeConstantOverride("margin_left", 8);
        content.AddThemeConstantOverride("margin_right", 8);
        content.AddThemeConstantOverride("margin_top", 4);
        content.AddThemeConstantOverride("margin_bottom", 4);
        button.AddChild(content);
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        content.AddChild(row);
        var icon = new TextureRect
        {
            Texture = Texture(ResourceLoader.Exists(path) ? path : MenuIcon),
            CustomMinimumSize = new Vector2(24, 24),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddChild(icon);
        if (path == KeysIcon)
        {
            var letter = Text("K", 14);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            letter.VerticalAlignment = VerticalAlignment.Center;
            icon.AddChild(letter);
            letter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        var label = Text(text);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        var key = Text("", 16);
        key.CustomMinimumSize = new Vector2(102, 0);
        key.VerticalAlignment = VerticalAlignment.Center;
        key.HorizontalAlignment = HorizontalAlignment.Right;
        key.AddThemeColorOverride("font_color", new Color("b3c0c2"));
        row.AddChild(key);
        button.Pressed += action;
        return (button, key);
    }

    internal static PanelContainer Panel() => new() { Theme = Theme };

    private static StyleBoxFlat Surface(string background, string border, int padding = 2) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthBottom = 1,
        BorderWidthTop = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        CornerDetail = 8,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = padding,
        ContentMarginBottom = padding
    };

    private static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 20 };
        const string font = "res://themes/kreon_regular_shared.tres";
        if (ResourceLoader.Exists(font)) theme.DefaultFont = GD.Load<Font>(font);
        var panel = Surface("1b2b35", "83918d");
        panel.ShadowColor = new Color("0b141c");
        panel.ShadowSize = 2;
        panel.ShadowOffset = new Vector2(0, 2);
        theme.SetStylebox("panel", "PanelContainer", panel);
        foreach (string type in new[] { "Button" })
        {
            theme.SetStylebox("normal", type, Surface("2e4351", "526c75", 6));
            theme.SetStylebox("hover", type, Surface("526575", "f2d68d", 6));
            theme.SetStylebox("pressed", type, Surface("15232d", "d1ac60", 6));
            theme.SetStylebox("hover_pressed", type, Surface("526575", "fff2cd", 6));
            theme.SetStylebox("disabled", type, Surface("23323a", "3c4c53", 6));
        }
        theme.SetTypeVariation("SlppMenuRow", "Button");
        theme.SetStylebox("normal", "SlppMenuRow", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "SlppMenuRow", new StyleBoxEmpty());
        foreach (string type in new[] { "Label", "Button", "Tree", "TooltipLabel" })
        {
            theme.SetColor("font_color", type, new Color("eee5cf"));
            theme.SetColor("font_hover_color", type, new Color("fff2cd"));
            theme.SetColor("font_pressed_color", type, new Color("f2d68d"));
            theme.SetColor("font_disabled_color", type, new Color("83918d"));
        }
        theme.SetStylebox("panel", "TooltipPanel", Surface("1b2b35", "83918d", 10));
        var separator = new StyleBoxLine { Color = new Color("405662"), Thickness = 1, ContentMarginTop = 5, ContentMarginBottom = 5 };
        theme.SetStylebox("separator", "HSeparator", separator);
        theme.SetConstant("separation", "HSeparator", 10);
        theme.SetConstant("v_separation", "Tree", 8);
        theme.SetConstant("h_separation", "Tree", 8);
        theme.SetStylebox("panel", "Tree", Surface("14232b", "526c75", 6));
        theme.SetStylebox("selected", "Tree", Surface("465c69", "d1ac60", 2));
        theme.SetStylebox("selected_focus", "Tree", Surface("465c69", "d1ac60", 2));
        var heading = new StyleBoxFlat
        {
            BgColor = new Color("2e4351"),
            BorderColor = new Color("526c75"),
            BorderWidthBottom = 1,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        foreach (string state in new[] { "normal", "hover", "pressed" }) theme.SetStylebox("title_button_" + state, "Tree", heading);
        return theme;
    }

    internal static MarginContainer Padding(Control parent, int amount)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, amount);
        parent.AddChild(margin);
        return margin;
    }
}
