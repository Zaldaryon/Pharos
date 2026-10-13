using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.World;

/// <summary>The stream each generator gets from a test's seed.</summary>
internal static class RandomStreams
{
    /// <summary>
    /// The seed of the generator <paramref name="key"/> names, from the test's seed: SplitMix64
    /// over the seed and the key's hash, so two generators never share a stream and one drawing
    /// more never shifts another.
    /// </summary>
    public static int Mix(int seed, string key)
    {
        ulong z = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) ^ SeededAttribute.Fnv64(key);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z ^ (z >> 32));
    }

    /// <summary>The key of a per-thread stream: the generator's, then the thread's name.</summary>
    public static string PerThread(string key) => key + "@" + (Thread.CurrentThread.Name ?? "unnamed");
}

/// <summary>
/// Re-seeds a <see cref="Random"/> where it is, by giving it the state of a new generator with
/// the seed: every holder of the instance sees the seeded stream, and putting the old state back
/// undoes it.
/// </summary>
internal static class RandomReseeder
{
    private static readonly FieldInfo? s_impl = typeof(Random).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConstructorInfo? s_derivedImpl = typeof(Random).GetNestedType("CompatDerivedImpl", BindingFlags.NonPublic)
        ?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Random), typeof(int)]);

    private static readonly string[] s_drawMethods = ["Next", "NextDouble", "Sample", "NextBytes", "NextInt64", "NextSingle"];

    /// <summary>Whether this runtime's <see cref="Random"/> can be re-seeded in place.</summary>
    public static bool Available => s_impl != null && s_derivedImpl != null;

    /// <summary>
    /// Gives <paramref name="random"/> the stream of <paramref name="seed"/> and returns its old
    /// state, or null when this runtime does not allow it.
    /// </summary>
    public static object? Reseed(Random random, int seed)
    {
        if (!Available) return null;
        try
        {
            object? impl = random.GetType() == typeof(Random)
                ? s_impl!.GetValue(new Random(seed))
                : s_derivedImpl!.Invoke([random, seed]);
            if (impl == null) return null;
            object? old = s_impl!.GetValue(random);
            s_impl.SetValue(random, impl);
            return old;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Puts back the state <see cref="Reseed"/> returned.</summary>
    public static void Put(Random random, object state) => s_impl?.SetValue(random, state);

    /// <summary>
    /// For a subclass that draws in methods of its own, which may keep state the seed does not
    /// reach, the note that says so; null otherwise.
    /// </summary>
    public static string? OverrideNote(Type type)
    {
        if (type == typeof(Random) || type == typeof(ThreadSafeRandom)) return null;
        string[] overridden = [.. type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => s_drawMethods.Contains(m.Name) && m.DeclaringType != typeof(Random) && m.GetBaseDefinition().DeclaringType == typeof(Random))
            .Select(m => m.Name)
            .Distinct()];
        return overridden.Length == 0 ? null : $"{type.Name} overrides {string.Join(", ", overridden)}: state of its own may not follow the seed";
    }
}

/// <summary>What one seeding did and how to undo it.</summary>
internal sealed class SeedingLog
{
    private readonly object _lock = new();
    private readonly List<Action> _undo = [];
    private readonly List<Action> _undoOnThread = [];
    private readonly Dictionary<(string Member, string How), (int Count, string? Note)> _seeded = [];
    private readonly List<UnseededRandom> _failed = [];

    /// <summary>Records an undo: <paramref name="onThread"/> when it must run on the side's game thread.</summary>
    public void Undo(Action undo, bool onThread = false)
    {
        lock (_lock) (onThread ? _undoOnThread : _undo).Add(undo);
    }

    public void Seeded(string member, string how, string? note)
    {
        lock (_lock)
        {
            _seeded.TryGetValue((member, how), out (int Count, string? Note) entry);
            _seeded[(member, how)] = (entry.Count + 1, entry.Note ?? note);
        }
    }

    public void Failed(UnseededRandom unseeded)
    {
        lock (_lock)
        {
            if (!_failed.Contains(unseeded)) _failed.Add(unseeded);
        }
    }

