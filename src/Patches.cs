using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace slpp;

[HarmonyPatch(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState))]
internal static class InitialStatePatch
{
    static void Postfix(SerializableRun serializableRun)
    {
        try { Recorder.BeginRoom(serializableRun); }
        catch (Exception error) { Recorder.Suspend(error); }
    }
}

[HarmonyPatch(typeof(GameAction), nameof(GameAction.Execute))]
internal static class ActionPatch
{
    static void Prefix(GameAction __instance)
    {
        if (__instance.State == MegaCrit.Sts2.Core.Entities.Actions.GameActionState.WaitingForExecution)
            Recorder.Capture(() => Recorder.RecordAction(__instance));
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.OnEndedTurnLocally))]
internal static class LiveEndTurnPatch
{
    static bool Prefix() => !Recorder.BlocksLiveActions;
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
internal static class LiveActionPatch
{
    static bool Prefix(GameAction action)
    {
        if (!Recorder.BlocksLiveActions || action.ActionType != GameActionType.CombatPlayPhaseOnly || ReferenceEquals(action, Recorder.ReplayingAction)) return true;
        action.Cancel();
        return false;
    }
}

[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice))]
internal static class ChoicePatch
{
    static void Prefix(Player player, uint choiceId, PlayerChoiceResult result) => Recorder.Capture(() => Recorder.RecordChoice(player, choiceId, result));
}

[HarmonyPatch(typeof(CardSelectCmd), "ShouldSelectLocalCard")]
internal static class LocalChoicePatch
{
    static void Postfix(ref bool __result) => __result = Recorder.SelectLocally(__result);
}

[HarmonyPatch(typeof(RelicSelectCmd), "ShouldSelectLocalRelic")]
internal static class LocalRelicChoicePatch
{
    static void Postfix(ref bool __result) => __result = Recorder.SelectLocally(__result);
}

[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
internal static class RemoteChoicePatch
{
    static bool Prefix(Player player, uint choiceId, ref Task<PlayerChoiceResult> __result)
    {
        var result = Recorder.ReplayChoice(player, choiceId);
        if (result == null) return true;
        __result = result;
        return false;
    }
}

[HarmonyPatch]
internal static class SaveDuringRestorePatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRun)),
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.IncrementNumReloads))
    };
    static bool Prefix(ref Task __result)
    {
        if (!Recorder.Restoring) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
internal static class ProgressWritePatch
{
    static bool Prefix() => !Recorder.Restoring;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager), "SaveProgress")]
internal static class ProgressManagerWritePatch
{
    static bool Prefix() => !Recorder.Restoring;
}

[HarmonyPatch]
internal static class PersistentEffectsPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRunHistory)),
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))
    };
    static bool Prefix() => !Recorder.Restoring;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Runs.RunManager), nameof(MegaCrit.Sts2.Core.Runs.RunManager.CleanUp))]
internal static class CleanupPatch
{
    static void Prefix()
    {
        try { Recorder.OnCleanup(); }
        catch (Exception error) { Recorder.Suspend(error); }
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager), "_UnhandledInput")]
internal static class ReplayInputPatch
{
    static bool Prefix() => !Recorder.Busy && !Hud.ModalOpen;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "_Input")]
internal static class ShortcutInputPatch
{
    static bool Prefix(MegaCrit.Sts2.Core.Nodes.NGame __instance, Godot.InputEvent inputEvent)
    {
        try
        {
            if (!Hud.HandleInput(inputEvent)) return true;
            __instance.GetViewport().SetInputAsHandled();
            return false;
        }
        catch (Exception error) { Hud.Disable(); ModLog.Error("Shortcuts disabled", error); return true; }
    }
}
