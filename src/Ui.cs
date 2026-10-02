using Godot;

namespace slpp;

internal static class Ui
{
    private static Theme? _theme;
    internal static Theme Theme => _theme ??= CreateTheme();
    internal const string BackIcon = "res://images/atlases/compressed.sprites/back_button_arrow.tres";
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
        if (!ResourceLoader.Exists(path)) { button.Text = label; return; }
        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = GD.Load<Texture2D>(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FlipH = flip
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 6);
    }

    internal static PanelContainer Panel() => new() { Theme = Theme };

    private static StyleBoxFlat Surface(string background, string border, int padding = 2) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthBottom = 2,
        BorderWidthTop = 2,
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        CornerDetail = 1,
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
        foreach (string type in new[] { "Button", "MenuButton" })
        {
            theme.SetStylebox("normal", type, Surface("2e4351", "526c75", 6));
            theme.SetStylebox("hover", type, Surface("526575", "f2d68d", 6));
            theme.SetStylebox("pressed", type, Surface("15232d", "d1ac60", 6));
            theme.SetStylebox("hover_pressed", type, Surface("203440", "f2d68d", 6));
            theme.SetStylebox("disabled", type, Surface("23323a", "3c4c53", 6));
        }
        foreach (string type in new[] { "Label", "Button", "MenuButton", "PopupMenu", "Tree", "TooltipLabel" })
        {
            theme.SetColor("font_color", type, new Color("eee5cf"));
            theme.SetColor("font_hover_color", type, new Color("fff2cd"));
            theme.SetColor("font_pressed_color", type, new Color("f2d68d"));
            theme.SetColor("font_disabled_color", type, new Color("83918d"));
        }
        theme.SetStylebox("panel", "PopupMenu", Surface("1b2b35", "83918d", 8));
        theme.SetStylebox("hover", "PopupMenu", Surface("465c69", "d1ac60", 4));
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetStylebox("panel", "TooltipPanel", Surface("1b2b35", "83918d", 8));
        theme.SetStylebox("panel", "AcceptDialog", Surface("1b2b35", "83918d", 16));
        var window = Surface("1b2b35", "83918d");
        window.ExpandMarginTop = 32;
        theme.SetStylebox("embedded_border", "Window", window);
        theme.SetStylebox("embedded_unfocused_border", "Window", window);
        theme.SetColor("title_color", "Window", new Color("eee5cf"));
        theme.SetConstant("title_height", "Window", 32);
        theme.SetStylebox("panel", "Tree", Surface("14232b", "526c75", 6));
        theme.SetStylebox("selected", "Tree", Surface("465c69", "d1ac60", 2));
        theme.SetStylebox("selected_focus", "Tree", Surface("465c69", "d1ac60", 2));
        theme.SetStylebox("title_button_normal", "Tree", Surface("2e4351", "526c75", 4));
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
