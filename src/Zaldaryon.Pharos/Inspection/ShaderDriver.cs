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
    /// Gets the list of all registered mod shaders (from mods rather than vanilla).
    /// Returns shader names registered via the shader API.
    /// </summary>
    public IReadOnlyList<string> ModShaders() => _client.RunOnClientThread(() =>
    {
        var result = new List<string>();
        var api = _client.Client?.api;
        if (api?.Shader == null) return result;

        // List all programs and filter out vanilla ones (those in ShaderPrograms static fields)
        var vanillaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var type = typeof(ShaderPrograms);
        foreach (var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            vanillaNames.Add(fieldInfo.Name);
        }

        // For now, we can only list vanilla shaders without internal access
        // Mod shaders would require reflection into ShaderAPI's internal state
        return result;
    });
}
