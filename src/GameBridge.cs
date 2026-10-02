using Environment = System.Environment;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace slpp;

internal static class GameBridge
{
    internal static RunManager Manager => RunManager.Instance;
    internal static RunState? State => Manager.DebugOnlyGetState();
    internal static bool Singleplayer => State != null && !Manager.IsCleaningUp && Manager.NetService?.Type == NetGameType.Singleplayer;
    internal static string Build => ReleaseInfoManager.Instance.ReleaseInfo?.Commit ?? "unknown";
    internal static bool InCombatRoom => State?.CurrentRoom is CombatRoom;
    internal static int Turn => InCombatRoom ? LocalContext.GetMe(State)?.PlayerCombatState?.TurnNumber ?? 0 : 0;
    internal static bool ChoiceOpen => NPlayerHand.Instance?.IsInCardSelection == true ||
        NOverlayStack.Instance?.Peek() is NCardGridSelectionScreen or NChooseACardSelectionScreen or NCardRewardSelectionScreen or NChooseARelicSelection;
    internal static bool TreasureOpen => NRun.Instance?.TreasureRoom is { } room &&
        AccessTools.Field(typeof(NTreasureRoom), "_isRelicCollectionOpen").GetValue(room) is true;
    internal static Task OpenChest() => (Task)AccessTools.Method(typeof(NTreasureRoom), "OpenChest").Invoke(NRun.Instance!.TreasureRoom, null)!;
    internal static bool Stable => Singleplayer && !Manager.IsCleaningUp && !Manager.NetService.IsGameLoading &&
        !Manager.ActionExecutor.IsRunning && Manager.ActionQueueSet.IsEmpty && !CombatManager.Instance.IsStarting &&
        !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo &&
        (!CombatManager.Instance.IsInProgress || (!CombatManager.Instance.PlayerActionsDisabled &&
          LocalContext.GetMe(State)?.PlayerCombatState?.Phase == PlayerTurnPhase.Play));

