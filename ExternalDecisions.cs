using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace slpp;

internal static class ExternalDecisions
{
    internal static readonly List<RewardsSet> RewardStack = [];
    internal static bool WaitingForDecision => NOverlayStack.Instance?.Peek() is NRewardsScreen || GameBridge.TreasureOpen || CrystalDecisions.Waiting;
    internal static void Reset() { RewardStack.Clear(); CrystalDecisions.Reset(); }
    internal static void Execute(RecordedCommand command)
    {
        var manager = GameBridge.Manager;
        switch (command.Kind)
        {
            case "event": manager.EventSynchronizer.ChooseLocalOption(command.Index); break;
            case "rest": _ = manager.RestSiteSynchronizer.ChooseLocalOption(command.Index); break;
            case "shop":
                var inventory = ((MerchantRoom)GameBridge.State!.CurrentRoom!).GetLocalInventory();
                var entry = inventory.AllEntries.ElementAt(command.Index);
                if (entry is MerchantCardRemovalEntry removal) _ = removal.OnTryPurchaseWrapper(inventory);
                else _ = entry.OnTryPurchaseWrapper(inventory);
                break;
            case "reward": _ = manager.RewardsSetSynchronizer.SelectLocalReward(RewardStack.Last().Rewards[command.Index]); break;
            case "skip-rewards": manager.RewardsSetSynchronizer.SkipLocalRewardsSet(); break;
            case "chest": _ = GameBridge.OpenChest(); break;
            case "proceed": _ = manager.ProceedFromTerminalRewardsScreen(); break;
            case "crystal": CrystalDecisions.Replay(command.Index); break;
            default: throw new InvalidOperationException("Unsupported decision: " + command.Kind);
        }
    }

    internal static async Task RememberRewardSet(RewardsSet set, Task task)
    {
        try { await task; }
        finally { RewardStack.Remove(set); }
    }
}

[HarmonyPatch(typeof(NTreasureRoom), "OpenChest")]
internal static class ChestPatch
{
    static void Prefix() => Recorder.RecordExternal("chest", 0, "Open chest");
    static void Postfix(ref Task __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen))]
internal static class ProceedPatch
{
    static void Prefix()
    {
        if (GameBridge.State?.CurrentRoomCount > 1) Recorder.RecordExternal("proceed", 0, "Return to event");
    }
    static void Postfix(ref Task __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
internal static class EventDecisionPatch
{
    static void Prefix(int index) => Recorder.RecordExternal("event", index, "Event option " + (index + 1));
}

[HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen))]
internal static class EventTaskPatch
{
    static void Postfix(ref Task __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
internal static class RestDecisionPatch
{
    static void Prefix(int index) => Recorder.RecordExternal("rest", index, "Rest option " + (index + 1));
    static void Postfix(ref Task<bool> __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
internal static class ShopDecisionPatch
{
    static void Prefix(MerchantEntry __instance, MerchantInventory? inventory)
    {
        int index = inventory?.AllEntries.ToList().IndexOf(__instance) ?? -1;
        if (index >= 0 && __instance.IsStocked && __instance.EnoughGold) Recorder.RecordExternal("shop", index, "Shop purchase");
    }
    static void Postfix(ref Task<bool> __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper))]
internal static class ShopRemovalPatch
{
    static void Prefix(MerchantCardRemovalEntry __instance, MerchantInventory? inventory)
    {
        int index = inventory?.AllEntries.ToList().IndexOf(__instance) ?? -1;
        if (index >= 0 && __instance.IsStocked && __instance.EnoughGold) Recorder.RecordExternal("shop", index, "Remove card");
    }
    static void Postfix(ref Task<bool> __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
internal static class RewardSetPatch
{
    static void Prefix(RewardsSet set) => ExternalDecisions.RewardStack.Add(set);
    static void Postfix(RewardsSet set, ref Task __result) => __result = ExternalDecisions.RememberRewardSet(set, __result);
}

[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
internal static class RewardDecisionPatch
{
    static void Prefix(Reward reward)
    {
        int index = ExternalDecisions.RewardStack.LastOrDefault()?.Rewards.IndexOf(reward) ?? -1;
        if (index >= 0) Recorder.RecordExternal("reward", index, "Claim reward");
    }
    static void Postfix(ref Task<bool> __result) => __result = Recorder.Track(__result);
}

[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SkipLocalRewardsSet))]
internal static class SkipRewardsPatch
{
    static void Prefix() => Recorder.RecordExternal("skip-rewards", 0, "Skip rewards");
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.OptionSelected))]
internal static class RewardChoiceReplayPatch
{
    static bool Prefix(ref Task<int?> __result)
    {
        if (!Recorder.Restoring) return true;
        if (Recorder.SelectLocally(true)) return true;
        var player = LocalContext.GetMe(GameBridge.State)!;
        uint id = GameBridge.Manager.PlayerChoiceSynchronizer.ChoiceIds[0] - 1;
        __result = ConvertChoice(Recorder.ReplayChoice(player, id, false)!);
        return false;
    }
    private static async Task<int?> ConvertChoice(Task<MegaCrit.Sts2.Core.GameActions.PlayerChoiceResult> result) => (await result).AsIndexOrNull();
}
