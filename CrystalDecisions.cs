using HarmonyLib;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace slpp;

internal static class CrystalDecisions
{
    internal static CrystalSphereMinigame? Active;
    private static bool _clicking;
    internal static bool Waiting => Active is { IsFinished: false } && !_clicking && NOverlayStack.Instance?.Peek() is NCrystalSphereScreen;
    internal static void Reset() { Active = null; _clicking = false; }
    internal static void Replay(int packed)
    {
        var game = Active ?? throw new InvalidOperationException("Crystal sphere event is not active");
        game.SetTool((CrystalSphereMinigame.CrystalSphereToolType)(packed >> 8));
        _ = game.CellClicked(game.cells[packed & 15, (packed >> 4) & 15]);
    }
    internal static async Task TrackClick(CrystalSphereMinigame game, Task task)
    {
        _clicking = true;
        try { await task; }
        finally { if (ReferenceEquals(Active, game)) _clicking = false; }
    }
}

[HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.PlayMinigame))]
internal static class CrystalStartPatch
{
    static void Prefix(CrystalSphereMinigame __instance) => CrystalDecisions.Active = __instance;
}

[HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.CellClicked))]
internal static class CrystalClickPatch
{
    static void Prefix(CrystalSphereMinigame __instance, CrystalSphereCell clickedCell) =>
        Recorder.RecordExternal("crystal", clickedCell.X | (clickedCell.Y << 4) | ((int)__instance.CrystalSphereTool << 8), "Crystal sphere");
    static void Postfix(CrystalSphereMinigame __instance, ref Task __result) => __result = CrystalDecisions.TrackClick(__instance, __result);
}

[HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.ForceMinigameEnd))]
internal static class CrystalExitPatch
{
    static bool Prefix() => !Recorder.Restoring;
}
