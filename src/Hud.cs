using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace slpp;

internal static class Hud
{
    private static CanvasLayer? _layer;
    private static PanelContainer _panel = null!;
    private static Label _status = null!;
    private static Button _undo = null!;
    private static Button _redo = null!;
    private static MenuButton _menuButton = null!;
    private static PopupMenu _menu = null!;
    private static AcceptDialog _history = null!;
    private static AcceptDialog _shortcuts = null!;
    private static Tree _rooms = null!;
    private static Tree _points = null!;
    private static ColorRect _shield = null!;
    private static readonly Dictionary<Shortcut, Label> Bindings = [];
    private static readonly HashSet<Key> Held = [];
    private static string _listVersion = "";
    private static bool _dragging;
    private static bool _disabled;
    private static Vector2 _dragOffset;
    private const int ShortcutsItem = 100;
    private const int ResetItem = 101;
    internal static bool ModalOpen => GodotObject.IsInstanceValid(_history) && (_history.Visible || _shortcuts.Visible || _menu.Visible);

    internal static void Install()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        _layer = new CanvasLayer { Layer = 100, Name = "slpp" };
        tree.Root.AddChild(_layer);
        _shield = new ColorRect { Color = new Color(0, 0, 0, .2f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false, Name = "SlppInputShield" };
        _layer.AddChild(_shield);
        _panel = Ui.Panel();
        _panel.Name = "SlppToolbar";
        _layer.AddChild(_panel);
        var box = new VBoxContainer();
        Ui.Padding(_panel, 4).AddChild(box);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        box.AddChild(row);
        var handle = Ui.Text("SL++", 16);
        handle.Name = "SlppDragHandle";
        handle.CustomMinimumSize = new Vector2(48, 0);
        handle.HorizontalAlignment = HorizontalAlignment.Center;
        handle.VerticalAlignment = VerticalAlignment.Center;
        handle.MouseFilter = Control.MouseFilterEnum.Stop;
        handle.MouseDefaultCursorShape = Control.CursorShape.Drag;
        handle.TooltipText = "Drag to move";
        handle.GuiInput += DragInput;
        row.AddChild(handle);
        _undo = Ui.Button("Undo", () => Run(() => Execute(Shortcut.Undo)), "SlppUndo");
        _redo = Ui.Button("Redo", () => Run(() => Execute(Shortcut.Redo)), "SlppRedo");
        row.AddChild(_undo);
        row.AddChild(_redo);
        _menuButton = new MenuButton { Text = "Menu", Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(68, 34), Name = "SlppMenuButton" };
        _menuButton.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(_menuButton);
        _menu = _menuButton.GetPopup();
        _menu.Name = "SlppMenu";
        _menu.AddThemeFontSizeOverride("font_size", 20);
        foreach (var shortcut in new[] { Shortcut.PreviousTurn, Shortcut.NextTurn, Shortcut.RestartRoom, Shortcut.RestartSeed, Shortcut.RandomSeed, Shortcut.Timeline })
        {
            if (shortcut is Shortcut.RestartRoom or Shortcut.Timeline) _menu.AddSeparator();
            _menu.AddItem(SlppConfig.Name(shortcut), (int)shortcut);
        }
        _menu.AddItem("Shortcuts", ShortcutsItem);
        _menu.AddSeparator();
        _menu.AddItem("Reset position", ResetItem);
        _menu.IdPressed += MenuSelected;
        _status = Ui.Text("", 16);
        _status.Name = "SlppStatus";
        box.AddChild(_status);
        _history = Dialog("History", "SlppHistory", new Vector2I(560, 360));
        _history.OkButtonText = "Load";
        _history.AddCancelButton("Close");
        var lists = new HBoxContainer();
        lists.AddThemeConstantOverride("separation", 12);
        _history.AddChild(lists);
        _rooms = List("SlppRooms", ["Floor", "Room"], 200);
        _points = List("SlppPoints", ["Step", "Turn", "Action"], 320);
        lists.AddChild(_rooms);
        lists.AddChild(_points);
        _rooms.ItemSelected += () => PopulatePoints(_rooms.GetSelected().GetMetadata(0).AsInt32());
        _rooms.ItemActivated += () => LoadPoint(_rooms.GetSelected().GetMetadata(0).AsInt32(), 0);
        _points.ItemActivated += LoadSelected;
        _history.Confirmed += LoadSelected;
        _shortcuts = Dialog("Shortcuts", "SlppShortcutList", new Vector2I(360, 340));
        var keys = new GridContainer { Columns = 2 };
        keys.AddThemeConstantOverride("h_separation", 32);
        keys.AddThemeConstantOverride("v_separation", 10);
        _shortcuts.AddChild(keys);
        foreach (var shortcut in Enum.GetValues<Shortcut>())
        {
            var label = Ui.Text(SlppConfig.Name(shortcut), 18);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            keys.AddChild(label);
            var key = Ui.Text("", 18);
            key.Name = "SlppBinding" + shortcut;
            key.HorizontalAlignment = HorizontalAlignment.Right;
            keys.AddChild(key);
            Bindings.Add(shortcut, key);
        }
    }

