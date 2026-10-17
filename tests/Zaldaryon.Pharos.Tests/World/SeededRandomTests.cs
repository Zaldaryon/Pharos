using System.Reflection;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Xunit;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>Seed derivation, in-place re-seeding and the code scan, without booting the game.</summary>
[Collection(SeededRandomCollection.Name)]
public class SeededRandomUnitTests
{
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void TheDefaultSeed_IsAStableNonNegativeHashOfTheName()
    {
        // The FNV-1a 64 test vector.
        Assert.Equal(0xaf63dc4c8601ec8cUL, SeededAttribute.Fnv64("a"));

        int seed = SeededAttribute.SeedFor("My.Tests.Grass_DropsSeeds");
        Assert.Equal(seed, SeededAttribute.SeedFor("My.Tests.Grass_DropsSeeds"));
        Assert.InRange(seed, 0, int.MaxValue);
        Assert.NotEqual(seed, SeededAttribute.SeedFor("My.Tests.Grass_DropsSeeds(2)"));
    }

    [Fact]
    public void TheoryArguments_AreFormattedUnderTheInvariantCulture()
    {
        MethodInfo method = typeof(Samples).GetMethod(nameof(Samples.Row))!;
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal($"{typeof(Samples).FullName}.Row(1.5, \"x\", null)", ScenarioRandomSeed.TestName(typeof(Samples), method, [1.5, "x", null]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void TheSeed_ComesFromTheVariable_ThenTheMethod_TheClass_AndTheName()
    {
        static string? None(string _) => null;
        MethodInfo fixedSeed = typeof(Samples).GetMethod(nameof(Samples.FixedSeed))!;
        MethodInfo hashed = typeof(Samples).GetMethod(nameof(Samples.Hashed))!;
        MethodInfo onClass = typeof(SeededClass).GetMethod(nameof(SeededClass.Plain))!;
        MethodInfo plain = typeof(Samples).GetMethod(nameof(Samples.Plain))!;

        Assert.Equal(new ScenarioRandomSeed(1234, "[Seeded] on the method"), ScenarioRandomSeed.Resolve(typeof(Samples), fixedSeed, null, None));
        Assert.Equal(new ScenarioRandomSeed(77, "[Seeded] on the class"), ScenarioRandomSeed.Resolve(typeof(SeededClass), onClass, null, None));
        Assert.Equal(SeededAttribute.SeedFor($"{typeof(Samples).FullName}.Hashed"), ScenarioRandomSeed.Resolve(typeof(Samples), hashed, null, None)!.Seed);
        Assert.Null(ScenarioRandomSeed.Resolve(typeof(Samples), plain, null, None));

        Assert.Equal(new ScenarioRandomSeed(42, "PHAROS_RANDOM_SEED"), ScenarioRandomSeed.Resolve(typeof(Samples), fixedSeed, null, _ => " 42 "));
        Assert.Null(ScenarioRandomSeed.Resolve(typeof(Samples), plain, null, _ => "42"));
        Assert.NotNull(ScenarioRandomSeed.Resolve(typeof(Samples), fixedSeed, null, _ => "forty-two")!.Error);

        Assert.Equal($"Random seed: 1234 ([Seeded] on the method). Replay it with PHAROS_RANDOM_SEED=1234.{Environment.NewLine}",
            ScenarioRandomSeed.Resolve(typeof(Samples), fixedSeed, null, None)!.OutputLine);
    }

    [Fact]
    public void Streams_DependOnTheSeedAndTheKey()
    {
        Assert.Equal(RandomStreams.Mix(1, "a"), RandomStreams.Mix(1, "a"));
        Assert.NotEqual(RandomStreams.Mix(1, "a"), RandomStreams.Mix(1, "b"));
        Assert.NotEqual(RandomStreams.Mix(1, "a"), RandomStreams.Mix(2, "a"));
    }

    [Fact]
    public void StaticGenerators_AreReseededInPlace_AndPutBack()
    {
        Assert.True(RandomReseeder.Available);
        object owner = new();
        FieldInfo[] fields = [.. new[] { nameof(Statics.Plain), nameof(Statics.ReadOnly), nameof(Statics.Locked), nameof(Statics.Normal) }
            .Select(n => typeof(Statics).GetField(n, AnyStatic)!)];
        Random[] instances = [.. fields.Select(f => (Random)f.GetValue(null)!)];
        Random plainTwin = new(5);
        Assert.Equal(plainTwin.Next(), Statics.Plain.Next());

        StaticRandomSeeding.SeedFields(owner, 1234, fields);
        try
        {
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.Same(instances[i], fields[i].GetValue(null));
                Random expected = (Random)Activator.CreateInstance(instances[i].GetType(), RandomStreams.Mix(1234, "static:" + RandomCode.Member(fields[i])))!;
                Assert.Equal(Draw(expected), Draw(instances[i]));
            }

            Assert.Equal(4, StaticRandomSeeding.Log!.SeededList().Count);
            Assert.All(StaticRandomSeeding.Log.SeededList(), s => Assert.Equal("in place", s.How));
        }
        finally
        {
            StaticRandomSeeding.Release(new object());
            Assert.Same(owner, StaticRandomSeeding.Owner);
            StaticRandomSeeding.Release(owner);
        }

        Assert.Null(StaticRandomSeeding.Owner);
        // The original goes on as if the test had drawn nothing from it.
        Assert.Equal(plainTwin.Next(), Statics.Plain.Next());
    }

    [Fact]
    public void SeedingTwice_StillPutsBackTheOriginal()
    {
        FieldInfo field = typeof(Statics).GetField(nameof(Statics.Twice), AnyStatic)!;
        Random twin = new(9);
        StaticRandomSeeding.SeedFields(this, 1, [field]);
        StaticRandomSeeding.SeedFields(this, 2, [field]);
        Assert.Equal(Draw(new Random(RandomStreams.Mix(2, "static:" + RandomCode.Member(field)))), Draw(Statics.Twice));
        StaticRandomSeeding.RestoreAll();
        Assert.Equal(Draw(twin), Draw(Statics.Twice));
    }

    [Fact]
    public void AThreadLocal_IsReplacedWithPerThreadStreams_AndPutBack()
    {
        FieldInfo field = typeof(Statics).GetField(nameof(Statics.PerThread), AnyStatic)!;
        ThreadLocal<Random> original = Statics.PerThread;
        StaticRandomSeeding.SeedFields(this, 7, [field]);
        try
        {
            Assert.NotSame(original, Statics.PerThread);
            int[] First(string name)
            {
                int[] values = [];
                Thread thread = new(() => values = Draw(Statics.PerThread.Value!)) { Name = name };
                thread.Start();
                thread.Join();
                return values;
            }

            string key = "static:" + RandomCode.Member(field);
            Assert.Equal(Draw(new Random(RandomStreams.Mix(7, key + "@one"))), First("one"));
            Assert.NotEqual(First("one"), First("two"));
            Assert.Equal("per thread", Assert.Single(StaticRandomSeeding.Log!.SeededList()).How);
        }
        finally
        {
            StaticRandomSeeding.RestoreAll();
        }

        Assert.Same(original, Statics.PerThread);
    }

    [Fact]
    public void AThreadStaticField_IsSeededOnTheCallingThreadOnly()
    {
        FieldInfo field = typeof(Statics).GetField(nameof(Statics.ThisThread), AnyStatic)!;
        SeedingLog log = new();
        RandomFields.Seed(field, null, "k", 3, log, fillNull: true);
        Assert.Equal(Draw(new Random(RandomStreams.Mix(3, "k"))), Draw(Statics.ThisThread!));

        Random? elsewhere = new Random(0);
        Thread thread = new(() => elsewhere = Statics.ThisThread);
        thread.Start();
        thread.Join();
        Assert.Null(elsewhere);

        foreach (Action undo in log.UndoHere()) undo();
        Assert.Null(Statics.ThisThread);
    }

    [Fact]
    public void TheStaticScan_SortsFieldsByHowTheyCanBeSeeded()
    {
        RandomCode.StaticFieldSet set = RandomCode.StaticFields(typeof(Statics).Assembly);
        Assert.Contains(set.Shared, f => f.DeclaringType == typeof(Statics) && f.Name == nameof(Statics.ReadOnly));
        Assert.Contains(set.Shared, f => f.DeclaringType == typeof(Statics) && f.Name == nameof(Statics.PerThread));
        Assert.Contains(set.PerThread, f => f.DeclaringType == typeof(Statics) && f.Name == nameof(Statics.ThisThread));
        Assert.Contains(set.Unseedable, u => u.Member.Contains("GenericHolder", StringComparison.Ordinal));
    }

    [Fact]
    public void ASubclassThatDrawsByItself_IsMarked()
    {
        Assert.Null(RandomReseeder.OverrideNote(typeof(Random)));
        Assert.Null(RandomReseeder.OverrideNote(typeof(ThreadSafeRandom)));
        Assert.Null(RandomReseeder.OverrideNote(typeof(NormalRandom)));
        Assert.Contains("overrides Next", RandomReseeder.OverrideNote(typeof(OwnRandom)));
    }

    [Fact]
    public void TheCodeScan_ReportsGeneratorsMadeInsideMethods()
    {
        IReadOnlyList<UnseededRandom> unseeded = RandomCode.FactsOf(typeof(Creators).Assembly).Unseeded;
        string creators = typeof(Creators).FullName!;

        Assert.Contains(unseeded, u => u.Member == $"{creators}.NoSeed" && u.Reason.Contains("with no seed", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member == $"{creators}.FromTicks" && u.Reason.Contains("Environment.TickCount", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member == $"{creators}.FromGuid" && u.Reason.Contains("Guid.NewGuid", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member == $"{creators}.InALambda" && u.Reason.Contains("with no seed", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member == $"{creators}.held" && u.Reason.Contains("does not reach", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member == $"{creators}.Lazy" && u.Reason.Contains($"sets {creators}.s_lazy", StringComparison.Ordinal));
        Assert.DoesNotContain(unseeded, u => u.Member == $"{creators}.Fixed");
        Assert.DoesNotContain(unseeded, u => u.Member.StartsWith(typeof(Statics).FullName + ".", StringComparison.Ordinal) && u.Reason.Contains("inside", StringComparison.Ordinal));
        Assert.All(unseeded, u => Assert.Same(typeof(Creators).Assembly, u.Assembly));
    }

    [Fact]
    public void ABlockEntityInitializedTwice_IsSeededOnce()
    {
        SampleBlockEntity blockEntity = new() { Pos = new BlockPos(1, 2, 3) };
        SideSeeding seeding = new("server", 5, new SeedingLog());
        seeding.SeedBlockEntity(blockEntity);
        Random expected = new(RandomStreams.Mix(5, $"server:be:@1,2,3#0:{typeof(SampleBlockEntity).FullName}.Rand"));
        Assert.Equal(Draw(expected), Draw(blockEntity.Rand));

        // As BELargeGear3m.CountGears does: the stream goes on rather than starting over.
        seeding.SeedBlockEntity(blockEntity);
        Assert.Equal(Draw(expected), Draw(blockEntity.Rand));
        Assert.Equal(1, Assert.Single(seeding.Log.SeededList()).Count);
    }

    [Fact]
    public void TheInitializersPharosPatches_AreTheGamesCallers()
    {
        IReadOnlyList<MethodBase> initializers = RandomCode.FactsOf(typeof(Vintagestory.Server.ServerMain).Assembly).Initializers;
        Assert.Contains(initializers, m => RandomCode.Member(m) == "Vintagestory.Server.ServerWorldMap.SpawnBlockEntity");
        Assert.Contains(initializers, m => RandomCode.Member(m) == "Vintagestory.Server.ServerMain.SpawnEntity_internal");
        Assert.Contains(initializers, m => RandomCode.Member(m) == "Vintagestory.Client.NoObf.ClientSystemEntities.entityFromPacket");
    }

    [Fact]
    public async Task ASeededScenario_SaysItsSeedFirst()
    {
        ScenarioRunnerHarness.RunResult seeded = await ScenarioRunnerHarness.RunAsync(typeof(PipelineSample), nameof(PipelineSample.Seeded));
        string output = Assert.Single(seeded.Passed).Output;
        string[] lines = output.Split(Environment.NewLine);
        Assert.StartsWith("Vintage Story ", lines[0]);
        Assert.Equal("Random seed: 1234 ([Seeded] on the method). Replay it with PHAROS_RANDOM_SEED=1234.", lines[1]);
        Assert.Equal(1234, PipelineSample.SeenSeed);

        ScenarioRunnerHarness.RunResult plain = await ScenarioRunnerHarness.RunAsync(typeof(PipelineSample), nameof(PipelineSample.Plain));
        Assert.DoesNotContain("Random seed", Assert.Single(plain.Passed).Output);
        Assert.Null(PipelineSample.SeenSeed);
    }

    private static int[] Draw(Random random) => [.. Enumerable.Range(0, 8).Select(_ => random.Next())];

    private static class Statics
    {
        public static Random Plain = new(5);
        public static readonly Random ReadOnly = new();
        public static ThreadSafeRandom Locked = new();
        public static NormalRandom Normal = new();
        public static Random Twice = new(9);
        public static ThreadLocal<Random> PerThread = new(() => new Random());

        [ThreadStatic]
        public static Random? ThisThread;
    }

    private static class GenericHolder<T>
    {
        public static Random Rand = new();
    }

    private sealed class OwnRandom : Random
    {
        private int _state;

        public override int Next() => _state++;
    }

#pragma warning disable CA1822, IDE0051
    private sealed class Creators
    {
        private Random? held = new Random();

        public int NoSeed() => new Random().Next();

        public int FromTicks() => new Random(Environment.TickCount).Next();

        public int FromGuid() => new Random(Guid.NewGuid().GetHashCode()).Next();

        public int Fixed() => new Random(12).Next();

        public Func<int> InALambda() => () => new Random().Next();

        private static Random? s_lazy;

        public Random? Held => held;

        public Random Lazy() => s_lazy ??= new Random();
    }

    private sealed class SampleBlockEntity : Vintagestory.API.Common.BlockEntity
    {
        public Random Rand = new(1);
    }
#pragma warning restore CA1822, IDE0051

    private sealed class Samples
    {
        [Seeded(1234)]
        public void FixedSeed()
        {
        }

        [Seeded]
        public void Hashed()
        {
        }

        public void Plain()
        {
        }

        public void Row(double a, string b, object? c)
        {
        }
    }

    [Seeded(77)]
    private sealed class SeededClass
    {
        public void Plain()
        {
        }
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class PipelineSample : IAsyncLifetime, IScenarioLifecycle
    {
        public static int? SeenSeed;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        void IScenarioLifecycle.BeforeBody() => SeenSeed = ScenarioTestInfo.Current?.RandomSeed?.Seed;

        Task IScenarioLifecycle.ApplyEnvironmentsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        Task IScenarioLifecycle.CleanupEnvironmentsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        void IScenarioLifecycle.CheckLoggedErrors()
        {
        }

        void IScenarioLifecycle.BodyTimedOut(bool stillRunning)
        {
        }

        string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut) => null;

        Zaldaryon.Pharos.Server.EmbeddedServerHost? IScenarioLifecycle.FixtureServer => null;

        [ServerScenario, Seeded(1234)]
        public void Seeded()
        {
        }

        [ServerScenario]
        public void Plain()
        {
        }
    }
#pragma warning restore xUnit1000
}

/// <summary>The tests that seed process-wide static generators run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SeededRandomCollection
{
    public const string Name = "Seeded random";
}
