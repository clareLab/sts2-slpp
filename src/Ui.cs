using Godot;

namespace slpp;

internal static class Ui
{
    internal static Label Text(string text, int size = 20)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    internal static Button Button(string text, Action action, string name = "")
    {
        var button = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None, Name = name };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.CustomMinimumSize = new Vector2(68, 34);
        button.Pressed += action;
        return button;
    }

    internal static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("17201fee"),
            BorderColor = new Color("697364"),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        return panel;
    }

    internal static MarginContainer Padding(Control parent, int amount)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, amount);
        parent.AddChild(margin);
        return margin;
    }
}
