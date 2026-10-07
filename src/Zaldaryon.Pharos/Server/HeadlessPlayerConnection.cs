using System.Reflection;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// One headless player's connection to an embedded server: an in-memory socket of its own and the
/// client packets of the multiplayer handshake. There is no client engine behind it.
/// </summary>
/// <remarks>
/// <para>
/// The server reads every installed socket of <c>ServerMain.MainSockets</c> each pass. An
/// in-memory TCP socket carries exactly one connection, so each player gets its own slot. Slot 0
/// belongs to the host's loopback socket and slot 1 is where the engine puts its real TCP
/// listener, so players take the first free slot from 2 on, growing the array when needed.
/// </para>
/// <para>
/// UDP is different: the engine casts <c>UdpSockets[0]</c> to the in-memory UDP server for every
/// connection that arrives over an in-memory socket, so all players share the host's.
/// </para>
/// <para>
/// A connection over an in-memory socket counts as local, and the server skips player
/// verification for local connections, so no auth server is involved.
/// </para>
/// </remarks>
internal sealed class HeadlessPlayerConnection
{
    private const int FirstPlayerSlot = 2;

    private readonly DummyTcpNetClient _client;
    private readonly DummyNetwork _network;

    private HeadlessPlayerConnection(DummyTcpNetClient client, DummyTcpNetServer socket, DummyNetwork network, int slot)
    {
        _client = client;
        Socket = socket;
        _network = network;
        Slot = slot;
    }

    /// <summary>The server-side socket installed for this player.</summary>
    public DummyTcpNetServer Socket { get; }

    /// <summary>The <c>MainSockets</c> index the socket occupies.</summary>
    public int Slot { get; }

    /// <summary>
    /// Installs a socket for the player and queues the opening handshake: the login token query,
    /// then the identification. The server processes them on its next ticks.
    /// </summary>
    /// <remarks>Runs on the server's game thread.</remarks>
    public static HeadlessPlayerConnection Open(ServerMain server, string playerName, string playerUid)
    {
        DummyNetwork network = new();
        network.Start();

        DummyTcpNetServer socket = new();
        socket.SetNetwork(network);

        DummyTcpNetClient client = new();
        client.SetNetwork(network);

        int slot = InstallSocket(server, socket);
        HeadlessPlayerConnection connection = new(client, socket, network, slot);

        // The server treats the first queued message as the connection itself, so queueing the
        // token query both connects and starts the handshake, exactly like a real client.
        connection.Send(new Packet_Client { Id = 33, LoginTokenQuery = new Packet_LoginTokenQuery() });
        connection.Send(new Packet_Client
        {
            Id = 1,
            Identification = new Packet_ClientIdentification
            {
                Playername = playerName,
                PlayerUID = playerUid,
                MdProtocolVersion = "1.0",
                ViewDistance = 128,
                RenderMetaBlocks = 0,
                NetworkVersion = EngineVersion.NetworkVersion,
                ShortGameVersion = EngineVersion.ShortGameVersion,
            },
        });

        return connection;
    }

    /// <summary>
    /// Asks to join the world once the player entity exists. The server answers by setting up
    /// the player's inventories and sending the world to the client.
    /// </summary>
    public void RequestJoin(string language = "en") =>
        Send(new Packet_Client { Id = 11, RequestJoin = new Packet_ClientRequestJoin { Language = language } });

    /// <summary>
    /// Reports the client as loaded and ready, which moves the player to the playing state, the
    /// same two packets a real client sends once its own player data arrives.
    /// </summary>
    public void ReportLoadedAndReady()
    {
        Send(new Packet_Client { Id = 26 });
        Send(new Packet_Client { Id = 29 });
    }

    /// <summary>
    /// Sends a chat line on the general chat group, as the chat box does. A leading slash makes
    /// the server run it as a command.
    /// </summary>
    public void Chat(string message) =>
        Send(new Packet_Client
        {
            Id = 4,
            Chatline = new Packet_ChatLine { Message = message, Groupid = GlobalConstants.GeneralChatGroup },
        });

    /// <summary>
    /// Tells the server the player is leaving, as a real client does when it quits.
    /// </summary>
    public void Leave() => Send(new Packet_Client { Id = 14, Leave = new Packet_ClientLeave { Reason = 0 } });

    /// <summary>
    /// Takes every packet the server has sent this player so far.
    /// </summary>
    public List<byte[]> DrainReceived()
    {
        List<byte[]> packets = [];
        while (_client.ReadMessage() is { } message)
        {
            byte[] data = new byte[message.messageLength];
            Array.Copy(message.message, data, message.messageLength);
            packets.Add(data);
        }

        return packets;
    }

    /// <summary>
    /// Removes the player's socket, unless the slot has been handed to someone else since.
    /// </summary>
    /// <remarks>Runs on the server's game thread.</remarks>
    public void Close(ServerMain server)
    {
        NetServer?[] sockets = server.MainSockets;
        if (Slot < sockets.Length && ReferenceEquals(sockets[Slot], Socket))
        {
            sockets[Slot] = null;
        }
    }

    private void Send(Packet_Client packet)
    {
        CitoMemoryStream stream = new();
        Packet_ClientSerializer.Serialize(stream, packet);

        // ToArray hands back the stream's whole growable buffer, not just what was written.
        byte[] data = new byte[stream.Position()];
        Array.Copy(stream.ToArray(), data, data.Length);
        _client.Send(data);
    }

    private static int InstallSocket(ServerMain server, DummyTcpNetServer socket)
    {
        NetServer?[] sockets = server.MainSockets;
        for (int i = FirstPlayerSlot; i < sockets.Length; i++)
        {
            if (sockets[i] == null)
            {
                sockets[i] = socket;
                return i;
            }
        }

        // The packet parser reads the property again on every pass, so a grown copy is picked up.
        int slot = Math.Max(sockets.Length, FirstPlayerSlot);
        NetServer?[] grown = new NetServer?[slot + 1];
        Array.Copy(sockets, grown, sockets.Length);
        grown[slot] = socket;
        server.MainSockets = grown!;
        return slot;
    }

    /// <summary>
    /// The versions the server checks in the identification, read from the engine that is actually
    /// loaded rather than from constants compiled into Pharos: the server rejects a network version
    /// mismatch outright.
    /// </summary>
    private static class EngineVersion
    {
        public static string NetworkVersion { get; } = Read(nameof(GameVersion.NetworkVersion));
        public static string ShortGameVersion { get; } = Read(nameof(GameVersion.ShortGameVersion));

        private static string Read(string name) =>
            typeof(GameVersion).GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
            ?? throw new MissingFieldException(nameof(GameVersion), name);
    }
}
