using System.Reflection;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Stages mod files and directories into a temporary location for test bootstrap.
/// Disposes by cleaning up the staging directory.
/// </summary>
public sealed class ModStager : IDisposable
{
    private readonly List<string> _stagedPaths = new();
    private readonly bool _ownsDirectory;
    private bool _disposed;

    /// <summary>
    /// Gets the full path to the staging directory.
    /// </summary>
    public string StagingDirectory { get; }

    /// <summary>
    /// Gets the list of successfully staged paths.
    /// </summary>
    public IReadOnlyList<string> StagedPaths => _stagedPaths;

    /// <summary>
    /// Initializes a new ModStager that creates a staging directory under the specified root.
    /// </summary>
    /// <param name="stagingRoot">The root directory in which to create the staging subdirectory.</param>
    public ModStager(string stagingRoot)
    {
        StagingDirectory = Path.Combine(stagingRoot, $"pharos-mods-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StagingDirectory);
        _ownsDirectory = true;
    }

    /// <summary>
    /// Stages all mod paths declared by the given attribute.
    /// </summary>
    /// <param name="attribute">The PharosModsAttribute containing mod paths to stage.</param>
    /// <exception cref="ArgumentNullException">Thrown when attribute is null.</exception>
    public void StageFrom(PharosModsAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        foreach (var path in attribute.ModPaths)
        {
            if (Directory.Exists(path))
            {
                StageDirectory(path);
            }
            else if (File.Exists(path))
            {
                var extension = Path.GetExtension(path);
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    StageZip(path);
                }
                else
                {
                    StageFile(path);
                }
            }
            else
            {
                throw new FileNotFoundException($"Mod path does not exist: '{path}'", path);
            }
        }
    }

    /// <summary>
    /// Stages all files from a directory into the staging directory.
    /// </summary>
    /// <param name="sourcePath">The directory path to copy from.</param>
    /// <exception cref="FileNotFoundException">Thrown when the directory does not exist.</exception>
    public void StageDirectory(string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Mod directory does not exist: '{sourcePath}'", sourcePath);
        }

        var dirInfo = new DirectoryInfo(sourcePath);
        var targetDir = Path.Combine(StagingDirectory, dirInfo.Name);
        Directory.CreateDirectory(targetDir);

        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file.FullName);
            var targetPath = Path.Combine(targetDir, relativePath);
            var targetFileDir = Path.GetDirectoryName(targetPath);
            if (targetFileDir is not null && !Directory.Exists(targetFileDir))
            {
                Directory.CreateDirectory(targetFileDir);
            }
            File.Copy(file.FullName, targetPath, overwrite: true);
        }

        _stagedPaths.Add(targetDir);
    }

    /// <summary>
    /// Stages a zip file by copying it to the staging directory.
    /// </summary>
    /// <param name="zipPath">The path to the zip file.</param>
    /// <exception cref="FileNotFoundException">Thrown when the zip file does not exist.</exception>
    public void StageZip(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException($"Mod zip file does not exist: '{zipPath}'", zipPath);
        }

        var fileName = Path.GetFileName(zipPath);
        var targetPath = Path.Combine(StagingDirectory, fileName);
        File.Copy(zipPath, targetPath, overwrite: true);
        _stagedPaths.Add(targetPath);
    }

    /// <summary>
    /// Stages a single file by copying it to the staging directory.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public void StageFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Mod file does not exist: '{filePath}'", filePath);
        }

        var fileName = Path.GetFileName(filePath);
        var targetPath = Path.Combine(StagingDirectory, fileName);
        File.Copy(filePath, targetPath, overwrite: true);
        _stagedPaths.Add(targetPath);
    }

    /// <summary>
    /// Disposes the stager and deletes the staging directory if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsDirectory && Directory.Exists(StagingDirectory))
        {
            try
            {
                Directory.Delete(StagingDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup; directory may be locked by another process.
            }
        }
    }

    /// <summary>
    /// Collects all PharosModsAttribute instances from a test class and its assembly.
    /// </summary>
    /// <param name="testClass">The test class type to scan.</param>
    /// <returns>An enumerable of PharosModsAttribute instances.</returns>
    public static IEnumerable<PharosModsAttribute> CollectFromType(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        // Get attributes from the type itself
        var typeAttributes = testClass.GetCustomAttributes<PharosModsAttribute>(inherit: true);

        // Get attributes from the assembly
        var assemblyAttributes = testClass.Assembly.GetCustomAttributes<PharosModsAttribute>();

        return typeAttributes.Concat(assemblyAttributes);
    }

    /// <summary>
    /// Resolves a raw path against an assembly directory.
    /// Returns absolute paths unchanged; resolves relative paths against the base directory.
    /// </summary>
    /// <param name="rawPath">The path to resolve.</param>
    /// <param name="assemblyDirectory">The base directory for relative path resolution.</param>
    /// <returns>The resolved absolute path.</returns>
    public static string ResolvePath(string rawPath, string assemblyDirectory)
    {
        ArgumentNullException.ThrowIfNull(rawPath);
        ArgumentNullException.ThrowIfNull(assemblyDirectory);

        if (Path.IsPathRooted(rawPath))
        {
            return rawPath;
        }

        return Path.GetFullPath(Path.Combine(assemblyDirectory, rawPath));
    }
}
