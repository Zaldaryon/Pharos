using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// The builder of the world <see cref="SavedWorldTests"/> boots from, which
/// <see cref="MarkerWorld"/> saves with <c>pharos fixture</c>.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242)]
public class FixtureBuilder : ServerScenarioBase
{
    /// <summary>Where the builder puts the room, from the spawn: far enough that its chunks are not loaded at boot.</summary>
    internal static readonly Vec3i RoomOffset = new(320, 0, 320);

    internal const string Marker = "game:rawclay-blue-none";

    [ServerScenario]
    public async Task BuildMarkerWorld()
    {
        BlockPos room = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos).AddCopy(RoomOffset.X, RoomOffset.Y, RoomOffset.Z);
        await Host.PlaceSchematicAsync(SchematicTests.Room, room);
        this.SetBlock(room.AddCopy(2, 1, 2), Marker);
        Assert.Equal(Marker, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(room.AddCopy(2, 1, 2)).Code.ToString()));
    }
}

/// <summary>
/// <c>pharos fixture</c>, run for real against this assembly: <see cref="FixtureBuilder"/> builds the
/// world in a worker of its own and its save is written, once for the class.
/// </summary>
public sealed class MarkerWorld : IDisposable
{
    private readonly string _results = Path.Combine(Path.GetTempPath(), "pharos-fixture-" + Guid.NewGuid().ToString("N")[..8]);

    public MarkerWorld()
    {
        Save = Path.Combine(_results, "pharos-marker.vcdbs");
        StringWriter output = new();
        FixtureOptions options = new(
            "FixtureBuilder.BuildMarkerWorld",
            Save,
            new RunOptions { Target = typeof(MarkerWorld).Assembly.Location, NoBuild = true, ResultsDirectory = _results, Xvfb = XvfbMode.Never });
        int exit = FixtureCommand.RunAsync(options, output, output, CancellationToken.None).GetAwaiter().GetResult();
        Output = output.ToString();
        if (exit != RunCommand.ExitPassed)
        {
            // xUnit does not dispose a fixture whose constructor threw.
            Dispose();
            throw new InvalidOperationException($"pharos fixture exited with {exit}:\n{Output}");
        }
    }

    /// <summary>The save the builder left.</summary>
    public string Save { get; }

    /// <summary>What <c>pharos fixture</c> printed.</summary>
    public string Output { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_results, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// <see cref="ServerWorldOptions.SaveFile"/>: each class boots into its own copy of the save
/// <see cref="FixtureBuilder"/> made, and the world rolls back to it between tests.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242)]
public class SavedWorldTests(MarkerWorld world) : ServerScenarioBase, IClassFixture<MarkerWorld>
{
    protected override ServerWorldOptions WorldOptions => base.WorldOptions with { SaveFile = world.Save };

    [ServerScenario]
    public void TheFixtureCommand_WroteTheSave_OfTheOneTestItNamed()
    {
        Assert.Contains("Zaldaryon.Pharos.Tests.World.FixtureBuilder.BuildMarkerWorld", world.Output);
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(world.Save), 0, 16));
    }

    [ServerScenario]
    public async Task ASavedWorld_HasWhatTheBuilderLeft_AndTheFixtureNeverChanges()
    {
        byte[] before = File.ReadAllBytes(world.Save);
        BlockPos room = await LoadRoomAsync();

        Assert.Equal(FixtureBuilder.Marker, CodeAt(room.AddCopy(2, 1, 2)));
        Assert.Equal("game:chest-east", CodeAt(room.AddCopy(1, 1, 1)));

        this.SetBlock(room.AddCopy(2, 1, 2), 0);
        await Host!.SaveWorldAsync(Path.Combine(Host.DataPath, "scratch-copy.vcdbs"));
        Assert.Equal(before, File.ReadAllBytes(world.Save));
    }

    [ServerScenario]
    public async Task ASavedWorld_IsRolledBackBetweenTests()
    {
        BlockPos room = await LoadRoomAsync();
        Assert.Equal(FixtureBuilder.Marker, CodeAt(room.AddCopy(2, 1, 2)));
        this.SetBlock(room.AddCopy(2, 1, 2), 0);
    }

    private async Task<BlockPos> LoadRoomAsync()
    {
        BlockPos room = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos)
            .AddCopy(FixtureBuilder.RoomOffset.X, FixtureBuilder.RoomOffset.Y, FixtureBuilder.RoomOffset.Z);
        await Host.EnsureChunksLoadedAsync(room, room.AddCopy(3, 2, 3));
        return room;
    }

    private string CodeAt(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());
}

/// <summary>What is wrong with a <see cref="ServerWorldOptions.SaveFile"/> is found before anything boots.</summary>
public class SaveFileOptionTests
{
    [Fact]
    public void ASaveFileWithASaveLocationToo_IsRefused()
    {
        ServerWorldOptions options = new() { SaveFile = "Fixtures/worlds/pharos-marker.vcdbs", SaveFileLocation = "elsewhere.vcdbs" };
        Assert.Throws<ArgumentException>(() => EmbeddedServerHost.CheckSaveFile(options));
    }

    [Fact]
    public void AMissingSaveFile_IsRefused()
    {
        Assert.Throws<FileNotFoundException>(() => EmbeddedServerHost.CheckSaveFile(new ServerWorldOptions { SaveFile = "Fixtures/worlds/no-such.vcdbs" }));
    }

    [Fact]
    public void ASaveWithAWriteAheadLog_IsRefused()
    {
        string save = Path.Combine(Path.GetTempPath(), "pharos-wal-" + Guid.NewGuid().ToString("N")[..8] + ".vcdbs");
        File.WriteAllText(save, "");
        File.WriteAllText(save + "-wal", "");
        try
        {
            Assert.Throws<InvalidDataException>(() => EmbeddedServerHost.CheckSaveFile(new ServerWorldOptions { SaveFile = save }));
        }
        finally
        {
            File.Delete(save);
            File.Delete(save + "-wal");
        }
    }
}

/// <summary><see cref="ServerWorldAttribute.SaveFile"/> reaches the world a scenario boots.</summary>
public class SaveFileAttributeTests
{
    [ServerWorld(SaveFile = "Fixtures/worlds/village.vcdbs")]
    private sealed class WithASave;

    [Fact]
    public void TheAttributesSaveFile_IsTheWorldsSaveFile() =>
        Assert.Equal("Fixtures/worlds/village.vcdbs", ScenarioAttributes.WorldOptions(typeof(WithASave)).SaveFile);
}
