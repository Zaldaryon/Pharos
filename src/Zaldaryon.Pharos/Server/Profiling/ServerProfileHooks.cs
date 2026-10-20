using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The game hooks <see cref="ServerProfiler"/> reads. They are installed when a measurement opens
/// and removed when it closes, and each calls the game's method unchanged when no measurement is
/// open on this thread.
/// </summary>
internal static class ServerProfileHooks
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.server-profile");
    private static readonly List<MethodBase> s_patched = [];
    private static bool s_installed;

    private static readonly MethodInfo s_entityTick = AccessTools.Method(typeof(Entity), nameof(Entity.OnGameTick));
    private static readonly MethodInfo s_timedEntityTick = AccessTools.Method(typeof(ServerProfileHooks), nameof(TimedEntityTick));
    private static readonly MethodInfo s_behaviorTick = AccessTools.Method(typeof(EntityBehavior), nameof(EntityBehavior.OnGameTick));
    private static readonly MethodInfo s_timedBehaviorTick = AccessTools.Method(typeof(ServerProfileHooks), nameof(TimedBehaviorTick));
    private static readonly MethodInfo s_entityCallback = typeof(Action<float>).GetMethod(nameof(Action.Invoke))!;
    private static readonly MethodInfo s_timedEntityCallback =
        AccessTools.Method(typeof(ServerProfileHooks), nameof(TimedEntityCallback));
    private static readonly MethodInfo s_blockCallback = typeof(Action<IWorldAccessor, BlockPos, float>).GetMethod(nameof(Action.Invoke))!;
    private static readonly MethodInfo s_timedBlockCallback =
        AccessTools.Method(typeof(ServerProfileHooks), nameof(TimedBlockCallback));

    private static readonly FieldInfo? s_receivedClient = typeof(ReceivedClientPacket).GetField("client",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_receivedPacket = typeof(ReceivedClientPacket).GetField("packet",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Installs the hooks; call on the game thread, before the first measured tick.</summary>
    public static void Install(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (s_lock)
        {
            if (s_installed) return;

            // The listener and server-system hooks are installed when the server boots, because the
            // automatic tick collector uses them too; this measurement just feeds them.
            MeasurementHooks.InstallServer();

            MethodInfo? tickEntities = AccessTools.Method(typeof(ServerSystemEntitySimulation), "TickEntities");
            if (tickEntities != null)
            {
                Patch(tickEntities, transpiler: nameof(TimeEntityTicks));
            }

            Patch(s_entityTick, transpiler: nameof(TimeBehaviorTicks));
            foreach (string name in new[] { nameof(EventManager.TriggerGameTick), nameof(EventManager.TriggerGameTickDebug) })
            {
                MethodInfo? trigger = AccessTools.Method(typeof(EventManager), name);
                if (trigger != null) Patch(trigger, transpiler: nameof(TimeCallbacks));
            }

            PatchAiTasks();

            Patch(AccessTools.Method(typeof(ServerMain), "SendPacket", [typeof(int), typeof(byte[])]),
                prefix: nameof(SentBytes));
            Patch(AccessTools.Method(typeof(ServerMain), "SendPacket", [typeof(int), typeof(Packet_Server)]),
                prefix: nameof(SentPacket));
            Patch(AccessTools.Method(typeof(ServerMain), "BroadcastArbitraryPacket",
                    [typeof(Packet_Server), typeof(IServerPlayer[])]),
                prefix: nameof(BroadcastSent));
            Patch(AccessTools.Method(typeof(ServerMain), "HandleSendingResult",
                    [typeof(EnumSendResult), typeof(int), typeof(ConnectedClient)]),
                postfix: nameof(SentBoxed));
            Patch(AccessTools.Method(typeof(ServerMain), "ProcessNetMessage", [typeof(NetIncomingMessage)]),
                postfix: nameof(ReceivedMessage));
            Patch(AccessTools.Method(typeof(ServerMain), "HandleClientPacket_mainthread", [typeof(ReceivedClientPacket)]),
                postfix: nameof(ReceivedPacket));
            Patch(AccessTools.Method(typeof(NetworkChannel), nameof(NetworkChannel.OnPacket),
                    [typeof(Packet_CustomPacket), typeof(IServerPlayer)]),
                prefix: nameof(ReceivedChannel));

            s_installed = true;
        }
    }

    /// <summary>Removes every hook; call on the game thread, after the last measured tick.</summary>
    public static void Remove()
    {
        lock (s_lock)
        {
            if (!s_installed) return;
            foreach (MethodBase original in s_patched)
            {
                s_harmony.Unpatch(original, HarmonyPatchType.All, s_harmony.Id);
            }

            s_patched.Clear();
            s_installed = false;
        }
    }

    private static void Patch(MethodInfo? original, string? prefix = null, string? postfix = null, string? transpiler = null)
    {
        if (original == null) return;
        s_harmony.Patch(original,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(ServerProfileHooks), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(ServerProfileHooks), postfix),
            transpiler: transpiler == null ? null : new HarmonyMethod(typeof(ServerProfileHooks), transpiler));
        s_patched.Add(original);
    }

    private static void PatchAiTasks()
    {
        Type? taskInterface = AccessTools.TypeByName("Vintagestory.API.Common.IAiTask");
        if (taskInterface == null) return;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.OfType<Type>()];
            }
            catch (Exception ex) when (ex is NotSupportedException or FileNotFoundException or TypeLoadException)
            {
                continue;
            }

            foreach (Type type in types)
            {
                if (type.IsAbstract || type.IsInterface || !taskInterface.IsAssignableFrom(type)) continue;
                foreach (string name in new[] { "ShouldExecute", "ContinueExecute" })
                {
                    MethodInfo? method = type.GetMethod(name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (method == null || method.IsAbstract) continue;
                    Patch(method, prefix: nameof(AiTaskStarting), postfix: nameof(AiTaskDone));
                }
            }
        }
    }

    // ---- tick and entity ----------------------------------------------------------------

    private static IEnumerable<CodeInstruction> TimeEntityTicks(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_entityTick))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedEntityTick)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static IEnumerable<CodeInstruction> TimeBehaviorTicks(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_behaviorTick))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedBehaviorTick)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static IEnumerable<CodeInstruction> TimeCallbacks(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_entityCallback))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedEntityCallback)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else if (instruction.Calls(s_blockCallback))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedBlockCallback)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static void TimedEntityTick(Entity entity, float dt)
    {
        if (ServerProfiler.Active is not { IsGameThread: true } profiler)
        {
            entity.OnGameTick(dt);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            entity.OnGameTick(dt);
        }
        finally
        {
            profiler.AddEntity(entity, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedBehaviorTick(EntityBehavior behavior, float dt)
    {
        if (ServerProfiler.Active is not { IsGameThread: true } profiler)
        {
            behavior.OnGameTick(dt);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            behavior.OnGameTick(dt);
        }
        finally
        {
            profiler.AddBehavior(behavior, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedEntityCallback(Action<float> handler, float elapsed)
    {
        if (ServerProfiler.Active is not { IsGameThread: true } profiler)
        {
            handler(elapsed);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            handler(elapsed);
        }
        finally
        {
            profiler.AddCallback(handler, null, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedBlockCallback(Action<IWorldAccessor, BlockPos, float> handler, IWorldAccessor world,
        BlockPos pos, float elapsed)
    {
        if (ServerProfiler.Active is not { IsGameThread: true } profiler)
        {
            handler(world, pos, elapsed);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            handler(world, pos, elapsed);
        }
        finally
        {
            profiler.AddCallback(handler, pos, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void AiTaskStarting(object __instance, out long __state)
    {
        if (ServerProfiler.Active is not { IsGameThread: true } profiler)
        {
            __state = 0;
            return;
        }

        profiler.LearnAiTask(__instance.GetType());
        __state = Stopwatch.GetTimestamp();
    }

    private static void AiTaskDone(object __instance, long __state)
    {
        if (__state != 0) ServerProfiler.Active?.AddAiTask(__instance, Stopwatch.GetElapsedTime(__state).TotalMilliseconds);
    }

    // ---- network --------------------------------------------------------------------------

    private static void SentBytes(int clientId, byte[] packetBytes) =>
        ServerProfiler.Active?.SentBytes(clientId, packetBytes.Length);

    private static void SentPacket(int clientId, Packet_Server packet, ServerMain __instance)
    {
        if (ServerProfiler.Active is not { } profiler) return;
        if (!__instance.Clients.TryGetValue(clientId, out ConnectedClient? client)) return;
        profiler.SentPacket(client, packet);
    }

    // A broadcast serializes once and sends the prepared bytes to every client, so it never reaches
    // SendPacket(int, Packet_Server); attribute it to each recipient here.
    private static void BroadcastSent(Packet_Server packet, IServerPlayer[] skipPlayers, ServerMain __instance)
    {
        if (ServerProfiler.Active is not { } profiler) return;
        foreach (ConnectedClient client in __instance.Clients.Values)
        {
            if (client.State is EnumClientState.Offline or EnumClientState.Queued) continue;
            if (skipPlayers != null && skipPlayers.Any(player => player?.ClientId == client.Id)) continue;
            profiler.SentPacket(client, packet);
        }
    }

    private static void SentBoxed(EnumSendResult result, int lengthSent, ConnectedClient client)
    {
        if (result == EnumSendResult.Ok) ServerProfiler.Active?.SentBoxed(client, lengthSent);
    }

    private static void ReceivedMessage(NetIncomingMessage msg)
    {
        if (ServerProfiler.Active is not { } profiler) return;
        if (msg.Type != NetworkMessageType.Data) return;
        if (msg.SenderConnection?.client is not { } client) return;
        profiler.ReceivedMessage(client, msg.messageLength);
    }

    private static void ReceivedPacket(ReceivedClientPacket cpk)
    {
        if (ServerProfiler.Active is not { } profiler) return;
        if (s_receivedClient?.GetValue(cpk) is not ConnectedClient client) return;
        if (s_receivedPacket?.GetValue(cpk) is not Packet_Client packet) return;
        profiler.ReceivedPacket(client, packet.Id);
    }

    private static void ReceivedChannel(Packet_CustomPacket p, IServerPlayer player) =>
        ServerProfiler.Active?.ReceivedChannel(player, p);
}
