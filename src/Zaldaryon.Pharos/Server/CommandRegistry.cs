using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// What the game keeps out of reach: the commands a mod registered, and a command's parsers.
/// </summary>
internal static class CommandRegistry
{
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.commands");
    private static readonly object s_lock = new();
    private static bool s_installed;

    /// <summary>
    /// The commands each mod registered, by the api that holds them. Weak, so a game that ended,
    /// such as a client's before a reconnect, does not stay in memory for the rest of the process.
    /// </summary>
    private static readonly ConditionalWeakTable<ICoreAPI, Dictionary<string, List<string>>> s_byMod = new();

    private static readonly FieldInfo? s_parsersField =
        typeof(ChatCommandImpl).GetField("_parsers", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_apiField =
        typeof(ChatCommandImpl).GetField("_cmdapi", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_apiOfCommandApi =
        typeof(ChatCommandApi).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Watches the commands mods register, so a mod's own commands can be found later.</summary>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;
        }

        MethodInfo? withName = AccessTools.Method(typeof(ChatCommandImpl), nameof(ChatCommandImpl.WithName), [typeof(string)]);
        MethodInfo? withAlias = AccessTools.Method(typeof(ChatCommandImpl), nameof(ChatCommandImpl.WithAlias), [typeof(string[])]);
        MethodInfo? withRootAlias = AccessTools.Method(typeof(ChatCommandImpl), nameof(ChatCommandImpl.WithRootAlias), [typeof(string)]);

        if (withName == null || withAlias == null || withRootAlias == null)
        {
            throw new MissingMethodException(
                "This Vintage Story build does not expose ChatCommandImpl.WithName, WithAlias and WithRootAlias.");
        }

        s_harmony.Patch(withName, prefix: new HarmonyMethod(typeof(CommandRegistry), nameof(BeforeWithName)));
        s_harmony.Patch(withAlias, prefix: new HarmonyMethod(typeof(CommandRegistry), nameof(BeforeWithAlias)));
        s_harmony.Patch(withRootAlias, prefix: new HarmonyMethod(typeof(CommandRegistry), nameof(BeforeWithRootAlias)));
    }

    /// <summary>The parsers of a command, empty when the command has none.</summary>
    public static ICommandArgumentParser[] ParsersOf(IChatCommand command) =>
        command is ChatCommandImpl impl && s_parsersField?.GetValue(impl) is ICommandArgumentParser[] parsers ? parsers : [];

    /// <summary>The commands a mod registered, by name.</summary>
    public static IEnumerable<(string Name, IChatCommand Command)> CommandsOf(EmbeddedServerHost host, string modId)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (s_lock)
        {
            if (!s_byMod.TryGetValue(host.Api, out Dictionary<string, List<string>>? byMod)) yield break;
            if (!byMod.TryGetValue(modId, out List<string>? names)) yield break;

            foreach (string name in names)
            {
                if (host.Api.ChatCommands.Get(name) is { } command) yield return (name, command);
            }
        }
    }

    private static void BeforeWithName(ChatCommandImpl __instance, string commandName) => Record(__instance, commandName);

    private static void BeforeWithAlias(ChatCommandImpl __instance, string[] names)
    {
        foreach (string name in names) Record(__instance, name);
    }

    private static void BeforeWithRootAlias(ChatCommandImpl __instance, string commandName) => Record(__instance, commandName);

    private static void Record(ChatCommandImpl command, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        ICoreAPI? api = ApiOf(command);
        if (api == null) return;
        string? modId = ModIdOf(api);
        if (modId == null) return;

        lock (s_lock)
        {
            Dictionary<string, List<string>> byMod = s_byMod.GetValue(api, _ => new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));

            if (!byMod.TryGetValue(modId, out List<string>? names))
            {
                names = [];
                byMod[modId] = names;
            }

            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
    }

    /// <summary>The mod whose system is registering the command, read from the call stack.</summary>
    private static string? ModIdOf(ICoreAPI api)
    {
        StackFrame[] frames = new StackTrace(2, true).GetFrames() ?? [];
        foreach (StackFrame frame in frames)
        {
            Type? type = frame.GetMethod()?.DeclaringType;
            if (type == null || !typeof(ModSystem).IsAssignableFrom(type)) continue;

            foreach (ModSystem system in api.ModLoader.Systems)
            {
                if (system.GetType() == type) return system.Mod?.Info?.ModID;
            }

            foreach (Mod mod in api.ModLoader.Mods)
            {
                if (mod.Systems.Any(system => system.GetType() == type)) return mod.Info?.ModID;
            }
        }

        return null;
    }

    private static ICoreAPI? ApiOf(ChatCommandImpl command)
    {
        if (s_apiField?.GetValue(command) is not ChatCommandApi api) return null;
        return s_apiOfCommandApi?.GetValue(api) as ICoreAPI;
    }
}
