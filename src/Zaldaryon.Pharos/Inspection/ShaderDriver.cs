using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A registered shader program's name and pass name.</summary>
public sealed record ShaderProgramInfo(string Name, string PassName);

/// <summary>
/// Reads the client's registered shader programs.
/// </summary>
public sealed class ShaderDriver
{
    private readonly HeadlessClient _client;

    internal ShaderDriver(HeadlessClient client) => _client = client;

    /// <summary>Lists all registered shader program names.</summary>
    public IReadOnlyList<ShaderProgramInfo> Programs() => _client.RunOnClientThread(() =>
    {
        var programs = new List<ShaderProgramInfo>();
        // ShaderPrograms is a static class with public static fields
        var type = typeof(Vintagestory.Client.NoObf.ShaderPrograms);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is ShaderProgramBase prog)
            {
                programs.Add(new ShaderProgramInfo(field.Name, prog.PassName ?? ""));
            }
        }
        return programs;
    });
}