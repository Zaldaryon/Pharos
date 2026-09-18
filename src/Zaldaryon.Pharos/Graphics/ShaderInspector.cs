using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Intercepts shader program state changes to expose uniform values, bound textures,
/// and redundant upload detection for headless client test scenarios.
/// Uses Harmony prefix patches on <see cref="ShaderProgramBase"/> methods.
/// Zero overhead when disabled: patches are not installed until <see cref="Enable"/> is called.
/// </summary>
public sealed class ShaderInspector
{
    private const string HarmonyId = "zaldaryon.pharos.shaders";

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly object _lock = new();
    private bool _enabled;

    // Per-program uniform values: programId → (uniformName → boxed value)
    private readonly Dictionary<int, Dictionary<string, object>> _uniformsByProgram = new();

    // Upload stats: key = "programId:uniformName" → (lastValue, totalUploads, duplicateCount)
    private readonly Dictionary<string, (object? lastValue, int total, int duplicates)> _uploadStats = new();

    // Bound textures: textureUnit → textureId
    private readonly Dictionary<int, int> _boundTextures = new();

    private int _totalUploads;
    private int _redundantUploads;

    // Single active instance receiving increments from static patch delegates.
    private static ShaderInspector? _active;
    private static readonly object _activeLock = new();

    // -------------------------------------------------------------------------
    // Public lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Whether uniform and texture intercepting patches are currently installed.</summary>
    public bool IsEnabled
    {
        get { lock (_lock) { return _enabled; } }
    }

    /// <summary>
    /// The currently bound shader program, or null if no shader is active.
    /// Reads <see cref="ShaderProgramBase.CurrentShaderProgram"/> directly.
    /// </summary>
    public ShaderProgramBase? ActiveProgram => ShaderProgramBase.CurrentShaderProgram;

