using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Routes the server's reads of its own time through Pharos, so a <see cref="VirtualClock"/> can
/// replace the wall clock with ticks.
/// </summary>
/// <remarks>
/// <para>
/// The game reads <c>ServerMain.totalUnpausedTime</c>, a <see cref="Stopwatch"/>, wherever the
/// server needs the time: in <c>Process</c> to decide which systems run and with what
/// <c>dt</c>, in <c>ElapsedMilliseconds</c> (<c>world.ElapsedMilliseconds</c>, tick listeners and
/// callbacks), in the calendar, ping and chunk systems and in the server API's uptime. Each read
/// is the field followed at once by <c>ElapsedMilliseconds</c> or <c>Elapsed</c>; this rewrites
/// the second instruction to ask Pharos instead, in every method of the game's library that has
/// one. Found rather than listed, so other game versions are covered.
/// </para>
/// <para>
/// <c>GameCalendar.Tick</c> reads its own watch the same way, and is rewritten so the server's
/// calendar can follow the virtual time.
/// </para>
/// <para>
/// Installed once per process before anything boots, from
/// <see cref="Platform.HeadlessPlatformResolver"/>: a trivial getter such as
/// <c>ElapsedMilliseconds</c> compiled into a caller before the patch would go on reading the
/// wall clock there. A read of any stopwatch other than the current server's returns what the
/// stopwatch says, and so does the current server's until a virtual clock is used.
/// </para>
/// </remarks>
internal static class ServerClockPatches
{
    private const string HarmonyId = "zaldaryon.pharos.serverclock";
    private static readonly object s_lock = new();
    private static bool s_installed;
    private static readonly List<string> s_rewritten = [];
    private static volatile ServerClockState? s_current;

