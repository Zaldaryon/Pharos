using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Detailed information about a shader program.</summary>
public sealed record ShaderProgramInfo(
    string Name,
    string PassName,
    int PassId,
    int ProgramId,
    bool IsCompiled,
    bool HasLoadError,
    bool HasVertexShader,
    bool HasFragmentShader,
    bool HasGeometryShader,
    IReadOnlyList<string> Uniforms,
    IReadOnlyList<string> Includes);

/// <summary>Compilation status of a shader.</summary>
public sealed record ShaderCompileResult(
    string ShaderName,
    bool Success,
    string? ErrorMessage);

/// <summary>
/// Reads the client's registered shader programs and their compilation status.
/// </summary>
public sealed class ShaderDriver
{
    private readonly HeadlessClient _client;

    internal ShaderDriver(HeadlessClient client) => _client = client;

    /// <summary>Lists all registered shader programs with detailed information.</summary>
    public IReadOnlyList<ShaderProgramInfo> Programs() => _client.RunOnClientThread(() =>
    {
        var programs = new List<ShaderProgramInfo>();
        var type = typeof(ShaderPrograms);

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is ShaderProgramBase prog)
            {
                var uniforms = prog.uniformLocations?.Keys.ToList() ?? new List<string>();
                var includes = prog.includes?.ToList() ?? new List<string>();

                programs.Add(new ShaderProgramInfo(
                    field.Name,
                    prog.PassName ?? "",
                    prog.PassId,
                    prog.ProgramId,
                    prog.ProgramId > 0,
                    prog.LoadError,
                    prog.VertexShader != null,
                    prog.FragmentShader != null,
                    prog.GeometryShader != null,
                    uniforms,
                    includes));
            }
        }

