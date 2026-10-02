using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using BaseLib.Config;
using BaseLib.Config.UI;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using System.Text.Json;

namespace slpp;

internal static class SelfTests
{
    private static readonly List<string> Passed = [];
    internal static async Task Run()
    {
        string? error = null;
        try
        {
            if (!File.Exists(ProjectSettings.GlobalizePath("user://.slpp-test-sandbox")))
                throw new InvalidOperationException("Self tests require an isolated user-data directory with .slpp-test-sandbox marker");
            for (int i = 0; i < 180; i++) await GameBridge.Frame();
            SaveManager.Instance.SetFtuesEnabled(false);
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=resume")) { await TestResume(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=ui")) { await TestUi(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=settings")) { await TestSettings(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=settings-resume")) { TestSettingsResume(); return; }
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<Ironclad>().Id).TotalLosses = 2;
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=world")) { await TestWorld(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=characters")) { await TestCharacters(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=crystal")) { await TestCrystal(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=potions")) { await TestPotions(); return; }
            if (OS.GetCmdlineArgs().Contains("--slpp-suite=choices")) { await TestChoices(); return; }
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
                ActModel.GetDefaultList(), [], "SLPP-TEST-001", GameMode.Standard);
            var point = run.Map!.GetAllMapPoints().Where(p => p.PointType == MapPointType.Monster).OrderBy(p => p.coord.row).First();
            await GameBridge.Manager.EnterMapCoord(point.coord);
            await GameBridge.Until(() => GameBridge.Stable && Recorder.Room?.Points.Count > 0, "first combat");
            int room = Recorder.History!.RoomCursor;
            string original = GameBridge.Fingerprint();
            var player = LocalContext.GetMe(GameBridge.State)!;
            var card = player.PlayerCombatState!.Hand.Cards.First(c => c.Type == CardType.Attack && c.CanPlay());
            var enemy = player.Creature.CombatState!.HittableEnemies.First();
            GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, enemy));
            await GameBridge.Until(() => Recorder.Room!.Points.Count > 1 && GameBridge.Stable, "card played");
            string played = GameBridge.Fingerprint();
            await Recorder.Restore(room, 0);
            Check(GameBridge.Fingerprint() == original, "undo restores initial state");
            await Recorder.Restore(room, 1);
            Check(GameBridge.Fingerprint() == played, "redo restores card result");
            player = LocalContext.GetMe(GameBridge.State)!;
            GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, player.PlayerCombatState!.TurnNumber));
            await GameBridge.Until(() => GameBridge.Turn == 2 && Recorder.Room!.Points.Count > 2 && GameBridge.Stable, "turn ended");
            string nextTurn = GameBridge.Fingerprint();
            int turnPoint = Recorder.History.PointCursor;
            await Recorder.Restore(room, 1);
            await Recorder.Restore(room, turnPoint);
            Check(GameBridge.Fingerprint() == nextTurn, "cross-turn redo");
            Recorder.Flush();
            GD.Print("[slpp] SELFTEST_CORE_OK");
            await TestChoices();
            await TestWorld();
            await TestCrystal();
            await TestPotions();
            await TestCharacters();
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            GD.PrintErr("[slpp] SELFTEST_FAILED " + ex);
        }
        finally
        {
            await Recorder.FlushAsync();
            var report = new { gameBuild = GameBridge.Build, passed = Passed, error, success = error == null };
            File.WriteAllText(ProjectSettings.GlobalizePath("user://slpp-selftest.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("[slpp] SELFTEST_RESULT " + JsonSerializer.Serialize(report));
            for (int i = 0; i < 30; i++) await GameBridge.Frame();
            ((SceneTree)Engine.GetMainLoop()).Quit(error == null ? 0 : 1);
        }
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Passed.Add(label);
        GD.Print("[slpp] PASS " + label);
    }

    private static async Task TestChoices()
    {
        if (GameBridge.State != null) GameBridge.Manager.CleanUp();
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Silent>(), true,
            ActModel.GetDefaultList(), [], "SLPP-CHOICE-001", GameMode.Standard);
        await GameBridge.Manager.EnterMapCoord(run.Map!.GetAllMapPoints().Where(p => p.PointType == MapPointType.Monster).OrderBy(p => p.coord.row).First().coord);
        await GameBridge.Until(() => GameBridge.Stable && Recorder.Room?.Points.Count > 0, "silent combat");
        var player = LocalContext.GetMe(GameBridge.State)!;
        for (int attempt = 0; !player.PlayerCombatState!.Hand.Cards.Any(c => c.Id.Entry == "SURVIVOR") && attempt < 3; attempt++)
        {
            int turn = player.PlayerCombatState.TurnNumber;
            GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, turn));
            await GameBridge.Until(() => GameBridge.Stable && GameBridge.Turn > turn, "draw survivor");
            for (int i = 0; i < 6; i++) await GameBridge.Frame();
        }
        var survivor = player.PlayerCombatState!.Hand.Cards.First(c => c.Id.Entry == "SURVIVOR");
        GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(survivor, null));
        await GameBridge.Until(() => NPlayerHand.Instance?.IsInCardSelection == true, "survivor selection");
        CompleteHandChoice();
        await GameBridge.Until(() => GameBridge.Stable && Recorder.Room!.Points.Last().Choices > 0 && !Recorder.Room.Points.Last().AwaitingChoice, "choice completed");
        int room = Recorder.History!.RoomCursor, final = Recorder.History.PointCursor;
        string expected = GameBridge.Fingerprint();
        int choice = Recorder.Room!.Points.FindLastIndex(p => p.AwaitingChoice);
        await Recorder.Restore(room, choice);
        Check(NPlayerHand.Instance?.IsInCardSelection == true, "undo returns to live choice UI");
        await Recorder.Restore(room, final);
        Check(GameBridge.Fingerprint() == expected, "choice replay matches result");
        await Recorder.Restore(room, choice);
        CompleteHandChoice(true);
        await GameBridge.Until(() => GameBridge.Stable && !Recorder.Room!.Points.Last().AwaitingChoice, "branch choice");
        Check(GameBridge.Fingerprint() != expected, "alternative choice changes branch");
        await Recorder.Restore(room, choice);
        string seed = GameBridge.State!.Rng.StringSeed;
        await Recorder.Restart(true);
        await Settle("restart from selection");
        Check(GameBridge.State!.Rng.StringSeed != seed && !Recorder.Faulted && !GameBridge.ChoiceOpen, "random restart while a card choice is pending");
        GD.Print("[slpp] SELFTEST_CHOICES_OK");
    }

    private static void CompleteHandChoice(bool last = false)
    {
        var cards = LocalContext.GetMe(GameBridge.State)!.PlayerCombatState!.Hand.Cards;
        var field = AccessTools.Field(typeof(NPlayerHand), "_selectionCompletionSource");
        var completion = (TaskCompletionSource<IEnumerable<CardModel>>)field.GetValue(NPlayerHand.Instance)!;
        completion.SetResult([last ? cards.Last() : cards.First()]);
    }

    private static async Task Settle(string label)
    {
        int frames = 0;
        await GameBridge.Until(() =>
        {
            bool ready = GameBridge.Stable && (!Recorder.HasExternalTask || ExternalDecisions.WaitingForDecision);
            frames = ready ? frames + 1 : 0;
            return frames > 10 && Recorder.Room?.Points.Count > 0;
        }, label);
    }

    private static async Task RoundTrip(string label)
    {
        await Settle(label);
        int room = Recorder.History!.RoomCursor, end = Recorder.History.PointCursor;
        string expected = GameBridge.Fingerprint();
        await Recorder.Restore(room, 0);
        await Recorder.Restore(room, end);
        Check(expected == GameBridge.Fingerprint(), label);
    }

    private static async Task Enter(MapPointType type)
    {
        if (Recorder.Room?.Points.Count == 0) await Settle("previous room start");
        var point = GameBridge.State!.Map!.GetAllMapPoints().Where(p => p.PointType == type).OrderBy(p => p.coord.row).First();
        await GameBridge.Manager.EnterMapCoord(point.coord);
        await Settle("enter " + type);
    }

    private static async Task TestWorld()
    {
        if (GameBridge.State != null) GameBridge.Manager.CleanUp();
        SaveManager.Instance.Progress.ObtainEpochOverride("NEOW_EPOCH", EpochState.Revealed);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            ActModel.GetDefaultList(), [], "SLPP-WORLD-001", GameMode.Standard);
        if (GameBridge.State!.CurrentRoom is not EventRoom) await GameBridge.Manager.EnterMapCoord(run.Map!.StartingMapPoint.coord);
        await Settle("Neow");
        var ev = ((EventRoom)GameBridge.State!.CurrentRoom!).LocalMutableEvent;
        GD.Print("[slpp] TEST_EVENT " + string.Join(",", ev.CurrentOptions.Select(o => o.TextKey)));
        int option = ev.CurrentOptions.ToList().FindIndex(o => !o.IsLocked);
        GameBridge.Manager.EventSynchronizer.ChooseLocalOption(option);
        await CompleteAnySelection();
        await RoundTrip("event option undo/redo");
        await Enter(MapPointType.Shop);
        var inventory = ((MerchantRoom)GameBridge.State.CurrentRoom!).GetLocalInventory();
        var entry = inventory.AllEntries.First(e => e.IsStocked && e.EnoughGold && e is not MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry);
        await entry.OnTryPurchaseWrapper(inventory);
        await RoundTrip("shop purchase undo/redo");
        await Recorder.Restore(Recorder.History!.RoomCursor, 0);
        inventory = ((MerchantRoom)GameBridge.State.CurrentRoom!).GetLocalInventory();
        var removal = inventory.AllEntries.OfType<MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry>().Single();
        _ = removal.OnTryPurchaseWrapper(inventory);
        await CompleteAnySelection();
        await RoundTrip("shop card removal undo/redo");
        int shopRoom = Recorder.History!.RoomCursor, shopPoint = Recorder.History.PointCursor;
        string shopHash = GameBridge.Fingerprint();
        await Enter(MapPointType.RestSite);
        _ = GameBridge.Manager.RestSiteSynchronizer.ChooseLocalOption(1);
        await CompleteAnySelection();
        await RoundTrip("smith selection undo/redo");
        int completedSmith = Recorder.History!.PointCursor;
        await Recorder.Restore(Recorder.History.RoomCursor, Recorder.Room!.Points.FindIndex(p => p.AwaitingChoice));
        Check(GameBridge.ChoiceOpen, "smith undo reopens live card selection");
        await Recorder.Restore(Recorder.History.RoomCursor, completedSmith);
        int restRoom = Recorder.History.RoomCursor, restPoint = Recorder.History.PointCursor;
        string restHash = GameBridge.Fingerprint();
        await Recorder.Restore(shopRoom, shopPoint);
        Check(GameBridge.Fingerprint() == shopHash, "return previous floor");
        await Recorder.Restore(restRoom, restPoint);
        Check(GameBridge.Fingerprint() == restHash, "redo later floor");
        await Enter(MapPointType.Treasure);
        _ = GameBridge.OpenChest();
        await GameBridge.Until(() => GameBridge.TreasureOpen, "open chest");
        await Settle("chest ready");
        await RoundTrip("treasure chest opening undo/redo");
        GameBridge.Manager.TreasureRoomRelicSynchronizer.PickRelicLocally(0);
        await GameBridge.Until(() => !GameBridge.TreasureOpen, "take treasure relic");
        await RoundTrip("treasure relic undo/redo");
        await Recorder.Restart(false);
        Check(Recorder.History.RoomCursor == 0 && Recorder.History.PointCursor == 0, "same seed restart");
        await Enter(MapPointType.Monster);
        await WinCombat();
        await Settle("combat rewards");
        var set = ExternalDecisions.RewardStack.Last();
        GD.Print("[slpp] TEST_REWARDS " + string.Join(",", set.Rewards!.Select(r => r.GetType().Name)));
        foreach (var reward in set.Rewards!.Where(r => r is not CardReward).ToList())
        {
            await GameBridge.Manager.RewardsSetSynchronizer.SelectLocalReward(reward);
            await Settle("take reward");
        }
        var cardReward = ExternalDecisions.RewardStack.Last().Rewards!.OfType<CardReward>().First();
        _ = GameBridge.Manager.RewardsSetSynchronizer.SelectLocalReward(cardReward);
        await CompleteAnySelection();
        await RoundTrip("combat victory and rewards undo/redo");
        string oldSeed = GameBridge.State.Rng.StringSeed;
        await Recorder.Restart(true);
        Check(GameBridge.State!.Rng.StringSeed != oldSeed, "random seed restart");
        GD.Print("[slpp] SELFTEST_WORLD_OK");
    }

    private static async Task CompleteAnySelection()
    {
        int frames = 0;
        await GameBridge.Until(() =>
        {
            if (NPlayerHand.Instance?.IsInCardSelection == true) CompleteHandChoice();
            var screen = NOverlayStack.Instance?.Peek();
            if (screen is NCardRewardSelectionScreen reward)
            {
                var source = (TaskCompletionSource<int?>)AccessTools.Field(reward.GetType(), "_completionSource").GetValue(reward)!;
                source.TrySetResult(0);
            }
            else if (screen is NCardGridSelectionScreen or NChooseACardSelectionScreen)
            {
                var source = (TaskCompletionSource<IEnumerable<CardModel>>)AccessTools.Field(screen.GetType(), "_completionSource").GetValue(screen)!;
                source.TrySetResult([LocalContext.GetMe(GameBridge.State)!.Deck.Cards.First(c => c.IsUpgradable)]);
            }
            bool ready = GameBridge.Stable && (!Recorder.HasExternalTask || ExternalDecisions.WaitingForDecision);
            frames = ready ? frames + 1 : 0;
            return frames > 10;
        }, "auto complete selection", 45);
    }

    private static async Task WinCombat()
    {
        for (int i = 0; CombatManager.Instance.IsInProgress && i < 150; i++)
        {
            var player = LocalContext.GetMe(GameBridge.State)!;
            var card = player.PlayerCombatState!.Hand.Cards.FirstOrDefault(c => c.CanPlay());
            if (card != null)
            {
                var target = card.TargetType == TargetType.AnyEnemy ? player.Creature.CombatState!.HittableEnemies.FirstOrDefault() : null;
                GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));
            }
            else GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, player.PlayerCombatState.TurnNumber));
            await CompleteAnySelection();
        }
        Check(!CombatManager.Instance.IsInProgress && LocalContext.GetMe(GameBridge.State)!.Creature.CurrentHp > 0, "natural combat victory");
    }

    private static async Task TestCharacters()
    {
        foreach (var character in new CharacterModel[] { ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(), ModelDb.Character<Defect>(), ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>() })
        {
            if (GameBridge.State != null) GameBridge.Manager.CleanUp();
            await NGame.Instance!.StartNewSingleplayerRun(character, true, ActModel.GetDefaultList(), [], "SLPP-" + character.Id.Entry, GameMode.Standard);
            await Enter(MapPointType.Monster);
            for (int step = 0; step < 6; step++)
            {
                var player = LocalContext.GetMe(GameBridge.State)!;
                var card = player.PlayerCombatState!.Hand.Cards.FirstOrDefault(c => c.CanPlay());
                if (card == null) GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, player.PlayerCombatState.TurnNumber));
                else GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy ? player.Creature.CombatState!.HittableEnemies.First() : null));
                await CompleteAnySelection();
            }
            await RoundTrip(character.Id.Entry + " six-action replay");
        }
        int combatRoom = Recorder.History!.RoomCursor;
        for (int i = 0; i < 35 && LocalContext.GetMe(GameBridge.State)!.Creature.CurrentHp > 0; i++)
        {
            var player = LocalContext.GetMe(GameBridge.State)!;
            GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, player.PlayerCombatState!.TurnNumber));
            await CompleteAnySelection();
        }
        Check(LocalContext.GetMe(GameBridge.State)!.Creature.CurrentHp == 0, "natural death reached");
        await Recorder.Restore(combatRoom, 0);
        Check(LocalContext.GetMe(GameBridge.State)!.Creature.CurrentHp > 0 && CombatManager.Instance.IsInProgress, "undo death returns to live combat");
        var current = LocalContext.GetMe(GameBridge.State)!;
        GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(current, current.PlayerCombatState!.TurnNumber));
        await Settle("prepare resume");
        await Recorder.Restore(Recorder.History.RoomCursor, Recorder.History.PointCursor);
        await Recorder.FlushAsync();
        File.WriteAllText(ProjectSettings.GlobalizePath("user://slpp-resume-expected.json"), JsonSerializer.Serialize(new { hash = GameBridge.Fingerprint(), room = Recorder.History.RoomCursor, point = Recorder.History.PointCursor, key = Recorder.History.RunKey }));
        GD.Print("[slpp] SELFTEST_CHARACTERS_OK");
    }

    private static async Task TestResume()
    {
        var expected = JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("user://slpp-resume-expected.json"))).RootElement;
        Check(Recorder.History?.RunKey == expected.GetProperty("key").GetString(), "journal discovered in new process");
        var result = SaveManager.Instance.LoadRunSave();
        var save = result.SaveData!;
        var state = RunState.FromSerializable(save);
        await GameBridge.Manager.SetUpSavedSingleplayer(state, save);
        await NGame.Instance!.LoadRun(state, save.PreFinishedRoom);
        await GameBridge.Until(() => !Recorder.Busy && GameBridge.Fingerprint() == expected.GetProperty("hash").GetString(), "automatic resume", 45);
        Check(Recorder.History!.RoomCursor == expected.GetProperty("room").GetInt32() && Recorder.History.PointCursor == expected.GetProperty("point").GetInt32(), "cursor restored after process restart");
        Check(GameBridge.Fingerprint() == expected.GetProperty("hash").GetString(), "full gameplay state restored after process restart");
        GD.Print("[slpp] SELFTEST_RESUME_OK");
    }

    private static async Task TestCrystal()
    {
        if (GameBridge.State != null) GameBridge.Manager.CleanUp();
        await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true, ActModel.GetDefaultList(), [], "SLPP-CRYSTAL", GameMode.Standard);
        await Settle("crystal initial room");
        await GameBridge.Manager.EnterAct(1, false);
        var player = LocalContext.GetMe(GameBridge.State)!;
        await MegaCrit.Sts2.Core.Commands.PlayerCmd.GainGold(250, player);
        var rooms = (RoomSet)AccessTools.Field(typeof(ActModel), "_rooms").GetValue(GameBridge.State!.Act)!;
        rooms.events.Clear(); rooms.events.Add(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()); rooms.eventsVisited = 0;
        var odds = (Dictionary<RoomType, float>)AccessTools.Field(GameBridge.State.Odds.UnknownMapPoint.GetType(), "_nonEventOdds").GetValue(GameBridge.State.Odds.UnknownMapPoint)!;
        foreach (var key in odds.Keys.ToList()) odds[key] = 0;
        await Enter(MapPointType.Unknown);
        Check(GameBridge.State.CurrentRoom is EventRoom er && er.LocalMutableEvent is MegaCrit.Sts2.Core.Models.Events.CrystalSphere, "crystal event fixture");
        GameBridge.Manager.EventSynchronizer.ChooseLocalOption(0);
        await Settle("crystal board");
        var game = CrystalDecisions.Active!;
        await game.CellClicked(game.cells[5,5]);
        await RoundTrip("crystal reveal undo/redo");
        while (CrystalDecisions.Active is { IsFinished: false } active)
        {
            await active.CellClicked(active.cells.Cast<MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereCell>().First(c => c.IsHidden));
            await CompleteAnySelection();
        }
        await RoundTrip("crystal complete undo/redo");
    }

    private static T Widget<T>(string name) where T : Node => GameBridge.Descendants(((SceneTree)Engine.GetMainLoop()).Root).OfType<T>().Single(n => n.Name == name);
    private static void Press(string name) => Widget<Button>(name).EmitSignal(Button.SignalName.Pressed);
    private static InputEventKey KeyEvent(Key key, bool ctrl = false, bool shift = false, bool pressed = true) =>
        new() { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, ShiftPressed = shift, Pressed = pressed };
    private static void ResetSettings() => AccessTools.Method(typeof(ModConfig), "RestoreDefaultsNoConfirm").Invoke(SlppConfig.Instance, null);

    private static async Task<NModConfigSubmenu> OpenConfig()
    {
        var page = NGame.Instance!.MainMenu!.SubmenuStack.PushSubmenuType<NModConfigSubmenu>();
        await GameBridge.Frame();
        var button = GameBridge.Descendants(page).OfType<NModListButton>().Single(b => b.ModName.Contains("Save & Load ++") || b.ModName == "slpp");
        button.EmitSignal(NClickableControl.SignalName.Released, button);
        for (int i = 0; i < 3; i++) await GameBridge.Frame();
        return page;
    }

    private static async Task TestSettings()
    {
        ResetSettings();
        Check(ReferenceEquals(ModConfigRegistry.Get("slpp"), SlppConfig.Instance), "config registered with BaseLib");
        var page = await OpenConfig();
        Check(SlppConfig.IsOpen && Widget<NConfigOptionRow>("UndoKey").SettingControl is NConfigLineEdit, "BaseLib creates native shortcut text fields");
        var toolbar = (NConfigTickbox)Widget<NConfigOptionRow>("Toolbar").SettingControl;
        toolbar.ForceToggleTick();
        var turns = (NConfigTickbox)Widget<NConfigOptionRow>("Turns").SettingControl;
        turns.ForceToggleTick();
        Check(!SlppConfig.Toolbar && !SlppConfig.Turns, "BaseLib component switches apply immediately");
        var undo = (NConfigLineEdit)Widget<NConfigOptionRow>("UndoKey").SettingControl;
        undo.Text = "E";
        undo.EmitSignal(LineEdit.SignalName.TextChanged, undo.Text);
        var redo = (NConfigLineEdit)Widget<NConfigOptionRow>("RedoKey").SettingControl;
        redo.Text = "";
        redo.EmitSignal(LineEdit.SignalName.TextChanged, redo.Text);
        Check(SlppConfig.Binding(Shortcut.Undo) == new KeyChord(Key.E) && SlppConfig.Binding(Shortcut.Redo) == null, "BaseLib shortcut fields rebind and clear");
        undo.Text = "not a key";
        undo.EmitSignal(LineEdit.SignalName.TextChanged, undo.Text);
        undo.EmitSignal(LineEdit.SignalName.FocusExited);
        Check(undo.Text == "E" && SlppConfig.UndoKey == "E", "invalid shortcut text is rejected by BaseLib");
        Check(KeyChord.TryParse("ctrl+shift+z", out var chord) && chord == new KeyChord(Key.Z, true, true), "shortcut modifiers and case parse correctly");
        Check(!KeyChord.TryParse("Ctrl+Ctrl+Z", out _) && !KeyChord.TryParse("Garbage", out _), "malformed shortcuts never execute");
        SlppConfig.Scale = 120;
        SlppConfig.AlignRight = true;
        SlppConfig.Instance.Changed();
        NGame.Instance!.MainMenu!.SubmenuStack.Pop();
        for (int i = 0; i < 3; i++) await GameBridge.Frame();
        Check(!SlppConfig.IsOpen, "BaseLib closes configuration normally");
        string path = ProjectSettings.GlobalizePath("user://mod_configs/slpp.cfg");
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        Check(saved["UndoKey"] == "E" && saved["RedoKey"] == "" && saved["Toolbar"] == "False", "BaseLib persists component and shortcut changes");
        SlppConfig.Toolbar = true;
        SlppConfig.UndoKey = "Q";
        SlppConfig.Instance.Load();
        Check(!SlppConfig.Toolbar && SlppConfig.UndoKey == "E", "BaseLib reload restores saved configuration");
        Check(GameBridge.Descendants(((SceneTree)Engine.GetMainLoop()).Root).OfType<CanvasLayer>().All(c => !c.Name.ToString().Contains("Settings")), "no standalone settings canvas remains");
    }

    private static void TestSettingsResume()
    {
        Check(!SlppConfig.Toolbar && !SlppConfig.Turns && SlppConfig.Scale == 120 && SlppConfig.AlignRight, "BaseLib settings survive process restart");
        Check(SlppConfig.UndoKey == "E" && SlppConfig.RedoKey == "", "BaseLib shortcuts survive process restart");
        ResetSettings();
        Check(SlppConfig.Toolbar && SlppConfig.Turns && SlppConfig.UndoKey == "Ctrl+Z", "BaseLib reset restores defaults");
    }

    private static async Task SendKey(Key key, bool ctrl = false, bool shift = false)
    {
        Input.ParseInputEvent(KeyEvent(key, ctrl, shift));
        Input.ParseInputEvent(KeyEvent(key, ctrl, shift, false));
        for (int i = 0; i < 6; i++) await GameBridge.Frame();
    }

    private static async Task TestUi()
    {
        ResetSettings();
        var page = await OpenConfig();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        for (int i = 0; i < 20; i++) await GameBridge.Frame();
        Check(SlppConfig.IsOpen, "BaseLib configuration renders");
        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(root.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://slpp-settings.png")) == Error.Ok, "BaseLib configuration screenshot");
        foreach (var section in GameBridge.Descendants(page).OfType<NConfigCollapsibleSection>())
            section.IsExpanded = section.Name.ToString().EndsWith("Shortcuts", StringComparison.Ordinal);
        ((NConfigLineEdit)Widget<NConfigOptionRow>("UndoKey").SettingControl).GrabFocus();
        for (int i = 0; i < 20; i++) await GameBridge.Frame();
        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(root.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://slpp-shortcuts.png")) == Error.Ok, "BaseLib shortcut fields screenshot");
        NGame.Instance!.MainMenu!.SubmenuStack.Pop();
        for (int i = 0; i < 5; i++) await GameBridge.Frame();
        MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage("zhs");
        if (GameBridge.State != null) GameBridge.Manager.CleanUp();
        await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true, ActModel.GetDefaultList(), [], "SLPP-UI", GameMode.Standard);
        await Enter(MapPointType.Monster);
        var player = LocalContext.GetMe(GameBridge.State)!;
        var card = player.PlayerCombatState!.Hand.Cards.First(c => c.Type == CardType.Attack && c.CanPlay());
        GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, player.Creature.CombatState!.HittableEnemies.First()));
        await Settle("UI play");
        string hash = GameBridge.Fingerprint();
        Press("SlppUndo");
        await GameBridge.Until(() => !Recorder.Busy && Recorder.History!.PointCursor == 0, "UI undo");
        Check(Recorder.History!.PointCursor == 0, "toolbar undo button");
        Press("SlppRedo");
        await GameBridge.Until(() => !Recorder.Busy && GameBridge.Fingerprint() == hash, "UI redo");
        Check(GameBridge.Fingerprint() == hash, "toolbar redo button");
        Check(Recorder.Room!.Points.All(p => !p.Label.Any(c => c >= 0x4e00 && c <= 0x9fff)), "timeline labels remain English");
        DisplayServer.WindowMoveToForeground();
        for (int i = 0; i < 15; i++) await GameBridge.Frame();
        Check(NGame.IsGameFocusedWindow(), "test window receives keyboard focus");
        SlppConfig.UndoKey = "E";
        var input = new LineEdit { Position = new Vector2(700, 100), Size = new Vector2(200, 40) };
        root.AddChild(input);
        input.GrabFocus();
        await GameBridge.Frame();
        await SendKey(Key.E);
        Check(!Recorder.Busy && GameBridge.Fingerprint() == hash, "typing does not trigger shortcuts");
        input.ReleaseFocus();
        root.RemoveChild(input);
        input.QueueFree();
        await SendKey(Key.Z, ctrl: true);
        Check(!Recorder.Busy && GameBridge.Fingerprint() == hash, "old shortcut is inactive after rebinding");
        SlppConfig.RedoKey = "E";
        await SendKey(Key.E);
        Check(!Recorder.Busy && GameBridge.Fingerprint() == hash, "duplicate shortcuts cannot trigger unintended actions");
        SlppConfig.RedoKey = "Ctrl+Y";
        await SendKey(Key.E);
        await GameBridge.Until(() => !Recorder.Busy && Recorder.History!.PointCursor == 0, "rebound shortcut");
        Check(GameBridge.Turn == 1, "rebound E undoes without native end turn");
        await SendKey(Key.Y, ctrl: true);
        await GameBridge.Until(() => !Recorder.Busy && GameBridge.Fingerprint() == hash, "shortcut redo");
        Check(GameBridge.Fingerprint() == hash, "redo keyboard shortcut");
        SlppConfig.UndoKey = "Ctrl+Z";
        SlppConfig.Actions = false;
        Hud.Tick();
        await SendKey(Key.Z, ctrl: true);
        Check(!Recorder.Busy && GameBridge.Fingerprint() == hash && !Widget<Button>("SlppUndo").Visible, "disabled component hides buttons and ignores shortcuts");
        SlppConfig.Toolbar = false;
        Hud.Tick();
        Check(!Widget<PanelContainer>("SlppToolbar").Visible, "toolbar toggle applies during a run");
        ResetSettings();
        await SendKey(Key.F9);
        Check(!SlppConfig.IsOpen && !Recorder.Busy, "removed F9 settings shortcut stays inactive");
        Press("SlppTimeline");
        for (int i = 0; i < 15; i++) await GameBridge.Frame();
        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(root.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://slpp-ui.png")) == Error.Ok, "SL toolbar screenshot");
    }

    private static async Task TestPotions()
    {
        if (GameBridge.State != null) GameBridge.Manager.CleanUp();
        await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true, ActModel.GetDefaultList(), [], "SLPP-POTION", GameMode.Standard);
        await Settle("potion fixture start");
        await MegaCrit.Sts2.Core.Commands.PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.StrengthPotion>(LocalContext.GetMe(GameBridge.State)!);
        await Enter(MapPointType.Monster);
        var player = LocalContext.GetMe(GameBridge.State)!;
        GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new UsePotionAction(player.Potions.First(), null, true));
        await RoundTrip("potion use undo/redo");
        await Recorder.Restore(Recorder.History!.RoomCursor, 0);
        player = LocalContext.GetMe(GameBridge.State)!;
        GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(new DiscardPotionGameAction(player, (uint)player.GetPotionSlotIndex(player.Potions.First()), true));
        await RoundTrip("potion discard undo/redo");
        await Recorder.FlushAsync();
        byte[] before = File.ReadAllBytes(Recorder.PathFor(Recorder.History!));
        var original = Recorder.Room!.Points[0];
        Recorder.Room.Points[0] = original with { Hash = new string('0', 64) };
        bool failed = false;
        try { await Recorder.Restore(Recorder.History!.RoomCursor, 0); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && Recorder.Faulted, "checksum mismatch stops replay and recording");
        Check(before.SequenceEqual(File.ReadAllBytes(Recorder.PathFor(Recorder.History!))), "failed restore preserves committed journal");
        Recorder.Room.Points[0] = original;
        await Recorder.Restore(Recorder.History!.RoomCursor, 0);
        Check(!Recorder.Faulted && GameBridge.Fingerprint() == original.Hash, "room anchor recovers after rejected replay");
    }
}
