using System.Reflection;
using HarmonyLib;

namespace Zaldaryon.Pharos.Server;

/// <summary>The exception a <see cref="ServerFaults.ThrowIn{T}"/> fault throws in the method it names.</summary>
public sealed class InjectedFaultException(string method) : Exception($"Injected fault in {method}.")
{
    /// <summary>The name of the method that threw.</summary>
    public string Method { get; } = method;
}

/// <summary>
/// Patches the methods that <see cref="ServerFaults.ThrowIn{T}"/> names. One prefix serves every target: it throws while a fault
/// that names the method still has throws left. The patch stays in place; the faults change what it does.
/// </summary>
internal static class FaultPatches
{
    private const string HarmonyId = "zaldaryon.pharos.faults";
    private static readonly object s_lock = new();
    private static readonly List<ThrowInFault> s_throws = [];
    private static readonly HashSet<MethodBase> s_patched = [];
    private static Harmony? s_harmony;

    /// <summary>Registers a fault, patching its method on the first fault that names it.</summary>
    internal static void Add(ThrowInFault fault)
    {
        lock (s_lock)
        {
            if (s_patched.Add(fault.Method))
            {
                s_harmony ??= new Harmony(HarmonyId);
                s_harmony.Patch(fault.Method, prefix: new HarmonyMethod(typeof(FaultPatches), nameof(Prefix)));
            }

            s_throws.Add(fault);
        }
    }

    /// <summary>Forgets a fault. Its patch stays, and does nothing more for it.</summary>
    internal static void Remove(ThrowInFault fault)
    {
        lock (s_lock) s_throws.Remove(fault);
    }

    // Harmony passes the method that was called, so this one prefix serves every patched target.
    private static void Prefix(MethodBase __originalMethod)
    {
        ThrowInFault? fault;
        lock (s_lock) fault = s_throws.FirstOrDefault(f => f.Method == __originalMethod && f.TryTake());
        if (fault is not null) throw new InjectedFaultException(__originalMethod.Name);
    }
}
