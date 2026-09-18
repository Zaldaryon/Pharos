namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Declares mod paths to stage into the client's mod directory before test bootstrap.
/// Apply to a test class or assembly to ensure mods are available during test execution.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PharosModsAttribute : Attribute
{
    /// <summary>
    /// Gets the list of mod paths declared by this attribute.
    /// Paths may be absolute or relative to the test assembly directory.
    /// Supports directories, zip files, and individual mod files.
    /// </summary>
    public IReadOnlyList<string> ModPaths { get; }

    /// <summary>
    /// Initializes a new instance declaring one or more mod paths to stage.
    /// </summary>
    /// <param name="modPaths">One or more paths to mods (directories, zips, or files).</param>
    public PharosModsAttribute(params string[] modPaths)
    {
        ModPaths = modPaths ?? Array.Empty<string>();
    }
}