    private static AcceptDialog Dialog(string title, string name, Vector2I size)
    {
        var dialog = new AcceptDialog { Title = title, Name = name, MinSize = size, OkButtonText = "Close", Unresizable = true };
        _layer!.AddChild(dialog);
        return dialog;
    }

    private static Tree List(string name, string[] columns, int width)
    {
        var list = new Tree
        {
            Name = name,
            Columns = columns.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            CustomMinimumSize = new Vector2(width, 260),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SelectMode = Tree.SelectModeEnum.Row
        };
        for (int i = 0; i < columns.Length; i++)
        {
            list.SetColumnTitle(i, columns[i]);
            if (i < columns.Length - 1) { list.SetColumnExpand(i, false); list.SetColumnCustomMinimumWidth(i, 48); }
        }
        return list;
    }

    private static void MenuSelected(long id)
    {
        if (id == ResetItem) ResetPosition();
        else if (id == ShortcutsItem) _shortcuts.CallDeferred(Window.MethodName.PopupCentered);
        else Run(() => Execute((Shortcut)id));
    }

    private static void LoadSelected()
    {
        if (_rooms.GetSelected() is not { } room || _points.GetSelected() is not { } point) return;
        LoadPoint(room.GetMetadata(0).AsInt32(), point.GetMetadata(0).AsInt32());
    }

