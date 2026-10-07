using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>A scenario against a dedicated server named by <c>PHAROS_REMOTE_SERVER</c>; skipped without one.</summary>
public sealed class RemoteServerScenarioAttribute : FactAttribute
{
    public RemoteServerScenarioAttribute()
    {
        if (RemoteServer.Endpoint == null)
        {
            Skip = "Set PHAROS_REMOTE_SERVER=host:port to a dedicated server with VerifyPlayerAuth off (see docker/dedicated-server).";
        }
    }
}

internal static class RemoteServer
{
    /// <summary>The server in <c>PHAROS_REMOTE_SERVER</c>, as host and port, or null.</summary>
    public static (string Host, int Port)? Endpoint
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("PHAROS_REMOTE_SERVER");
            if (string.IsNullOrWhiteSpace(value)) return null;

            int colon = value.LastIndexOf(':');
            return colon > 0 && int.TryParse(value[(colon + 1)..], out int port)
                ? (value[..colon], port)
                : (value, 42420);
        }
    }
}

/// <summary>
/// An engine-mode client against a dedicated server this process does not run, as in CI and
/// cloud environments.
/// </summary>
[Collection("Sequential")]
public class RemoteServerTests
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(180);

    private static HeadlessClient BootClient() =>
        HeadlessClientBootstrap.Boot(new HeadlessClientOptions { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 });

    [RemoteServerScenario]
    public async Task Offline_JoinsTheDedicatedServer()
    {
        using IDisposable gate = await ScenarioHostPool.EnterAsync(TimeSpan.FromMinutes(10));
        (string host, int port) = RemoteServer.Endpoint!.Value;
        using HeadlessClient client = BootClient();
        using RemoteServerSession session = client.ConnectRemote(host, port, ClientAuth.Offline("PharosRemote"));

        bool joined = await session.WaitForPlayerJoinedAsync(JoinTimeout);

        Assert.True(joined, $"The client never joined {host}:{port}. Disconnect reason: {session.DisconnectReason ?? "none"}");
        Assert.Equal("PharosRemote", client.RunOnClientThread(() => client.Client.player.PlayerName));
    }

    [RemoteServerScenario]
    public async Task Walking_MovesThePlayerOnTheDedicatedServer()
    {
        using IDisposable gate = await ScenarioHostPool.EnterAsync(TimeSpan.FromMinutes(10));
        (string host, int port) = RemoteServer.Endpoint!.Value;
        using HeadlessClient client = BootClient();
        using RemoteServerSession session = client.ConnectRemote(host, port, ClientAuth.Offline("PharosWalker"));
        Assert.True(await session.WaitForPlayerJoinedAsync(JoinTimeout), $"Disconnect reason: {session.DisconnectReason ?? "none"}");
        await session.StepFramesAsync(60);

        Vec3d start = client.RunOnClientThread(() => client.Client.EntityPlayer.Pos.XYZ.Clone());
        client.Controls.Press(PlayerAction.Forward);
        await session.StepFramesAsync(60);
        client.Controls.Release(PlayerAction.Forward);
        Vec3d end = client.RunOnClientThread(() => client.Client.EntityPlayer.Pos.XYZ.Clone());

        Assert.True(end.HorizontalSquareDistanceTo(start) > 1, "Holding forward did not walk the player");
    }
}
