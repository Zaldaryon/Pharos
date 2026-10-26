using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Audio;

/// <summary>A sound the client created or started.</summary>
/// <param name="Location">The sound asset, such as <c>game:sounds/block/planks1.ogg</c>.</param>
/// <param name="Position">Where it plays in the world, or null for a sound relative to the listener.</param>
/// <param name="Volume">The volume, 0 to 1.</param>
/// <param name="Range">The audible range in blocks.</param>
/// <param name="Type">The sound category: sound, music, ambient, weather, entity, and so on.</param>
/// <param name="Loop">Whether it loops.</param>
/// <param name="Sequence">The order in which sounds were recorded, starting at 1.</param>
public sealed record RecordedSound(string Location, Vec3f? Position, float Volume, float Range, EnumSoundType Type, bool Loop, long Sequence)
{
    internal static RecordedSound From(SoundParams sound, long sequence) => new(
        sound.Location?.ToString() ?? "",
        sound.Position == null ? null : new Vec3f(sound.Position.X, sound.Position.Y, sound.Position.Z),
        sound.Volume,
        sound.Range,
        sound.SoundType,
        sound.ShouldLoop,
        sequence);
}

/// <summary>
/// Records every sound a headless client creates and starts, in place of the audio device it
/// does not have.
/// </summary>
/// <remarks>
/// The null audio device hands the game a silent sound object for every sound it asks for. The
/// game still decides which sounds to play, where, how loud and how often, for the player's own
/// footsteps, block and item interactions, entities, the ambience and music, and sounds the
/// server tells it to play, so recording those requests shows what a player would have heard.
/// One headless client runs per process, so the recorder of the most recently booted client is
/// the one that receives them.
/// </remarks>
public sealed class SoundRecorder
{
    private static SoundRecorder? s_current;

    private readonly object _lock = new();
    private readonly List<RecordedSound> _created = [];
    private readonly List<RecordedSound> _started = [];
    private readonly HashSet<long> _stopped = [];
    private long _sequence;

    /// <summary>Sounds the game created, in order, whether or not it started them.</summary>
    public IReadOnlyList<RecordedSound> Created
    {
        get { lock (_lock) return [.. _created]; }
    }

    /// <summary>Sounds the game started playing, in order.</summary>
    public IReadOnlyList<RecordedSound> Started
    {
        get { lock (_lock) return [.. _started]; }
    }

    /// <summary>
    /// Whether a sound whose asset location contains <paramref name="locationFragment"/> was
    /// started, such as <c>"walk/grass"</c> or <c>"block/planks"</c>.
    /// </summary>
    public bool WasPlayed(string locationFragment) =>
        Started.Any(s => s.Location.Contains(locationFragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>The started sounds whose asset location contains <paramref name="locationFragment"/>.</summary>
    public IReadOnlyList<RecordedSound> Played(string locationFragment) =>
        Started.Where(s => s.Location.Contains(locationFragment, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Computes the gain a listener at <paramref name="listener"/> would hear for <paramref name="sound"/>,
    /// using OpenAL's inverse-distance-clamped model: gain = volume * clamp(range / distance, 0, 1) * level.
    /// </summary>
    public float Audibility(RecordedSound sound, Vec3f listener, float level = 1f)
    {
        if (sound.Position == null) return sound.Volume * level;
        float dx = sound.Position.X - listener.X;
        float dy = sound.Position.Y - listener.Y;
        float dz = sound.Position.Z - listener.Z;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= 0f) return sound.Volume * level;
        float gain = sound.Volume * Math.Clamp(sound.Range / distance, 0f, 1f) * level;
        return gain;
    }

    /// <summary>Sounds that are still playing (loops never stopped). Empty when none leaked.</summary>
    public IReadOnlyList<RecordedSound> StillPlaying => Started.Where(s => s.Loop && !_stopped.Contains(s.Sequence)).ToList();

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _created.Clear();
            _started.Clear();
        }
    }

    /// <summary>Makes this the recorder the null audio device reports to.</summary>
    internal void Activate() => Volatile.Write(ref s_current, this);

    internal static long OnCreated(SoundParams sound) => s_current?.Record(sound, started: false) ?? 0;

    internal static void OnStarted(SoundParams sound) => s_current?.Record(sound, started: true);
    internal static void OnStopped(long sequence) { if (sequence > 0) s_current?._stopped.Add(sequence); }
    internal static void OnDisposed(long sequence) { if (sequence > 0) s_current?._stopped.Add(sequence); }

    private long Record(SoundParams sound, bool started)
    {
        lock (_lock)
        {
            RecordedSound recorded = RecordedSound.From(sound, ++_sequence);
            (started ? _started : _created).Add(recorded);
            return recorded.Sequence;
        }
    }
}