    public IReadOnlyList<SeededRandom> SeededList()
    {
        lock (_lock) return [.. _seeded.Select(e => new SeededRandom(e.Key.Member, e.Key.How, e.Value.Count, e.Value.Note)).OrderBy(s => s.Member, StringComparer.Ordinal)];
    }

    public IReadOnlyList<UnseededRandom> FailedList()
    {
        lock (_lock) return [.. _failed];
    }

    /// <summary>Takes the undos, newest first: those that run anywhere, then those for the game thread.</summary>
    public (Action[] Anywhere, Action[] OnThread) TakeUndo()
    {
        lock (_lock)
        {
            Action[] anywhere = [.. Enumerable.Reverse(_undo)];
            Action[] onThread = [.. Enumerable.Reverse(_undoOnThread)];
            _undo.Clear();
            _undoOnThread.Clear();
            return (anywhere, onThread);
        }
    }

    /// <summary>Runs every undo that runs anywhere, here, and returns the ones for the game thread.</summary>
    public Action[] UndoHere()
    {
        (Action[] anywhere, Action[] onThread) = TakeUndo();
        List<Exception> errors = [];
        foreach (Action undo in anywhere)
        {
            try
            {
                undo();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0) throw new AggregateException("Pharos could not put back every random generator.", errors);
        return onThread;
    }
}

/// <summary>Seeds the fields that hold random generators.</summary>
internal static class RandomFields
{
    /// <summary>
    /// Seeds the generator <paramref name="field"/> holds on <paramref name="target"/> (null for a
    /// static) with the stream of <paramref name="key"/>. <paramref name="fillNull"/> sets a null
    /// <c>[ThreadStatic]</c> field to a seeded generator, on the calling thread.
    /// </summary>
    public static void Seed(FieldInfo field, object? target, string key, int seed, SeedingLog log, bool fillNull = false)
    {
        string member = RandomCode.Member(field);
        Assembly assembly = field.DeclaringType?.Assembly ?? typeof(RandomFields).Assembly;
        bool threadStatic = field.IsStatic && field.IsDefined(typeof(ThreadStaticAttribute));
        object? value;
        try
        {
            value = field.GetValue(target);
        }
        catch (Exception ex)
        {
            log.Failed(new UnseededRandom(member, $"could not be read: {ex.GetType().Name}", assembly));
            return;
        }

        if (field.FieldType == typeof(ThreadLocal<Random>))
        {
            if (field.IsStatic && field.IsInitOnly)
            {
                log.Failed(new UnseededRandom(member, "a static readonly ThreadLocal<Random>, which cannot be replaced", assembly));
                return;
            }

            // Never disposed: the game's other threads may still be inside Value when the original
            // is put back. The finalizer collects it.
            ThreadLocal<Random> perThread = new(() => new Random(RandomStreams.Mix(seed, RandomStreams.PerThread(key))));
            field.SetValue(target, perThread);
            log.Undo(() => field.SetValue(target, value));
            log.Seeded(member, "per thread", null);
            return;
        }

        int streamSeed = RandomStreams.Mix(seed, key);
        if (value is Random random)
        {
            if (RandomReseeder.Reseed(random, streamSeed) is { } old)
            {
                log.Undo(() => RandomReseeder.Put(random, old));
                log.Seeded(member, threadStatic ? "in place, on the game thread" : "in place", RandomReseeder.OverrideNote(random.GetType()));
                return;
            }

            // The runtime does not allow it: a new generator of the same type, if the field can take one.
            if (field.IsStatic && field.IsInitOnly)
            {
                log.Failed(new UnseededRandom(member, "a static readonly field, and this runtime's Random cannot be re-seeded in place", assembly));
                return;
            }

            if (Create(random.GetType(), streamSeed) is not { } replacement)
            {
                log.Failed(new UnseededRandom(member, $"{random.GetType().Name} has no constructor that takes a seed", assembly));
                return;
            }

            field.SetValue(target, replacement);
            log.Undo(() => field.SetValue(target, value), onThread: threadStatic);
            log.Seeded(member, "replaced", RandomReseeder.OverrideNote(random.GetType()));
            return;
        }

        if (value == null && threadStatic && fillNull)
        {
            if (Create(field.FieldType, streamSeed) is not { } created)
            {
                log.Failed(new UnseededRandom(member, $"{field.FieldType.Name} has no constructor that takes a seed", assembly));
                return;
            }

            field.SetValue(null, created);
            log.Undo(() => field.SetValue(null, null), onThread: true);
            log.Seeded(member, "set, on the game thread", null);
        }
    }

