using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.World;

/// <summary>A random generator Pharos seeded.</summary>
/// <param name="Member">The field: its declaring type's full name and its name.</param>
/// <param name="How">How it was seeded: <c>in place</c>, <c>per thread</c>, <c>replaced</c>, or on the game thread.</param>
/// <param name="Count">How many objects' fields of this member were seeded: 1 for a static.</param>
/// <param name="Note">What may still escape the seed, or null.</param>
public sealed record SeededRandom(string Member, string How, int Count, string? Note)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{Member} ({How}{(Count > 1 ? $", {Count} objects" : "")}){(Note != null ? ": " + Note : "")}";
}

/// <summary>A random generator Pharos could not seed.</summary>
/// <param name="Member">The field, or the method that creates the generator: its declaring type's full name and its name.</param>
/// <param name="Reason">Why Pharos could not seed it.</param>
/// <param name="Assembly">The assembly the member belongs to, to keep to a mod's own: <c>Unseeded.Where(u => u.Assembly == typeof(MyMod).Assembly)</c>.</param>
public sealed record UnseededRandom(string Member, string Reason, Assembly Assembly)
{
    /// <inheritdoc />
    public override string ToString() => $"{Member}: {Reason}";
}

/// <summary>
/// The random generators of one side, server or client, and the process-wide static ones:
/// <c>Host.Random</c> and <c>Client.Random</c>. See <c>docs/writing-server-scenarios.md</c>, "Seeded randomness".
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Seed"/> seeds, each with a stream of its own derived from the seed and the
/// generator's name: the world's per-thread generator (<c>World.Rand</c>); the static generators
/// of the game, of the side's mods and of the test assembly; the generators held by the side's
/// systems, mod systems, blocks, items and their behaviours, loaded entities and block entities
/// and their behaviours; and, while the seed is active, those of every entity and block entity
/// the side makes after it.
/// </para>
/// <para>
/// The draws become replayable, not the whole simulation: the server's tick length follows real
/// time, so how many draws a tick makes can change, and work on the game's other threads draws
/// its own reproducible values at times that are not. <see cref="Unseeded"/> lists what Pharos
/// cannot reach.
/// </para>
/// </remarks>
public sealed class GameRandom
{
    private readonly RandomSide _side;
    private readonly object _lock = new();
    private SideSeeding? _seeding;

    internal GameRandom(RandomSide side)
    {
        _side = side;
    }

    /// <summary>The seed in effect, or null while the game's own generators run.</summary>
    public int? CurrentSeed
    {
        get
        {
            lock (_lock) return _seeding?.SeedValue;
        }
    }

    /// <summary>Where <see cref="CurrentSeed"/> came from, as the test's output says it, or null.</summary>
    public string? CurrentSource { get; private set; }

    /// <summary>
    /// Seeds this side's generators and the static ones with <paramref name="seed"/>, undoing an
    /// earlier seeding first. The statics of the calling assembly are seeded too, so a test under
    /// a plain <c>[Fact]</c> gets its own. In a client-server scenario this seeds this side only,
    /// with the statics: the scenario's <c>SeedRandom</c> seeds both sides.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Seed(int seed) => Seed(seed, Assembly.GetCallingAssembly());

    /// <summary>
    /// Seeds as <see cref="Seed(int)"/> does, and the static generators of
    /// <paramref name="assemblies"/> too, from now on.
    /// </summary>
    public void Seed(int seed, params Assembly[] assemblies)
    {
        lock (_lock)
        {
            foreach (Assembly assembly in assemblies)
            {
                if (assembly != typeof(GameRandom).Assembly && !_extra.Contains(assembly)) _extra.Add(assembly);
            }
        }

        string source = $"{_side.Label}.Random.Seed({seed})";
        StaticRandomSeeding.SeedFor(this, seed, Assemblies());
        SeedSide(seed, source);
        RandomSeeding.Notes?.AddLine($"{source} re-seeded the random generators.");
    }

    private readonly List<Assembly> _extra = [];

