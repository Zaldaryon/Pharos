using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Zaldaryon.Pharos.Memory;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Hooks on OpenTK's <c>GL.Gen*</c> and <c>GL.Delete*</c> for buffers, vertex arrays, textures,
/// framebuffers and renderbuffers, in both bindings the game and mods use. They feed the
/// <see cref="GlCommandProxy"/> counters while one records, and the <see cref="GlResourceLedger"/>
/// while a <see cref="GlResourceScope"/> is open, and do nothing otherwise.
/// </summary>
/// <remarks>
/// Installed with the other game hooks before anything boots: a caller the runtime compiled before
/// the patch could have the small OpenTK wrapper inlined and never reach it.
/// </remarks>
internal static class GlResourceHooks
{
    // Not the proxy's id: GlCommandProxy.Disable unpatches everything under its own.
    private const string HarmonyId = "zaldaryon.pharos.gl-resources";

    private static readonly object s_lock = new();
    private static readonly ConcurrentDictionary<string, GlResourceKind> s_kinds = new(StringComparer.Ordinal);
    private static IReadOnlyList<MethodBase> s_patched = [];
    private static InvalidOperationException? s_failure;

    private static readonly (string Name, GlResourceKind Kind)[] s_families =
    [
        ("Buffer", GlResourceKind.Buffer),
        ("VertexArray", GlResourceKind.VertexArray),
        ("Texture", GlResourceKind.Texture),
        ("Framebuffer", GlResourceKind.Framebuffer),
        ("Renderbuffer", GlResourceKind.Renderbuffer),
    ];

    /// <summary>The methods patched.</summary>
    public static IReadOnlyList<MethodBase> Patched => s_patched;

    /// <summary>Patches every Gen and Delete overload once.</summary>
    /// <exception cref="InvalidOperationException">A method is missing or could not be patched.</exception>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_patched.Count > 0) return;
            if (s_failure != null) throw s_failure;

