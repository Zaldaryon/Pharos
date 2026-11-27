using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Cli.ParallelRuns;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// The builder of the world <see cref="LiveSavedWorldRollbackTests"/> boots from: an engine-mode
/// client joins, so the save carries that player's data.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class JoinedWorldBuilder : ClientServerScenarioBase
{
    internal const string Player = "SavedPlayer";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override string PlayerName => Player;

    [ClientServerScenario]
    public async Task JoinOnce()
    {
        await Session!.StepFramesAsync(30);
        Assert.Equal(EnumGameMode.Creative, ServerHost!.RunOnGameThread(() => Server!.GetClientByPlayername(PlayerName).Player.WorldData.CurrentGameMode));
    }
}

/// <summary>
/// <c>pharos fixture</c> run against <see cref="JoinedWorldBuilder"/>, once for the class.
/// </summary>
public sealed class JoinedWorld : IDisposable
{
    private readonly string _results = Path.Combine(Path.GetTempPath(), "pharos-fixture-" + Guid.NewGuid().ToString("N")[..8]);

    public JoinedWorld()
    {
        Save = Path.Combine(_results, "pharos-joined.vcdbs");
        StringWriter output = new();
        FixtureOptions options = new(
            "JoinedWorldBuilder.JoinOnce",
            Save,
            new RunOptions { Target = typeof(JoinedWorld).Assembly.Location, NoBuild = true, ResultsDirectory = _results, Xvfb = XvfbMode.Auto });
        int exit = FixtureCommand.RunAsync(options, output, output, CancellationToken.None).GetAwaiter().GetResult();
        if (exit != RunCommand.ExitPassed)
        {
            // xUnit does not dispose a fixture whose constructor threw.
            Dispose();
            throw new InvalidOperationException($"pharos fixture exited with {exit}:\n{output}");
        }
    }

    /// <summary>The save the builder left.</summary>
    public string Save { get; }

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
/// <see cref="WorldIsolation.Rollback"/> on a world booted from a save that already holds the
/// joining player. The game reports such a player as a spectator until its data is flagged as
/// connected, which is still the case when the pair is captured; a rollback must put back the
/// mode the player really has, not that placeholder. Whichever test runs second sees the rollback.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveSavedWorldRollbackTests(JoinedWorld world) : ClientServerScenarioBase, IClassFixture<JoinedWorld>
{
    protected override ServerWorldOptions WorldOptions => base.WorldOptions with { SaveFile = world.Save };

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override string PlayerName => JoinedWorldBuilder.Player;

    [ClientServerScenario]
    public Task FirstOrSecond_KeepsTheSavedPlayersGameMode() => AssertCreativeAsync();

    [ClientServerScenario]
    public Task SecondOrFirst_KeepsTheSavedPlayersGameMode() => AssertCreativeAsync();

    private async Task AssertCreativeAsync()
    {
        await Session!.StepFramesAsync(30);
        Assert.Equal(EnumGameMode.Creative, ServerHost!.RunOnGameThread(() => ((ServerWorldPlayerData)Server!.GetClientByPlayername(PlayerName).Player.WorldData).GameMode));
        Assert.Equal(EnumGameMode.Creative, Client!.RunOnClientThread(() => Client.Client.player.WorldData.CurrentGameMode));
    }
}
