using System.IO.Compression;
using System.Text.Json;

namespace slpp;

public sealed class Timeline
{
    public int Schema { get; set; } = 1;
    public string GameBuild { get; set; } = "";
    public uint ModelHash { get; set; }
    public string RunKey { get; set; } = "";
    public List<RoomRecord> Rooms { get; set; } = [];
    public int RoomCursor { get; set; }
    public int PointCursor { get; set; }
    public RoomRecord? Current => Rooms.ElementAtOrDefault(RoomCursor);

    public void Branch()
    {
        var room = Current;
        if (room == null) return;
        var point = room.Points[PointCursor];
        room.Commands.RemoveRange(point.Commands, room.Commands.Count - point.Commands);
        room.Choices.RemoveRange(point.Choices, room.Choices.Count - point.Choices);
        room.Points.RemoveRange(PointCursor + 1, room.Points.Count - PointCursor - 1);
        Rooms.RemoveRange(RoomCursor + 1, Rooms.Count - RoomCursor - 1);
    }
}

public sealed class RoomRecord
{
    public string Save { get; set; } = "";
    public int Floor { get; set; }
    public string Label { get; set; } = "";
    public List<uint> ChoiceIds { get; set; } = [];
    public List<int> RewardIds { get; set; } = [];
    public uint ActionId { get; set; }
    public uint HookId { get; set; }
    public List<RecordedCommand> Commands { get; set; } = [];
    public List<RecordedChoice> Choices { get; set; } = [];
    public List<TimelinePoint> Points { get; set; } = [];
}

public sealed record RecordedCommand(string Kind, byte[] Data, int Index = 0, string Label = "", string? Card = null, string? Potion = null);
public sealed record RecordedChoice(uint Id, byte[] Data);
public sealed record TimelinePoint(int Commands, int Choices, int Turn, bool AwaitingChoice, string Hash, string Label, int FingerprintVersion = 1);

public static class TimelineFile
{
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    public static byte[] Encode(Timeline timeline)
    {
        using var buffer = new MemoryStream();
        using (var stream = new BrotliStream(buffer, CompressionLevel.Fastest, true))
            JsonSerializer.Serialize(stream, timeline, Options);
        return buffer.ToArray();
    }

    public static Timeline Decode(byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes);
        using var stream = new BrotliStream(buffer, CompressionMode.Decompress);
        var timeline = JsonSerializer.Deserialize<Timeline>(stream, Options) ?? throw new InvalidDataException("Empty timeline");
        if (timeline.Schema != 1) throw new InvalidDataException("Unsupported timeline schema");
        if (string.IsNullOrWhiteSpace(timeline.RunKey) || timeline.RunKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidDataException("Invalid run key");
        if (timeline.Rooms.Count == 0 || timeline.RoomCursor < 0 || timeline.RoomCursor >= timeline.Rooms.Count)
            throw new InvalidDataException("Invalid room cursor");
        if (timeline.PointCursor < 0 || timeline.PointCursor >= timeline.Current!.Points.Count)
            throw new InvalidDataException("Invalid decision cursor");
        foreach (var room in timeline.Rooms)
        {
            int commands = -1, choices = -1;
            if (room.Points.Count == 0 || string.IsNullOrWhiteSpace(room.Save)) throw new InvalidDataException("Empty room");
            foreach (var p in room.Points)
            {
                if (p.Commands < 0 || p.Commands > room.Commands.Count || p.Choices < 0 || p.Choices > room.Choices.Count)
                    throw new InvalidDataException("Invalid decision boundary");
                if (p.Commands < commands || p.Choices < choices || p.Hash.Length != 64 || !p.Hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid decision ordering or checksum");
                if (p.FingerprintVersion is < 1 or > 2) throw new InvalidDataException("Unsupported fingerprint version");
                commands = p.Commands; choices = p.Choices;
            }
        }
        return timeline;
    }

    public static Timeline ReadWithBackup(string path)
    {
        try { return Decode(File.ReadAllBytes(path)); }
        catch (Exception first) when (first is IOException or InvalidDataException or JsonException)
        {
            return Decode(File.ReadAllBytes(path + ".bak"));
        }
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temp, path, true);
    }
}
