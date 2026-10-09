using Vintagestory.API.Datastructures;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>
/// An engine-mode client that leaves the server and joins again in the same process, with
/// <c>TestMods/pharosreconnectmod</c> counting its client side's starts, disposals and handler
/// calls. The counts are process-wide, so each test compares them with where it began.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosreconnectmod")]
public class LiveReconnectTests : ClientServerScenarioBase
{
    private const string Channel = "pharosreconnect";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task AfterAKick_TheClientJoinsAgain_AndItsModsStartOnce()
    {
        Counts before = await ReadCounts();
        long mark = Client!.ModNetwork.Mark();

        Session!.DisconnectSimulator.SimulateKick("bye");
        Assert.True(await Session.StepUntilAsync(() => Client.RunOnClientThread(() => Client.Client.disconnectReason)?.Contains("bye") == true, 120));
        Assert.False(Client.IsJoined);

        await Session.ReconnectAsync();

        Assert.True(Client.IsJoined);
        Assert.True(Session.IsConnected);
        Assert.False(Session.IsLinkSevered);
        Assert.False(Client.DisconnectSimulator.IsDisconnected);
        Assert.Equal((1, 2), (Session.Reconnects, Client.SessionNumber));

        Counts after = await ReadCounts();
        Assert.Equal(1, after.Started - before.Started);
        Assert.Equal(1, after.Disposed - before.Disposed);
        Assert.Equal(1, after.Active);
        Assert.NotEqual(before.Instance, after.Instance);

        // The server greets the player again, and the log goes on across the reconnect.
        Assert.True(await Session.StepUntilAsync(() => ReadCountsNow().Hellos - before.Hellos == 1, 120));
        Assert.Single(Client.ModNetwork.Messages(Channel), m => m.Sequence > mark && m.Direction == ModMessageDirection.Received);

        Assert.Equal(1, ServerHost!.RunOnGameThread(() => Server!.Clients.Values.Count(c => c.PlayerName == PlayerName)));
        Assert.True(await Session.WaitForWorldReadyAsync(1));
    }

    [ClientServerScenario]
    public async Task TwoReconnects_LeaveOneLiveInstance_AndEachHandlerRunsOnce()
    {
        Counts before = await ReadCounts();

        Session!.DisconnectSimulator.SimulateKick("first");
        await Session.ReconnectAsync();
        await Session.ReconnectAsync();

        Counts after = await ReadCounts();
        Assert.Equal((2, 2, 1), (after.Started - before.Started, after.Disposed - before.Disposed, after.Active));
        Assert.Equal(3, Client!.SessionNumber);

        // The event bus belongs to each game, the hotkey and the settings watcher to the process:
        // each must reach the one live instance once.
        Client.RunOnClientThread(() => Client.Client.api.Event.PushEvent("pharos:reconnect:ping", new TreeAttribute()));
        await Client.Hotkeys.TriggerAsync("pharosreconnect");
        object frequency = Client.Settings.Get("musicFrequency")!;
        using (Client.Settings.Apply(ClientSettingsProfile.Of("reconnect", ("musicFrequency", Convert.ToInt32(frequency) == 1 ? 2 : 1))))
        {
        }

        Counts fired = await ReadCounts();
        Assert.Equal(1, fired.Pings - after.Pings);
        Assert.Equal(1, fired.Hotkeys - after.Hotkeys);

        // The game keeps one handler per hotkey: it must be the live instance's, not a disposed one's.
        Assert.Equal(fired.Instance, fired.HotkeyInstance);
        Assert.Equal(2, fired.Watched - after.Watched);
    }

