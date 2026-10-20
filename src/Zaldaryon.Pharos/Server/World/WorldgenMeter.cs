using System.Collections.Concurrent;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The measurement behind <see cref="ServerWorldgenDriver"/>: it wraps the registered generators,
/// records each call on the thread that ran it, and restores the game's lists when it closes.
/// </summary>
internal sealed class WorldgenMeter
{
    private static readonly object s_openLock = new();
    private static bool s_open;

    private const EnumWorldGenPass FirstPass = EnumWorldGenPass.Terrain;

    private readonly IWorldGenHandler _handlers;
    private readonly ModOwners _owners;
    private readonly List<Action> _restores = [];
    private readonly ConcurrentQueue<Call> _calls = new();
    private readonly int _requestedColumns;
    private readonly long _startTicks;

    private long _loadedTicks;
    private int _completed;

    private WorldgenMeter(IWorldGenHandler handlers, ModOwners owners, int requestedColumns, long startTicks)
    {
        _handlers = handlers;
        _owners = owners;
        _requestedColumns = requestedColumns;
        _startTicks = startTicks;
    }

    /// <summary>Whether the game called back that the requested area is loaded.</summary>
    internal bool Completed => Volatile.Read(ref _completed) == 1;

    /// <summary>Opens a measurement and asks the server to generate the area; call on the game thread.</summary>
    internal static WorldgenMeter Open(EmbeddedServerHost host, int chunkX1, int chunkZ1, int chunkX2, int chunkZ2)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (chunkX1 > chunkX2) (chunkX1, chunkX2) = (chunkX2, chunkX1);
        if (chunkZ1 > chunkZ2) (chunkZ1, chunkZ2) = (chunkZ2, chunkZ1);

        ICoreServerAPI api = host.Api;
        ServerMain server = host.Server;
        CheckArea(server, api, chunkX1, chunkZ1, chunkX2, chunkZ2);

