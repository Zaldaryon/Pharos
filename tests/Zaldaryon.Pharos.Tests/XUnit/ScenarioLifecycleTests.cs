using Vintagestory.API.MathTools;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

// These run real derived scenarios end to end, so they share the collection that keeps every
// GL- and engine-booting test in this assembly sequential.

[Collection("Sequential")]
[ServerWorld(seed: 777, playStyle: "creativebuilding", worldType: "superflat")]
[ServerMods("TestMods/pharostestmod")]
public class ServerScenarioLifecycleTests : ServerScenarioBase
{
    [ServerScenario]
    public void InitializeAsync_BootsTheServerFromTheClassAttributes()
    {
        Assert.NotNull(Host);
        Assert.True(Host!.IsRunning);
        Assert.Equal(777, Api!.World.Seed);
        Assert.Equal("superflat", Api.WorldManager.SaveGame.WorldType);
    }

    [ServerScenario]
    public void InitializeAsync_StagesTheServerMods()
    {
        Assert.True(Api!.ModLoader.IsModEnabled("pharostestmod"), "The staged content mod was not loaded");
    }

    [ServerScenario]
    public async Task ExecuteSuccess_RunsAConsoleCommand()
    {
        await ExecuteSuccess("/time set midnight");
        double night = Host!.RunOnGameThread(() => Api!.World.Calendar.HourOfDay);

        await ExecuteSuccess("/time set 12:00");
        double noon = Host.RunOnGameThread(() => Api!.World.Calendar.HourOfDay);

        Assert.True(Math.Abs(noon - 12) < 1, $"The clock reads {noon:0.0} after /time set 12:00");
        Assert.True(night < 1 || night > 23, $"The clock read {night:0.0} after /time set midnight");
    }

    [ServerScenario]
    public async Task ExecuteSuccess_FailsOnAnUnknownCommand()
    {
        CommandExecutionException error = await Assert.ThrowsAsync<CommandExecutionException>(() => ExecuteSuccess("/pharosnosuchcommand"));

        Assert.Equal(Vintagestory.API.Common.EnumCommandStatus.NoSuchCommand, error.Result.Status);
    }
}

[Collection("Sequential")]
[ServerWorld(Isolation = WorldIsolation.Recycle)]
public class ServerScenarioRecycleTests : ServerScenarioBase
{
    private static ServerMain? s_first;

    [ServerScenario]
    public void First_SharesTheServerWithTheOtherTest() => AssertShared();

    [ServerScenario]
    public void Second_SharesTheServerWithTheOtherTest() => AssertShared();

    private void AssertShared()
    {
        Assert.NotNull(Server);
        if (s_first == null) s_first = Server;
        else Assert.Same(s_first, Server);
    }
}

[Collection("Sequential")]
[ServerWorld(Isolation = WorldIsolation.Restart)]
public class ServerScenarioRestartTests : ServerScenarioBase
{
    private static ServerMain? s_first;

    [ServerScenario]
    public void First_GetsItsOwnServer() => AssertFresh();

    [ServerScenario]
    public void Second_GetsItsOwnServer() => AssertFresh();

    private void AssertFresh()
    {
        Assert.NotNull(Server);
        if (s_first == null) s_first = Server;
        else Assert.NotSame(s_first, Server);
    }
}

[Collection("Sequential")]
public class ClientScenarioLifecycleTests : ClientScenarioBase
{
    private static HeadlessClient? s_first;

    [ClientScenario]
    public void First_BootsAndSharesTheClient() => AssertSharedAndSteps();

    [ClientScenario]
    public void Second_BootsAndSharesTheClient() => AssertSharedAndSteps();

    private void AssertSharedAndSteps()
    {
        Assert.NotNull(Client);
        Assert.False(Client!.IsDisposed);
        if (s_first == null) s_first = Client;
        else Assert.Same(s_first, Client);

        long before = FrameController!.TotalFrames;
        Client.StepFrames(3);
        Assert.Equal(before + 3, FrameController.TotalFrames);
    }
}

[Collection("Sequential")]
public class FreshClientScenarioLifecycleTests : ClientScenarioBase
{
    private static HeadlessClient? s_first;

    protected override IsolationMode IsolationMode => IsolationMode.FreshClient;

    [ClientScenario]
    public void First_GetsItsOwnClient() => AssertFresh();

    [ClientScenario]
    public void Second_GetsItsOwnClient() => AssertFresh();

    private void AssertFresh()
    {
        Assert.NotNull(Client);
        if (s_first == null) s_first = Client;
        else Assert.NotSame(s_first, Client);
    }
}

[Collection("Sequential")]
[ServerMods("TestMods/pharostestmod")]
public class ClientServerScenarioLifecycleTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public void InitializeAsync_JoinsTheClientWithTheServerModsOnBothSides()
    {
        Assert.True(IsConnected);
        Assert.True(Client!.IsJoined);
        Assert.Equal(PlayerName, Client.Client.player.PlayerName);

        var serverApi = (Vintagestory.API.Server.ICoreServerAPI)Server!.Api;
        Assert.True(serverApi.ModLoader.IsModEnabled("pharostestmod"), "Server did not load the staged mod");
        Assert.True(Client.Client.api.ModLoader.IsModEnabled("pharostestmod"), "Client did not load the staged mod");
    }

    [ClientServerScenario]
    public async Task BlockPlacedOnTheServer_ReachesTheClient()
    {
        BlockPos spawn = Client!.Client.EntityPlayer.Pos.AsBlockPos;
        BlockPos pos = new(spawn.X + 2, spawn.Y, spawn.Z + 2, 0);
        var serverApi = (Vintagestory.API.Server.ICoreServerAPI)Server!.Api;
        int stone = serverApi.World.GetBlock(new Vintagestory.API.Common.AssetLocation("rock-granite")).BlockId;

        ServerHost!.RunOnGameThread(() => serverApi.World.BlockAccessor.SetBlock(stone, pos));

        bool synced = await StepUntilAsync(() => Client.Client.World.BlockAccessor.GetBlock(pos).BlockId == stone, maxFrames: 1200);
        Assert.True(synced, "The block set on the server never reached the client");
    }
}