    private static void LoadPoint(int room, int point)
    {
        _history.Hide();
        Run(() => Recorder.Restore(room, point));
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
            case Shortcut.Timeline: _history.CallDeferred(Window.MethodName.PopupCentered); break;
        }
        return Task.CompletedTask;
    }

    private static void DragInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
        _dragging = true;
        _dragOffset = _panel.GetGlobalMousePosition() - _panel.Position;
    }

    private static void SavePosition()
    {
        _dragging = false;
        SlppConfig.Instance.Save();
    }

    private static void ResetPosition()
    {
        _dragging = false;
        SlppConfig.ToolbarX = SlppConfig.ToolbarY = -1;
        SlppConfig.Instance.Save();
    }

    internal static bool HandleInput(InputEvent input)
    {
        if (_layer == null || _disabled) return false;
        if (_dragging && input is InputEventMouse)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) SavePosition();
            return true;
        }
        if (input is not InputEventKey key) return false;
        if (!key.Pressed && Held.Remove(key.Keycode)) return true;
        if (!NGame.IsGameFocusedWindow()) { Held.Clear(); return false; }
        if (SlppConfig.IsOpen || ModalOpen) return false;
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
        if (_layer == null || _disabled) return;
        bool allowed = GameBridge.State == null || GameBridge.Singleplayer;
        _layer.Visible = allowed && GameBridge.State != null && !GameBridge.Manager.IsPaused && !SlppConfig.IsOpen;
        if (!_layer.Visible) { _history.Hide(); _shortcuts.Hide(); _menu.Hide(); }
        if (!NGame.IsGameFocusedWindow()) { Held.Clear(); if (_dragging) SavePosition(); }
        _panel.Visible = (SlppConfig.Toolbar && Recorder.History != null) || Recorder.Faulted;
        _status.Text = Recorder.Busy ? "Restoring..." : "History paused";
        _status.TooltipText = Recorder.Faulted ? Recorder.Status : "";
        _status.MouseFilter = Control.MouseFilterEnum.Pass;
        _status.Visible = Recorder.Busy || (SlppConfig.Status && Recorder.Faulted);
        _shield.Visible = Recorder.Busy;
        _undo.Visible = _redo.Visible = SlppConfig.Actions;
        _undo.Disabled = _redo.Disabled = _menuButton.Disabled = Recorder.Busy;
        _undo.TooltipText = Tip(Shortcut.Undo);
        _redo.TooltipText = Tip(Shortcut.Redo);
        for (int i = 0; i < _menu.ItemCount; i++)
        {
            int id = _menu.GetItemId(i);
            if (!_menu.IsItemSeparator(i) && id < ShortcutsItem)
                _menu.SetItemDisabled(i, Recorder.Busy || !SlppConfig.Enabled((Shortcut)id));
        }
        foreach (var pair in Bindings)
            pair.Value.Text = !SlppConfig.Hotkeys || !SlppConfig.Enabled(pair.Key) ? "Off" : SlppConfig.Binding(pair.Key)?.ToString() ?? "Unbound";
        Vector2 viewport = ((SceneTree)Engine.GetMainLoop()).Root.GetVisibleRect().Size;
        _shield.Size = viewport;
        _panel.Size = _panel.GetCombinedMinimumSize();
        float scale = Math.Min(Math.Clamp(SlppConfig.Scale, 80, 140) / 100f,
            Math.Min((viewport.X - 16) / _panel.Size.X, (viewport.Y - 16) / _panel.Size.Y));
        _panel.Scale = Vector2.One * Math.Max(.1f, scale);
        Vector2 limit = (viewport - _panel.Size * _panel.Scale - Vector2.One * 8).Max(Vector2.One * 8);
        Vector2 position = new(SlppConfig.AlignRight ? limit.X - 8 : 16, 88);
        if (float.IsFinite(SlppConfig.ToolbarX) && float.IsFinite(SlppConfig.ToolbarY) && SlppConfig.ToolbarX >= 0 && SlppConfig.ToolbarY >= 0)
            position = new Vector2(SlppConfig.ToolbarX, SlppConfig.ToolbarY) * viewport;
        if (_dragging)
        {
            position = (_panel.GetGlobalMousePosition() - _dragOffset).Clamp(Vector2.One * 8, limit);
            SlppConfig.ToolbarX = position.X / viewport.X;
            SlppConfig.ToolbarY = position.Y / viewport.Y;
        }
        _panel.Position = position.Clamp(Vector2.One * 8, limit);
        RefreshHistory();
    }

    private static void RefreshHistory()
    {
        if (Recorder.History is not { } h) return;
        string version = $"{h.RunKey}:{h.Rooms.Count}:{h.RoomCursor}:{h.PointCursor}:{h.Current?.Points.Count}";
        if (_listVersion == version) return;
        _listVersion = version;
        _rooms.Clear();
        var root = _rooms.CreateItem();
        for (int i = 0; i < h.Rooms.Count; i++)
        {
            var room = h.Rooms[i];
            var item = _rooms.CreateItem(root);
            item.SetText(0, room.Floor.ToString());
            item.SetText(1, room.Label.Split(['\u00b7', '/']).Last().Trim());
            item.SetMetadata(0, i);
            if (i == h.RoomCursor) item.Select(0);
        }
        PopulatePoints(h.RoomCursor);
    }

    private static void PopulatePoints(int index)
    {
        _points.Clear();
        if (Recorder.History?.Rooms.ElementAtOrDefault(index) is not { } room) return;
        var root = _points.CreateItem();
        for (int i = 0; i < room.Points.Count; i++)
        {
            var point = room.Points[i];
            var item = _points.CreateItem(root);
            item.SetText(0, i.ToString());
            item.SetText(1, point.Turn > 0 ? point.Turn.ToString() : "");
            item.SetText(2, point.Label);
            item.SetMetadata(0, i);
            if (i == (index == Recorder.History.RoomCursor ? Recorder.History.PointCursor : 0)) item.Select(0);
        }
    }

    internal static void Disable()
    {
        _disabled = true;
        _dragging = false;
        Held.Clear();
        if (_layer != null) _layer.Visible = false;
        if (GodotObject.IsInstanceValid(_shield)) _shield.Visible = false;
        if (GodotObject.IsInstanceValid(_history)) _history.Hide();
        if (GodotObject.IsInstanceValid(_shortcuts)) _shortcuts.Hide();
        if (GodotObject.IsInstanceValid(_menu)) _menu.Hide();
    }

    private static string Tip(Shortcut shortcut)
    {
        string? key = SlppConfig.Binding(shortcut)?.ToString();
        return SlppConfig.Name(shortcut) + (key != null && SlppConfig.Hotkeys ? "\n" + key : "");
    }
}
