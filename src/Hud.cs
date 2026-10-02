using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace slpp;

internal static class Hud
{
    private static CanvasLayer? _layer;
    private static PanelContainer _panel = null!;
    private static Label _status = null!;
    private static VBoxContainer _details = null!;
    private static HBoxContainer _restart = null!;
    private static ItemList _rooms = null!;
    private static ItemList _points = null!;
    private static ColorRect _shield = null!;
    private static readonly Dictionary<Shortcut, Button> Buttons = [];
    private static readonly HashSet<Key> Held = [];
    private static string _listVersion = "";
    private static bool _expanded;

    internal static void Install()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        _layer = new CanvasLayer { Layer = 100, Name = "slpp" };
        tree.Root.AddChild(_layer);
        _shield = new ColorRect { Color = new Color(0, 0, 0, .35f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _layer.AddChild(_shield);
        _panel = Ui.Panel();
        _panel.Name = "SlppToolbar";
        _layer.AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        Ui.Padding(_panel, 8).AddChild(box);
        var row = new HBoxContainer();
        box.AddChild(row);
        Add(row, Shortcut.Undo, "Undo");
        Add(row, Shortcut.Redo, "Redo");
        Add(row, Shortcut.PreviousTurn, "Prev turn");
        Add(row, Shortcut.NextTurn, "Next turn");
        Add(row, Shortcut.Timeline, "Timeline");
        _status = Ui.Text(Recorder.Status, 16);
        _status.CustomMinimumSize = new Vector2(460, 0);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_status);
        _restart = new HBoxContainer();
        box.AddChild(_restart);
        Add(_restart, Shortcut.RestartRoom, "Restart room");
        Add(_restart, Shortcut.RestartSeed, "Same seed");
        Add(_restart, Shortcut.RandomSeed, "Random seed");
        _details = new VBoxContainer { Visible = false };
        box.AddChild(_details);
        _rooms = new ItemList { CustomMinimumSize = new Vector2(460, 130), Name = "SlppRooms" };
        _points = new ItemList { CustomMinimumSize = new Vector2(460, 170), Name = "SlppPoints" };
        _rooms.AddThemeFontSizeOverride("font_size", 17);
        _points.AddThemeFontSizeOverride("font_size", 17);
        _details.AddChild(_rooms);
        _details.AddChild(_points);
        _rooms.ItemSelected += index => PopulatePoints((int)index);
        _rooms.ItemActivated += index => Run(() => Recorder.Restore((int)index, 0));
        _points.ItemActivated += index =>
        {
            int room = _rooms.GetSelectedItems().FirstOrDefault(Recorder.History?.RoomCursor ?? 0);
            Run(() => Recorder.Restore(room, (int)index));
        };
        var hint = Ui.Text("Double-click to restore. New actions replace future steps.", 15);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _details.AddChild(hint);
    }

    private static void Add(HBoxContainer row, Shortcut shortcut, string label)
    {
        var button = Ui.Button(label, () => Run(() => Execute(shortcut)), "Slpp" + shortcut);
        button.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(button);
        Buttons.Add(shortcut, button);
    }

    internal static async void Run(Func<Task> callback)
    {
        try { await callback(); }
        catch (Exception e) { GD.PrintErr("[slpp] " + e.Message); }
    }

    private static Task Execute(Shortcut shortcut)
    {
        if (Recorder.Busy || SlppConfig.IsOpen) return Task.CompletedTask;
        switch (shortcut)
        {
            case Shortcut.Undo: return Recorder.Step(-1);
            case Shortcut.Redo: return Recorder.Step(1);
            case Shortcut.PreviousTurn: return Recorder.TurnStep(-1);
            case Shortcut.NextTurn: return Recorder.TurnStep(1);
            case Shortcut.RestartRoom: return Recorder.History is { } h ? Recorder.Restore(h.RoomCursor, 0) : Task.CompletedTask;
            case Shortcut.RestartSeed: return Recorder.Restart(false);
            case Shortcut.RandomSeed: return Recorder.Restart(true);
            case Shortcut.Timeline: _expanded = !_expanded; break;
        }
        return Task.CompletedTask;
    }

