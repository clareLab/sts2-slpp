using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace slpp;

internal static class HistoryPreview
{
    private static Control? _anchor;
    private static string _hovered = "";
    private static ulong _hoverSince;
    private static bool _attempted;
    private static Vector2? _pointer;

    internal static void Input(Tree points, InputEvent input)
    {
        if (input is InputEventMouse mouse) _pointer = points.GetGlobalTransform() * mouse.Position;
    }

    internal static void Leave()
    {
        _pointer = null;
        Clear();
    }

    internal static RecordedCommand Capture(RecordedCommand command, CombatReplayEvent evt)
    {
        try
        {
            if (evt.action is NetPlayCardAction play)
                return command with { Card = JsonSerializer.Serialize(play.card.ToCardModel().ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableCard>()) };
            uint? slot = evt.action switch { NetUsePotionAction use => use.potionIndex, NetDiscardPotionGameAction discard => discard.potionSlotIndex, _ => null };
            if (slot.HasValue && GameBridge.State?.GetPlayer(evt.playerId!.Value)?.PotionSlots.ElementAtOrDefault((int)slot.Value) is { } potion)
                return command with { Potion = JsonSerializer.Serialize(potion.ToSerializable((int)slot.Value), JsonSerializationUtility.GetTypeInfo<SerializablePotion>()) };
        }
        catch (Exception error) { GD.PrintErr("[slpp] Preview capture unavailable: " + error.Message); }
        return command;
    }

    private static AbstractModel? Model(RecordedCommand? command)
    {
        if (command?.Card is { } card)
            return CardModel.FromSerializable(JsonSerializer.Deserialize(card, JsonSerializationUtility.GetTypeInfo<SerializableCard>())!);
        if (command?.Potion is { } potion)
            return PotionModel.FromSerializable(JsonSerializer.Deserialize(potion, JsonSerializationUtility.GetTypeInfo<SerializablePotion>())!);
        if (command?.Kind == "action" && GameBridge.Unpack<CombatReplayEvent>(command.Data).action is NetPlayCardAction play)
            return ModelDb.GetById<CardModel>(play.modelId);
        return null;
    }

    private static RecordedCommand? Command(RoomRecord room, TimelinePoint point) => point.AwaitingChoice ? null : room.Commands.ElementAtOrDefault(point.Commands - 1);

    internal static string Icon(RoomRecord room, TimelinePoint point)
    {
        try
        {
            var command = Command(room, point);
            return Model(command) switch
            {
                CardModel card => card.PortraitPath,
                PotionModel potion => potion.ImagePath,
                _ => point.AwaitingChoice ? Ui.KeysIcon : command == null ? Ui.RoomIcon(room.Label) : command.Kind == "action" ? Ui.ShortcutIcon(Shortcut.NextTurn) : Ui.PositionIcon
            };
        }
        catch { return Ui.PositionIcon; }
    }

    internal static void Tick(Tree points, Tree rooms, PanelContainer panel, CanvasLayer layer)
    {
        Vector2 pointer = _pointer.HasValue ? points.GetGlobalTransform().AffineInverse() * _pointer.Value : -Vector2.One;
        var item = new Rect2(Vector2.Zero, points.Size).HasPoint(pointer) ? points.GetItemAtPosition(pointer) : null;
        int roomIndex = rooms.GetSelected()?.GetMetadata(0).AsInt32() ?? -1;
        int pointIndex = item?.GetMetadata(0).AsInt32() ?? -1;
        var room = Recorder.History?.Rooms.ElementAtOrDefault(roomIndex);
        var point = room?.Points.ElementAtOrDefault(pointIndex);
        string key = point == null ? "" : $"{roomIndex}:{pointIndex}:{points.GetScroll()}:{panel.Position}";
        if (key != _hovered)
        {
            Clear();
            _hovered = key;
            _hoverSince = Time.GetTicksMsec();
        }
        if (point == null || _attempted || NHoverTipSet.shouldBlockHoverTips || Time.GetTicksMsec() - _hoverSince < 300) return;
        _attempted = true;
        try
        {
            var model = Model(Command(room!, point));
            if (model == null) return;
            var tips = model switch
            {
                CardModel card => new IHoverTip[] { new CardHoverTip(card) },
                PotionModel potion => potion.HoverTips.ToArray(),
                _ => []
            };
            var rect = panel.GetGlobalRect();
            bool right = rect.End.X + 380 < panel.GetViewportRect().Size.X;
            _anchor = new Control { Name = "SlppPreviewAnchor", MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(right ? rect.End.X + 12 : rect.Position.X - 12, Math.Clamp(_pointer!.Value.Y - 30, 8, Math.Max(8, panel.GetViewportRect().Size.Y - 450))) };
            layer.AddChild(_anchor);
            var tip = NHoverTipSet.CreateAndShow(_anchor, tips, (model is CardModel) == right ? HoverTipAlignment.Left : HoverTipAlignment.Right);
            if (tip == null) return;
            tip.Name = "SlppHistoryPreview";
            tip.Reparent(layer);
            foreach (var control in GameBridge.Descendants(tip).OfType<Control>()) control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        catch (Exception error)
        {
            Clear();
            _hovered = key;
            _attempted = true;
            GD.PrintErr("[slpp] Preview unavailable: " + error.Message);
        }
    }

    internal static void Clear()
    {
        if (GodotObject.IsInstanceValid(_anchor))
        {
            NHoverTipSet.Remove(_anchor!);
            _anchor!.QueueFree();
        }
        _anchor = null;
        _attempted = false;
        _hovered = "";
    }
}
