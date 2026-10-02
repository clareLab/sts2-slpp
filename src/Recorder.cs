using Environment = System.Environment;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Saves.Validation;
using MegaCrit.Sts2.Core.Models;

namespace slpp;

internal static class Recorder
{
    internal static Timeline? History { get; private set; }
    internal static bool Restoring { get; private set; }
    internal static bool Busy { get; private set; }
    internal static bool Faulted { get; private set; }
    internal static bool WaitingForChoice { get; private set; }
    internal static bool BlocksLiveActions => Restoring || _resumePending;
    internal static GameAction? ReplayingAction { get; private set; }
    internal static string Status { get; private set; } = "Start a single-player run";
    internal static string? LastError { get; private set; }
    private static int _replayChoice;
    private static TimelinePoint? _target;
    private static RoomRecord? _replayRoom;
    private static bool _dirty;
    private static bool _needPoint;
    private static long _flushAfter;
    private static Task _writeTask = Task.CompletedTask;
    private static string? _writeError;
    private static int _stableFrames;
    private static int _externalDepth;
    private static int _generation;
    private static bool _detached;
    private static bool _resumePending;
    private static bool _atHistoricalPosition;
    private static string? _profileDirectory;
    internal static string OperationStage { get; set; } = "";
    internal static int OperationStep { get; private set; }
    internal static int OperationTotal { get; private set; }
    internal static bool HasExternalTask => _externalDepth > 0;
    internal static RoomRecord? Room => History?.Current;
    internal static string ProfileDirectory => ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath($"slpp/{GameBridge.Build}-{ModelIdSerializationCache.Hash:X8}"));
    internal static string PathFor(Timeline h) => Path.Combine(_profileDirectory ?? ProfileDirectory, h.RunKey + ".slpp");
    internal static void CheckProfile()
    {
        string path = ProfileDirectory;
        if (_profileDirectory == path) return;
        if (_profileDirectory != null) { Flush(); _writeTask.GetAwaiter().GetResult(); }
        _profileDirectory = path;
        History = null; _detached = _resumePending = _needPoint = Faulted = false;
        _atHistoricalPosition = false;
        LoadRecent();
    }

    internal static void BeginRoom(SerializableRun save)
    {
        if (Restoring || !GameBridge.Singleplayer) return;
        Faulted = false;
        string key = save.StartTime + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(save.SerializableRng.Seed ?? ""))).Substring(0, 12);
        _profileDirectory ??= ProfileDirectory;
        if (Entry.AllowAutoResume && History?.RunKey != key && File.Exists(Path.Combine(_profileDirectory, key + ".slpp")))
        {
            try
            {
                var saved = TimelineFile.ReadWithBackup(Path.Combine(_profileDirectory, key + ".slpp"));
                if (saved.GameBuild == GameBridge.Build && saved.ModelHash == ModelIdSerializationCache.Hash)
                { TimelineText.Normalize(saved); History = saved; _detached = true; }
            }
            catch (Exception e) { GD.PrintErr("[slpp] Archive could not be resumed: " + e.Message); }
        }
        if (Entry.AllowAutoResume && _detached && History?.RunKey == key)
        {
            _resumePending = true;
            _detached = false;
            return;
        }
        if (History == null || History.RunKey != key)
            History = new Timeline { RunKey = key, GameBuild = GameBridge.Build, ModelHash = ModelIdSerializationCache.Hash };
        else if (Room?.Points.Count > 0) History.Branch();
        var manager = GameBridge.Manager;
        var room = new RoomRecord
        {
            Save = GameBridge.SaveJson(save),
            Floor = GameBridge.State!.TotalFloor + 1,
            Label = $"Floor {GameBridge.State.TotalFloor + 1}",
            ActionId = manager.ActionQueueSet.NextActionId,
            HookId = manager.ActionQueueSynchronizer.NextHookId,
            ChoiceIds = manager.PlayerChoiceSynchronizer.ChoiceIds.ToList(),
            RewardIds = manager.RewardsSetSynchronizer.GetNextRewardIds().ToList()
        };
        History.Rooms.Add(room);
        History.RoomCursor = History.Rooms.Count - 1;
        History.PointCursor = 0;
        _needPoint = true;
        _stableFrames = 0;
        _detached = false;
        GD.Print($"[slpp] Checkpoint {room.Floor}");
    }

    internal static void RecordAction(GameAction action)
    {
        if (Restoring || Faulted || _resumePending || !GameBridge.Singleplayer || Room == null) return;
        if (action is not (PlayCardAction or UsePotionAction or DiscardPotionGameAction or EndPlayerTurnAction or PickRelicAction)) return;
        if (Room.Points.Count == 0) AddPoint(false, "Room start");
        if (_atHistoricalPosition) History!.Branch();
        else if (_needPoint && Room.Commands.Count > Room.Points[History!.PointCursor].Commands)
            AddPoint(false, Room.Commands.Last().Label);
        _atHistoricalPosition = false;
        var evt = new CombatReplayEvent { eventType = CombatReplayEventType.GameAction, playerId = action.OwnerId, action = action.ToNetAction() };
        string label = TimelineText.Action(evt);
        Room.Commands.Add(HistoryPreview.Capture(new RecordedCommand("action", GameBridge.Pack(evt), Label: label), evt));
        _needPoint = true;
        _stableFrames = 0;
    }

    internal static void RecordChoice(Player player, uint id, PlayerChoiceResult result)
    {
        if (BlocksLiveActions || Faulted || Room == null || !GameBridge.Singleplayer) return;
        if (_atHistoricalPosition)
        {
            History!.Branch();
            _atHistoricalPosition = false;
        }
        AddPoint(true, "Choose");
        History!.Branch();
        Room.Choices.Add(new RecordedChoice(id, GameBridge.Pack(result.ToNetData())));
        WaitingForChoice = false;
        _needPoint = true;
        _stableFrames = 0;
    }

    internal static bool SelectLocally(bool original)
    {
        if (!Restoring || !original) return original;
        if (_target != null && _replayChoice < _target.Choices) return false;
        WaitingForChoice = true;
        if (_target?.AwaitingChoice != true)
            LastError = "Replay encountered an unrecorded choice";
        return true;
    }

    internal static Task<PlayerChoiceResult>? ReplayChoice(Player player, uint id, bool sync = true)
    {
        if (!Restoring || _replayRoom == null || _target == null) return null;
        if (_replayChoice >= _target.Choices) throw new InvalidOperationException("No recorded choice available");
        var choice = _replayRoom.Choices[_replayChoice++];
        if (choice.Id != id) throw new InvalidOperationException($"Choice ID mismatch: {id} != {choice.Id}");
        var result = PlayerChoiceResult.FromNetData(player, GameBridge.State!, GameBridge.Unpack<NetPlayerChoiceResult>(choice.Data));
        if (sync) GameBridge.Manager.PlayerChoiceSynchronizer.SyncLocalChoice(player, id, result);
        return Task.FromResult(result);
    }

    internal static void Tick()
    {
        if (_resumePending && !Busy && (GameBridge.Stable || GameBridge.ChoiceOpen) && History != null)
        {
            _resumePending = false;
            Hud.Run(() => Restore(History.RoomCursor, History.PointCursor));
        }
        if (!Restoring && !Faulted && _needPoint && Room != null)
        {
            if (GameBridge.Stable && (_externalDepth == 0 || ExternalDecisions.WaitingForDecision))
            {
                if (++_stableFrames >= 3)
                {
                    AddPoint(false, Room.Commands.LastOrDefault()?.Label ?? "Room start");
                    _needPoint = false;
                }
            }
            else _stableFrames = 0;
        }
        if (_dirty && !Restoring && Environment.TickCount64 >= _flushAfter && _writeTask.IsCompleted) Flush();
        if (_writeError != null && !Busy) Status = "History write failed: " + _writeError;
    }

    private static void AddPoint(bool choice, string label)
    {
        if (Room == null) return;
        if (Room.Points.Count == 0)
            Room.Label = GameBridge.State!.CurrentRoom?.RoomType.ToString() switch
            { "Monster" => "Combat", "Elite" => "Elite", "Boss" => "Boss", "Event" => "Event", "Shop" => "Shop", "RestSite" => "Rest site", "Treasure" => "Treasure", _ => "Room" };
        var point = new TimelinePoint(Room.Commands.Count, Room.Choices.Count, GameBridge.Turn, choice, GameBridge.Fingerprint(), label, 2);
        if (Room.Points.LastOrDefault() is { } last && last.Commands == point.Commands && last.Choices == point.Choices && last.AwaitingChoice == choice)
            return;
        Room.Points.Add(point);
        History!.PointCursor = Room.Points.Count - 1;
        Status = $"Step {History.PointCursor}";
        MarkDirty();
        GD.Print($"[slpp] Point {History.PointCursor}: {point.Label} cmd={point.Commands} choices={point.Choices} hash={point.Hash[..8]}");
    }

    internal static void MarkDirty() { _dirty = true; _flushAfter = Environment.TickCount64 + 250; }
    internal static void Flush()
    {
        if (Faulted || History?.Current?.Points.Count is not > 0) return;
        _dirty = false;
        string json = JsonSerializer.Serialize(History, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        string path = PathFor(History);
        var previous = _writeTask;
        _writeTask = Task.Run(async () => { await previous; TimelineFile.WriteAtomic(path, TimelineFile.Encode(JsonSerializer.Deserialize<Timeline>(json)!)); })
            .ContinueWith(t =>
            {
                _writeError = t.IsFaulted ? t.Exception!.GetBaseException().Message : null;
                if (t.IsFaulted) GD.PrintErr("[slpp] History write failed: " + t.Exception);
            });
    }

    internal static async Task FlushAsync()
    {
        Flush(); await _writeTask;
        if (_writeError != null) throw new IOException(_writeError);
    }

    internal static async Task Restore(int roomIndex, int pointIndex)
    {
        if (Busy || History == null) return;
        if (GameBridge.State != null && !GameBridge.Singleplayer) return;
        if (History.GameBuild != GameBridge.Build || History.ModelHash != ModelIdSerializationCache.Hash)
            throw new InvalidOperationException("Game version or content index does not match this history");
        var room = History.Rooms[roomIndex];
        var target = room.Points[pointIndex];
        var recoveryRoom = Room ?? room;
        int recoveryIndex = History.RoomCursor;
        OperationStage = "Waiting for the current action";
        OperationStep = OperationTotal = 0;
        Busy = true;
        try
        {
            if (!Faulted && GameBridge.Singleplayer)
                await GameBridge.Until(() => GameBridge.ChoiceOpen || ExternalDecisions.WaitingForDecision ||
                    (GameBridge.Stable && _externalDepth == 0), "Timed out waiting for the current action before restoring");
        }
        catch { Busy = false; throw; }
        Restoring = true;
        _detached = false;
        _generation++;
        _externalDepth = 0;
        LastError = null;
        Status = "Restoring";
        _replayRoom = room;
        _target = target;
        _replayChoice = 0;
        WaitingForChoice = false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var fastMode = SaveManager.Instance.PrefsSave.FastMode;
        var originalProgress = SaveManager.Instance.Progress;
        bool completed = false;
        try
        {
            var progressJson = JsonSerializer.Serialize(originalProgress.ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableProgress>());
            SaveManager.Instance.Progress = ProgressState.FromSerializable(JsonSerializer.Deserialize(progressJson, JsonSerializationUtility.GetTypeInfo<SerializableProgress>())!, new DeserializationContext());
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            await Replay(room, target);
            OperationStage = "Verifying state";
            string actual = GameBridge.Fingerprint(target.FingerprintVersion);
            if (actual != target.Hash && target.FingerprintVersion == 1 && !GameBridge.InCombatRoom && target.Turn > 0)
                actual = await CheckLegacyBoundary(roomIndex, room, target, actual);
            if (actual != target.Hash)
                throw new InvalidOperationException($"Replay state mismatch: {actual[..8]} != {target.Hash[..8]}");
            if (target.FingerprintVersion == 1)
                room.Points[pointIndex] = target with { Hash = GameBridge.Fingerprint(), Turn = GameBridge.Turn, FingerprintVersion = 2 };
            History.RoomCursor = roomIndex;
            History.PointCursor = pointIndex;
            _atHistoricalPosition = true;
            Faulted = false;
            _needPoint = false;
            Status = "Restored";
            GD.Print($"[slpp] RESTORE_OK Floor {room.Floor} Step {pointIndex} ({watch.ElapsedMilliseconds} ms)");
            MarkDirty();
            completed = true;
        }
        catch (Exception ex)
        {
            Suspend(ex);
            try
            {
                _generation++;
                _externalDepth = 0;
                _target = null;
                _replayRoom = null;
                OperationStage = "Recovering room";
                OperationStep = OperationTotal = 0;
                await GameBridge.Load(recoveryRoom);
                await GameBridge.Until(() => GameBridge.Stable || GameBridge.ChoiceOpen, "Room recovery timed out");
                History.RoomCursor = recoveryIndex;
                History.PointCursor = 0;
                _atHistoricalPosition = true;
                Status = "Room reloaded. History paused.";
            }
            catch (Exception recoveryError)
            {
                GD.PrintErr("[slpp] Room recovery failed: " + recoveryError);
                await MegaCrit.Sts2.Core.Nodes.NGame.Instance!.ReturnToMainMenu();
            }
            throw;
        }
        finally
        {
            Restoring = false;
            if (!completed) Busy = false;
            SaveManager.Instance.Progress = originalProgress;
            SaveManager.Instance.PrefsSave.FastMode = fastMode;
            _target = null;
            _replayRoom = null;
        }
        try
        {
            await SaveManager.Instance.IncrementNumReloads(GameBridge.ReadSave(room.Save), MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer);
            await FlushAsync();
        }
        catch (Exception ex) { Status = "Restored, but saving failed: " + ex.Message; throw; }
        finally { Busy = false; }
    }

    internal static void Suspend(Exception error)
    {
        Faulted = true;
        _needPoint = _dirty = _resumePending = false;
        LastError = error.ToString();
        Status = "History paused";
        GD.PrintErr("[slpp] " + LastError);
    }

    internal static void Capture(Action callback)
    {
        if (Faulted) return;
        try { callback(); }
        catch (Exception error) { Suspend(error); }
    }

    private static async Task Replay(RoomRecord room, TimelinePoint target)
    {
        _generation++;
        _externalDepth = _replayChoice = 0;
        _replayRoom = room;
        _target = target;
        WaitingForChoice = false;
        LastError = null;
        OperationTotal = target.Commands;
        OperationStep = 0;
        await GameBridge.Load(room);
        OperationStage = "Restoring room start";
        await WaitBoundary(target.Commands == 0);
        for (int i = 0; i < target.Commands; i++)
        {
            var command = room.Commands[i];
            OperationStage = $"Replaying {i + 1} / {target.Commands}: {command.Label}";
            if (command.Kind == "action")
            {
                var evt = GameBridge.Unpack<CombatReplayEvent>(command.Data);
                ReplayingAction = evt.action!.ToGameAction(GameBridge.State!.GetPlayer(evt.playerId!.Value)!);
                try { GameBridge.Manager.ActionQueueSynchronizer.RequestEnqueue(ReplayingAction); }
                finally { ReplayingAction = null; }
            }
            else ExternalDecisions.Execute(command);
            await WaitBoundary(i == target.Commands - 1);
            OperationStep = i + 1;
        }
        if (_replayChoice != target.Choices) throw new InvalidOperationException("Recorded choices were not fully consumed");
    }

    private static async Task<string> CheckLegacyBoundary(int roomIndex, RoomRecord room, TimelinePoint target, string actual)
    {
        var previous = History!.Rooms.Take(roomIndex).LastOrDefault(r => r.Commands.Any(c => c.Kind == "action"));
        if (previous == null) return actual;
        var end = previous.Points[^1];
        await Replay(previous, end);
        if (GameBridge.Fingerprint(end.FingerprintVersion) != end.Hash)
            throw new InvalidOperationException("Previous combat could not be verified");
        var players = NetFullCombatState.FromRun(GameBridge.State!, null).Players;
        await Replay(room, target);
        return GameBridge.Fingerprint(1, players);
    }

    private static async Task WaitBoundary(bool final)
    {
        int frames = 0;
        await GameBridge.Until(() =>
        {
            if (LastError != null) throw new InvalidOperationException(LastError);
            bool ready = (GameBridge.Stable && (_externalDepth == 0 || ExternalDecisions.WaitingForDecision)) || (final && _target?.AwaitingChoice == true && WaitingForChoice && GameBridge.ChoiceOpen);
            frames = ready ? frames + 1 : 0;
            return frames >= 5;
        }, $"Timed out during {OperationStage}");
    }

    internal static Task Step(int direction)
    {
        if (History == null || Room == null || Room.Points.Count == 0) return Task.CompletedTask;
        int index = History.PointCursor + direction;
        if (direction < 0 && Room.Commands.Count > Room.Points[History.PointCursor].Commands) index = History.PointCursor;
        if (index < 0 && History.RoomCursor > 0)
            return Restore(History.RoomCursor - 1, History.Rooms[History.RoomCursor - 1].Points.Count - 1);
        if (index >= Room.Points.Count && History.RoomCursor + 1 < History.Rooms.Count)
            return Restore(History.RoomCursor + 1, 0);
        return index >= 0 && index < Room.Points.Count ? Restore(History.RoomCursor, index) : Task.CompletedTask;
    }

    internal static bool RecordExternal(string kind, int index, string label)
    {
        try { return RecordExternalCore(kind, index, label); }
        catch (Exception error) { Suspend(error); return false; }
    }

    private static bool RecordExternalCore(string kind, int index, string label)
    {
        if (Restoring || Faulted || _resumePending || !GameBridge.Singleplayer || Room == null) return false;
        if (Room.Points.Count == 0) AddPoint(false, "Room start");
        if (_atHistoricalPosition) History!.Branch();
        else if (_needPoint) AddPoint(false, Room.Commands.LastOrDefault()?.Label ?? "Room start");
        _atHistoricalPosition = false;
        Room.Commands.Add(new RecordedCommand(kind, [], index, label));
        _needPoint = true;
        _stableFrames = 0;
        return true;
    }
    internal static async Task Track(Task task)
    {
        int generation = _generation;
        _externalDepth++;
        try { await task; }
        catch (Exception error)
        {
            if (Restoring && generation == _generation) LastError = error.ToString();
            throw;
        }
        finally { if (generation == _generation) _externalDepth--; }
    }
    internal static async Task<T> Track<T>(Task<T> task)
    {
        int generation = _generation;
        _externalDepth++;
        try { return await task; }
        catch (Exception error)
        {
            if (Restoring && generation == _generation) LastError = error.ToString();
            throw;
        }
        finally { if (generation == _generation) _externalDepth--; }
    }
    internal static Task TurnStep(int direction)
    {
        if (History == null || Room == null) return Task.CompletedTask;
        var candidates = Room.Points.Select((p, i) => (p, i)).Where(x => !x.p.AwaitingChoice && x.p.Turn > 0)
            .GroupBy(x => x.p.Turn).Select(g => g.First().i).ToList();
        int destination = direction < 0 ? candidates.LastOrDefault(i => i < History.PointCursor, -1) : candidates.FirstOrDefault(i => i > History.PointCursor, -1);
        return destination < 0 ? Task.CompletedTask : Restore(History.RoomCursor, destination);
    }

    internal static async Task Restart(bool random)
    {
        if (Busy || History == null) return;
        if (GameBridge.State != null && !GameBridge.Singleplayer) return;
        if (!random && History.Rooms[0].Floor == 1) { await Restore(0, 0); return; }
        var save = GameBridge.ReadSave(History.Rooms[0].Save);
        var seedState = RunState.FromSerializable(save);
        Flush();
        await _writeTask;
        OperationStage = "Restarting run";
        OperationStep = OperationTotal = 0;
        Busy = true;
        try
        {
            if (!Faulted && GameBridge.Singleplayer)
                await GameBridge.Until(() => GameBridge.ChoiceOpen || ExternalDecisions.WaitingForDecision ||
                    (GameBridge.Stable && _externalDepth == 0), "Timed out waiting for the current action before restarting");
            Restoring = true;
            _generation++;
            _externalDepth = 0;
            GameBridge.CleanUp();
            MegaCrit.Sts2.Core.Nodes.NGame.Instance!.RootSceneContainer.SetCurrentScene(new Control());
            ExternalDecisions.Reset();
            History = null;
            Faulted = false;
            Restoring = false;
            _atHistoricalPosition = false;
            await MegaCrit.Sts2.Core.Nodes.NGame.Instance!.StartNewSingleplayerRun(seedState.Players[0].Character, true,
                seedState.Acts.Select(a => a.CanonicalInstance).ToList(), seedState.Modifiers,
                random ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)) : seedState.Rng.StringSeed, seedState.GameMode, seedState.AscensionLevel);
        }
        finally { Busy = Restoring = false; }
    }

    internal static void LoadRecent()
    {
        if (!Entry.AllowAutoResume) return;
        string dir = ProfileDirectory;
        if (!Directory.Exists(dir)) return;
        foreach (string path in Directory.GetFiles(dir, "*.slpp").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var history = TimelineFile.ReadWithBackup(path);
                if (history.GameBuild != GameBridge.Build || history.ModelHash != ModelIdSerializationCache.Hash) continue;
                TimelineText.Normalize(history);
                History = history;
                _detached = true;
                Status = "History ready";
                return;
            }
            catch (Exception e) { GD.PrintErr("[slpp] Cannot read " + Path.GetFileName(path) + ": " + e.Message); }
        }
    }

    internal static void OnCleanup()
    {
        if (Restoring || Busy) return;
        _writeTask.GetAwaiter().GetResult();
        Flush();
        _writeTask.GetAwaiter().GetResult();
        _detached = true;
        _resumePending = false;
        _needPoint = false;
        _generation++;
        _externalDepth = 0;
        ExternalDecisions.Reset();
    }
}
