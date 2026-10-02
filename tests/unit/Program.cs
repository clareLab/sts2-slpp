using slpp;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
static Timeline Sample() => new()
{
    RunKey = "123-ABC",
    GameBuild = "test",
    RoomCursor = 0,
    PointCursor = 1,
    Rooms = [new RoomRecord
    {
        Save = "{}", Floor = 1, Commands = [new("action", [1]), new("shop", [], 1)],
        Choices = [new(0, [2])],
        Points = [new(0, 0, 1, false, new('A',64), "start"), new(1, 0, 1, true, new('B',64), "choice", 2), new(1, 1, 1, false, new('C',64), "done"), new(2, 1, 1, false, new('D',64), "shop")]
    }, new RoomRecord { Save = "{}", Floor = 2, Points = [new(0,0,0,false,new('E',64),"start")] }]
};

var timeline = TimelineFile.Decode(TimelineFile.Encode(Sample()));
Check(timeline.Rooms.Count == 2 && timeline.Current!.Commands[0].Data.SequenceEqual(new byte[] { 1 }), "compressed roundtrip keeps binary commands and future rooms");
Check(timeline.Current!.Points[0].FingerprintVersion == 1 && timeline.Current.Points[1].FingerprintVersion == 2, "legacy and current fingerprint versions survive roundtrip");
timeline.Branch();
Check(timeline.Rooms.Count == 1 && timeline.Current!.Choices.Count == 0 && timeline.Current.Commands.Count == 1 && timeline.Current.Points.Count == 2, "branch at pending choice removes its result and all future rooms");

foreach (var corrupt in new Action<Timeline>[] {
    h => h.Schema = 99, h => h.RunKey = "../escape", h => h.PointCursor = 50,
    h => h.Current!.Points[1] = h.Current.Points[1] with { Commands = 100 },
    h => h.Current!.Points[2] = h.Current.Points[2] with { Commands = 0 },
    h => h.Current!.Points[0] = h.Current.Points[0] with { FingerprintVersion = 99 }
})
{
    var h = Sample(); corrupt(h);
    bool rejected = false;
    try { TimelineFile.Decode(TimelineFile.Encode(h)); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "invalid archive rejected");
}
string directory = Path.Combine(Path.GetTempPath(), "slpp-test-" + Guid.NewGuid());
try
{
    string path = Path.Combine(directory, "run.slpp");
    TimelineFile.WriteAtomic(path, TimelineFile.Encode(Sample()));
    TimelineFile.WriteAtomic(path, TimelineFile.Encode(timeline));
    Check(TimelineFile.ReadWithBackup(path).Rooms.Count == 1, "atomic replacement uses latest archive");
    File.WriteAllBytes(path, [0, 1, 2]);
    Check(TimelineFile.ReadWithBackup(path).Rooms.Count == 2, "corrupt primary falls back to previous committed archive");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("TIMELINE_TESTS_OK");
