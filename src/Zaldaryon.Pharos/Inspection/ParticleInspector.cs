using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Particles the client spawned, as they were when it spawned them.</summary>
/// <param name="ProviderType">The type of the particle properties, such as <c>SimpleParticleProperties</c>.</param>
/// <param name="Model">Quads or cubes.</param>
/// <param name="Spawned">How many particles the pool took; fewer than asked when the particle setting or the pool limits them.</param>
/// <param name="Position">
/// Where they start: the minimum position for <c>SimpleParticleProperties</c>; for other
/// properties, one they give when asked, which can draw from their random numbers.
/// </param>
/// <param name="Quantity">The quantity asked for: the minimum for <c>SimpleParticleProperties</c>.</param>
/// <param name="Color">The color of <c>SimpleParticleProperties</c>, as RGBA.</param>
/// <param name="LifeLength">How long they live, in seconds, for <c>SimpleParticleProperties</c>.</param>
/// <param name="Size">Their minimum size, for <c>SimpleParticleProperties</c>.</param>
/// <param name="OffThread">Spawned on the particle thread: entities' and queued async particles.</param>
/// <param name="Async">The properties asked to be spawned off the main thread.</param>
public sealed record SpawnedParticles(
    string ProviderType,
    EnumParticleModel Model,
    int Spawned,
    Vec3d? Position,
    float? Quantity,
    int? Color,
    float? LifeLength,
    float? Size,
    bool OffThread,
    bool Async);

/// <summary>The particles a client spawns while it is open. Dispose it to stop.</summary>
public sealed class ParticleCapture : IDisposable
{
    private readonly object _lock = new();
    private readonly List<SpawnedParticles> _spawned = [];
    private readonly int _limit;

    internal ParticleCapture(ClientMain game, bool includeOffThread, int limit)
    {
        _game = new WeakReference<ClientMain>(game);
        IncludeOffThread = includeOffThread;
        _limit = limit;
    }

    // Weak, so a capture nobody disposed does not keep a finished client alive.
    private readonly WeakReference<ClientMain> _game;

    internal bool IsOf(ClientMain game) => _game.TryGetTarget(out ClientMain? own) && ReferenceEquals(own, game);

    internal bool IncludeOffThread { get; }

    /// <summary>What was spawned so far, oldest first.</summary>
    public IReadOnlyList<SpawnedParticles> Spawned
    {
        get
        {
            lock (_lock) return [.. _spawned];
        }
    }

    /// <summary>Whether more was spawned than the capture keeps.</summary>
    public bool Truncated
    {
        get
        {
            lock (_lock) return _truncated;
        }
    }

    private bool _truncated;

    internal void Add(SpawnedParticles spawned)
    {
        lock (_lock)
        {
            if (_spawned.Count >= _limit)
            {
                _truncated = true;
                return;
            }

            _spawned.Add(spawned);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => ParticleInspector.Stop(this);
}

/// <summary>
/// How many particles a client has alive, and what it spawns: a mod's own, server-sent ones, block
/// breaking, entities' and the weather's.
/// </summary>
public sealed class ParticleInspector
{
    private static readonly object s_lock = new();
    private static ParticleCapture[] s_captures = [];

    private readonly HeadlessClient _client;

    internal ParticleInspector(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>
    /// How many particles of <paramref name="model"/> are alive, on the main thread and the
    /// particle thread together.
    /// </summary>
    public int Alive(EnumParticleModel model) => _client.RunOnClientThread(() =>
    {
        SystemRenderParticles system = System();
        int alive = 0;
        if (Internals.MainThreadPools(system) is { } main && (int)model < main.Length)
        {
            alive += AliveIn(main[(int)model]);
        }

        if (Internals.OffThreadPools(system) is { } off && (int)model < off.Length) alive += AliveIn(off[(int)model]);
        return alive;
    });

    // A pool's own count is updated only while particles render; its particle pool's is current.
    private static int AliveIn(IParticlePool pool) => pool is ParticlePoolQuads quads ? quads.ParticlesPool.AliveCount : pool.QuantityAlive;

    /// <summary>How many particles are alive, of every model.</summary>
    public int Alive() => Enum.GetValues<EnumParticleModel>().Sum(Alive);

    /// <summary>
    /// Records what the client spawns until the capture is disposed. Ambient particles spawn all
    /// the time: filter <see cref="ParticleCapture.Spawned"/> by type or position.
    /// </summary>
    /// <param name="includeOffThread">Whether to record particles spawned on the particle thread too.</param>
    /// <param name="limit">How many spawns to keep; later ones are dropped and <see cref="ParticleCapture.Truncated"/> is set.</param>
    public ParticleCapture Capture(bool includeOffThread = true, int limit = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ParticleCapture capture = new(ClientGame.Require(_client), includeOffThread, limit);
        lock (s_lock) s_captures = [.. s_captures, capture];
        return capture;
    }

    internal static void Stop(ParticleCapture capture)
    {
        lock (s_lock) s_captures = s_captures.Where(c => !ReferenceEquals(c, capture)).ToArray();
    }

    private SystemRenderParticles System() =>
        Internals.ParticleSystem(Internals.Manager(ClientGame.Require(_client)))
        ?? throw new InvalidOperationException("The client has no particle system yet: it has not started a game.");

    // Records what a pool took. Installed by ClientInspectionPatches; runs on the thread that spawned.
    internal static void AfterSpawn(ParticlePoolQuads __instance, IParticlePropertiesProvider particleProperties, int __result)
    {
        ParticleCapture[] captures = s_captures;
        if (captures.Length == 0) return;

        // Inspection never breaks the game: whatever goes wrong here stays here.
        try
        {
            ClientMain game = Internals.Game(__instance);
            bool offThread = Internals.OffThread(__instance);
            SpawnedParticles? spawned = null;
            foreach (ParticleCapture capture in captures)
            {
                if (!capture.IsOf(game) || (offThread && !capture.IncludeOffThread)) continue;
                spawned ??= Snapshot(particleProperties, __result, offThread);
                capture.Add(spawned);
            }
        }
        catch (Exception)
        {
        }
    }

    // Properties are reused and changed by the game: what matters is copied now.
    private static SpawnedParticles Snapshot(IParticlePropertiesProvider properties, int spawned, bool offThread)
    {
        if (properties is SimpleParticleProperties simple)
        {
            return new SpawnedParticles(simple.GetType().Name, simple.ParticleModel, spawned, simple.MinPos?.Clone(), simple.MinQuantity,
                simple.Color, simple.LifeLength, simple.MinSize, offThread, simple.Async);
        }

        // Read from the properties themselves, which may draw from their random numbers.
        Vec3d? position = null;
        float? quantity = null;
        try
        {
            position = properties.Pos?.Clone();
            quantity = properties.Quantity;
        }
        catch (Exception)
        {
            // Some properties need state they no longer have.
        }

        return new SpawnedParticles(properties.GetType().Name, properties.ParticleModel, spawned, position, quantity, null, null, null, offThread, properties.Async);
    }

    private static class Internals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "particleManager")]
        public static extern ref ParticleManager Manager(ClientMain game);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "particleSystem")]
        public static extern ref SystemRenderParticles ParticleSystem(ParticleManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "mainthreadpools")]
        public static extern ref IParticlePool[] MainThreadPools(SystemRenderParticles system);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "offthreadpools")]
        public static extern ref IParticlePool[] OffThreadPools(SystemRenderParticles system);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "game")]
        public static extern ref ClientMain Game(ParticlePoolQuads pool);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "offthread")]
        public static extern ref bool OffThread(ParticlePoolQuads pool);
    }
}
