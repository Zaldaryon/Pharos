using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>The parts of the server's state a state hash covers.</summary>
[Flags]
public enum StateHashParts
{
    /// <summary>No part.</summary>
    None = 0,

    /// <summary>The block ids of a region, read in a fixed order.</summary>
    Blocks = 1,

    /// <summary>The loaded entities that are not players.</summary>
    Entities = 2,

    /// <summary>The players: positions and rotations, sorted by player id.</summary>
    Players = 4,

    /// <summary>Every part.</summary>
    All = Blocks | Entities | Players
}

/// <summary>What a state hash recording covers, and how much it keeps.</summary>
public sealed record StateHashOptions
{
    /// <summary>The parts to hash. Blocks need a region.</summary>
    public StateHashParts Parts { get; init; } = StateHashParts.All;

    /// <summary>Hashes one tick in this many. The default is every tick.</summary>
    public int Every { get; init; } = 1;

    /// <summary>The largest region, in blocks, a recording may hash. A larger region is refused.</summary>
    public int MaxBlocks { get; init; } = 262_144;

    /// <summary>How many samples a recording keeps. Recording stops there, and the recording is marked truncated.</summary>
    public int MaxTicks { get; init; } = 72_000;
}

/// <summary>The hashes of the chosen parts after one tick.</summary>
/// <param name="Tick">The server's tick count after the tick.</param>
/// <param name="Blocks">The hash of the region's blocks, or 0 when blocks are not hashed.</param>
/// <param name="Entities">The hash of the loaded entities, or 0 when entities are not hashed.</param>
/// <param name="Players">The hash of the players, or 0 when players are not hashed.</param>
public readonly record struct StateHashTick(long Tick, ulong Blocks, ulong Entities, ulong Players);

/// <summary>Where two state hash recordings first differ.</summary>
/// <param name="Kind">"diverged" when a sample differs, or "length" when one recording ends first.</param>
/// <param name="Index">The sample the difference is at, counted from the first sample.</param>
/// <param name="Tick">The tick of that sample.</param>
/// <param name="Parts">The parts that differ at that sample; empty for a length difference.</param>
/// <param name="Compared">How many samples both recordings have in common.</param>
public sealed record StateHashDivergence(string Kind, int Index, long Tick, IReadOnlyList<string> Parts, int Compared);

/// <summary>A state hash recording as saved, or read back to compare.</summary>
public sealed record StateHashFile(
    long StartTick,
    int Every,
    int TickMs,
    int[]? Region,
    StateHashParts Parts,
    bool Truncated,
    IReadOnlyList<StateHashTick> Ticks)
{
    /// <summary>The format's schema version, written to every file.</summary>
    public const int Schema = 1;

    /// <summary>The kind the file names itself, so a diff can refuse other JSON.</summary>
    public const string Kind = "pharos-state-hashes";

    /// <summary>Reads a file written by <see cref="StateHashRecording.Save"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a state hash recording of this schema.</exception>
    public static StateHashFile Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads a recording from its JSON text.</summary>
    /// <exception cref="InvalidDataException">The text is not a state hash recording of this schema.</exception>
    public static StateHashFile Parse(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root?["kind"]?.GetValue<string>() != Kind || root["schema"]?.GetValue<int>() != Schema)
        {
            throw new InvalidDataException("The file is not a state hash recording of schema " + Schema + ".");
        }

        int[]? region = root["region"] is JsonArray r ? r.Select(n => n!.GetValue<int>()).ToArray() : null;
        List<StateHashTick> ticks = [];
        foreach (JsonNode? node in root["ticks"]!.AsArray())
        {
            ticks.Add(new StateHashTick(
                node!["tick"]!.GetValue<long>(),
                Convert.ToUInt64(node["blocks"]!.GetValue<string>(), 16),
                Convert.ToUInt64(node["entities"]!.GetValue<string>(), 16),
                Convert.ToUInt64(node["players"]!.GetValue<string>(), 16)));
        }

        return new StateHashFile(
            root["startTick"]!.GetValue<long>(),
            root["every"]!.GetValue<int>(),
            root["tickMs"]!.GetValue<int>(),
            region,
            Enum.Parse<StateHashParts>(root["parts"]!.GetValue<string>()),
            root["truncated"]!.GetValue<bool>(),
            ticks);
    }

    /// <summary>
    /// The first place the two recordings differ, or null when they are the same for every sample they both have
    /// and end together.
    /// </summary>
    /// <exception cref="ArgumentException">The recordings cover different regions, parts, sampling, or tick lengths, so they cannot be compared.</exception>
    public static StateHashDivergence? Compare(StateHashFile a, StateHashFile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!(a.Region ?? []).SequenceEqual(b.Region ?? []) || a.Parts != b.Parts || a.Every != b.Every || a.TickMs != b.TickMs)
        {
            throw new ArgumentException("The recordings cover different regions, parts, sampling or tick lengths, so they cannot be compared.");
        }

        int common = Math.Min(a.Ticks.Count, b.Ticks.Count);
        for (int i = 0; i < common; i++)
        {
            StateHashTick left = a.Ticks[i];
            StateHashTick right = b.Ticks[i];
            List<string> differing = [];
            if (left.Blocks != right.Blocks) differing.Add("blocks");
            if (left.Entities != right.Entities) differing.Add("entities");
            if (left.Players != right.Players) differing.Add("players");
            if (differing.Count > 0) return new StateHashDivergence("diverged", i, left.Tick, differing, common);
        }

        if (a.Ticks.Count == b.Ticks.Count) return null;

        IReadOnlyList<StateHashTick> longer = a.Ticks.Count > b.Ticks.Count ? a.Ticks : b.Ticks;
        return new StateHashDivergence("length", common, longer[common].Tick, [], common);
    }
}

