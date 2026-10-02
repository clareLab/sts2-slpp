using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Replay;

namespace slpp;

internal static class TimelineText
{
    private static Dictionary<string, string>? _cards;

    internal static string Card(ModelId id)
    {
        if (_cards == null)
        {
            try
            {
                using var file = Godot.FileAccess.Open("res://localization/eng/cards.json", Godot.FileAccess.ModeFlags.Read);
                _cards = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText()) ?? [];
            }
            catch { _cards = []; }
        }
        return _cards.GetValueOrDefault(id.Entry + ".title") ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Entry.Replace('_', ' ').ToLowerInvariant());
    }

    internal static string Action(CombatReplayEvent evt) => evt.action switch
    {
        NetPlayCardAction play => Card(play.modelId),
        NetEndPlayerTurnAction => "End turn", NetUsePotionAction => "Use potion", NetDiscardPotionGameAction => "Discard potion",
        NetPickRelicAction => "Choose relic", _ => "Action"
    };

    internal static void Normalize(Timeline history)
    {
        foreach (var room in history.Rooms)
        {
            if (!room.Label.StartsWith("Act ", StringComparison.Ordinal))
                room.Label = $"Act {GameBridge.ReadSave(room.Save).CurrentActIndex + 1} · Floor {room.Floor}";
            for (int i = 0; i < room.Commands.Count; i++)
            {
                var command = room.Commands[i];
                string label = command.Kind switch
                {
                    "action" => Action(GameBridge.Unpack<CombatReplayEvent>(command.Data)),
                    "chest" => "Open chest", "proceed" => "Return to event", "event" => "Event option " + (command.Index + 1),
                    "rest" => "Rest option " + (command.Index + 1), "shop" => "Shop purchase", "reward" => "Claim reward",
                    "skip-rewards" => "Skip rewards", "crystal" => "Crystal sphere", _ => "Action"
                };
                room.Commands[i] = command with { Label = label };
            }
            for (int i = 0; i < room.Points.Count; i++)
            {
                var point = room.Points[i];
                room.Points[i] = point with { Label = point.AwaitingChoice ? "Choose" : point.Commands == 0 ? "Room start" : room.Commands[point.Commands - 1].Label };
            }
        }
    }
}
