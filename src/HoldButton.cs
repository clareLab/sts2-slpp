using Godot;

namespace slpp;

internal sealed class HoldButton
{
    private readonly Button _button;
    private readonly Control _border;
    private readonly Action _activate;
    private readonly Func<ulong> _clock;
    private ulong? _started;
    private Vector2[] _outline = [];
    private float _length;
    internal float Progress { get; private set; }

    internal HoldButton(Button button, Action activate, Func<ulong>? clock = null)
    {
        _button = button;
        _activate = activate;
        _clock = clock ?? Time.GetTicksMsec;
        _border = new Control { Name = "HoldBorder", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        button.AddChild(_border);
        _border.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _border.Resized += Outline;
        _border.Draw += Draw;
        button.ButtonDown += () =>
        {
            if (button.Disabled || !button.IsVisibleInTree()) return;
            _started = _clock();
            Progress = 0;
            _border.Show();
            _border.QueueRedraw();
        };
        button.ButtonUp += Cancel;
        button.MouseExited += Cancel;
        button.VisibilityChanged += () => { if (!button.IsVisibleInTree()) Cancel(); };
    }

    internal void Cancel()
    {
        _started = null;
        Progress = 0;
        _border.Hide();
    }

    internal void Tick(bool allowed)
    {
        if (_started is not { } started) return;
        if (!allowed || _button.Disabled || !_button.IsVisibleInTree() || !_button.IsPressed()) { Cancel(); return; }
        Progress = Math.Clamp((_clock() - started) / 1000f, 0, 1);
        _border.QueueRedraw();
        if (Progress < 1) return;
        Cancel();
        _activate();
    }

    private void Outline()
    {
        var size = _border.Size;
        const float inset = 1;
        float radius = Math.Max(0, Math.Min(5, Math.Min(size.X, size.Y) / 2 - inset));
        var points = new List<Vector2> { new(size.X / 2, inset) };
        Vector2[] centers = [new(size.X - inset - radius, inset + radius), new(size.X - inset - radius, size.Y - inset - radius), new(inset + radius, size.Y - inset - radius), new(inset + radius, inset + radius)];
        for (int corner = 0; corner < centers.Length; corner++)
            for (int step = 0; step <= 8; step++)
            {
                float angle = (corner - 1 + step / 8f) * Mathf.Pi / 2;
                points.Add(centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        points.Add(points[0]);
        _outline = points.ToArray();
        _length = Enumerable.Range(1, _outline.Length - 1).Sum(i => _outline[i - 1].DistanceTo(_outline[i]));
    }

    private void Draw()
    {
        if (_outline.Length == 0) Outline();
        _border.DrawPolyline(_outline, new Color("526c75"), 2, true);
        float remaining = _length * Progress;
        if (remaining <= 0) return;
        var points = new List<Vector2> { _outline[0] };
        for (int i = 1; i < _outline.Length && remaining > 0; i++)
        {
            float length = _outline[i - 1].DistanceTo(_outline[i]);
            if (length <= 0) continue;
            points.Add(_outline[i - 1].Lerp(_outline[i], Math.Min(1, remaining / length)));
            remaining -= length;
        }
        _border.DrawPolyline(points.ToArray(), new Color("f2d68d"), 2, true);
    }
}
