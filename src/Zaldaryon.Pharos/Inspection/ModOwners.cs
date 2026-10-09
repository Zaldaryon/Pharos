using System.Reflection;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Which mod a renderer, listener or callback belongs to, by the assembly its code is in.</summary>
/// <remarks>
/// Each loaded code mod's assembly and the assemblies of its systems, the ones the game compiled
/// from a source mod included. Code in no mod's assembly has no owner: the engine's own systems,
/// and helpers of the game's API a mod uses, such as a block entity's animation renderer.
/// </remarks>
internal sealed class ModOwners
{
    private readonly Dictionary<Assembly, string> _byAssembly;

    private ModOwners(Dictionary<Assembly, string> byAssembly)
    {
        _byAssembly = byAssembly;
    }

    /// <summary>The owners of the mods <paramref name="api"/> has loaded. Run on the game's thread.</summary>
    public static ModOwners Of(ICoreAPI? api)
    {
        if (api?.ModLoader?.Mods is null) throw new InvalidOperationException("The client has not loaded its mods yet: it has not started a game.");
        Dictionary<Assembly, string> byAssembly = [];
        foreach (Mod mod in api.ModLoader.Mods)
        {
            // A code mod's own assembly, even one with no systems.
            if (mod is Vintagestory.Common.ModContainer { Assembly: { } assembly }) byAssembly.TryAdd(assembly, mod.Info.ModID);
            foreach (ModSystem system in mod.Systems)
            {
                byAssembly.TryAdd(system.GetType().Assembly, mod.Info.ModID);
            }
        }

        return new ModOwners(byAssembly);
    }

    /// <summary>The mod whose assembly holds <paramref name="type"/>, or null.</summary>
    public string? OfType(Type? type) => type != null && _byAssembly.TryGetValue(type.Assembly, out string? mod) ? mod : null;

    /// <summary>
    /// The mod whose code <paramref name="handler"/> runs, lambdas included, or null: the object it
    /// is bound to first, so a mod's class that inherits a game method still counts as the mod's.
    /// </summary>
    public string? OfDelegate(Delegate? handler) => OfType(handler?.Target?.GetType()) ?? OfType(handler?.Method.DeclaringType);

    /// <summary>A readable name for <paramref name="handler"/>: its type and method.</summary>
    public static string Describe(Delegate? handler) =>
        handler == null ? "(none)" : $"{handler.Method.DeclaringType?.FullName ?? "?"}.{handler.Method.Name}";
}