    internal static bool HandleInput(InputEvent input)
    {
        if (_layer == null || input is not InputEventKey key) return false;
        if (!key.Pressed && Held.Remove(key.Keycode)) return true;
        if (!NGame.IsGameFocusedWindow()) { Held.Clear(); return false; }
        if (SlppConfig.IsOpen) return false;
        if (GameBridge.State != null && !GameBridge.Singleplayer) return false;
        if (((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        if (MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.IsConsoleVisible || NGame.Instance?.Transition.InTransition == true) return false;
        if (!SlppConfig.Hotkeys || Recorder.History == null || GameBridge.Manager.IsPaused) return false;
        var chord = KeyChord.From(key);
        var matches = Enum.GetValues<Shortcut>().Where(s => SlppConfig.Enabled(s) && SlppConfig.Binding(s) == chord).ToArray();
        if (matches.Length == 0) return false;
        if (key.Pressed)
        {
            Held.Add(key.Keycode);
            if (!key.Echo && matches.Length == 1) Run(() => Execute(matches[0]));
        }
        return true;
    }

    internal static void Tick()
    {
        if (_layer == null) return;
        bool allowed = GameBridge.State == null || GameBridge.Singleplayer;
        _layer.Visible = allowed && GameBridge.State != null && !GameBridge.Manager.IsPaused && !SlppConfig.IsOpen;
        if (!NGame.IsGameFocusedWindow()) Held.Clear();
        _panel.Visible = (SlppConfig.Toolbar && Recorder.History != null) || Recorder.Faulted;
        _status.Text = Recorder.Status;
        _status.Visible = SlppConfig.Status || Recorder.Busy || Recorder.Faulted;
        _shield.Visible = Recorder.Busy || Recorder.Faulted;
        Vector2 viewport = ((SceneTree)Engine.GetMainLoop()).Root.GetVisibleRect().Size;
        _shield.Size = viewport;
        _restart.Visible = SlppConfig.RoomRestart || SlppConfig.QuickRestart || Recorder.Faulted;
        _details.Visible = SlppConfig.Timeline && _expanded;
        foreach (var pair in Buttons)
        {
            pair.Value.Disabled = Recorder.Busy || SlppConfig.IsOpen;
            pair.Value.Visible = SlppConfig.Enabled(pair.Key) || (Recorder.Faulted && pair.Key == Shortcut.RestartRoom);
            pair.Value.TooltipText = Tip(pair.Key);
        }
        foreach (Control control in new Control[] { _panel })
        {
            control.Size = control.GetCombinedMinimumSize();
            float scale = Math.Min(Math.Clamp(SlppConfig.Scale, 80, 140) / 100f, Math.Min((viewport.X - 32) / control.Size.X, (viewport.Y - 104) / control.Size.Y));
            control.Scale = Vector2.One * scale;
            control.Position = new Vector2(SlppConfig.AlignRight ? viewport.X - control.Size.X * scale - 16 : 16, 88);
        }
        var h = Recorder.History;
        if (h != null)
        {
            string version = $"{h.RunKey}:{h.Rooms.Count}:{h.RoomCursor}:{h.PointCursor}:{h.Current?.Points.Count}";
            if (_listVersion != version)
            {
                _listVersion = version;
                _rooms.Clear();
                foreach (var room in h.Rooms) _rooms.AddItem($"{room.Label} · {Math.Max(0, room.Points.Count - 1)} steps");
                _rooms.Select(h.RoomCursor);
                PopulatePoints(h.RoomCursor);
                if (_points.ItemCount > h.PointCursor) _points.Select(h.PointCursor);
            }
        }
    }

    private static string Tip(Shortcut shortcut)
    {
        string? key = SlppConfig.Binding(shortcut)?.ToString();
        return SlppConfig.Name(shortcut) + (key != null && SlppConfig.Hotkeys ? " · " + key : "");
    }

    private static void PopulatePoints(int index)
    {
        _points.Clear();
        if (Recorder.History?.Rooms.ElementAtOrDefault(index) is not { } room) return;
        for (int i = 0; i < room.Points.Count; i++)
        {
            var point = room.Points[i];
            _points.AddItem($"{i} · Turn {point.Turn} · {point.Label}{(point.AwaitingChoice ? " (choosing)" : "")}");
        }
    }
}
