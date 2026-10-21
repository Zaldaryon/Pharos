using System.Collections.Concurrent;
using HarmonyLib;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Items;

/// <summary>The held-item hook a call belongs to.</summary>
public enum HeldHook
{
    /// <summary>The held interaction started.</summary>
    InteractStart,
    /// <summary>The held interaction stepped.</summary>
    InteractStep,
    /// <summary>The held interaction stopped.</summary>
    InteractStop,
    /// <summary>The held interaction was cancelled.</summary>
    InteractCancel,
    /// <summary>The held attack started.</summary>
    AttackStart,
    /// <summary>The held attack stepped.</summary>
    AttackStep,
    /// <summary>The held attack stopped.</summary>
    AttackStop,
    /// <summary>The held attack was cancelled.</summary>
    AttackCancel,
}

/// <summary>One call of a held-item hook.</summary>
/// <param name="Hook">The hook that was called.</param>
/// <param name="Side">Whether the client or the server called it.</param>
/// <param name="ItemCode">The code of the item that was held, or an empty code.</param>
/// <param name="SecondsUsed">How long the item had been held, in seconds.</param>
/// <param name="Result">What the hook returned, for the hooks that return something.</param>
/// <param name="Sequence">The order of the call among all the recorded ones.</param>
public sealed record HeldHookCall(
    HeldHook Hook,
    EnumAppSide Side,
    string ItemCode,
    float SecondsUsed,
    EnumHandInteract Result,
    long Sequence);

/// <summary>Records the held-item hooks while a trace is open.</summary>
public sealed class HeldTraceScope(HeldTrace trace) : IDisposable
{
    private bool _disposed;

    /// <summary>Starts recording the held-item hooks.</summary>
    public static HeldTraceScope Start() => new(HeldTraceRecorder.Trace());

    /// <summary>What the hooks did.</summary>
    public HeldTrace Trace { get; } = trace;

    /// <summary>Stops recording.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HeldTraceRecorder.Stop(Trace);
    }
}

/// <summary>The held-item hooks of the game, and what they did.</summary>
public sealed class HeldDriver
{
    /// <summary>Records the held-item hooks, until the scope is disposed.</summary>
    public HeldTraceScope Trace() => HeldTraceScope.Start();
}

/// <summary>What the held-item hooks did, while a trace was open.</summary>
public sealed class HeldTrace
{
    private readonly List<HeldHookCall> _calls = [];

    internal HeldTrace()
    {
    }

    /// <summary>The calls, in order.</summary>
    public IReadOnlyList<HeldHookCall> Calls
    {
        get
        {
            lock (_calls) return [.. _calls];
        }
    }

    /// <summary>How many calls were recorded.</summary>
    public int Count
    {
        get
        {
            lock (_calls) return _calls.Count;
        }
    }

    /// <summary>The last call, or null when there was none.</summary>
    public HeldHookCall? Last
    {
        get
        {
            lock (_calls) return _calls.Count == 0 ? null : _calls[^1];
        }
    }

    /// <summary>The last call of a hook, or null when there was none.</summary>
    public HeldHookCall? LastOf(HeldHook hook)
    {
        lock (_calls)
        {
            for (int index = _calls.Count - 1; index >= 0; index--)
            {
                if (_calls[index].Hook == hook) return _calls[index];
            }

            return null;
        }
    }

    /// <summary>The calls of a hook, in order.</summary>
    public IReadOnlyList<HeldHookCall> Of(HeldHook hook)
    {
        lock (_calls) return [.. _calls.Where(call => call.Hook == hook)];
    }

    /// <summary>How many times a hook was called.</summary>
    public int CountOf(HeldHook hook)
    {
        lock (_calls) return _calls.Count(call => call.Hook == hook);
    }

    /// <summary>The calls of one side, in order.</summary>
    public IReadOnlyList<HeldHookCall> On(EnumAppSide side)
    {
        lock (_calls) return [.. _calls.Where(call => call.Side == side)];
    }

    internal void Record(HeldHookCall call)
    {
        lock (_calls) _calls.Add(call);
    }
}

/// <summary>Records the held-item hooks of the game, while a trace is open.</summary>
public static class HeldTraceRecorder
{
    private static readonly ConcurrentDictionary<HeldTrace, byte> s_traces = new();
    private static long s_sequence;
    private static bool s_installed;

    /// <summary>Starts recording the held-item hooks, until the trace is disposed.</summary>
    public static HeldTrace Trace()
    {
        Install();
        HeldTrace trace = new();
        s_traces[trace] = 0;
        return trace;
    }

    /// <summary>Stops recording into a trace.</summary>
    public static void Stop(HeldTrace trace) => s_traces.TryRemove(trace, out _);

    /// <summary>Whether the hooks are recorded at the moment.</summary>
    public static bool Recording => !s_traces.IsEmpty;

    internal static void Record(HeldHook hook, EntityAgent byEntity, ItemSlot slot, float secondsUsed, EnumHandInteract result)
    {
        if (s_traces.IsEmpty) return;

        EnumAppSide side = byEntity?.World?.Api?.Side ?? EnumAppSide.Universal;
        string item = slot?.Itemstack?.Collectible?.Code?.ToString() ?? "";
        HeldHookCall call = new(hook, side, item, secondsUsed, result, Interlocked.Increment(ref s_sequence));
        foreach (HeldTrace trace in s_traces.Keys) trace.Record(call);
    }

    private static void Install()
    {
        if (s_installed) return;
        s_installed = true;

        Harmony harmony = new("zaldaryon.pharos.held-trace");
        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStart)),
            prefix: new HarmonyMethod(typeof(HeldTraceRecorder), nameof(BeforeUseStart)));
        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStep)),
            postfix: new HarmonyMethod(typeof(HeldTraceRecorder), nameof(AfterUseStep)));
        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStop)),
            prefix: new HarmonyMethod(typeof(HeldTraceRecorder), nameof(BeforeUseStop)));
        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseCancel)),
            postfix: new HarmonyMethod(typeof(HeldTraceRecorder), nameof(AfterUseCancel)));
    }

    private static void BeforeUseStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType, bool firstEvent, ref EnumHandHandling handling)
    {
        Record(useType == EnumHandInteract.HeldItemAttack ? HeldHook.AttackStart : HeldHook.InteractStart, byEntity, slot, 0, EnumHandInteract.None);
    }

    private static void AfterUseStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandInteract __result)
    {
        Record(StepHook(byEntity, secondsPassed), byEntity, slot, secondsPassed, __result);
    }

    private static void BeforeUseStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType)
    {
        Record(useType == EnumHandInteract.HeldItemAttack ? HeldHook.AttackStop : HeldHook.InteractStop, byEntity, slot, secondsPassed, EnumHandInteract.None);
    }

    private static void AfterUseCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandInteract __result)
    {
        Record(CancelHook(byEntity), byEntity, slot, secondsPassed, __result);
    }

    private static HeldHook StepHook(EntityAgent byEntity, float secondsPassed) =>
        byEntity?.Controls?.HandUse == EnumHandInteract.HeldItemAttack ? HeldHook.AttackStep : HeldHook.InteractStep;

    private static HeldHook CancelHook(EntityAgent byEntity) =>
        byEntity?.Controls?.HandUse == EnumHandInteract.HeldItemAttack ? HeldHook.AttackCancel : HeldHook.InteractCancel;

}
