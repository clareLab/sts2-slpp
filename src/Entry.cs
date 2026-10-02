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
    private static bool _hudFailed;

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
        ((SceneTree)Engine.GetMainLoop()).Root.TreeExiting += () =>
        {
            try { Recorder.OnCleanup(); }
            catch (Exception error) { GD.PrintErr("[slpp] Cleanup failed: " + error); }
        };
        GD.Print($"[slpp] Loaded {typeof(Entry).Assembly.GetName().Version?.ToString(3)}");
    }

    private static void Tick()
    {
        try
        {
            if (!Recorder.Faulted)
            {
                if (SaveManager.Instance.IsProfileInitialized && !Recorder.Busy) Recorder.CheckProfile();
                Recorder.Tick();
            }
        }
        catch (Exception error) { Recorder.Suspend(error); }
        if (!_hudFailed)
        {
            try { Hud.Tick(); }
            catch (Exception error) { _hudFailed = true; Hud.Disable(); GD.PrintErr("[slpp] Toolbar disabled: " + error); }
        }
        if (_started || NGame.Instance?.MainMenu == null || !SaveManager.Instance.IsProfileInitialized) return;
        _started = true;
        try { SlppConfig.InstallLabels(); Hud.Install(); }
        catch (Exception error) { _hudFailed = true; Hud.Disable(); GD.PrintErr("[slpp] Toolbar unavailable: " + error); }
        GD.Print("[slpp] Main menu ready");
        if (SelfTest) _ = SelfTests.Run();
    }
}
