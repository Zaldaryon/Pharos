using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Server;

internal static class ServerModNetworkHooks
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.server-mod-network");
    private static bool s_installed;

    internal static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            try
            {
                // Install before Launch so the JIT has not inlined these network boundaries.
                // Sends are postfixes: a send that throws records nothing.
                Patch(typeof(ServerMain), "SendArbitraryPacket", [typeof(Packet_Server), typeof(IServerPlayer[])], nameof(Sent), postfix: true);
                Patch(typeof(ServerMain), "BroadcastArbitraryPacket", [typeof(Packet_Server), typeof(IServerPlayer[])], nameof(Broadcast), postfix: true);
                Patch(typeof(ServerMain), "SendPacket", [typeof(ConnectedClient), typeof(Packet_UdpPacket)], nameof(SentUdp), postfix: true);
                Patch(typeof(NetworkChannel), "OnPacket", [typeof(Packet_CustomPacket), typeof(IServerPlayer)], nameof(ReceivedTcp));
                Patch(typeof(UdpNetworkChannel), "OnPacket", [typeof(Packet_CustomPacket), typeof(IServerPlayer)], nameof(ReceivedUdp));
                s_installed = true;
            }
            catch { s_harmony.UnpatchAll(s_harmony.Id); throw; }
        }
    }

    private static void Patch(Type type, string method, Type[] args, string hook, bool postfix = false)
    {
        MethodInfo target = AccessTools.DeclaredMethod(type, method, args)
            ?? throw new NotSupportedException($"The engine lacks {type.Name}.{method}; server mod recording is unavailable.");
        var patch = new HarmonyMethod(typeof(ServerModNetworkHooks), hook);
        if (postfix) s_harmony.Patch(target, postfix: patch);
        else s_harmony.Patch(target, prefix: patch);
    }

    private static void Sent(ServerMain __instance, Packet_Server packet, IServerPlayer[] players)
    {
        if (packet.Id != 55 || packet.CustomPacket is not { } custom || ServerModNetworkDriver.Of(__instance) is not { } driver) return;
        foreach (var player in players)
        {
            if (player == null || player.ConnectionState == EnumClientState.Offline) break;
            driver.Observe(player.ClientId, player, custom, false, ModMessageDirection.Sent);
        }
    }

    private static void Broadcast(ServerMain __instance, Packet_Server packet, IServerPlayer[]? skipPlayers)
    {
        if (packet.Id != 55 || packet.CustomPacket is not { } custom || ServerModNetworkDriver.Of(__instance) is not { } driver) return;
        // The prepared broadcast is disjoint from SendArbitraryPacket.
        foreach (var client in __instance.Clients.Values)
            if (client.State is not (EnumClientState.Offline or EnumClientState.Queued) &&
                !(skipPlayers?.Any(p => p?.ClientId == client.Id) ?? false))
                driver.Observe(client.Id, client.Player, custom, false, ModMessageDirection.Sent);
    }

    private static void SentUdp(ServerMain __instance, ConnectedClient client, Packet_UdpPacket packet)
    {
        if (packet.Id == 6 && packet.ChannelPacket is { } custom)
            ServerModNetworkDriver.Of(__instance)?.Observe(client.Id, client.Player, custom, true, ModMessageDirection.Sent);
        // A later TCP fallback is Id=79, not a second mod send.
    }

    private static void ReceivedTcp(NetworkChannel __instance, Packet_CustomPacket p, IServerPlayer player) =>
        ServerModNetworkDriver.Of(ServerModNetworkDriver.ServerOf(__instance))?.Observe(player.ClientId, player, p, false, ModMessageDirection.Received);

    private static void ReceivedUdp(UdpNetworkChannel __instance, Packet_CustomPacket packet, IServerPlayer player) =>
        ServerModNetworkDriver.Of(ServerModNetworkDriver.ServerOf(__instance))?.Observe(player.ClientId, player, packet, true, ModMessageDirection.Received);
}
