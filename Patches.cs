using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Saves;

namespace slpp;

[HarmonyPatch(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState))]
internal static class InitialStatePatch
{
    static void Postfix(SerializableRun serializableRun) => Recorder.BeginRoom(serializableRun);
}

[HarmonyPatch(typeof(GameAction), nameof(GameAction.Execute))]
internal static class ActionPatch
{
    static void Prefix(GameAction __instance)
    {
        if (__instance.State == MegaCrit.Sts2.Core.Entities.Actions.GameActionState.WaitingForExecution)
            Recorder.RecordAction(__instance);
    }
}

[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice))]
internal static class ChoicePatch
{
    static void Prefix(Player player, uint choiceId, PlayerChoiceResult result) => Recorder.RecordChoice(player, choiceId, result);
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
        if (!Recorder.Restoring && !Recorder.Faulted) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
internal static class ProgressWritePatch
{
    static bool Prefix() => !Recorder.Restoring && !Recorder.Faulted;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager), "SaveProgress")]
internal static class ProgressManagerWritePatch
{
    static bool Prefix() => !Recorder.Restoring && !Recorder.Faulted;
}

[HarmonyPatch]
internal static class PersistentEffectsPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRunHistory)),
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))
    };
    static bool Prefix() => !Recorder.Restoring && !Recorder.Faulted;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Runs.RunManager), nameof(MegaCrit.Sts2.Core.Runs.RunManager.CleanUp))]
internal static class CleanupPatch
{
    static void Prefix() => Recorder.OnCleanup();
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager), "_UnhandledInput")]
internal static class ReplayInputPatch
{
    static bool Prefix() => !Recorder.Busy && !Recorder.Faulted;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "_Input")]
internal static class ShortcutInputPatch
{
    static bool Prefix(MegaCrit.Sts2.Core.Nodes.NGame __instance, Godot.InputEvent inputEvent)
    {
        if (!Hud.HandleInput(inputEvent)) return true;
        __instance.GetViewport().SetInputAsHandled();
        return false;
    }
}
