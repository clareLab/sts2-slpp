using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace slpp;

internal static class Hud
{
    private static CanvasLayer? _layer;
    private static PanelContainer _panel = null!;
    private static Control _spinner = null!;
    private static ProgressBar _progress = null!;
    private static Label _status = null!;
    private static Button _historyButton = null!;
    private static Control _resizeHandle = null!;
    private static bool _resizing;
    private static float _resizeStartY;
    private static float _resizeStartHeight;
    private static Vector2 _resizePosition;
    internal const float MinimumHistoryHeight = 280;
    private static Button _undo = null!;
    private static Button _redo = null!;
    private static Button _menuButton = null!;
    private static PanelContainer _menu = null!;
    private static PanelContainer _history = null!;
    private static PanelContainer _shortcuts = null!;
    private static Tree _rooms = null!;
    private static Tree _points = null!;
    private static Button _load = null!;
    private static ColorRect _shield = null!;
    private static PanelContainer? _flyout;
    private static readonly Dictionary<Shortcut, (Button Button, Label Key)> MenuItems = [];
    private static readonly Dictionary<Shortcut, Label> Bindings = [];
    private static readonly HashSet<Key> Held = [];
    private static string _listVersion = "";
    private static bool _dragging;
    private static bool _disabled;
    private static Vector2 _dragOffset;
    internal static bool ModalOpen => GodotObject.IsInstanceValid(_flyout) && _flyout!.Visible;

