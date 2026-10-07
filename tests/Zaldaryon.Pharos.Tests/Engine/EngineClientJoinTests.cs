using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests.Engine;

[Collection("Sequential")]
public class EngineClientJoinTests
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(120);

    private static ServerWorldOptions World(string name) => new()
    {
        WorldName = name,
        Seed = "1234",
        PlayStyle = "creativebuilding",
        WorldType = "superflat"
    };

    private static HeadlessClientOptions EngineOptions => new()
    {
        Width = 640,
        Height = 360,
        DisableAudio = true,
        BootMode = ClientBootMode.Engine
    };

    [Fact]
    public void Boot_EngineMode_StartsTheClientEngine()
    {
        using HeadlessClient client = HeadlessClientBootstrap.Boot(EngineOptions);

        Assert.True(client.IsEngineMode);
        Assert.False(client.IsJoined);

        // ClientMain.Start builds the full client system list; the fixture bootstrap installs one.
        Assert.True(client.Client.clientSystems.Length > 10, $"Expected the vanilla client systems, got {client.Client.clientSystems.Length}");
        Assert.NotNull(client.Client.networkProc);
    }

    [Fact]
    public void ConnectLoopback_EngineMode_JoinsAndSpawnsThePlayer()
    {
        using EmbeddedServerHost server = EmbeddedServerHost.Boot(World("PharosEngineJoin"));
        using HeadlessClient client = HeadlessClientBootstrap.Boot(EngineOptions);
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "EnginePilot");

        bool joined = session.WaitForPlayerJoined(JoinTimeout);

        Assert.True(joined, $"Client did not join within {JoinTimeout}. Disconnect reason: {client.Client.disconnectReason ?? "none"}");
        Assert.True(session.IsConnected);
        Assert.True(client.IsJoined);
        Assert.NotNull(client.Client.EntityPlayer);
        Assert.Equal("EnginePilot", client.Client.player.PlayerName);

        // The server sees the same player as a real, world-present entity that went through
        // character creation with the default class and is now playing.
        var serverClient = Assert.Single(server.Server.Clients.Values, c => c.PlayerName == "EnginePilot");
        Assert.NotNull(serverClient.Entityplayer);
        Assert.Equal("commoner", serverClient.Entityplayer.WatchedAttributes.GetString("characterClass"));
        session.StepFrames(5);
        Assert.Equal(EnumClientState.Playing, serverClient.State);
    }

    [Fact]
    public void ConnectLoopback_EngineMode_SelectsTheConfiguredCharacterClass()
    {
        using EmbeddedServerHost server = EmbeddedServerHost.Boot(World("PharosEngineClass"));
        using HeadlessClient client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions
        {
            Width = 640,
            Height = 360,
            DisableAudio = true,
            BootMode = ClientBootMode.Engine,
            CharacterClass = "hunter"
        });
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "HunterPilot");

        Assert.True(session.WaitForPlayerJoined(JoinTimeout), "Client did not join");
        session.StepFrames(5);

        var serverClient = Assert.Single(server.Server.Clients.Values, c => c.PlayerName == "HunterPilot");
        Assert.Equal("hunter", serverClient.Entityplayer.WatchedAttributes.GetString("characterClass"));
        Assert.Equal("hunter", client.Client.EntityPlayer.WatchedAttributes.GetString("characterClass"));
    }

    [Fact]
    public void ConnectLoopback_EngineModeWithoutCharacterSelection_WaitsOnTheDialog()
    {
        using EmbeddedServerHost server = EmbeddedServerHost.Boot(World("PharosEngineNoSelect"));
        using HeadlessClient client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions
        {
            Width = 640,
            Height = 360,
            DisableAudio = true,
            BootMode = ClientBootMode.Engine,
            CompleteCharacterSelection = false
        });
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "WaitingPilot");

        // The player spawns, but without a character the client never reports ready.
        Assert.False(session.WaitForPlayerJoined(TimeSpan.FromSeconds(45)));
        Assert.NotNull(client.Client.EntityPlayer);
        Assert.True(client.Client.BlocksReceivedAndLoaded);
        Assert.Contains(client.Client.api.Gui.OpenedGuis, g => g.GetType().Name == "GuiDialogCreateCharacter");
    }

    [Fact]
    public async Task ConnectLoopback_EngineMode_MeshesTheSpawnAndRendersAFrame()
    {
        using EmbeddedServerHost server = EmbeddedServerHost.Boot(World("PharosEngineRender"));
        using HeadlessClient client = HeadlessClientBootstrap.Boot(EngineOptions);
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "RenderPilot");

        Assert.True(session.WaitForPlayerJoined(JoinTimeout), "Client did not join");

        ChunkPos spawnChunk = ChunkPos.FromBlockPos(client.Client.EntityPlayer.Pos.AsBlockPos);
        bool meshed = await session.WaitForChunkMeshedAsync(spawnChunk, maxFrames: 3000);
        Assert.True(meshed);

        // Look down at the superflat ground so the frame cannot be sky alone.
        client.TestPlayer.Camera.SetOrientation(0f, -1.2f, 0f);
        await session.StepFramesAsync(30);

        FramebufferSnapshot frame = client.CaptureFrame();
        Assert.Equal(640, frame.Width);
        Assert.Equal(360, frame.Height);
        Assert.True(DistinctColors(frame) > 16, "Rendered frame is blank or a flat color");
    }

    private static int DistinctColors(FramebufferSnapshot frame)
    {
        HashSet<int> colors = [];
        byte[] rgba = frame.RawRgba;
        for (int i = 0; i < rgba.Length; i += 4 * 97)
        {
            colors.Add(rgba[i] << 16 | rgba[i + 1] << 8 | rgba[i + 2]);
        }

        return colors.Count;
    }
}