            Harmony harmony = new(HarmonyId);
            List<MethodBase> patched = [];
            List<string> failed = [];
            foreach (Type gl in (Type[])[typeof(OpenTK.Graphics.OpenGL.GL), typeof(OpenTK.Graphics.OpenGL4.GL)])
            {
                foreach ((string family, GlResourceKind kind) in s_families)
                {
                    foreach (MethodInfo method in gl.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == "Gen" + family || m.Name == "Gen" + family + "s" || m.Name == "Delete" + family || m.Name == "Delete" + family + "s"))
                    {
                        if (Hook(method) is not { } hook)
                        {
                            continue;
                        }

                        try
                        {
                            harmony.Patch(method, prefix: hook.Prefix, postfix: hook.Postfix);
                            s_kinds[method.Name] = kind;
                            patched.Add(method);
                        }
                        catch (Exception ex)
                        {
                            failed.Add($"{gl.Namespace}.GL.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}): {ex.Message}");
                        }
                    }
                }
            }

            if (failed.Count > 0 || patched.Count == 0)
            {
                harmony.UnpatchAll(HarmonyId);
                throw s_failure = new InvalidOperationException("Pharos could not hook OpenGL object creation: " + (failed.Count > 0 ? string.Join("; ", failed) : "no Gen or Delete methods found."));
            }

            s_patched = patched;
        }
    }

    // The patch for an overload's shape, or null for a shape left out.
    private static (HarmonyMethod? Prefix, HarmonyMethod? Postfix)? Hook(MethodInfo method)
    {
        bool gen = method.Name.StartsWith("Gen", StringComparison.Ordinal);
        ParameterInfo[] parameters = method.GetParameters();
        string? name = (gen, parameters.Length) switch
        {
            (true, 0) when method.ReturnType == typeof(int) => nameof(GenOne),
            (false, 1) when parameters[0].ParameterType == typeof(int) => nameof(DeleteOneInt),
            (false, 1) when parameters[0].ParameterType == typeof(uint) => nameof(DeleteOneUInt),
            (_, 2) when parameters[0].ParameterType == typeof(int) => Shape(parameters[1].ParameterType) is { } shape ? (gen ? "Gen" : "Delete") + shape : null,
            _ => null,
        };
        if (name == null) return null;

        // The array and pointer forms of Gen* mark their ids parameter [Out]. A patched copy of a
        // pointer form is rejected as an invalid program, and an array form's ids cannot be read
        // from a patch without crashing. Neither the game nor its API calls these forms, so they
        // are left out; deleting an object made through them counts as deleting one made before
        // the scope, never as a leak.
        if (gen && (name.EndsWith("Pointer", StringComparison.Ordinal) || name.EndsWith("Array", StringComparison.Ordinal))) return null;

        HarmonyMethod patch = new(typeof(GlResourceHooks), name);
        return gen ? (null, patch) : (patch, null);
    }

    private static string? Shape(Type type) =>
        type == typeof(int[]) ? "IntArray"
        : type == typeof(uint[]) ? "UIntArray"
        : type == typeof(int).MakeByRefType() ? "IntRef"
        : type == typeof(uint).MakeByRefType() ? "UIntRef"
        : type == typeof(int*) ? "IntPointer"
        : type == typeof(uint*) ? "UIntPointer"
        : null;

    private static bool Listening => GlResourceLedger.Live != null || GlCommandProxy.IsRecording;

    private static void Created(MethodBase method, uint id)
    {
        GlResourceKind kind = s_kinds.GetValueOrDefault(method.Name);
        GlCommandProxy.OnResource(kind, created: true);
        GlResourceLedger.Live?.OnCreated(kind, id, Environment.CurrentManagedThreadId);
    }

    private static void Deleted(MethodBase method, uint id)
    {
        GlResourceKind kind = s_kinds.GetValueOrDefault(method.Name);
        GlCommandProxy.OnResource(kind, created: false);
        GlResourceLedger.Live?.OnDeleted(kind, id, Environment.CurrentManagedThreadId);
    }

    private static void GenOne(int __result, MethodBase __originalMethod)
    {
        if (Listening) Created(__originalMethod, (uint)__result);
    }

    private static void DeleteOneInt(int __0, MethodBase __originalMethod)
    {
        if (Listening) Deleted(__originalMethod, (uint)__0);
    }

    private static void DeleteOneUInt(uint __0, MethodBase __originalMethod)
    {
        if (Listening) Deleted(__originalMethod, __0);
    }

    // The array is marked [Out], which Harmony hands over only by reference.
    private static void GenIntRef(int __0, ref int __1, MethodBase __originalMethod)
    {
        if (!Listening) return;
        for (int i = 0; i < __0; i++) Created(__originalMethod, (uint)Unsafe.Add(ref __1, i));
    }

    private static void GenUIntRef(int __0, ref uint __1, MethodBase __originalMethod)
    {
        if (!Listening) return;
        for (int i = 0; i < __0; i++) Created(__originalMethod, Unsafe.Add(ref __1, i));
    }

    // Deletions are read before the call: the ids are what the caller hands in.
    private static void DeleteIntArray(int __0, int[] __1, MethodBase __originalMethod)
    {
        if (!Listening || __1 == null) return;
        for (int i = 0; i < Math.Min(__0, __1.Length); i++) Deleted(__originalMethod, (uint)__1[i]);
    }

    private static void DeleteUIntArray(int __0, uint[] __1, MethodBase __originalMethod)
    {
        if (!Listening || __1 == null) return;
        for (int i = 0; i < Math.Min(__0, __1.Length); i++) Deleted(__originalMethod, __1[i]);
    }

    private static void DeleteIntRef(int __0, ref int __1, MethodBase __originalMethod)
    {
        if (!Listening) return;
        for (int i = 0; i < __0; i++) Deleted(__originalMethod, (uint)Unsafe.Add(ref __1, i));
    }

    private static void DeleteUIntRef(int __0, ref uint __1, MethodBase __originalMethod)
    {
        if (!Listening) return;
        for (int i = 0; i < __0; i++) Deleted(__originalMethod, Unsafe.Add(ref __1, i));
    }

    private static unsafe void DeleteIntPointer(int __0, int* __1, MethodBase __originalMethod)
    {
        if (!Listening || __1 == null) return;
        for (int i = 0; i < __0; i++) Deleted(__originalMethod, (uint)__1[i]);
    }

    private static unsafe void DeleteUIntPointer(int __0, uint* __1, MethodBase __originalMethod)
    {
        if (!Listening || __1 == null) return;
        for (int i = 0; i < __0; i++) Deleted(__originalMethod, __1[i]);
    }
}