    internal static void Install()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        _layer = new CanvasLayer { Layer = 100, Name = "slpp" };
        tree.Root.AddChild(_layer);
        _shield = new ColorRect { Color = new Color(0, 0, 0, .2f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false, Name = "SlppInputShield" };
        _layer.AddChild(_shield);
        _shield.GuiInput += input =>
        {
            if (!Recorder.Busy && input is InputEventMouseButton { Pressed: true }) CloseFlyout();
        };
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
        handle.GuiInput += input => DragInput(handle, input);
        row.AddChild(handle);
        _undo = Ui.Button("Undo", () => Run(() => Execute(Shortcut.Undo)), "SlppUndo");
        _redo = Ui.Button("Redo", () => Run(() => Execute(Shortcut.Redo)), "SlppRedo", flip: true);
        row.AddChild(_undo);
        row.AddChild(_redo);
        _menuButton = new Button { Name = "SlppMenuButton", ToggleMode = true };
        Ui.Icon(_menuButton, Ui.MenuIcon, "Menu");
        _menuButton.Pressed += () => { if (_flyout == _menu && ModalOpen) CloseFlyout(); else ShowFlyout(_menu); };
        _historyButton = new Button { Name = "SlppTimeline", ToggleMode = true };
        Ui.Icon(_historyButton, Ui.ShortcutIcon(Shortcut.Timeline), "History");
        _historyButton.Pressed += () => Run(() => Execute(Shortcut.Timeline));
        row.AddChild(_historyButton);
        row.AddChild(_menuButton);
        _spinner = new Control { Name = "SlppSpinner", CustomMinimumSize = new Vector2(32, 40), MouseFilter = Control.MouseFilterEnum.Stop };
        _spinner.Draw += () =>
        {
            float angle = Time.GetTicksMsec() / 180f;
            _spinner.DrawArc(_spinner.Size / 2, 9, 0, Mathf.Tau, 40, new Color("526c75"), 2, true);
            _spinner.DrawArc(_spinner.Size / 2, 9, angle, angle + 4.4f, 32, new Color("f2d68d"), 2, true);
        };
        row.AddChild(_spinner);
        _progress = new ProgressBar { Name = "SlppProgress", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 3), MouseFilter = Control.MouseFilterEnum.Ignore, MaxValue = 1 };
        _progress.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color("23323a") });
        _progress.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color("d1ac60") });
        box.AddChild(_progress);
        _status = Ui.Text("", 16);
        _status.Name = "SlppStatus";
        box.AddChild(_status);
        _menu = Sheet("SlppMenu", 344, out var menu);
        menu.AddThemeConstantOverride("separation", 2);
        foreach (var shortcut in new[] { Shortcut.PreviousTurn, Shortcut.NextTurn, Shortcut.RestartRoom, Shortcut.RestartSeed, Shortcut.RandomSeed })
        {
            if (shortcut == Shortcut.RestartRoom) menu.AddChild(new HSeparator());
            var item = Ui.MenuRow(SlppConfig.Name(shortcut), Ui.ShortcutIcon(shortcut), () => Run(() => Execute(shortcut)), "Slpp" + shortcut);
            menu.AddChild(item.Button);
            MenuItems.Add(shortcut, item);
        }
        menu.AddChild(new HSeparator());
        menu.AddChild(Ui.MenuRow("Shortcuts", Ui.KeysIcon, () => ShowFlyout(_shortcuts), "SlppShortcuts").Button);
        menu.AddChild(new HSeparator());
        menu.AddChild(Ui.MenuRow("Reset position", Ui.PositionIcon, () => { ResetPosition(); CloseFlyout(); }, "SlppResetPosition").Button);
        _history = Sheet("SlppHistory", 568, out var history);
        Header(history, "History");
        var lists = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", 12);
        history.AddChild(lists);
        _rooms = List("SlppRooms", ["Floor", "Room"], 184);
        _points = List("SlppPoints", ["Step", "Turn", "Action"], 348);
        lists.AddChild(_rooms);
        lists.AddChild(_points);
        _points.GuiInput += input => HistoryPreview.Input(_points, input);
        _points.MouseExited += HistoryPreview.Leave;
        _rooms.ItemSelected += () => PopulatePoints(_rooms.GetSelected().GetMetadata(0).AsInt32());
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        history.AddChild(footer);
        _load = Ui.TextButton("Load", LoadSelected, "SlppLoad");
        footer.AddChild(_load);
        _resizeHandle = new Control { Name = "SlppHistoryResize", CustomMinimumSize = new Vector2(0, 12), MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Vsize, TooltipText = "Drag to resize" };
        _resizeHandle.Draw += () =>
        {
            var center = _resizeHandle.Size / 2;
            var color = new Color(_resizing || new Rect2(Vector2.Zero, _resizeHandle.Size).HasPoint(_resizeHandle.GetLocalMousePosition()) ? "f2d68d" : "83918d");
            _resizeHandle.DrawLine(center + new Vector2(-16, -2), center + new Vector2(16, -2), color, 1, true);
            _resizeHandle.DrawLine(center + new Vector2(-16, 2), center + new Vector2(16, 2), color, 1, true);
        };
        _resizeHandle.GuiInput += ResizeInput;
        history.AddChild(_resizeHandle);
        _shortcuts = Sheet("SlppShortcutList", 344, out var shortcuts);
        Header(shortcuts, "Shortcuts");
        var keys = new GridContainer { Columns = 2 };
        keys.AddThemeConstantOverride("h_separation", 20);
        keys.AddThemeConstantOverride("v_separation", 12);
        shortcuts.AddChild(keys);
        foreach (var shortcut in Enum.GetValues<Shortcut>())
        {
            var label = Ui.WrappedText(SlppConfig.Name(shortcut), 20, 148);
            label.Name = "SlppShortcutName" + shortcut;
            keys.AddChild(label);
            var key = Ui.WrappedText("", 18, 148);
            key.Name = "SlppBinding" + shortcut;
            key.HorizontalAlignment = HorizontalAlignment.Right;
            key.VerticalAlignment = VerticalAlignment.Center;
            keys.AddChild(key);
            Bindings.Add(shortcut, key);
        }
    }

    private static PanelContainer Sheet(string name, int width, out VBoxContainer content)
    {
        var panel = Ui.Panel();
        panel.Name = name;
        panel.Visible = false;
        panel.CustomMinimumSize = new Vector2(width, 0);
        _layer!.AddChild(panel);
        content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 12);
        Ui.Padding(panel, 12).AddChild(content);
        return panel;
    }

    private static void Header(Control parent, string title)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = Ui.Text(title, 22);
        label.Name = "Slpp" + title + "Title";
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        if (title != "History")
        {
            var back = Ui.Button("Back", () => ShowFlyout(_menu), "Slpp" + title + "Back");
            back.CustomMinimumSize = new Vector2(28, 28);
            back.Flat = true;
            row.AddChild(back);
        }
        var close = new Button { Name = "Slpp" + title + "Close" };
        Ui.Icon(close, Ui.CloseIcon, "Close");
        close.CustomMinimumSize = new Vector2(28, 28);
        close.Flat = true;
        close.Pressed += CloseFlyout;
        row.AddChild(close);
        parent.AddChild(row);
    }

    private static void ShowFlyout(PanelContainer panel)
    {
        HistoryPreview.Clear();
        _flyout?.Hide();
        _flyout = panel;
        panel.Show();
        _menuButton.SetPressedNoSignal(panel == _menu);
        _historyButton.SetPressedNoSignal(panel == _history);
        _shield.Color = Colors.Transparent;
        _shield.Show();
        PositionFlyout();
    }

    private static void CloseFlyout()
    {
        HistoryPreview.Clear();
        if (_resizing) SaveHeight();
        _flyout?.Hide();
        _flyout = null;
        _menuButton.SetPressedNoSignal(false);
        _historyButton.SetPressedNoSignal(false);
        _shield.Visible = Recorder.Busy;
    }

    private static void PositionFlyout()
    {
        if (!ModalOpen) return;
        Vector2 viewport = _panel.GetViewportRect().Size;
        _flyout!.Size = _flyout.GetCombinedMinimumSize();
        if (_flyout == _history)
        {
            float requested = float.IsFinite(SlppConfig.HistoryHeight) ? SlppConfig.HistoryHeight : 420;
            float available = _resizing ? viewport.Y - _resizePosition.Y - 8 : Math.Max(viewport.Y - _panel.GetGlobalRect().End.Y - 14, _panel.Position.Y - 14);
            float height = Math.Clamp(requested, MinimumHistoryHeight, Math.Max(MinimumHistoryHeight, available / _panel.Scale.Y));
            _history.Size = new Vector2(_history.Size.X, height);
        }
        float scale = Math.Min(_panel.Scale.X, Math.Min((viewport.X - 16) / _flyout.Size.X, (viewport.Y - 16) / _flyout.Size.Y));
        _flyout.Scale = Vector2.One * Math.Max(.1f, scale);
        Vector2 size = _flyout.Size * _flyout.Scale;
        float gap = 6 * scale;
        float y = _panel.Position.Y + _panel.Size.Y * _panel.Scale.Y + gap;
        if (y + size.Y > viewport.Y - 8) y = _panel.Position.Y - size.Y - gap;
        if (_resizing) { _flyout.Position = _resizePosition; return; }
        _flyout.Position = new Vector2(_panel.Position.X, y).Clamp(Vector2.One * 8, (viewport - size - Vector2.One * 8).Max(Vector2.One * 8));
    }

    private static Tree List(string name, string[] columns, int width)
    {
        var list = new Tree
        {
            Name = name,
            Columns = columns.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            HideFolding = true,
            ScrollHorizontalEnabled = false,
            CustomMinimumSize = new Vector2(width, 120),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SelectMode = Tree.SelectModeEnum.Row
        };
        for (int i = 0; i < columns.Length; i++)
        {
            list.SetColumnTitle(i, i == columns.Length - 1 ? "" : columns[i]);
            list.SetColumnClipContent(i, true);
            list.SetColumnTitleAlignment(i, i == columns.Length - 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            if (i < columns.Length - 1) { list.SetColumnExpand(i, false); list.SetColumnCustomMinimumWidth(i, 56); }
        }
        var heading = Ui.Text(columns[^1], 18);
        heading.Name = "ColumnHeading";
        heading.VerticalAlignment = VerticalAlignment.Center;
        list.AddChild(heading);
        return list;
    }

    private static void LoadSelected()
    {
        if (_rooms.GetSelected() is not { } room || _points.GetSelected() is not { } point) return;
        LoadPoint(room.GetMetadata(0).AsInt32(), point.GetMetadata(0).AsInt32());
    }

    private static void LoadPoint(int room, int point)
    {
        CloseFlyout();
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
        if (shortcut == Shortcut.Timeline)
        {
            if (_flyout == _history && ModalOpen) CloseFlyout(); else ShowFlyout(_history);
            return Task.CompletedTask;
        }
        CloseFlyout();
        switch (shortcut)
        {
            case Shortcut.Undo: return Recorder.Step(-1);
            case Shortcut.Redo: return Recorder.Step(1);
            case Shortcut.PreviousTurn: return Recorder.TurnStep(-1);
            case Shortcut.NextTurn: return Recorder.TurnStep(1);
            case Shortcut.RestartRoom: return Recorder.History is { } h ? Recorder.Restore(h.RoomCursor, 0) : Task.CompletedTask;
            case Shortcut.RestartSeed: return Recorder.Restart(false);
            case Shortcut.RandomSeed: return Recorder.Restart(true);
        }
        return Task.CompletedTask;
    }

    private static void DragInput(Control handle, InputEvent input)
    {
        if (input is not InputEventMouse mouse) return;
        Vector2 pointer = handle.GetGlobalTransform() * mouse.Position;
        if (mouse is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && !Recorder.Busy)
        {
            _dragging = true;
            _dragOffset = pointer - _panel.Position;
        }
        else if (_dragging)
        {
            if (mouse is InputEventMouseMotion)
            {
                if ((mouse.ButtonMask & MouseButtonMask.Left) == 0) { SavePosition(); return; }
                MoveToPointer(pointer);
            }
            else if (mouse is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                MoveToPointer(pointer);
                SavePosition();
            }
        }
        handle.AcceptEvent();
    }

    private static void MoveToPointer(Vector2 position)
    {
        Vector2 viewport = _panel.GetViewportRect().Size;
        Vector2 limit = (viewport - _panel.Size * _panel.Scale - Vector2.One * 8).Max(Vector2.One * 8);
        _panel.Position = (position - _dragOffset).Clamp(Vector2.One * 8, limit);
        SlppConfig.ToolbarX = _panel.Position.X / viewport.X;
        SlppConfig.ToolbarY = _panel.Position.Y / viewport.Y;
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
        if (input is not InputEventKey key) return false;
        if (!key.Pressed && Held.Remove(key.Keycode)) return true;
        if (!NGame.IsGameFocusedWindow()) { Held.Clear(); return false; }
        if (SlppConfig.IsOpen) return false;
        if (ModalOpen && key.Keycode == Key.Escape)
        {
            if (key.Pressed && !key.Echo) { Held.Add(key.Keycode); CloseFlyout(); }
            return true;
        }
        if (GameBridge.State != null && !GameBridge.Singleplayer) return false;
        if (((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        if (MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.IsConsoleVisible || NGame.Instance?.Transition.InTransition == true) return false;
        if (!SlppConfig.Hotkeys || Recorder.History == null || GameBridge.Manager.IsPaused) return false;
        var chord = KeyChord.From(key);
        var matches = Enum.GetValues<Shortcut>().Where(s => SlppConfig.Enabled(s) && SlppConfig.Binding(s) == chord).ToArray();
        if (matches.Length == 0 || (ModalOpen && (matches.Length != 1 || matches[0] != Shortcut.Timeline))) return false;
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
        _layer.Visible = allowed && GameBridge.State != null && !GameBridge.Manager.IsPaused && !SlppConfig.IsOpen && NGame.Instance?.Transition.InTransition != true;
        if (!_layer.Visible || Recorder.Busy) CloseFlyout();
        if (_dragging && (!_layer.Visible || !SlppConfig.Toolbar || Recorder.Busy)) SavePosition();
        if (!NGame.IsGameFocusedWindow()) { Held.Clear(); if (_dragging) SavePosition(); if (_resizing) SaveHeight(); }
        _panel.Visible = (SlppConfig.Toolbar && Recorder.History != null) || Recorder.Faulted;
        _spinner.Visible = Recorder.Busy;
        _spinner.TooltipText = Recorder.OperationStage;
        if (Recorder.Busy) _spinner.QueueRedraw();
        _progress.Visible = Recorder.Busy && Recorder.OperationTotal > 0;
        _progress.Value = Recorder.OperationTotal > 0 ? (double)Recorder.OperationStep / Recorder.OperationTotal : 0;
        _status.Text = "History paused";
        _status.TooltipText = Recorder.Faulted ? Recorder.Status : "";
        _status.MouseFilter = Control.MouseFilterEnum.Pass;
        _status.Visible = !Recorder.Busy && SlppConfig.Status && Recorder.Faulted;
        _shield.Visible = Recorder.Busy || ModalOpen;
        _shield.Color = Recorder.Busy ? new Color(0, 0, 0, .2f) : Colors.Transparent;
        _undo.Visible = _redo.Visible = SlppConfig.Actions;
        _historyButton.Visible = SlppConfig.Timeline;
        _historyButton.TooltipText = Tip(Shortcut.Timeline);
        _undo.Disabled = _redo.Disabled = _menuButton.Disabled = _historyButton.Disabled = Recorder.Busy;
        _load.Disabled = Recorder.Busy || _rooms.GetSelected() == null || _points.GetSelected() == null;
        _undo.TooltipText = Tip(Shortcut.Undo);
        _redo.TooltipText = Tip(Shortcut.Redo);
        foreach (var pair in MenuItems)
        {
            pair.Value.Button.Disabled = Recorder.Busy || !SlppConfig.Enabled(pair.Key);
            pair.Value.Key.Text = SlppConfig.Hotkeys ? SlppConfig.Binding(pair.Key)?.ToString() ?? "" : "";
            pair.Value.Button.GetChild<Control>(0).Modulate = pair.Value.Button.Disabled ? new Color("83918d") : Colors.White;
        }
        foreach (var pair in Bindings)
        {
            string binding = !SlppConfig.Hotkeys || !SlppConfig.Enabled(pair.Key) ? "Off" : SlppConfig.Binding(pair.Key)?.ToString() ?? "Unbound";
            if (pair.Value.Text == binding) continue;
            pair.Value.Text = binding;
            Ui.FitLabel(pair.Value, 18);
        }
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
        if (_dragging) position = _panel.Position;
        _panel.Position = position.Clamp(Vector2.One * 8, limit);
        RefreshHistory();
        PositionFlyout();
        if (_history.Visible) { Ui.FitTree(_rooms); Ui.FitTree(_points); }
        if (_history.Visible && _layer.Visible && !_resizing && !_dragging) HistoryPreview.Tick(_points, _rooms, _history, _layer);
        else HistoryPreview.Clear();
        if (_history.Visible) _resizeHandle.QueueRedraw();
    }

    private static void ResizeInput(InputEvent input)
    {
        if (input is not InputEventMouse mouse) return;
        Vector2 pointer = _resizeHandle.GetGlobalTransform() * mouse.Position;
        if (mouse is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _resizing = true;
            _resizeStartY = pointer.Y;
            _resizeStartHeight = _history.Size.Y;
            _resizePosition = _history.Position;
            HistoryPreview.Clear();
        }
        else if (_resizing)
        {
            float max = (_history.GetViewportRect().Size.Y - _resizePosition.Y - 8) / _history.Scale.Y;
            SlppConfig.HistoryHeight = Math.Clamp(_resizeStartHeight + (pointer.Y - _resizeStartY) / _history.Scale.Y, MinimumHistoryHeight, Math.Max(MinimumHistoryHeight, max));
            PositionFlyout();
            if (mouse is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } || (mouse.ButtonMask & MouseButtonMask.Left) == 0) SaveHeight();
        }
        _resizeHandle.AcceptEvent();
    }

    private static void SaveHeight()
    {
        _resizing = false;
        SlppConfig.Instance.Save();
    }

    private static void RefreshHistory()
    {
        if (Recorder.History is not { } h) return;
        string version = $"{h.RunKey}:{h.Rooms.Count}:{h.RoomCursor}:{h.PointCursor}:{h.Current?.Points.Count}";
        if (_listVersion == version) return;
        _listVersion = version;
        HistoryPreview.Clear();
        _rooms.Clear();
        var root = _rooms.CreateItem();
        for (int i = 0; i < h.Rooms.Count; i++)
        {
            var room = h.Rooms[i];
            var item = _rooms.CreateItem(root);
            for (int column = 0; column < _rooms.Columns; column++) item.SetTooltipText(column, " ");
            item.SetText(0, room.Floor.ToString());
            item.SetTextAlignment(0, HorizontalAlignment.Center);
            item.SetText(1, room.Label.Split(['\u00b7', '/']).Last().Trim());
            Ui.TreeIcon(item, 1, Ui.RoomIcon(room.Label));
            item.SetMetadata(0, i);
            if (i == h.RoomCursor) item.Select(0);
        }
        PopulatePoints(h.RoomCursor);
    }

    private static void PopulatePoints(int index)
    {
        HistoryPreview.Clear();
        _points.Clear();
        if (Recorder.History?.Rooms.ElementAtOrDefault(index) is not { } room) return;
        var root = _points.CreateItem();
        for (int i = 0; i < room.Points.Count; i++)
        {
            var point = room.Points[i];
            var item = _points.CreateItem(root);
            for (int column = 0; column < _points.Columns; column++) item.SetTooltipText(column, " ");
            item.SetText(0, i.ToString());
            item.SetTextAlignment(0, HorizontalAlignment.Center);
            item.SetTextAlignment(1, HorizontalAlignment.Center);
            item.SetText(1, point.Turn > 0 ? point.Turn.ToString() : "");
            item.SetText(2, point.Label);
            Ui.TreeIcon(item, 2, HistoryPreview.Icon(room, point));
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
        _flyout = null;
    }

    private static string Tip(Shortcut shortcut)
    {
        string? key = SlppConfig.Binding(shortcut)?.ToString();
        return SlppConfig.Name(shortcut) + (key != null && SlppConfig.Hotkeys ? "\n" + key : "");
    }
}