    /// <summary>Seeds every random instance field of <paramref name="target"/>, each under <paramref name="key"/> and its name.</summary>
    public static void SeedObject(object target, string key, int seed, SeedingLog log)
    {
        foreach (FieldInfo field in RandomCode.InstanceFields(target.GetType()))
        {
            Seed(field, target, $"{key}:{RandomCode.Member(field)}", seed, log);
        }
    }

    private static Random? Create(Type type, int seed)
    {
        if (type == typeof(Random)) return new Random(seed);
        if (type.IsAbstract || type.GetConstructor([typeof(int)]) is not { } ctor) return null;
        return (Random)ctor.Invoke([seed]);
    }
}

/// <summary>
/// The process-wide static generators, seeded for one owner at a time: client and server share
/// them in one process, so whoever seeded them last owns them, and only the owner, or the
/// scenario's teardown, puts them back.
/// </summary>
internal static class StaticRandomSeeding
{
    private static readonly object s_lock = new();
    private static object? s_owner;
    private static SeedingLog? s_log;
    private static int? s_seed;

    /// <summary>Who seeded the static generators, or null while the game's own run.</summary>
    public static object? Owner
    {
        get
        {
            lock (s_lock) return s_owner;
        }
    }

    /// <summary>The seed of the static generators, or null.</summary>
    public static int? Seed
    {
        get
        {
            lock (s_lock) return s_seed;
        }
    }

    /// <summary>What the static seeding did, or null.</summary>
    public static SeedingLog? Log
    {
        get
        {
            lock (s_lock) return s_log;
        }
    }

    /// <summary>Seeds the static generators of <paramref name="assemblies"/> for <paramref name="owner"/>, undoing an earlier seeding first.</summary>
    public static void SeedFor(object owner, int seed, IEnumerable<Assembly> assemblies)
    {
        List<RandomCode.StaticFieldSet> sets = [.. assemblies.Distinct().Select(RandomCode.StaticFields)];
        SeedFields(owner, seed, sets.SelectMany(f => f.Shared), sets.SelectMany(f => f.Unseedable));
    }

    /// <summary>Seeds <paramref name="fields"/>, static fields with one value for the process, for <paramref name="owner"/>.</summary>
    internal static void SeedFields(object owner, int seed, IEnumerable<FieldInfo> fields, IEnumerable<UnseededRandom>? unseedable = null)
    {
        lock (s_lock)
        {
            RestoreLocked();
            SeedingLog log = new();
            foreach (FieldInfo field in fields.Distinct())
            {
                RandomFields.Seed(field, null, "static:" + RandomCode.Member(field), seed, log);
            }

            foreach (UnseededRandom unseeded in unseedable ?? []) log.Failed(unseeded);
            s_owner = owner;
            s_log = log;
            s_seed = seed;
        }
    }

    /// <summary>Puts the static generators back if <paramref name="owner"/> seeded them.</summary>
    public static void Release(object owner)
    {
        lock (s_lock)
        {
            if (ReferenceEquals(s_owner, owner)) RestoreLocked();
        }
    }

    /// <summary>Puts the static generators back, whoever seeded them.</summary>
    public static void RestoreAll()
    {
        lock (s_lock) RestoreLocked();
    }

    private static void RestoreLocked()
    {
        SeedingLog? log = s_log;
        s_owner = null;
        s_log = null;
        s_seed = null;
        log?.UndoHere();
    }
}

/// <summary>
/// The seeding of one side while it is active: seeds the entities and block entities the side
/// makes after <see cref="GameRandom.Seed"/>, each keyed by what it is and where, and how many of
/// it came before in this seeding, never by its entity id, which depends on the tests before.
/// </summary>
internal sealed class SideSeeding(string side, int seed, SeedingLog log)
{
    private readonly Dictionary<string, int> _counts = [];

    // What this seeding already seeded: a block entity whose Initialize runs again, as some do, keeps its stream.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _seen = [];

    private bool FirstTime(object target)
    {
        lock (_seen) return _seen.TryAdd(target, target);
    }

