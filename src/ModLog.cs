using Godot;

namespace slpp;

internal static class ModLog
{
    internal sealed record Message(DateTime Time, string Text, bool Error);
    private static readonly object Gate = new();
    private static readonly Queue<Message> Messages = new();
    private static Exception? _lastError;
    private static int _version;
    private static int _lastErrorVersion;
    private static int _seenVersion;
    internal static bool Unread { get { lock (Gate) return _lastErrorVersion > _seenVersion; } }
    internal static int Version { get { lock (Gate) return _version; } }

    internal static (int Version, Message[] Messages) Snapshot()
    {
        lock (Gate) return (_version, Messages.Reverse().ToArray());
    }

    internal static void Acknowledge(int version)
    {
        lock (Gate) _seenVersion = Math.Max(_seenVersion, Math.Min(version, _version));
    }

    internal static void Info(string text) => Add(text, null);
    internal static void Error(string context, Exception error) => Add(context + ": " + error.Message, error);

    private static void Add(string text, Exception? error)
    {
        lock (Gate)
        {
            if (error != null && ReferenceEquals(error, _lastError)) return;
            Messages.Enqueue(new Message(DateTime.Now, text, error != null));
            while (Messages.Count > 64) Messages.Dequeue();
            _version++;
            if (error != null) { _lastError = error; _lastErrorVersion = _version; }
        }
        if (error != null) GD.PrintErr("[slpp] " + text + "\n" + error);
        else GD.Print("[slpp] " + text);
    }
}
