using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Gives each side its own data folder when a server and a client run in one process.
/// </summary>
/// <remarks>
/// <para>
/// The game keeps its data folder in one static field, <c>GamePaths.DataPath</c>, set once per
/// process by the program that starts it. Pharos boots a server into its sandbox and then a client
/// into a folder of its own, so after the client boots the field points at the client's folder for
/// good, and a server mod that reads or writes its config at run time, from a command or a tick,
/// would get the client's file.
/// </para>
/// <para>
/// This rewrites the mod API's data and config path members (<c>DataBasePath</c>,
/// <c>GetOrCreateDataPath</c>, <c>LoadModConfig</c>, <c>StoreModConfig</c>) to take the folder of
/// the side that calls them: a server's API uses the data path the server was started with.
/// </para>
/// <para>
/// <c>LoadModConfig&lt;T&gt;</c> and <c>StoreModConfig&lt;T&gt;</c> cannot be rewritten that way:
/// reference types share one compiled body, and a patched copy of it would lose the type the
/// caller asked for. They read <c>GamePaths.ModConfig</c>, which answers with the server's folder
/// on the server's game thread, where a mod's start, ticks and commands run. On any other thread,
/// and for a mod that reads <c>GamePaths.DataPath</c> itself, the shared field applies.
/// </para>
/// </remarks>
internal static class SideDataPaths
{
    private const string HarmonyId = "zaldaryon.pharos.sidedatapaths";
    private static readonly Harmony s_harmony = new(HarmonyId);
    private static readonly object s_lock = new();
    private static bool s_patched;

    private static readonly FieldInfo s_dataPathField = AccessTools.Field(typeof(GamePaths), nameof(GamePaths.DataPath));
    private static readonly MethodInfo s_modConfigGetter = AccessTools.PropertyGetter(typeof(GamePaths), nameof(GamePaths.ModConfig));

    public static void Patch()
    {
        lock (s_lock)
        {
            if (s_patched) return;

            HarmonyMethod transpiler = new(typeof(SideDataPaths), nameof(Transpile));
            foreach (MethodBase method in Targets())
            {
                s_harmony.Patch(method, transpiler: transpiler);
            }

            s_harmony.Patch(s_modConfigGetter, prefix: new HarmonyMethod(typeof(SideDataPaths), nameof(ModConfigPrefix)));

            s_patched = true;
        }
    }

    /// <summary>
    /// Makes <c>GamePaths.ModConfig</c> read on the calling thread, a server's game thread, use
    /// <paramref name="dataPath"/>. The thread lives and dies with its server.
    /// </summary>
    public static void UseServerDataPath(string dataPath) => t_serverDataPath = dataPath;

    [ThreadStatic]
    private static string? t_serverDataPath;

    private static bool ModConfigPrefix(ref string __result)
    {
        if (t_serverDataPath is not { } dataPath) return true;
        __result = Path.Combine(dataPath, "ModConfig");
        return false;
    }

    /// <summary>The data folder of the side <paramref name="api"/> belongs to.</summary>
    public static string DataPath(APIBase api) =>
        GameMainOf(api) is ServerMain { progArgs.DataPath: { Length: > 0 } serverPath } ? serverPath : GamePaths.DataPath;

    /// <summary>The mod config folder of the side <paramref name="api"/> belongs to.</summary>
    public static string ModConfig(APIBase api) => Path.Combine(DataPath(api), "ModConfig");

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "gamemain")]
    private static extern ref GameMain GameMainOf(APIBase api);

    private static IEnumerable<MethodBase> Targets()
    {
        Type api = typeof(APIBase);
        yield return AccessTools.PropertyGetter(api, nameof(APIBase.DataBasePath));
        yield return AccessTools.Method(api, nameof(APIBase.GetOrCreateDataPath), [typeof(string)]);
        foreach (MethodInfo method in api.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsGenericMethodDefinition && m.Name is nameof(APIBase.LoadModConfig) or nameof(APIBase.StoreModConfig)))
        {
            yield return method;
        }
    }

    // Swaps each read of the shared data folder for the calling side's.
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo dataPath = AccessTools.Method(typeof(SideDataPaths), nameof(DataPath));
        MethodInfo modConfig = AccessTools.Method(typeof(SideDataPaths), nameof(ModConfig));

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.LoadsField(s_dataPathField))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, dataPath);
            }
            else if (instruction.Calls(s_modConfigGetter))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, modConfig);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}
