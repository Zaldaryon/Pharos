using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.Client;
using Vintagestory.Client.Network;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Network;

/// <summary>The client's mod network channels, read where the game keeps them.</summary>
internal static class ModChannels
{
    /// <summary>The network API of <paramref name="game"/>.</summary>
    public static NetworkAPI? Api(ClientMain game) => game.api?.Network as NetworkAPI;

    /// <summary>Every channel the client registered, TCP ones first.</summary>
    public static IEnumerable<NetworkChannel> All(NetworkAPI api) =>
        ClientChannels(api).Values.Cast<NetworkChannel>().Concat(ClientUdpChannels(api).Values);

    /// <summary>
    /// The channel a packet the client sends with <paramref name="channelId"/> belongs to. Once the
    /// server has listed its channels, a channel sends with the id the server gave it.
    /// </summary>
    public static NetworkChannel? ForSentId(NetworkAPI api, int channelId, bool udp) =>
        (udp ? ClientUdpChannels(api).Values : ClientChannels(api).Values.Where(c => c is not UdpNetworkChannel))
        .FirstOrDefault(c => c.Connected && Id(c) == channelId);

    public static string Name(NetworkChannelBase channel) => NameField(channel);

    public static int Id(NetworkChannelBase channel) => IdField(channel);

    public static Dictionary<Type, int> MessageTypes(NetworkChannelBase channel) => MessageTypesField(channel);

    /// <summary>The type registered for <paramref name="messageId"/> on <paramref name="channel"/>, or null.</summary>
    public static Type? TypeOf(NetworkChannelBase channel, int messageId) =>
        MessageTypes(channel).FirstOrDefault(pair => pair.Value == messageId).Key;

    /// <summary>Whether <paramref name="channel"/> has a handler for <paramref name="messageId"/>.</summary>
    public static bool HasHandler(NetworkChannel channel, int messageId)
    {
        Action<Packet_CustomPacket>[] handlers = channel is UdpNetworkChannel udp ? UdpHandlersField(udp) : HandlersField(channel);
        return messageId >= 0 && messageId < handlers.Length && handlers[messageId] != null;
    }

    public static ClientMain? GameOf(NetworkChannel channel) => ApiField(channel) is { } api ? GameField(api) : null;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "clientchannels")]
    private static extern ref Dictionary<int, NetworkChannel> ClientChannels(NetworkAPI api);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "clientchannelsUdp")]
    private static extern ref Dictionary<int, UdpNetworkChannel> ClientUdpChannels(NetworkAPI api);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "channelName")]
    private static extern ref string NameField(NetworkChannelBase channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "channelId")]
    private static extern ref int IdField(NetworkChannelBase channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "messageTypes")]
    private static extern ref Dictionary<Type, int> MessageTypesField(NetworkChannelBase channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "handlers")]
    private static extern ref Action<Packet_CustomPacket>[] HandlersField(NetworkChannel channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "handlersUdp")]
    private static extern ref Action<Packet_CustomPacket>[] UdpHandlersField(UdpNetworkChannel channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "api")]
    private static extern ref NetworkAPI ApiField(NetworkChannel channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "game")]
    private static extern ref ClientMain GameField(ClientSystem system);
}

/// <summary>
/// Records the mod messages a client sends and receives: where the game hands a channel packet to
/// its socket, and where a channel hands one to its handler.
/// </summary>
/// <remarks>
/// Installed when an engine-mode client boots, before it connects: the hooked methods are small
/// enough to be compiled into their callers otherwise. They record only for clients Pharos
/// registered, and never throw.
/// </remarks>
internal static class ModNetworkPatches
{
    private const int CustomPacketId = 23;
    private const int UdpOverTcpId = 35;
    private const int UdpChannelPacketId = 6;

    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.modnetwork");
    private static bool s_installed;

    // Set on the client thread while ModNetworkDriver delivers a message.
    [ThreadStatic]
    private static bool t_injecting;

    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;

