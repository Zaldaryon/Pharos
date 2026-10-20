using System.Reflection;

namespace Zaldaryon.Pharos.Server;

/// <summary>The game's code for an AI task, as its registry names it.</summary>
internal static class AiTaskCodes
{
    private static readonly Dictionary<Assembly, IReadOnlyDictionary<Type, string>?> s_byAssembly = [];
    private static readonly object s_lock = new();

    /// <summary>
    /// The code <paramref name="type"/> is registered under, or its type name when the game did not
    /// register it. Registry codes are stable across runs and across mods; a type name is only the
    /// fallback for a task a mod never registered.
    /// </summary>
    public static string Resolve(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        IReadOnlyDictionary<Type, string>? codes = Registry(type.Assembly);
        return codes != null && codes.TryGetValue(type, out string? code) ? code : type.Name;
    }

    /// <summary>The code of <paramref name="task"/>, using the codes this measurement already resolved.</summary>
    public static string CodeOf(object task, IReadOnlyDictionary<Type, string> known)
    {
        ArgumentNullException.ThrowIfNull(task);
        return known.TryGetValue(task.GetType(), out string? code) ? code : task.GetType().Name;
    }

    private static IReadOnlyDictionary<Type, string>? Registry(Assembly assembly)
    {
        lock (s_lock)
        {
            if (s_byAssembly.TryGetValue(assembly, out IReadOnlyDictionary<Type, string>? known)) return known;
            IReadOnlyDictionary<Type, string>? codes = null;
            try
            {
                Type? registry = assembly.GetType("Vintagestory.GameContent.AiTaskRegistry", throwOnError: false);
                FieldInfo? field = registry?.GetField("TaskCodes", BindingFlags.Public | BindingFlags.Static);
                codes = field?.GetValue(null) as IReadOnlyDictionary<Type, string>;
            }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or TargetInvocationException)
            {
                codes = null;
            }

            s_byAssembly[assembly] = codes;
            return codes;
        }
    }
}