    internal static string SaveJson(SerializableRun save) => JsonSerializer.Serialize(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
    internal static SerializableRun ReadSave(string text) => JsonSerializer.Deserialize(text, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
    internal static byte[] Pack<T>(T item) where T : IPacketSerializable
    {
        var writer = new PacketWriter { WarnOnGrow = false };
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
    }
    internal static T Unpack<T>(byte[] bytes) where T : IPacketSerializable, new()
    {
        var reader = new PacketReader();
        reader.Reset(bytes);
        return reader.Read<T>();
    }
    internal static string Fingerprint(int version = 2, IReadOnlyList<NetFullCombatState.PlayerState>? legacyPlayers = null)
    {
        if (State == null) return "";
        var writer = new PacketWriter { WarnOnGrow = false };
        var combat = NetFullCombatState.FromRun(State, null);
        if (!InCombatRoom && !CombatManager.Instance.IsInProgress)
        {
            if (version >= 2) combat.Creatures.Clear();
            for (int i = 0; i < combat.Players.Count; i++)
            {
                var player = combat.Players[i];
                if (version >= 2)
                {
                    player.turnNumber = player.energy = player.stars = 0;
                    player.phase = PlayerTurnPhase.None;
                    player.piles.Clear();
                    player.orbs.Clear();
                }
                else if (legacyPlayers?.FirstOrDefault(p => p.playerId == player.playerId) is { } legacy)
                {
                    player.turnNumber = legacy.turnNumber;
                    player.phase = legacy.phase;
                    player.energy = legacy.energy;
                    player.stars = legacy.stars;
                }
                combat.Players[i] = player;
            }
        }
        combat.Serialize(writer);
        foreach (var player in State.Players)
        {
            var saved = player.ToSerializable();
            saved.DiscoveredCards.Clear(); saved.DiscoveredEnemies.Clear(); saved.DiscoveredEpochs.Clear();
            saved.DiscoveredPotions.Clear(); saved.DiscoveredRelics.Clear();
            saved.Serialize(writer);
        }
        writer.WriteString(State.CurrentRoom?.ModelId?.ToString() ?? "");
        if (State.CurrentRoom is EventRoom eventRoom)
        {
            writer.WriteBool(eventRoom.LocalMutableEvent.IsFinished);
            writer.WriteString(string.Join("|", eventRoom.LocalMutableEvent.CurrentOptions.Select(o => o.TextKey)));
            eventRoom.LocalMutableEvent.Rng.ToSerializable().Serialize(writer);
        }
        if (State.CurrentRoom is MerchantRoom merchant)
            foreach (var entry in merchant.GetLocalInventory().AllEntries)
            { writer.WriteBool(entry.IsStocked); writer.WriteInt(entry.Cost); }
        if (State.CurrentRoom is TreasureRoom) writer.WriteBool(TreasureOpen);
        if (CrystalDecisions.Active is { } crystal && State.CurrentRoom is EventRoom)
        {
            writer.WriteInt(crystal.DivinationCount);
            crystal.Rng.ToSerializable().Serialize(writer);
            foreach (var cell in crystal.cells) writer.WriteBool(cell.IsHidden);
        }
        foreach (var rewards in ExternalDecisions.RewardStack)
            foreach (var reward in rewards.Rewards ?? []) writer.WriteBool(reward.SuccessfullySelected);
        return Convert.ToHexString(SHA256.HashData(writer.Buffer.AsSpan(0, writer.BytePosition)));
    }
    internal static async Task Frame() => await ((SceneTree)Engine.GetMainLoop()).ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);
    internal static async Task Until(Func<bool> predicate, string description, int seconds = 30)
    {
        long deadline = Environment.TickCount64 + seconds * 1000;
        while (!predicate())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException(description);
            await Frame();
        }
    }
    internal static async Task Load(RoomRecord record)
    {
        if (SaveManager.Instance.CurrentRunSaveTask is { } saveTask) await saveTask;
        Recorder.OperationStage = "Loading room";
        var save = ReadSave(record.Save);
        CleanUp();
        ExternalDecisions.Reset();
        var state = RunState.FromSerializable(save);
        await Manager.SetUpSavedSingleplayer(state, save);
        await PreloadManager.LoadRunAssets(state.Players.Select(p => p.Character));
        await PreloadManager.LoadActAssets(state.Act);
        Manager.Launch();
        NGame.Instance!.RootSceneContainer.SetCurrentScene(NRun.Create(state));
        await Manager.GenerateMap();
        Manager.ActionQueueSet.FastForwardNextActionId(record.ActionId);
        Manager.ActionQueueSynchronizer.FastForwardHookId(record.HookId);
        Manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(record.ChoiceIds);
        Manager.RewardsSetSynchronizer.FastForwardRewardIds(record.RewardIds);
        await Manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(save.PreFinishedRoom, state));
    }

    internal static void CleanUp()
    {
        var cancellations = new List<Action>();
        foreach (var node in Descendants(((SceneTree)Engine.GetMainLoop()).Root))
        {
            if (node is NPlayerHand)
                Detach<IEnumerable<CardModel>>(node, "_selectionCompletionSource", [], cancellations);
            else if (node is NCardRewardSelectionScreen)
                Detach<int?>(node, "_completionSource", null, cancellations);
            else if (node is NCardGridSelectionScreen or NChooseACardSelectionScreen)
                Detach<IEnumerable<CardModel>>(node, "_completionSource", [], cancellations);
            else if (node is NChooseARelicSelection)
                Detach<IEnumerable<RelicModel>>(node, "_completionSource", [], cancellations);
        }
        try { if (State != null) Manager.CleanUp(); }
        finally { foreach (var cancel in cancellations) cancel(); }
    }

    private static void Detach<T>(object node, string fieldName, T empty, List<Action> cancellations)
    {
        var field = AccessTools.Field(node.GetType(), fieldName) ?? throw new MissingFieldException(node.GetType().Name, fieldName);
        if (field.GetValue(node) is TaskCompletionSource<T> source && !source.Task.IsCompleted)
        {
            var replacement = new TaskCompletionSource<T>();
            replacement.SetResult(empty);
            field.SetValue(node, replacement);
            cancellations.Add(() => source.TrySetCanceled());
        }
    }

    internal static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