    [ClientServerScenario]
    public async Task AfterALostConnection_TheClientJoinsOverAFreshLink_AndMoves()
    {
        Session!.DisconnectSimulator.SimulateNetworkError("cut");
        Assert.True(Session.IsLinkSevered);

        await Session.ReconnectAsync();

        Assert.True(Client!.IsJoined);
        Assert.False(Session.IsLinkSevered);
        Assert.True(await Session.WaitForWorldReadyAsync(1));

        // The server moving the player proves both its TCP and UDP reach the new connection.
        double start = ServerPlayerX();
        await Session.HoldAsync(PlayerAction.Forward, 40);
        await Session.StepFramesAsync(10);
        Assert.NotEqual(start, ServerPlayerX(), 3);
    }

    [ClientServerScenario]
    public async Task AfterTheClientLeaves_ItJoinsAgain()
    {
        Counts before = await ReadCounts();
        Session!.Disconnect();
        Assert.False(Client!.IsJoined);

        await Session.ReconnectAsync();

        Assert.True(Client.IsJoined);
        Counts after = await ReadCounts();
        Assert.Equal((1, 1), (after.Started - before.Started, after.Disposed - before.Disposed));
        Assert.Equal(1, ServerHost!.RunOnGameThread(() => Server!.Clients.Values.Count(c => c.PlayerName == PlayerName)));
    }

    [ClientServerScenario]
    public async Task WhileJoined_ReconnectLeavesAndComesBack_WithTheLogsOfTheNewSession()
    {
        Counts before = await ReadCounts();
        Client!.Logs.Clear();

        await Session!.ReconnectAsync();

        Assert.True(Client.IsJoined);
        Counts after = await ReadCounts();
        Assert.Equal((1, 1, 1), (after.Started - before.Started, after.Disposed - before.Disposed, after.Active));

        // The log capture is attached to the new game once, not lost with the old one or doubled.
        Assert.Single(Client.Logs.Entries, e => e.Message.Contains("Loading and pre-starting client side mods"));
    }

    [ClientServerScenario]
    public async Task Reconnect_KeepsTheNetworkConditionsAndTheRecording()
    {
        Client!.NetworkDegradation.Configure(new DegradedNetworkProfile(LatencyMs: 50));
        Client.PacketRecorder.Start();
        try
        {
            await Session!.ReconnectAsync();

            Assert.True(Client.IsJoined);
            Assert.Contains(Client.PacketRecorder.GetRecordedPackets(), p => p.Direction == PacketDirection.Outbound);
        }
        finally
        {
            Client.PacketRecorder.Stop();
            Client.PacketRecorder.Clear();
            Client.NetworkDegradation.Configure(DegradedNetworkProfile.None);
        }
    }

    private double ServerPlayerX() => ServerHost!.RunOnGameThread(() =>
        ((ServerMain)Server!).Clients.Values.Single(c => c.PlayerName == PlayerName).Entityplayer.Pos.X);

    private readonly record struct Counts(int Instance, int Active, int Started, int Disposed, int Hellos, int Pings, int Hotkeys, int Watched, int HotkeyInstance = 0);

    private async Task<Counts> ReadCounts() => Parse((await Client!.Commands.ExecuteSuccessAsync(".pharosreconnect counts")).Message!);

    private Counts ReadCountsNow() => Client!.RunOnClientThread(() =>
    {
        int[] counts = (int[])AppDomain.CurrentDomain.GetData("pharosreconnect:counts")!;
        lock (counts) return new Counts(0, counts[0] - counts[1], counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]);
    });

    private static Counts Parse(string text)
    {
        Dictionary<string, int> values = text.Split(' ').Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        return new Counts(values["instance"], values["active"], values["started"], values["disposed"], values["hellos"], values["pings"], values["hotkeys"], values["watched"], values["hotkeyinstance"]);
    }
}

/// <summary>What <see cref="ClientServerLoopbackSession.ReconnectAsync"/> refuses, without booting anything.</summary>
public class ReconnectGuardTests
{
    [Fact]
    public async Task ADisposedSession_RefusesToReconnect()
    {
        ClientServerLoopbackSession session = (ClientServerLoopbackSession)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ClientServerLoopbackSession));
        typeof(ClientServerLoopbackSession).GetField("_disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session, true);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReconnectAsync());
    }
}
