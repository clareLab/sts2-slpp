using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace slpp;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public static bool SelfTest => OS.GetCmdlineArgs().Contains("--slpp-selftest");
    internal static bool AllowAutoResume => !SelfTest || OS.GetCmdlineArgs().Contains("--slpp-suite=resume");
    private static bool _started;

    public static void Initialize()
    {
        SlppConfig.Register();
        var harmony = new Harmony("clareLab.slpp");
        try { harmony.PatchAll(typeof(Entry).Assembly); }
        catch (Exception e)
        {
            harmony.UnpatchAll(harmony.Id);
            GD.PrintErr("[slpp] Incompatible game API. Mod disabled: " + e);
            return;
        }
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame += Tick;
        ((SceneTree)Engine.GetMainLoop()).Root.TreeExiting += Recorder.OnCleanup;
        GD.Print($"[slpp] Loaded {typeof(Entry).Assembly.GetName().Version?.ToString(3)}");
    }

    private static void Tick()
    {
        if (SaveManager.Instance.IsProfileInitialized && !Recorder.Busy)
        {
            Recorder.CheckProfile();
        }
        Recorder.Tick();
        Hud.Tick();
        if (_started || NGame.Instance?.MainMenu == null || !SaveManager.Instance.IsProfileInitialized) return;
        _started = true;
        SlppConfig.InstallLabels();
        Hud.Install();
        GD.Print("[slpp] Main menu ready");
        if (SelfTest) _ = SelfTests.Run();
    }
}