    /// <summary>
    /// Puts the game's own generators back: the static ones too when this side seeded them last.
    /// Does nothing when nothing is seeded. The scenario base classes call it when the test ends.
    /// </summary>
    /// <exception cref="AggregateException">Some generators could not be put back.</exception>
    public void Restore()
    {
        StaticRandomSeeding.Release(this);
        RestoreSide();
    }

    /// <summary>What the current seeding reached, statics included; empty when nothing is seeded.</summary>
    public IReadOnlyList<SeededRandom> Seeded
    {
        get
        {
            SeedingLog? side;
            lock (_lock) side = _seeding?.Log;
            IEnumerable<SeededRandom> statics = StaticRandomSeeding.Log?.SeededList() ?? [];
            return [.. statics.Concat(side?.SeededList() ?? []).OrderBy(s => s.Member, StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// What Pharos cannot seed in the game, the side's mods and the test assembly: instance fields
    /// of objects it does not reach, generators made inside methods with no seed or one taken from
    /// a clock, <c>[ThreadStatic]</c> fields, and anything the current seeding failed on. The code
    /// of each assembly is read once per process, when the first host boots or a seeding first
    /// meets it.
    /// </summary>
    public IReadOnlyList<UnseededRandom> Unseeded
    {
        get
        {
            List<UnseededRandom> unseeded = [];
            foreach (Assembly assembly in Assemblies())
            {
                RandomCode.StaticFieldSet fields = RandomCode.StaticFields(assembly);
                unseeded.AddRange(fields.Unseedable);
                unseeded.AddRange(fields.PerThread.Select(f => new UnseededRandom(RandomCode.Member(f), _side.HasOwnThread
                    ? "a [ThreadStatic] field: seeded on the server's and the client's game threads only"
                    : "a [ThreadStatic] field, not seeded: a fixture-mode client runs on the test's own thread", assembly)));
                unseeded.AddRange(RandomCode.FactsOf(assembly).Unseeded);
            }

            unseeded.AddRange(RandomSeeding.PatchFailures);
            unseeded.AddRange(StaticRandomSeeding.Log?.FailedList() ?? []);
            lock (_lock) unseeded.AddRange(_seeding?.Log.FailedList() ?? []);
            return [.. unseeded.DistinctBy(u => (u.Member, u.Reason)).OrderBy(u => u.Member, StringComparer.Ordinal)];
        }
    }

    /// <summary>The assemblies this side seeds the statics of: the game's, its mods' and the test assembly.</summary>
    internal IReadOnlyList<Assembly> Assemblies()
    {
        Assembly[] extra;
        lock (_lock) extra = [.. _extra];
        return [.. RandomSeeding.CoreAssemblies().Concat(_side.ModAssemblies().Where(RandomSeeding.Scanned)).Concat(extra).Distinct()];
    }

    /// <summary>
    /// When the host boots, before the world ticks: patches the game's and the mods' entity and
    /// block entity initializers, and reads their code, once per process. Never throws.
    /// </summary>
    internal void HookAtBoot()
    {
        try
        {
            RandomSeeding.Hook(Assemblies());
        }
        catch (Exception)
        {
            // A seeding hooks again, and reports what it could not patch.
        }
    }

    /// <summary>Seeds this side's own generators, not the statics.</summary>
    internal void SeedSide(int seed, string source)
    {
        RestoreSide();
        IReadOnlyList<Assembly> assemblies = Assemblies();
        RandomSeeding.Hook(assemblies);

        SideSeeding seeding = new(_side.Key, seed, new SeedingLog());
        try
        {
            _side.Run(() =>
            {
                _side.SeedObjects(seeding);
                if (!_side.HasOwnThread) return;
                foreach (FieldInfo field in assemblies.SelectMany(a => RandomCode.StaticFields(a).PerThread))
                {
                    RandomFields.Seed(field, null, RandomStreams.PerThread("static:" + RandomCode.Member(field)), seed, seeding.Log, fillNull: true);
                }
            });
        }
        catch
        {
            Undo(seeding);
            throw;
        }

        lock (_lock)
        {
            _seeding = seeding;
            CurrentSource = source;
        }

        RandomSeeding.Activate(_side.AppSide, seeding);
    }

    /// <summary>For the host's disposal: puts back what this side seeded, and never throws.</summary>
    internal void ReleaseOnDispose()
    {
        try
        {
            Restore();
        }
        catch (Exception)
        {
            // The host goes away regardless.
        }
    }

    private void RestoreSide()
    {
        SideSeeding? seeding;
        lock (_lock)
        {
            seeding = _seeding;
            _seeding = null;
            CurrentSource = null;
        }

        if (seeding == null) return;
        RandomSeeding.Activate(_side.AppSide, null);
        Undo(seeding);
    }

    // What runs anywhere first, here; then what belongs to the game thread, there.
    private void Undo(SideSeeding seeding)
    {
        Exception? failure = null;
        Action[] onThread;
        try
        {
            onThread = seeding.Log.UndoHere();
        }
        catch (Exception ex)
        {
            failure = ex;
            onThread = seeding.Log.TakeUndo().OnThread;
        }

        if (onThread.Length > 0)
        {
            try
            {
                _side.Run(() =>
                {
                    foreach (Action undo in onThread) undo();
                });
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null) throw failure;
    }
}

/// <summary>One side's objects and thread, as <see cref="GameRandom"/> seeds them.</summary>
internal abstract class RandomSide
{
    /// <summary>How the test names the side: <c>Host</c> or <c>Client</c>.</summary>
    public abstract string Label { get; }

    /// <summary>How stream keys name the side: <c>server</c> or <c>client</c>.</summary>
    public abstract string Key { get; }

    public abstract EnumAppSide AppSide { get; }

    /// <summary>Whether the side runs on a thread of its own, where <c>[ThreadStatic]</c> fields can be seeded.</summary>
    public abstract bool HasOwnThread { get; }

    public abstract void Run(Action action);

    public abstract IEnumerable<Assembly> ModAssemblies();

    /// <summary>Seeds the side's long-lived objects, entities and block entities; runs on its thread.</summary>
    public abstract void SeedObjects(SideSeeding seeding);

    protected static IEnumerable<Assembly> SystemsOf(ICoreAPI? api)
    {
        try
        {
            return api?.ModLoader?.Systems?.Select(s => s.GetType().Assembly).Distinct().ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Seeds <paramref name="main"/>, the systems, the mod systems, the blocks and items with
    /// their behaviours, then the entities by id and the block entities by position, so their
    /// keys do not depend on the order the collections enumerate in.
    /// </summary>
    protected static void SeedAll(SideSeeding seeding, object main, IEnumerable<object?> systems, ICoreAPI? api, IWorldAccessor? world,
        IEnumerable<Entity> entities, IEnumerable<BlockEntity> blockEntities)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        void Seed(object? target, string key)
        {
            if (target != null && visited.Add(target)) RandomFields.SeedObject(target, key, seeding.SeedValue, seeding.Log);
        }

        string side = seeding.Side;
        Seed(main, $"{side}:main");
        foreach (object? system in systems) Seed(system, $"{side}:system:{system?.GetType().FullName}");

        IEnumerable<ModSystem> modSystems;
        try
        {
            modSystems = api?.ModLoader?.Systems?.ToList() ?? [];
        }
        catch (Exception)
        {
            modSystems = [];
        }

        foreach (ModSystem modSystem in modSystems) Seed(modSystem, $"{side}:modsystem:{modSystem.GetType().FullName}");

        foreach ((string kind, IEnumerable<CollectibleObject?>? collectibles) in new (string, IEnumerable<CollectibleObject?>?)[] { ("block", world?.Blocks), ("item", world?.Items) })
        {
            foreach (CollectibleObject? collectible in collectibles ?? [])
            {
                if (collectible?.Code == null) continue;
                string key = $"{side}:{kind}:{collectible.Code}";
                Seed(collectible, key);
                CollectibleBehavior[] behaviors = collectible.CollectibleBehaviors ?? [];
                for (int i = 0; i < behaviors.Length; i++) Seed(behaviors[i], $"{key}/c{i}");
                if (collectible is Block { BlockBehaviors: { } blockBehaviors })
                {
                    for (int i = 0; i < blockBehaviors.Length; i++) Seed(blockBehaviors[i], $"{key}/b{i}");
                }
            }
        }

        foreach (Entity entity in entities.Where(e => e != null).OrderBy(e => e.EntityId)) seeding.SeedEntity(entity);
        foreach (BlockEntity blockEntity in blockEntities.Where(b => b?.Pos != null).OrderBy(b => b.Pos.X).ThenBy(b => b.Pos.Y).ThenBy(b => b.Pos.Z).ThenBy(b => b.Pos.dimension))
        {
            seeding.SeedBlockEntity(blockEntity);
        }
    }
}

/// <summary>An embedded server's side.</summary>
internal sealed class ServerRandomSide(EmbeddedServerHost host) : RandomSide
{
    public override string Label => "Host";
    public override string Key => "server";
    public override EnumAppSide AppSide => EnumAppSide.Server;
    public override bool HasOwnThread => true;

    public override void Run(Action action) => host.RunOnGameThread(action);

    public override IEnumerable<Assembly> ModAssemblies() => SystemsOf(host.Server.Api);

    public override void SeedObjects(SideSeeding seeding)
    {
        ServerMain server = host.Server;
        SeedAll(seeding, server, Systems(server) ?? [], server.Api, server, ((IServerWorldAccessor)server).LoadedEntities.Values.ToList(), BlockEntities(server));
    }

    private static List<BlockEntity> BlockEntities(ServerMain server)
    {
        List<BlockEntity> blockEntities = [];
        ref FastRWLock chunksLock = ref LoadedChunksLock(server);
        chunksLock.AcquireReadLock();
        try
        {
            foreach (ServerChunk chunk in LoadedChunks(server).Values)
            {
                if (chunk?.BlockEntities is { } inChunk) blockEntities.AddRange(inChunk.Values.Where(b => b != null));
            }
        }
        finally
        {
            chunksLock.ReleaseReadLock();
        }

        return blockEntities;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Systems")]
    private static extern ref ServerSystem[]? Systems(ServerMain server);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunks")]
    private static extern ref Dictionary<long, ServerChunk> LoadedChunks(ServerMain server);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunksLock")]
    private static extern ref FastRWLock LoadedChunksLock(ServerMain server);
}

/// <summary>A headless client's side.</summary>
internal sealed class ClientRandomSide(HeadlessClient client) : RandomSide
{
    public override string Label => "Client";
    public override string Key => "client";
    public override EnumAppSide AppSide => EnumAppSide.Client;
    public override bool HasOwnThread => client.IsEngineMode;

    public override void Run(Action action) => client.RunOnClientThread(action);

    public override IEnumerable<Assembly> ModAssemblies() => SystemsOf(client.Client.api);

    public override void SeedObjects(SideSeeding seeding)
    {
        ClientMain game = client.Client;
        List<BlockEntity> blockEntities = [];
        if (game.WorldMap is { } map && Chunks(map) is { } chunks)
        {
            foreach (ClientChunk chunk in chunks.Values)
            {
                if (chunk?.BlockEntities is { } inChunk) blockEntities.AddRange(inChunk.Values.Where(b => b != null));
            }
        }

        IWorldAccessor? world = game.BlocksReceivedAndLoaded || !client.IsEngineMode ? game : null;
        SeedAll(seeding, game, game.clientSystems ?? [], game.api, world, game.LoadedEntities?.Values.ToList() ?? [], blockEntities);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "chunks")]
    private static extern ref Dictionary<long, ClientChunk>? Chunks(ClientWorldMap map);
}