    private static readonly MethodInfo s_elapsedMsGetter = AccessTools.PropertyGetter(typeof(Stopwatch), nameof(Stopwatch.ElapsedMilliseconds));
    private static readonly MethodInfo s_elapsedGetter = AccessTools.PropertyGetter(typeof(Stopwatch), nameof(Stopwatch.Elapsed));
    private static readonly FieldInfo s_serverWatchField = AccessTools.Field(typeof(ServerMain), nameof(ServerMain.totalUnpausedTime));
    private static readonly FieldInfo s_calendarWatchField = AccessTools.Field(typeof(GameCalendar), "watchIngameTime");
    private static readonly MethodInfo s_process = AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Process));
    private static readonly MethodInfo s_elapsedProperty = AccessTools.PropertyGetter(typeof(ServerMain), nameof(ServerMain.ElapsedMilliseconds));

    /// <summary>The state of the server booted last, whose reads Pharos answers.</summary>
    internal static ServerClockState? Current
    {
        get => s_current;
        set => s_current = value;
    }

    /// <summary>The methods rewritten, as <c>Type.Method</c>, for diagnostics and tests.</summary>
    internal static IReadOnlyList<string> Rewritten
    {
        get
        {
            lock (s_lock) return s_rewritten.ToArray();
        }
    }

    /// <summary>Whether the tick in <c>Process</c> and the <c>ElapsedMilliseconds</c> getter were rewritten.</summary>
    internal static bool Supported { get; private set; }

    /// <summary>How long finding and patching the readers took, for diagnostics.</summary>
    internal static TimeSpan InstallTime { get; private set; }

    /// <summary>Installs the rewrites; does nothing the second time.</summary>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;

            Stopwatch took = Stopwatch.StartNew();
            Harmony harmony = new(HarmonyId);
            HarmonyMethod serverTranspiler = new(typeof(ServerClockPatches), nameof(RewriteServerTime));
            bool tick = false, getter = false;
            foreach (MethodBase method in FindReaders(typeof(ServerMain).Assembly, s_serverWatchField))
            {
                try
                {
                    harmony.Patch(method, transpiler: serverTranspiler);
                    s_rewritten.Add($"{method.DeclaringType?.FullName}.{method.Name}");
                    tick |= method == s_process;
                    getter |= method == s_elapsedProperty;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Pharos could not route {method.DeclaringType?.FullName}.{method.Name} through the virtual clock: {ex.Message}");
                }
            }

            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameCalendar), nameof(GameCalendar.Tick)),
                    transpiler: new HarmonyMethod(typeof(ServerClockPatches), nameof(RewriteCalendarTime)));
                s_rewritten.Add($"{typeof(GameCalendar).FullName}.{nameof(GameCalendar.Tick)}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Pharos could not let the calendar follow the virtual clock: {ex.Message}");
            }

            Supported = tick && getter;
            InstallTime = took.Elapsed;
        }
    }

    /// <summary>Throws when the game could not be patched for a virtual clock.</summary>
    /// <exception cref="NotSupportedException">The server's tick or time getter was not found.</exception>
    public static void EnsureSupported()
    {
        Install();
        if (!Supported)
        {
            throw new NotSupportedException(
                $"This game version ({Vintagestory.API.Config.GameVersion.OverallVersion}) does not read the server's time where Pharos expects it, " +
                "in ServerMain.Process and ServerMain.ElapsedMilliseconds, so a virtual clock cannot drive it.");
        }
    }

    /// <summary>
    /// The methods of <paramref name="assembly"/> that read <paramref name="field"/> and then its
    /// <c>ElapsedMilliseconds</c> or <c>Elapsed</c>.
    /// </summary>
    internal static IEnumerable<MethodBase> FindReaders(Assembly assembly, FieldInfo field)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        byte[] token = BitConverter.GetBytes(field.MetadataToken);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (Type type in types)
        {
            if (type.ContainsGenericParameters) continue;

            MethodBase[] methods;
            try
            {
                methods = [.. type.GetMethods(All), .. type.GetConstructors(All)];
            }
            catch
            {
                continue;
            }

            foreach (MethodBase method in methods)
            {
                if (method.IsAbstract || method.IsGenericMethodDefinition) continue;
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                if ((method.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0) continue;

                byte[]? il;
                try
                {
                    il = method.GetMethodBody()?.GetILAsByteArray();
                }
                catch
                {
                    continue;
                }

                if (il != null && LoadsField(il, token) && ReadsElapsed(method, field)) yield return method;
            }
        }
    }

    // A quick look for ldfld (0x7B) and the field's token; a false match is weeded out by ReadsElapsed.
    private static bool LoadsField(byte[] il, byte[] token)
    {
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] == 0x7B && il[i + 1] == token[0] && il[i + 2] == token[1] && il[i + 3] == token[2] && il[i + 4] == token[3]) return true;
        }

        return false;
    }

    private static bool ReadsElapsed(MethodBase method, FieldInfo field)
    {
        try
        {
            List<CodeInstruction> instructions = PatchProcessor.GetOriginalInstructions(method);
            for (int i = 0; i + 1 < instructions.Count; i++)
            {
                if (instructions[i].LoadsField(field) && (instructions[i + 1].Calls(s_elapsedMsGetter) || instructions[i + 1].Calls(s_elapsedGetter))) return true;
            }
        }
        catch
        {
            // A body Harmony cannot read is left alone.
        }

        return false;
    }

    /// <summary>
    /// Replaces each read of <paramref name="field"/>'s <c>ElapsedMilliseconds</c> or
    /// <c>Elapsed</c> with a call that takes the stopwatch: <paramref name="readMs"/>,
    /// <paramref name="readElapsed"/>, and <paramref name="firstReadMs"/> for the first one when
    /// given.
    /// </summary>
    internal static IEnumerable<CodeInstruction> Rewrite(
        IEnumerable<CodeInstruction> instructions, FieldInfo field, MethodInfo readMs, MethodInfo? readElapsed, MethodInfo? firstReadMs = null)
    {
        bool afterField = false;
        bool first = true;
        foreach (CodeInstruction instruction in instructions)
        {
            if (afterField && instruction.Calls(s_elapsedMsGetter))
            {
                MethodInfo target = first && firstReadMs != null ? firstReadMs : readMs;
                first = false;
                yield return new CodeInstruction(OpCodes.Call, target).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else if (afterField && readElapsed != null && instruction.Calls(s_elapsedGetter))
            {
                yield return new CodeInstruction(OpCodes.Call, readElapsed).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }

            afterField = instruction.LoadsField(field);
        }
    }

    private static IEnumerable<CodeInstruction> RewriteServerTime(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        Rewrite(instructions, s_serverWatchField,
            AccessTools.Method(typeof(ServerClockPatches), nameof(ElapsedMilliseconds)),
            AccessTools.Method(typeof(ServerClockPatches), nameof(Elapsed)),
            original == s_process ? AccessTools.Method(typeof(ServerClockPatches), nameof(BeginTick)) : null);

    private static IEnumerable<CodeInstruction> RewriteCalendarTime(IEnumerable<CodeInstruction> instructions) =>
        Rewrite(instructions, s_calendarWatchField, AccessTools.Method(typeof(ServerClockPatches), nameof(CalendarElapsedMilliseconds)), readElapsed: null);

    /// <summary>What the server's code gets for <c>totalUnpausedTime.ElapsedMilliseconds</c>.</summary>
    public static long ElapsedMilliseconds(Stopwatch watch) =>
        s_current is { } state && ReferenceEquals(state.Watch, watch) ? state.Read() : watch.ElapsedMilliseconds;

    /// <summary>What the server's code gets for <c>totalUnpausedTime.Elapsed</c>.</summary>
    public static TimeSpan Elapsed(Stopwatch watch) =>
        s_current is { } state && ReferenceEquals(state.Watch, watch) ? state.ReadElapsed() : watch.Elapsed;

    /// <summary>The first read in <c>ServerMain.Process</c>, past its early returns: a server tick starts.</summary>
    public static long BeginTick(Stopwatch watch) =>
        s_current is { } state && ReferenceEquals(state.Watch, watch) ? state.BeginTick() : watch.ElapsedMilliseconds;

    /// <summary>What <c>GameCalendar.Tick</c> gets for its watch's <c>ElapsedMilliseconds</c>.</summary>
    public static long CalendarElapsedMilliseconds(Stopwatch watch) =>
        s_current?.TakeCalendarElapsed(watch) ?? watch.ElapsedMilliseconds;

    /// <summary>The milliseconds a followed calendar has yet to bank, or null when its watch is not followed.</summary>
    public static long? CalendarPendingMilliseconds(Stopwatch watch) => s_current?.PendingCalendarElapsed(watch);
}