    public string Side { get; } = side;
    public int SeedValue { get; } = seed;
    public SeedingLog Log { get; } = log;

    private string Next(string key)
    {
        lock (_counts)
        {
            _counts.TryGetValue(key, out int count);
            _counts[key] = count + 1;
            return $"{key}#{count}";
        }
    }

    public void SeedEntity(Entity entity)
    {
        if (!FirstTime(entity)) return;
        string key = Next($"{Side}:entity:{entity.Code}");
        RandomFields.SeedObject(entity, key, SeedValue, Log);
        if (entity.SidedProperties?.Behaviors is { } behaviors)
        {
            for (int i = 0; i < behaviors.Count; i++)
            {
                if (behaviors[i] is { } behavior) RandomFields.SeedObject(behavior, $"{key}/{i}", SeedValue, Log);
            }
        }
    }

    public void SeedBlockEntity(BlockEntity blockEntity)
    {
        if (!FirstTime(blockEntity)) return;
        string key = Next($"{Side}:be:{blockEntity.Block?.Code}@{blockEntity.Pos?.X},{blockEntity.Pos?.Y},{blockEntity.Pos?.Z}");
        RandomFields.SeedObject(blockEntity, key, SeedValue, Log);
        if (blockEntity.Behaviors is { } behaviors)
        {
            for (int i = 0; i < behaviors.Count; i++)
            {
                if (behaviors[i] is { } behavior) RandomFields.SeedObject(behavior, $"{key}/{i}", SeedValue, Log);
            }
        }
    }
}

/// <summary>
/// Seeds the game's random generators: the hooks that seed objects made during a test, and the
/// scenario-level seeding the scenario base classes use.
/// </summary>
internal static class RandomSeeding
{
    private const string HarmonyId = "zaldaryon.pharos.random";
    private static readonly Harmony s_harmony = new(HarmonyId);
    private static readonly object s_patchLock = new();
    private static readonly HashSet<MethodBase> s_patched = [];
    private static readonly List<UnseededRandom> s_patchFailures = [];

    private static volatile SideSeeding? s_server;
    private static volatile SideSeeding? s_client;

    /// <summary>
    /// The output notes of the scenario test running now, captured on the test's flow when its
    /// body starts: a reseed from inside <c>RunOnGameThread</c> still reaches them.
    /// </summary>
    internal static volatile IsolationLog.Notes? Notes;

    /// <summary>The initializers Pharos could not patch.</summary>
    public static IReadOnlyList<UnseededRandom> PatchFailures
    {
        get
        {
            lock (s_patchLock) return [.. s_patchFailures];
        }
    }

    /// <summary>Makes <paramref name="seeding"/> seed what its side makes from now on, or stops it (null).</summary>
    public static void Activate(EnumAppSide side, SideSeeding? seeding)
    {
        if (side == EnumAppSide.Server) s_server = seeding;
        else s_client = seeding;
    }

