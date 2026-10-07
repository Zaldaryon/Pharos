using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>Test account sessions, read from the environment.</summary>
internal static class TestAccounts
{
    /// <summary>
    /// The account in <c>PHAROS_VS_*</c>, or in <c>NIGHTVEIL_VS_*</c>, or null when neither is set.
    /// </summary>
    public static ClientAuth? Online => ClientAuth.FromEnvironment() ?? ClientAuth.FromEnvironment("NIGHTVEIL_VS");
}

/// <summary>A scenario that needs a real Vintage Story account; skipped without one.</summary>
public sealed class OnlineAuthScenarioAttribute : FactAttribute
{
    public OnlineAuthScenarioAttribute()
    {
        if (TestAccounts.Online == null)
        {
            Skip = "Set PHAROS_VS_PLAYERNAME, _PLAYERUID, _SESSIONKEY and _SESSIONSIGNATURE to run online auth tests.";
        }
    }
}

public class ClientAuthTests
{
    [Fact]
    public void Offline_DerivesTheUidAndCarriesNoSession()
    {
        ClientAuth auth = ClientAuth.Offline("Alice");

        Assert.False(auth.IsOnline);
        Assert.Equal("pharos-alice", auth.PlayerUid);
    }

    [Fact]
    public void ToString_NeverShowsTheSession()
    {
        ClientAuth auth = ClientAuth.Online("Alice", "uid", "secret-key", "secret-signature");

        Assert.True(auth.IsOnline);
        Assert.DoesNotContain("secret", auth.ToString());
    }

    [Fact]
    public void FromEnvironment_IsNullWhenAnyPartIsMissing()
    {
        Assert.Null(ClientAuth.FromEnvironment("PHAROS_TEST_NO_SUCH_ACCOUNT"));
    }
}

[Collection("Sequential")]
public class NetworkConnectTests : ServerScenarioBase
{
    protected override ServerWorldOptions WorldOptions => new() { WorldName = "Pharos-NetworkConnect", ListenPort = 0 };

    private static HeadlessClient BootClient() =>
        HeadlessClientBootstrap.Boot(new HeadlessClientOptions { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 });

    [ServerScenario]
    public async Task Offline_JoinsOverRealTcp()
    {
        Assert.NotNull(Host!.Port);
        using HeadlessClient client = BootClient();
        using ClientServerLoopbackSession session = client.ConnectTcp(Host, ClientAuth.Offline("TcpPlayer"));

        Assert.True(await session.WaitForPlayerJoinedAsync(TimeSpan.FromSeconds(90)), "The client never joined over TCP");

        var connected = Host.RunOnGameThread(() => Host.Server.GetClientByPlayername("TcpPlayer"));
        Assert.NotNull(connected);
        Assert.False(connected!.IsSinglePlayerClient, "The connection should be a network client, not an in-memory one");
    }

    [ServerScenario]
    public async Task ConnectRemote_JoinsAServerThatTicksOnItsOwn()
    {
        using HeadlessClient client = BootClient();
        using CancellationTokenSource stop = new();

        // Stands in for a dedicated server: it ticks on its own clock, not the client's.
        Task ticking = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                Host!.Tick();
                await Task.Delay(15);
            }
        });

        try
        {
            using RemoteServerSession session = client.ConnectRemote("127.0.0.1", Host!.Port!.Value, ClientAuth.Offline("RemotePlayer"));

            bool joined = await session.WaitForPlayerJoinedAsync(TimeSpan.FromSeconds(90));
            Assert.True(joined, $"The client never joined. Disconnect reason: {session.DisconnectReason ?? "none"}");
            await session.StepFramesAsync(10);
            Assert.True(session.IsJoined);
        }
        finally
        {
            stop.Cancel();
            await ticking;
        }
    }
}

[Collection("Sequential")]
public class OnlineAuthTests : ServerScenarioBase
{
    protected override ServerWorldOptions WorldOptions => new() { WorldName = "Pharos-OnlineAuth", ListenPort = 0, VerifyPlayerAuth = true };

    private static HeadlessClient BootClient() =>
        HeadlessClientBootstrap.Boot(new HeadlessClientOptions { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 });

    [OnlineAuthScenario]
    public async Task Online_JoinsAServerThatVerifiesPlayers()
    {
        ClientAuth account = TestAccounts.Online!;
        using HeadlessClient client = BootClient();
        using ClientServerLoopbackSession session = client.ConnectTcp(Host!, account);

        bool joined = await session.WaitForPlayerJoinedAsync(TimeSpan.FromSeconds(90));

        Assert.True(joined, $"The account was not admitted. Disconnect reason: {client.RunOnClientThread(() => client.Client.disconnectReason) ?? "none"}");
        Assert.NotNull(Host!.RunOnGameThread(() => Host.Server.GetClientByPlayername(account.PlayerName)));
    }

    [OnlineAuthScenario]
    public async Task Offline_IsTurnedAwayByAServerThatVerifiesPlayers()
    {
        using HeadlessClient client = BootClient();
        using ClientServerLoopbackSession session = client.ConnectTcp(Host!, ClientAuth.Offline("Impostor"));

        bool joined = await session.WaitForPlayerJoinedAsync(TimeSpan.FromSeconds(30));

        Assert.False(joined);
        Assert.Null(Host!.RunOnGameThread(() => Host.Server.GetClientByPlayername("Impostor")));
    }
}