    /// <summary>
    /// A snapshot of currently bound texture IDs per texture unit.
    /// Thread-safe copy of the live dictionary.
    /// </summary>
    public IReadOnlyDictionary<int, int> BoundTextures
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<int, int>(_boundTextures);
            }
        }
    }

    /// <summary>
    /// Activates shader state intercepting by installing Harmony patches.
    /// No-op if already enabled.
    /// </summary>
    public void Enable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (_enabled) return;
                _active = this;
                ApplyPatches();
                _enabled = true;
            }
        }
    }

    /// <summary>Deactivates intercepting and removes Harmony patches.</summary>
    public void Disable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (!_enabled) return;
                _harmony.UnpatchAll(HarmonyId);
                if (ReferenceEquals(_active, this)) _active = null;
                _enabled = false;
            }
        }
    }

    /// <summary>
    /// Resets all recorded uniform values, upload counters, and texture bindings
    /// without changing enabled state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _uniformsByProgram.Clear();
            _uploadStats.Clear();
            _boundTextures.Clear();
            _totalUploads = 0;
            _redundantUploads = 0;
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of current shader state and upload counters.
    /// </summary>
    public ShaderSnapshot Snapshot()
    {
        ShaderProgramBase? current = ShaderProgramBase.CurrentShaderProgram;

        lock (_lock)
        {
            // Flatten uniforms across all programs into one dict (last write wins per name).
            var uniforms = new Dictionary<string, object>();
            foreach (var kvp in _uniformsByProgram)
            {
                foreach (var u in kvp.Value)
                {
                    uniforms[u.Key] = u.Value;
                }
            }

            // Build redundant-uploads dict: uniformName → total duplicate count.
            var redundant = new Dictionary<string, int>();
            foreach (var kvp in _uploadStats)
            {
                if (kvp.Value.duplicates > 0)
                {
                    // key is "programId:uniformName", extract uniform name
                    int colon = kvp.Key.IndexOf(':');
                    string uName = colon >= 0 ? kvp.Key[(colon + 1)..] : kvp.Key;
                    if (redundant.TryGetValue(uName, out int existing))
                        redundant[uName] = existing + kvp.Value.duplicates;
                    else
                        redundant[uName] = kvp.Value.duplicates;
                }
            }

            return new ShaderSnapshot
            {
                ActiveProgramId = current?.ProgramId ?? 0,
                ActiveProgramName = current?.PassName ?? string.Empty,
                Uniforms = uniforms,
                BoundTextures = new Dictionary<int, int>(_boundTextures),
                RedundantUploads = redundant,
                TotalUploads = _totalUploads,
                RedundantUploadCount = _redundantUploads,
            };
        }
    }

    /// <summary>
    /// Returns the last uploaded value for the named uniform in the currently active program.
    /// Throws <see cref="KeyNotFoundException"/> if the uniform was not recorded.
    /// Throws <see cref="InvalidCastException"/> if the stored type does not match <typeparamref name="T"/>.
    /// </summary>
    public T GetUniform<T>(string name)
    {
        ShaderProgramBase? current = ShaderProgramBase.CurrentShaderProgram
            ?? throw new InvalidOperationException("No shader program is currently active.");

        lock (_lock)
        {
            if (!_uniformsByProgram.TryGetValue(current.ProgramId, out var uniformMap))
                throw new KeyNotFoundException($"No uniforms recorded for program '{current.PassName}'.");

            if (!uniformMap.TryGetValue(name, out object? val))
                throw new KeyNotFoundException($"Uniform '{name}' was not recorded for program '{current.PassName}'.");

            return (T)val;
        }
    }

    /// <summary>
    /// Reads back the raw bytes of the named uniform buffer object from the currently active shader.
    /// <para>
    /// Requires a live OpenGL context. Call from the frame thread only (after a frame step).
    /// </para>
    /// </summary>
    /// <param name="blockName">The UBO block name as declared in the shader source.</param>
    /// <returns>Raw byte array of the UBO contents.</returns>
    public byte[] UBOContents(string blockName)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockName);

        ShaderProgramBase current = ShaderProgramBase.CurrentShaderProgram
            ?? throw new InvalidOperationException("No shader program is currently active.");

        if (!current.ubos.TryGetValue(blockName, out UBORef? uboRef) || uboRef == null)
            throw new KeyNotFoundException($"UBO block '{blockName}' not found in program '{current.PassName}'.");

        if (uboRef.Size <= 0)
            return Array.Empty<byte>();

        byte[] data = new byte[uboRef.Size];
        GL.BindBuffer(BufferTarget.UniformBuffer, uboRef.Handle);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, uboRef.Size, data);
        return data;
    }

    // -------------------------------------------------------------------------
    // Internal counter helpers called by static Harmony prefix methods
    // -------------------------------------------------------------------------

    internal static void OnUniformFloat(ShaderProgramBase instance, string name, float value)
    {
        ShaderInspector? active = _active;
        if (active == null) return;
        active.RecordUniform(instance.ProgramId, name, (object)value);
    }

    internal static void OnUniformInt(ShaderProgramBase instance, string name, int value)
    {
        ShaderInspector? active = _active;
        if (active == null) return;
        active.RecordUniform(instance.ProgramId, name, (object)value);
    }

    internal static void OnUniformFloatArray(ShaderProgramBase instance, string name, float[] values)
    {
        ShaderInspector? active = _active;
        if (active == null) return;
        // Store a copy to prevent mutation of the original array.
        float[] copy = (float[])values.Clone();
        active.RecordUniform(instance.ProgramId, name, copy);
    }

    internal static void OnUniformIntArray(ShaderProgramBase instance, string name, int[] values)
    {
        ShaderInspector? active = _active;
        if (active == null) return;
        int[] copy = (int[])values.Clone();
        active.RecordUniform(instance.ProgramId, name, copy);
    }

    internal static void OnBindTexture(int textureUnit, int textureId)
    {
        ShaderInspector? active = _active;
        if (active == null) return;
        lock (active._lock)
        {
            active._boundTextures[textureUnit] = textureId;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void RecordUniform(int programId, string name, object value)
    {
        lock (_lock)
        {
            string key = $"{programId}:{name}";

            bool isDuplicate = false;
            if (_uploadStats.TryGetValue(key, out var stats))
            {
                isDuplicate = ValuesEqual(stats.lastValue, value);
                _uploadStats[key] = (value, stats.total + 1, isDuplicate ? stats.duplicates + 1 : stats.duplicates);
            }
            else
            {
                _uploadStats[key] = (value, 1, 0);
            }

            if (!_uniformsByProgram.TryGetValue(programId, out var uniformMap))
            {
                uniformMap = new Dictionary<string, object>();
                _uniformsByProgram[programId] = uniformMap;
            }
            uniformMap[name] = value;

            _totalUploads++;
            if (isDuplicate) _redundantUploads++;
        }
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Array value equality via StructuralComparisons.
        if (a is Array arrA && b is Array arrB)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(arrA, arrB);
        }

        return a.Equals(b);
    }

    // -------------------------------------------------------------------------
    // Harmony patch application
    // -------------------------------------------------------------------------

    private void ApplyPatches()
    {
        Type baseType = typeof(ShaderProgramBase);
        BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;

        // Patch all Uniform overloads by parameter signature.
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(float)],
            nameof(Prefix_Uniform_Float));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(int)],
            nameof(Prefix_Uniform_Int));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_Uniform_IntCount_FloatArray));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(Vec2f)],
            nameof(Prefix_Uniform_Vec2f));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(Vec2i)],
            nameof(Prefix_Uniform_Vec2i));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(float), typeof(float)],
            nameof(Prefix_Uniform_Float2));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(Vec3f)],
            nameof(Prefix_Uniform_Vec3f));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(Vec3i)],
            nameof(Prefix_Uniform_Vec3i));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(float), typeof(float), typeof(float)],
            nameof(Prefix_Uniform_Float3));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(float), typeof(float), typeof(float), typeof(float)],
            nameof(Prefix_Uniform_Float4));
        PatchMethod(baseType, "Uniform", pub, [typeof(string), typeof(Vec4f)],
            nameof(Prefix_Uniform_Vec4f));
        PatchMethod(baseType, "Uniforms2", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_UniformsN));
        PatchMethod(baseType, "Uniforms3", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_UniformsN));
        PatchMethod(baseType, "Uniforms4", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_UniformsN));
        PatchMethod(baseType, "UniformMatrix", pub, [typeof(string), typeof(float[])],
            nameof(Prefix_UniformMatrix));
        // NOTE: UniformMatrix(string, ref Matrix4) is intentionally not patched. Harmony ref-parameter
        // prefix access requires unsafe pointer handling and this overload is rarely used in practice
        // (VS only calls it for internal camera matrix updates). The float[] overload covers all common paths.
        PatchMethod(baseType, "UniformMatrices", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_UniformMatrixArray));
        PatchMethod(baseType, "UniformMatrices4x3", pub, [typeof(string), typeof(int), typeof(float[])],
            nameof(Prefix_UniformMatrixArray));
        PatchMethod(baseType, "BindTexture2D", pub, [typeof(string), typeof(int), typeof(int)],
            nameof(Prefix_BindTexture2D));
        PatchMethod(baseType, "BindTexture2D", pub, [typeof(string), typeof(int)],
            nameof(Prefix_BindTexture2D_2Param));
        PatchMethod(baseType, "BindTextureCube", pub, [typeof(string), typeof(int), typeof(int)],
            nameof(Prefix_BindTextureCube));
    }

    private void PatchMethod(Type type, string methodName, BindingFlags flags, Type[] paramTypes, string prefixName)
    {
        MethodInfo? method = type.GetMethod(methodName, flags, null, paramTypes, null);
        if (method != null)
        {
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(ShaderInspector), prefixName));
        }
    }

    // -------------------------------------------------------------------------
    // Static Harmony prefix methods — return true so originals still execute
    // -------------------------------------------------------------------------

    private static void Prefix_Uniform_Float(ShaderProgramBase __instance, string uniformName, float value)
    {
        if (_active != null) OnUniformFloat(__instance, uniformName, value);
    }

    private static void Prefix_Uniform_Int(ShaderProgramBase __instance, string uniformName, int value)
    {
        if (_active != null) OnUniformInt(__instance, uniformName, value);
    }

    private static void Prefix_Uniform_IntCount_FloatArray(ShaderProgramBase __instance, string uniformName, int count, float[] value)
    {
        if (_active != null)
        {
            float[] slice = new float[Math.Min(count, value.Length)];
            Array.Copy(value, slice, slice.Length);
            OnUniformFloatArray(__instance, uniformName, slice);
        }
    }

    private static void Prefix_Uniform_Vec2f(ShaderProgramBase __instance, string uniformName, Vec2f value)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [value.X, value.Y]);
    }

    private static void Prefix_Uniform_Vec2i(ShaderProgramBase __instance, string uniformName, Vec2i value)
    {
        if (_active != null) OnUniformIntArray(__instance, uniformName, [value.X, value.Y]);
    }

    private static void Prefix_Uniform_Float2(ShaderProgramBase __instance, string uniformName, float valueX, float valueY)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [valueX, valueY]);
    }

    private static void Prefix_Uniform_Vec3f(ShaderProgramBase __instance, string uniformName, Vec3f value)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [value.X, value.Y, value.Z]);
    }

    private static void Prefix_Uniform_Vec3i(ShaderProgramBase __instance, string uniformName, Vec3i value)
    {
        if (_active != null) OnUniformIntArray(__instance, uniformName, [value.X, value.Y, value.Z]);
    }

    private static void Prefix_Uniform_Float3(ShaderProgramBase __instance, string uniformName, float valueX, float valueY, float valueZ)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [valueX, valueY, valueZ]);
    }

    private static void Prefix_Uniform_Float4(ShaderProgramBase __instance, string uniformName, float valueX, float valueY, float valueZ, float valueW)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [valueX, valueY, valueZ, valueW]);
    }

    private static void Prefix_Uniform_Vec4f(ShaderProgramBase __instance, string uniformName, Vec4f value)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, [value.X, value.Y, value.Z, value.W]);
    }

    /// <summary>Shared prefix for Uniforms2, Uniforms3, Uniforms4 — all have (string, int, float[]) signature.</summary>
    private static void Prefix_UniformsN(ShaderProgramBase __instance, string uniformName, int count, float[] values)
    {
        if (_active != null)
        {
            float[] slice = new float[Math.Min(count * 4, values.Length)];
            Array.Copy(values, slice, slice.Length);
            OnUniformFloatArray(__instance, uniformName, slice);
        }
    }

    private static void Prefix_UniformMatrix(ShaderProgramBase __instance, string uniformName, float[] matrix)
    {
        if (_active != null) OnUniformFloatArray(__instance, uniformName, matrix);
    }

    private static void Prefix_UniformMatrixArray(ShaderProgramBase __instance, string uniformName, int count, float[] matrix)
    {
        if (_active != null)
        {
            // Capture as-is; the count defines how many matrices are packed into the array.
            OnUniformFloatArray(__instance, uniformName, matrix);
        }
    }

    private static void Prefix_BindTexture2D(ShaderProgramBase __instance, string samplerName, int textureId, int textureNumber)
    {
        if (_active != null) OnBindTexture(textureNumber, textureId);
    }

    private static void Prefix_BindTexture2D_2Param(ShaderProgramBase __instance, string samplerName, int textureId)
    {
        if (_active != null)
        {
            // The 2-param overload resolves the texture unit from textureLocations[samplerName].
            // Use unit -1 as a sentinel when the unit is not easily available without field access.
            // Most callers use the 3-param overload; this captures the texture ID regardless.
            if (__instance.textureLocations.TryGetValue(samplerName, out int unit))
            {
                OnBindTexture(unit, textureId);
            }
        }
    }

    private static void Prefix_BindTextureCube(ShaderProgramBase __instance, string samplerName, int textureId, int textureNumber)
    {
        if (_active != null) OnBindTexture(textureNumber, textureId);
    }
}