    /// <summary>
    /// Patches the methods of <paramref name="assemblies"/> that initialize block entities and
    /// entities, once each, so what they make during a seeded test is seeded. Hosts call it when
    /// they boot, before the world ticks; a seeding calls it again for mods loaded since.
    /// </summary>
    public static void Hook(IEnumerable<Assembly> assemblies)
    {
        lock (s_patchLock)
        {
            foreach (Assembly assembly in assemblies.Distinct())
            {
                foreach (MethodBase method in RandomCode.FactsOf(assembly).Initializers)
                {
                    if (!s_patched.Add(method)) continue;
                    try
                    {
                        // Last, so other transpilers still find the game's own calls.
                        s_harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RandomSeeding), nameof(SeedAfterInitializing)) { priority = Priority.Last });
                    }
                    catch (Exception ex)
                    {
                        s_patchFailures.Add(new UnseededRandom(RandomCode.Member(method), $"what it initializes during a test is not seeded: {ex.Message}", assembly));
                    }
                }
            }
        }
    }

    private static SideSeeding? Active(ICoreAPI? api) => api?.Side switch
    {
        EnumAppSide.Server => s_server,
        EnumAppSide.Client => s_client,
        _ => null,
    };

    // After each blockEntity.Initialize(api) and entity.AfterInitialized(spawn) call, which stays
    // as it is, the object is passed to a seeding call: its argument and the object go through
    // locals so the call keeps its stack.
    private static IEnumerable<CodeInstruction> SeedAfterInitializing(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        MethodInfo blockEntityReady = typeof(RandomSeeding).GetMethod(nameof(BlockEntityReady), BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo entityReady = typeof(RandomSeeding).GetMethod(nameof(EntityReady), BindingFlags.Static | BindingFlags.NonPublic)!;
        CodeInstruction? previous = null;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Callvirt && previous?.opcode != OpCodes.Constrained
                && instruction.operand is MethodInfo called && RandomCode.SeededAfter(called) is { } definition)
            {
                Type argumentType = definition.GetParameters()[0].ParameterType;
                LocalBuilder argument = generator.DeclareLocal(argumentType);
                LocalBuilder target = generator.DeclareLocal(definition.DeclaringType!);
                CodeInstruction first = new(OpCodes.Stloc, argument);
                first.MoveLabelsFrom(instruction);
                first.MoveBlocksFrom(instruction);
                yield return first;
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Stloc, target);
                yield return new CodeInstruction(OpCodes.Ldloc, argument);
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldloc, target);
                yield return new CodeInstruction(OpCodes.Call, definition == RandomCode.BlockEntityInitialize ? blockEntityReady : entityReady);
            }
            else
            {
                yield return instruction;
            }

            previous = instruction;
        }
    }

    private static void BlockEntityReady(BlockEntity blockEntity)
    {
        try
        {
            Active(blockEntity.Api)?.SeedBlockEntity(blockEntity);
        }
        catch (Exception)
        {
            // Seeding never breaks the game.
        }
    }

    private static void EntityReady(Entity entity)
    {
        try
        {
            Active(entity.Api)?.SeedEntity(entity);
        }
        catch (Exception)
        {
            // Seeding never breaks the game.
        }
    }

    /// <summary>
    /// Seeds a scenario's server and client with <paramref name="seed"/>: the static generators
    /// once, for <paramref name="owner"/>, then each side.
    /// </summary>
    public static void SeedScenario(object owner, int seed, string source, GameRandom? server, GameRandom? client)
    {
        IEnumerable<Assembly> assemblies = (server?.Assemblies() ?? []).Concat(client?.Assemblies() ?? []);
        StaticRandomSeeding.SeedFor(owner, seed, assemblies);
        server?.SeedSide(seed, source);
        client?.SeedSide(seed, source);
    }

    /// <summary>
    /// Puts back every generator a scenario seeded: the statics, whoever seeded them, then each
    /// side. Throws what failed, after trying everything.
    /// </summary>
    public static void EndScenario(GameRandom? server, GameRandom? client)
    {
        List<Exception> errors = [];

        // The statics first: they belong to the whole process and need no game thread.
        try
        {
            StaticRandomSeeding.RestoreAll();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        foreach (GameRandom? side in new[] { server, client })
        {
            try
            {
                side?.Restore();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        Notes = null;
        if (errors.Count == 1) throw errors[0];
        if (errors.Count > 1) throw new AggregateException(errors);
    }

    /// <summary>
    /// For a test body left running: stops seeding new objects and puts the static generators
    /// back, without touching the hosts' threads.
    /// </summary>
    public static void Abandon()
    {
        s_server = null;
        s_client = null;
        Notes = null;
        try
        {
            StaticRandomSeeding.RestoreAll();
        }
        catch (Exception)
        {
            // The process is given up anyway.
        }
    }

    /// <summary>The assemblies whose static generators a side seeds, besides its mods.</summary>
    public static IEnumerable<Assembly> CoreAssemblies()
    {
        yield return typeof(ICoreAPI).Assembly;
        yield return typeof(Vintagestory.Server.ServerMain).Assembly;
        if (ScenarioTestInfo.Current?.TestClass.Assembly is { } tests) yield return tests;
    }

    /// <summary>Whether a mod's assembly is one Pharos seeds: not Pharos's own.</summary>
    public static bool Scanned(Assembly assembly) =>
        assembly.GetName().Name is { } name && !name.StartsWith("Zaldaryon.Pharos", StringComparison.Ordinal) && !assembly.IsDynamic;
}
