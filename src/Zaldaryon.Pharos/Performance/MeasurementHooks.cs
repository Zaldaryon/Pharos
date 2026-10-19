using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Performance;

/// <summary>
/// The game hooks frame measurements read from. Each does nothing unless a measurement is open on
/// the thread it runs on, and none changes what the game does.
/// </summary>
internal static class MeasurementHooks
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.performance");
    private static bool s_clientInstalled;
    private static bool s_serverInstalled;

    private static readonly MethodInfo s_onRenderFrame = AccessTools.Method(typeof(IRenderer), nameof(IRenderer.OnRenderFrame));
    private static readonly MethodInfo s_timedRender = AccessTools.Method(typeof(MeasurementHooks), nameof(TimedRender));
    private static readonly MethodInfo s_sleep = AccessTools.Method(typeof(Thread), nameof(Thread.Sleep), [typeof(int)]);
    private static readonly MethodInfo s_timedSleep = AccessTools.Method(typeof(MeasurementHooks), nameof(TimedSleep));
    private static readonly MethodInfo s_onServerTick = AccessTools.Method(typeof(ServerSystem), nameof(ServerSystem.OnServerTick));
    private static readonly MethodInfo s_timedServerSystemTick = AccessTools.Method(typeof(MeasurementHooks), nameof(TimedServerSystemTick));
    private static readonly MethodInfo s_tickListenerTriggered = AccessTools.Method(typeof(GameTickListener), nameof(GameTickListener.OnTriggered));
    private static readonly MethodInfo s_timedTickListener = AccessTools.Method(typeof(MeasurementHooks), nameof(TimedTickListener));
    private static readonly MethodInfo s_blockListenerTriggered = AccessTools.Method(typeof(GameTickListenerBlock), nameof(GameTickListenerBlock.OnTriggered));
    private static readonly MethodInfo s_timedBlockListener = AccessTools.Method(typeof(MeasurementHooks), nameof(TimedTickListenerBlock));

    /// <summary>Installs the client hooks; called when an engine-mode client boots, before its first frame.</summary>
    public static void InstallClient()
    {
        lock (s_lock)
        {
            if (s_clientInstalled) return;
            MethodInfo trigger = AccessTools.Method(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage));
            s_harmony.Patch(trigger,
                prefix: new HarmonyMethod(typeof(MeasurementHooks), nameof(StageStarting)),
                postfix: new HarmonyMethod(typeof(MeasurementHooks), nameof(StageDone)),
                transpiler: new HarmonyMethod(typeof(MeasurementHooks), nameof(TimeEachRenderer)));
            foreach (string name in new[] { nameof(EventManager.TriggerGameTick), nameof(EventManager.TriggerGameTickDebug) })
            {
                s_harmony.Patch(AccessTools.Method(typeof(EventManager), name),
                    prefix: new HarmonyMethod(typeof(MeasurementHooks), nameof(PartStarting)),
                    postfix: new HarmonyMethod(typeof(MeasurementHooks), nameof(GameTickDone)));
            }

            s_harmony.Patch(AccessTools.Method(typeof(ClientMain), nameof(ClientMain.ExecuteMainThreadTasks)),
                prefix: new HarmonyMethod(typeof(MeasurementHooks), nameof(PartStarting)),
                postfix: new HarmonyMethod(typeof(MeasurementHooks), nameof(MainThreadTasksDone)));
            s_clientInstalled = true;
        }
    }

    /// <summary>Installs the server hooks; called when an embedded server boots, before its first tick.</summary>
    public static void InstallServer()
    {
        lock (s_lock)
        {
            if (s_serverInstalled) return;
            s_harmony.Patch(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Process)),
                prefix: new HarmonyMethod(typeof(MeasurementHooks), nameof(ServerTickStarting)),
                postfix: new HarmonyMethod(typeof(MeasurementHooks), nameof(ServerTickDone)),
                transpiler: new HarmonyMethod(typeof(MeasurementHooks), nameof(TimeServerProcess)));
            foreach (string name in new[] { nameof(EventManager.TriggerGameTick), nameof(EventManager.TriggerGameTickDebug) })
            {
                if (AccessTools.Method(typeof(EventManager), name) is { } trigger)
                {
                    s_harmony.Patch(trigger,
                        transpiler: new HarmonyMethod(typeof(MeasurementHooks), nameof(TimeEachTickListener)));
                }
            }

            s_serverInstalled = true;
        }
    }

    // Each renderer call in the stage loop goes through TimedRender, in both of the game's branches.
    private static IEnumerable<CodeInstruction> TimeEachRenderer(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_onRenderFrame))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedRender).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static void TimedRender(IRenderer renderer, float deltaTime, EnumRenderStage stage)
    {
        if (FrameCollector.Active is not { } collector)
        {
            renderer.OnRenderFrame(deltaTime, stage);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            renderer.OnRenderFrame(deltaTime, stage);
        }
        finally
        {
            collector.Renderer(renderer, stage, Stopwatch.GetTimestamp() - start);
        }
    }

    private static void StageStarting(out long __state) => __state = FrameCollector.Active != null ? Stopwatch.GetTimestamp() : 0;

    private static void StageDone(EnumRenderStage stage, long __state)
    {
        if (__state != 0) FrameCollector.Active?.Stage(stage, Stopwatch.GetTimestamp() - __state);
    }

    private static void PartStarting(out long __state) => __state = FrameCollector.Active != null ? Stopwatch.GetTimestamp() : 0;

    private static void GameTickDone(long __state)
    {
        if (__state != 0) FrameCollector.Active?.GameTick(Stopwatch.GetTimestamp() - __state);
    }

    private static void MainThreadTasksDone(long __state)
    {
        if (__state != 0) FrameCollector.Active?.MainThreadTasks(Stopwatch.GetTimestamp() - __state);
    }

    // The server sleeps out the rest of its tick inside Process: its work ends where it sleeps.
    // Each system's tick is wrapped so a measurement can break the tick down by system.
    private static IEnumerable<CodeInstruction> TimeServerProcess(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_sleep))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedSleep).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else if (instruction.Calls(s_onServerTick))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedServerSystemTick).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    // Each game-tick listener's callback is wrapped so a measurement can break the tick down by mod.
    private static IEnumerable<CodeInstruction> TimeEachTickListener(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(s_tickListenerTriggered))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedTickListener).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else if (instruction.Calls(s_blockListenerTriggered))
            {
                yield return new CodeInstruction(OpCodes.Call, s_timedBlockListener).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static void TimedServerSystemTick(ServerSystem system, float deltaTime)
    {
        if (ServerTickCollector.Active is not { } collector)
        {
            system.OnServerTick(deltaTime);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            system.OnServerTick(deltaTime);
        }
        finally
        {
            collector.RecordSystemTick(system.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedTickListener(GameTickListener listener, long elapsedMilliseconds)
    {
        if (ServerTickCollector.Active is not { } collector)
        {
            listener.OnTriggered(elapsedMilliseconds);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            listener.OnTriggered(elapsedMilliseconds);
        }
        finally
        {
            collector.RecordListenerTick(listener.ProfilerName ?? "unknown", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedTickListenerBlock(GameTickListenerBlock listener, IWorldAccessor world, BlockPos pos,
        long elapsedMilliseconds)
    {
        if (ServerTickCollector.Active is not { } collector)
        {
            listener.OnTriggered(world, pos, elapsedMilliseconds);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            listener.OnTriggered(world, pos, elapsedMilliseconds);
        }
        finally
        {
            collector.RecordListenerTick(listener.ProfilerName ?? "unknown", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static void TimedSleep(int milliseconds)
    {
        ServerTickCollector.TickWorkDone();
        Thread.Sleep(milliseconds);
    }

    private static void ServerTickStarting(ServerMain __instance) => ServerTickCollector.TickStarting(__instance.Suspended);

    private static void ServerTickDone() => ServerTickCollector.TickWorkDone();
}
