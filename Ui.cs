using Godot;

namespace slpp;

internal static class Ui
{
    internal static Label Text(string text, int size = 20)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color("e8e6dc"));
        return label;
    }

    internal static Button Button(string text, Action action, string name = "")
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        if (name != "") button.Name = name;
        button.AddThemeFontSizeOverride("font_size", 20);
        button.CustomMinimumSize = new Vector2(0, 38);
        button.Pressed += action;
        return button;
    }

    internal static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color("17201fee"), BorderColor = new Color("697364"),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
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