/// <summary>Records the server's state hash after each tick, through <see cref="EmbeddedServerHost.StateHash"/>.</summary>
public sealed class StateHashRecorder
{
    private readonly EmbeddedServerHost _host;

    internal StateHashRecorder(EmbeddedServerHost host) => _host = host;

    /// <summary>Starts hashing the server's state after each tick, until the recording is disposed.</summary>
    /// <param name="region">The blocks to hash, required when the parts include blocks.</param>
    /// <param name="options">What to hash and how much to keep.</param>
    /// <exception cref="ArgumentException">The region is missing, or larger than the options allow, or the sampling is not positive.</exception>
    public StateHashRecording Record(Cuboidi? region = null, StateHashOptions? options = null)
    {
        options ??= new StateHashOptions();
        if (options.Every < 1) throw new ArgumentOutOfRangeException(nameof(options), options.Every, "The sampling must be at least one tick.");
        if (options.MaxTicks < 1) throw new ArgumentOutOfRangeException(nameof(options), options.MaxTicks, "The recording must keep at least one sample.");
        if (options.Parts.HasFlag(StateHashParts.Blocks))
        {
            if (region is null) throw new ArgumentException("Hashing blocks needs a region.", nameof(region));
            long volume = (long)(region.X2 - region.X1 + 1) * (region.Y2 - region.Y1 + 1) * (region.Z2 - region.Z1 + 1);
            if (volume > options.MaxBlocks) throw new ArgumentException($"The region holds {volume} blocks, more than the {options.MaxBlocks} allowed.", nameof(region));
        }

        return _host.RunOnGameThread(() => new StateHashRecording(_host, region, options));
    }
}

/// <summary>
/// A running state hash recording. Dispose it to stop; <see cref="Save"/> writes what it has.
/// </summary>
public sealed class StateHashRecording : IDisposable
{
    private readonly EmbeddedServerHost _host;
    private readonly Cuboidi? _region;
    private readonly StateHashOptions _options;
    private readonly List<StateHashTick> _ticks = [];
    private long _seen;
    private bool _disposed;

    internal StateHashRecording(EmbeddedServerHost host, Cuboidi? region, StateHashOptions options)
    {
        _host = host;
        _region = region;
        _options = options;
        StartTick = host.TickCount;
        host.TickProcessed += OnTickProcessed;
    }

    /// <summary>The server's tick count when recording started.</summary>
    public long StartTick { get; }

    /// <summary>How many samples have been taken.</summary>
    public int Count => _ticks.Count;

    /// <summary>Whether recording stopped at <see cref="StateHashOptions.MaxTicks"/> before the test ended.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The samples, in tick order.</summary>
    public IReadOnlyList<StateHashTick> Ticks => _ticks;

    /// <summary>Writes the recording as JSON, creating its folder.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, ToJson());
    }

    /// <summary>The recording as JSON, in the format <see cref="StateHashFile.Parse"/> reads.</summary>
    public string ToJson()
    {
        JsonObject root = new()
        {
            ["schema"] = StateHashFile.Schema,
            ["kind"] = StateHashFile.Kind,
            ["startTick"] = StartTick,
            ["every"] = _options.Every,
            ["tickMs"] = _host.TickMsForHashes,
            ["region"] = _region is null ? null : new JsonArray(_region.X1, _region.Y1, _region.Z1, _region.X2, _region.Y2, _region.Z2),
            ["parts"] = _options.Parts.ToString(),
            ["truncated"] = Truncated,
        };

        JsonArray ticks = [];
        foreach (StateHashTick tick in _ticks)
        {
            ticks.Add(new JsonObject
            {
                ["tick"] = tick.Tick,
                ["blocks"] = tick.Blocks.ToString("x16", CultureInfo.InvariantCulture),
                ["entities"] = tick.Entities.ToString("x16", CultureInfo.InvariantCulture),
                ["players"] = tick.Players.ToString("x16", CultureInfo.InvariantCulture),
            });
        }

        root["ticks"] = ticks;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Stops recording. The samples stay available.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.TickProcessed -= OnTickProcessed;
    }

    // Runs on the game thread, right after the tick's processing.
    private void OnTickProcessed()
    {
        if (_disposed) return;
        _seen++;
        if (_seen % _options.Every != 0) return;
        if (_ticks.Count >= _options.MaxTicks)
        {
            Truncated = true;
            Dispose();
            return;
        }

        _ticks.Add(StateHashing.Sample(_host, _region, _options.Parts, _host.TickCount));
    }
}