        return programs;
    });

    /// <summary>Gets information about a specific shader program by name.</summary>
    public ShaderProgramInfo? GetProgram(string name) => _client.RunOnClientThread(() =>
    {
        var type = typeof(ShaderPrograms);
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

        if (field?.GetValue(null) is not ShaderProgramBase prog)
            return null;

        var uniforms = prog.uniformLocations?.Keys.ToList() ?? new List<string>();
        var includes = prog.includes?.ToList() ?? new List<string>();

        return new ShaderProgramInfo(
            field.Name,
            prog.PassName ?? "",
            prog.PassId,
            prog.ProgramId,
            prog.ProgramId > 0,
            prog.LoadError,
            prog.VertexShader != null,
            prog.FragmentShader != null,
            prog.GeometryShader != null,
            uniforms,
            includes);
    });

    /// <summary>Gets the current active shader program, if any.</summary>
    public ShaderProgramInfo? CurrentProgram => _client.RunOnClientThread(() =>
    {
        var prog = ShaderProgramBase.CurrentShaderProgram;
        if (prog == null) return null;

        // Find the field name
        var type = typeof(ShaderPrograms);
        string? name = null;
        foreach (var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (fieldInfo.GetValue(null) == prog)
            {
                name = fieldInfo.Name;
                break;
            }
        }

        var uniforms = prog.uniformLocations?.Keys.ToList() ?? new List<string>();
        var includes = prog.includes?.ToList() ?? new List<string>();

        return new ShaderProgramInfo(
            name ?? "Unknown",
            prog.PassName ?? "",
            prog.PassId,
            prog.ProgramId,
            prog.ProgramId > 0,
            prog.LoadError,
            prog.VertexShader != null,
            prog.FragmentShader != null,
            prog.GeometryShader != null,
            uniforms,
            includes);
    });

    /// <summary>Lists all shader programs that failed to compile or load.</summary>
    public IReadOnlyList<ShaderProgramInfo> FailedPrograms() => _client.RunOnClientThread(() =>
    {
        return Programs().Where(p => p.HasLoadError || !p.IsCompiled).ToList();
    });

    /// <summary>
    /// Gets a shader's uniform locations dictionary.
    /// Useful for checking if expected uniforms are present.
    /// </summary>
    public IReadOnlyDictionary<string, int>? GetUniformLocations(string programName) => _client.RunOnClientThread(() =>
    {
        var type = typeof(ShaderPrograms);
        var field = type.GetField(programName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

        if (field?.GetValue(null) is not ShaderProgramBase prog)
            return null;

        return prog.uniformLocations != null
            ? new Dictionary<string, int>(prog.uniformLocations)
            : null;
    });

    /// <summary>
    /// Checks if a uniform exists in a shader program.
    /// </summary>
    public bool HasUniform(string programName, string uniformName) => _client.RunOnClientThread(() =>
    {
        var uniforms = GetUniformLocations(programName);
        return uniforms?.ContainsKey(uniformName) ?? false;
    });

    /// <summary>Gets the current shadow map quality level.</summary>
    public int ShadowMapQuality => _client.RunOnClientThread(() => ShaderProgramBase.shadowmapQuality);

    /// <summary>
    /// Attempts to recompile a shader program. Returns compilation result.
    /// Note: This modifies shader state; use only for testing shader modifications.
    /// </summary>
    public ShaderCompileResult TryRecompile(string programName) => _client.RunOnClientThread(() =>
    {
        var type = typeof(ShaderPrograms);
        var field = type.GetField(programName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

        if (field?.GetValue(null) is not ShaderProgramBase prog)
            return new ShaderCompileResult(programName, false, "Shader program not found");

        try
        {
            // The Compile method typically handles errors internally
            prog.Compile();
            return new ShaderCompileResult(programName, !prog.LoadError, prog.LoadError ? "Compilation failed (check logs)" : null);
        }
        catch (Exception ex)
        {
            return new ShaderCompileResult(programName, false, ex.Message);
        }
    });

    /// <summary>
    /// Gets the list of all registered shader names from the shader registry.
    /// Includes both vanilla and mod-registered shaders.
    /// </summary>
    public IReadOnlyList<string> AllRegisteredShaders() => _client.RunOnClientThread(() =>
    {
        var result = new List<string>();

        // Access ShaderRegistry.shaderIdsByName via reflection
        var registryType = typeof(ShaderRegistry);
        var idsField = registryType.GetField("shaderIdsByName", BindingFlags.NonPublic | BindingFlags.Static);
        if (idsField?.GetValue(null) is Dictionary<string, int> ids)
        {
            result.AddRange(ids.Keys);
        }

        return result;
    });

    /// <summary>
    /// Gets the list of mod-registered shaders (not from vanilla EnumShaderProgram).
    /// </summary>
    public IReadOnlyList<ModShaderInfo> ModShaders() => _client.RunOnClientThread(() =>
    {
        var result = new List<ModShaderInfo>();

        // Get all vanilla shader names from EnumShaderProgram
        var vanillaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumName in Enum.GetNames(typeof(EnumShaderProgram)))
        {
            vanillaNames.Add(enumName.ToLowerInvariant());
            // Handle suffixed variants like "Entityanimated_Oit"
            var underscoreIdx = enumName.IndexOf('_');
            if (underscoreIdx > 0)
                vanillaNames.Add(enumName.Substring(0, underscoreIdx).ToLowerInvariant());
        }

        // Access ShaderRegistry internals
        var registryType = typeof(ShaderRegistry);
        var idsField = registryType.GetField("shaderIdsByName", BindingFlags.NonPublic | BindingFlags.Static);
        var programsField = registryType.GetField("shaderPrograms", BindingFlags.NonPublic | BindingFlags.Static);

        if (idsField?.GetValue(null) is not Dictionary<string, int> ids ||
            programsField?.GetValue(null) is not ShaderProgram[] programs)
            return result;

        foreach (var kvp in ids)
        {
            // Skip vanilla shaders
            if (vanillaNames.Contains(kvp.Key.ToLowerInvariant()))
                continue;

            var prog = programs[kvp.Value];
            if (prog == null) continue;

            var uniforms = (prog as ShaderProgramBase)?.uniformLocations?.Keys.ToList() ?? [];
            var includes = prog.includes?.ToList() ?? [];

            result.Add(new ModShaderInfo(
                kvp.Key,
                prog.PassName ?? "",
                prog.PassId,
                prog.ProgramId,
                prog.ProgramId > 0 && !prog.LoadError,
                prog.LoadError,
                prog.VertexShader != null,
                prog.FragmentShader != null,
                prog.GeometryShader != null,
                uniforms,
                includes,
                prog.AssetDomain ?? "game"));
        }

        return result;
    });

    /// <summary>
    /// Gets detailed information about a mod shader by name.
    /// </summary>
    public ModShaderInfo? GetModShader(string name) => _client.RunOnClientThread(() =>
    {
        var registryType = typeof(ShaderRegistry);
        var idsField = registryType.GetField("shaderIdsByName", BindingFlags.NonPublic | BindingFlags.Static);
        var programsField = registryType.GetField("shaderPrograms", BindingFlags.NonPublic | BindingFlags.Static);

        if (idsField?.GetValue(null) is not Dictionary<string, int> ids ||
            programsField?.GetValue(null) is not ShaderProgram[] programs)
            return null;

        if (!ids.TryGetValue(name, out int passId))
            return null;

        var prog = programs[passId];
        if (prog == null) return null;

        var uniforms = (prog as ShaderProgramBase)?.uniformLocations?.Keys.ToList() ?? [];
        var includes = prog.includes?.ToList() ?? [];

        return new ModShaderInfo(
            name,
            prog.PassName ?? "",
            prog.PassId,
            prog.ProgramId,
            prog.ProgramId > 0 && !prog.LoadError,
            prog.LoadError,
            prog.VertexShader != null,
            prog.FragmentShader != null,
            prog.GeometryShader != null,
            uniforms,
            includes,
            prog.AssetDomain ?? "game");
    });
}

/// <summary>Detailed information about a mod-registered shader program.</summary>
public sealed record ModShaderInfo(
    string Name,
    string PassName,
    int PassId,
    int ProgramId,
    bool IsCompiled,
    bool HasLoadError,
    bool HasVertexShader,
    bool HasFragmentShader,
    bool HasGeometryShader,
    IReadOnlyList<string> Uniforms,
    IReadOnlyList<string> Includes,
    string AssetDomain);