        lock (s_openLock)
        {
            if (s_open) throw new InvalidOperationException("A worldgen measurement is already open.");

            IWorldGenHandler handlers = api.Event.GetRegisteredWorldGenHandlers(api.WorldManager.SaveGame.WorldType)
                ?? throw new InvalidOperationException(
                    $"The world type '{api.WorldManager.SaveGame.WorldType}' has no worldgen handlers.");
            ModOwners owners = ModOwners.Of(api);
            int requested = (chunkX2 - chunkX1 + 1) * (chunkZ2 - chunkZ1 + 1);
            WorldgenMeter meter = new(handlers, owners, requested, Stopwatch.GetTimestamp());

            s_open = true;
            try
            {
                meter.WrapHandlers();
                server.LoadChunkColumnFast(chunkX1, chunkZ1, chunkX2, chunkZ2,
                    new ChunkLoadOptions { OnLoaded = meter.OnLoaded });
            }
            catch
            {
                meter.Abort();
                throw;
            }

            return meter;
        }
    }

    /// <summary>Restores the handlers and builds the profile; call on the game thread.</summary>
    internal WorldgenProfile Close()
    {
        Restore();
        long endTicks = _completed == 1 ? _loadedTicks : Stopwatch.GetTimestamp();
        return Build(endTicks);
    }

    /// <summary>Restores the handlers without building a profile, for a measurement the test abandoned.</summary>
    internal void Abort()
    {
        Restore();
        while (_calls.TryDequeue(out _))
        {
        }
    }

    private void Restore()
    {
        foreach (Action restore in _restores) restore();
        _restores.Clear();
        lock (s_openLock) s_open = false;
    }

    private void OnLoaded()
    {
        _loadedTicks = Stopwatch.GetTimestamp();
        Volatile.Write(ref _completed, 1);
    }

    // ---- wrapping ---------------------------------------------------------------------------

    private void WrapHandlers()
    {
        for (int pass = (int)FirstPass; pass < _handlers.OnChunkColumnGen.Length; pass++)
        {
            List<ChunkColumnGenerationDelegate>? list = _handlers.OnChunkColumnGen[pass];
            if (list == null) continue;

            string stage = StageName((EnumWorldGenPass)pass);
            for (int i = 0; i < list.Count; i++)
            {
                ChunkColumnGenerationDelegate original = list[i];
                HandlerInfo info = Info(stage, original);
                ChunkColumnGenerationDelegate wrapped = request =>
                    Record(info, request.ChunkX, request.ChunkZ, () => original(request));
                Replace(list, i, original, wrapped);
            }
        }

        List<MapRegionGeneratorDelegate> regions = _handlers.OnMapRegionGen;
        for (int i = 0; i < regions.Count; i++)
        {
            MapRegionGeneratorDelegate original = regions[i];
            HandlerInfo info = Info("region", original);
            MapRegionGeneratorDelegate wrapped = (region, regionX, regionZ, parameters) =>
                Record(info, regionX, regionZ, () => original(region, regionX, regionZ, parameters));
            Replace(regions, i, original, wrapped);
        }

        List<MapChunkGeneratorDelegate> mapChunks = _handlers.OnMapChunkGen;
        for (int i = 0; i < mapChunks.Count; i++)
        {
            MapChunkGeneratorDelegate original = mapChunks[i];
            HandlerInfo info = Info("mapchunk", original);
            MapChunkGeneratorDelegate wrapped = (mapChunk, chunkX, chunkZ) =>
                Record(info, chunkX, chunkZ, () => original(mapChunk, chunkX, chunkZ));
            Replace(mapChunks, i, original, wrapped);
        }
    }

    private void Replace<T>(List<T> list, int index, T original, T wrapped) where T : Delegate
    {
        list[index] = wrapped;
        _restores.Add(() =>
        {
            if (index < list.Count && ReferenceEquals(list[index], wrapped)) list[index] = original;
        });
    }

    private HandlerInfo Info(string stage, Delegate handler) =>
        new(stage, ModOwners.Describe(handler), _owners.OfDelegate(handler));

    private void Record(HandlerInfo info, int x, int z, Action body)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            body();
        }
        finally
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            _calls.Enqueue(new Call(info, x, z, ms));
        }
    }

    // ---- results ----------------------------------------------------------------------------

    private WorldgenProfile Build(long endTicks)
    {
        List<Call> calls = [];
        while (_calls.TryDequeue(out Call call)) calls.Add(call);

        List<WorldgenHandlerProfile> handlers = [];
        foreach (IGrouping<HandlerInfo, Call> group in calls.GroupBy(call => call.Info))
        {
            double[] perItem = [.. group
                .GroupBy(call => (call.X, call.Z))
                .Select(column => column.Sum(call => call.Ms))];
            TimingStats stats = TimingStats.From(perItem.Select(ms => TimeSpan.FromMilliseconds(ms)));
            handlers.Add(new WorldgenHandlerProfile(
                group.Key.Stage, group.Key.Handler, group.Key.Mod, group.LongCount(), perItem, stats));
        }

        handlers.Sort((left, right) =>
        {
            int byTime = right.TotalMs.CompareTo(left.TotalMs);
            if (byTime != 0) return byTime;
            int byStage = string.CompareOrdinal(left.Stage, right.Stage);
            return byStage != 0 ? byStage : string.CompareOrdinal(left.Handler, right.Handler);
        });

        List<Call> passCalls = [.. calls.Where(call => call.Info.IsPass)];
        int columns = passCalls.Select(call => (call.X, call.Z)).Distinct().Count();

        List<WorldgenModProfile> mods = [];
        foreach (IGrouping<string, Call> group in passCalls.GroupBy(call => call.Info.Owner))
        {
            double[] perColumn = [.. group
                .GroupBy(call => (call.X, call.Z))
                .Select(column => column.Sum(call => call.Ms))];
            TimingStats stats = TimingStats.From(perColumn.Select(ms => TimeSpan.FromMilliseconds(ms)));
            IReadOnlyList<WorldgenHandlerProfile> modHandlers =
                [.. handlers.Where(handler => handler.IsPass && handler.Owner == group.Key)];
            mods.Add(new WorldgenModProfile(group.Key, group.Key == "game" ? null : group.Key,
                group.LongCount(), perColumn.Length, perColumn, modHandlers, stats));
        }

        mods.Sort((left, right) =>
        {
            int byTime = right.TotalMs.CompareTo(left.TotalMs);
            return byTime != 0 ? byTime : string.CompareOrdinal(left.Owner, right.Owner);
        });

        double wallMs = (endTicks - _startTicks) * 1000.0 / Stopwatch.Frequency;
        return new WorldgenProfile(_requestedColumns, columns, wallMs, handlers, mods);
    }

    // ---- argument checks --------------------------------------------------------------------

    private static void CheckArea(ServerMain server, ICoreServerAPI api, int chunkX1, int chunkZ1, int chunkX2, int chunkZ2)
    {
        int sizeX = api.WorldManager.MapSizeX / GlobalConstants.ChunkSize;
        int sizeZ = api.WorldManager.MapSizeZ / GlobalConstants.ChunkSize;
        if (chunkX1 < 0 || chunkZ1 < 0 || chunkX2 >= sizeX || chunkZ2 >= sizeZ)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkX1),
                $"The area ({chunkX1},{chunkZ1}) to ({chunkX2},{chunkZ2}) leaves the {sizeX} by {sizeZ} chunk world.");
        }

        long columns = (long)(chunkX2 - chunkX1 + 1) * (chunkZ2 - chunkZ1 + 1);
        if (columns > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkX1),
                $"The area covers {columns} chunk columns; measure at most 64 at a time.");
        }

        for (int x = chunkX1; x <= chunkX2; x++)
        {
            for (int z = chunkZ1; z <= chunkZ2; z++)
            {
                if (server.IsChunkColumnFullyLoaded(x, z))
                {
                    throw new InvalidOperationException(
                        $"Chunk column ({x},{z}) is already loaded; measure an area the world has not generated.");
                }
            }
        }
    }

    private static string StageName(EnumWorldGenPass pass) => pass switch
    {
        EnumWorldGenPass.Terrain => "terrain",
        EnumWorldGenPass.TerrainFeatures => "terrain_features",
        EnumWorldGenPass.Vegetation => "vegetation",
        EnumWorldGenPass.NeighbourSunLightFlood => "sunlight_flood",
        EnumWorldGenPass.Done => "done",
        _ => "none",
    };

    private sealed record HandlerInfo(string Stage, string Handler, string? Mod)
    {
        public string Owner => Mod ?? "game";

        public bool IsPass => Stage is not ("region" or "mapchunk");
    }

    private sealed record Call(HandlerInfo Info, int X, int Z, double Ms);
}