            // A game version that renamed one leaves that hook out; the client still boots.
            Patch(AccessTools.Method(typeof(ClientMain), nameof(ClientMain.SendPacketClient)), nameof(BeforeSendPacketClient));
            Patch(AccessTools.Method(typeof(UdpNetClient), nameof(UdpNetClient.Send)), nameof(BeforeUdpSend));
            Patch(AccessTools.Method(typeof(DummyUdpNetClient), nameof(DummyUdpNetClient.Send)), nameof(BeforeDummyUdpSend));
            Patch(AccessTools.Method(typeof(NetworkChannel), nameof(NetworkChannel.OnPacket)), nameof(BeforeTcpHandled));
            Patch(AccessTools.DeclaredMethod(typeof(UdpNetworkChannel), nameof(UdpNetworkChannel.OnPacket)), nameof(BeforeUdpHandled));
            s_installed = true;
        }
    }

    private static void Patch(System.Reflection.MethodBase? target, string prefix)
    {
        if (target == null) return;
        try
        {
            s_harmony.Patch(target, prefix: new HarmonyMethod(typeof(ModNetworkPatches), prefix));
        }
        catch (Exception)
        {
            // Recording is a convenience: it never stops a client booting.
        }
    }

    /// <summary>Runs <paramref name="deliver"/> with what it hands a channel marked as injected.</summary>
    public static void Injecting(Action deliver)
    {
        t_injecting = true;
        try
        {
            deliver();
        }
        finally
        {
            t_injecting = false;
        }
    }

    /// <summary>Records a datagram Pharos's own linked socket is asked to send, before the link decides its fate.</summary>
    public static void LinkedUdpSending(UNetClient socket, Packet_UdpPacket packet) => RecordUdpSend(socket, packet);

    private static void BeforeSendPacketClient(ClientMain __instance, Packet_Client packetClient)
    {
        try
        {
            if (packetClient?.Id == CustomPacketId && packetClient.CustomPacket is { } custom)
            {
                RecordSent(__instance, custom, udp: false);
            }
            else if (packetClient?.Id == UdpOverTcpId && packetClient.UdpPacket is { Id: UdpChannelPacketId, ChannelPacket: { } channelPacket })
            {
                RecordSent(__instance, channelPacket, udp: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void BeforeUdpSend(UdpNetClient __instance, Packet_UdpPacket packet) => RecordUdpSend(__instance, packet);

    // Subclasses that call this as their base, Pharos's linked socket among them, record for themselves.
    private static void BeforeDummyUdpSend(DummyUdpNetClient __instance, Packet_UdpPacket packet)
    {
        if (__instance.GetType() == typeof(DummyUdpNetClient)) RecordUdpSend(__instance, packet);
    }

    private static void RecordUdpSend(UNetClient socket, Packet_UdpPacket packet)
    {
        try
        {
            if (packet is not { Id: UdpChannelPacketId, ChannelPacket: { } channelPacket }) return;
            ClientMain? game = ModNetworkLog.Clients().FirstOrDefault(c => ReferenceEquals(c.UdpNetClient, socket));
            if (game != null) RecordSent(game, channelPacket, udp: true);
        }
        catch (Exception)
        {
        }
    }

    private static void RecordSent(ClientMain game, Packet_CustomPacket packet, bool udp)
    {
        if (ModNetworkLog.Of(game) is not { } log || ModChannels.Api(game) is not { } api) return;
        NetworkChannel? channel = ModChannels.ForSentId(api, packet.ChannelId, udp);
        log.Add(ModMessageDirection.Sent, channel != null ? ModChannels.Name(channel) : $"#{packet.ChannelId}", udp, packet.MessageId,
            channel != null ? ModChannels.TypeOf(channel, packet.MessageId)?.FullName : null, packet.Data, injected: false);
    }

    private static void BeforeTcpHandled(NetworkChannel __instance, Packet_CustomPacket p) => RecordReceived(__instance, p, udp: false);

    private static void BeforeUdpHandled(UdpNetworkChannel __instance, Packet_CustomPacket packet) => RecordReceived(__instance, packet, udp: true);

    private static void RecordReceived(NetworkChannel channel, Packet_CustomPacket packet, bool udp)
    {
        try
        {
            if (packet == null || ModNetworkLog.Of(ModChannels.GameOf(channel)) is not { } log) return;
            log.Add(ModMessageDirection.Received, ModChannels.Name(channel), udp, packet.MessageId,
                ModChannels.TypeOf(channel, packet.MessageId)?.FullName, packet.Data, t_injecting);
        }
        catch (Exception)
        {
        }
    }
}
