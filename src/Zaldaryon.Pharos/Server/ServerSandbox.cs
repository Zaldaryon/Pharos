using System;
using System.IO;
using System.Threading;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Manages an isolated filesystem sandbox for server instances.
/// </summary>
/// <remarks>
/// <para>
/// ServerSandbox creates a temporary directory structure under the system temp path
/// with isolated Mods, ModConfig, and Saves subdirectories. This ensures each server
/// instance operates in complete filesystem isolation from others.
/// </para>
/// <para>
/// The sandbox supports staging mod files (.zip, .dll) and configuration files (.json)
/// before server launch, ensuring mods and configs are available when StartServerSide
/// is called during server initialization.
/// </para>
/// <para>
/// Cleanup uses exponential backoff retry logic to handle SQLite file locks that may
/// persist briefly after server shutdown.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using ServerSandbox sandbox = new();
/// sandbox.StageModFile("/path/to/mymod.dll");
/// sandbox.StageConfigFile("mymod", "{\"enabled\": true}");
///
/// using var server = EmbeddedServerHost.Boot(sandbox: sandbox);
/// // ... run tests ...
/// // Cleanup happens automatically on dispose
/// </code>
/// </example>
public sealed class ServerSandbox : IDisposable
{
    private bool _disposed;
    private readonly bool _deleteOnDispose;

    /// <summary>
    /// The root path of the sandbox directory.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// The path to the Mods subdirectory where staged mod files are placed.
    /// </summary>
    public string ModsPath { get; }

    /// <summary>
    /// The path to the ModConfig subdirectory where staged configuration files are placed.
    /// </summary>
    public string ConfigPath { get; }

    /// <summary>
    /// The path to the Saves subdirectory where world saves are stored.
    /// </summary>
    public string SavesPath { get; }

    /// <summary>
    /// Creates a new server sandbox with an isolated temporary directory.
    /// </summary>
    /// <param name="deleteOnDispose">Whether to delete the sandbox directory on dispose. Default is true.</param>
    public ServerSandbox(bool deleteOnDispose = true)
    {
        _deleteOnDispose = deleteOnDispose;

        string guid = Guid.NewGuid().ToString("N")[..8];
        RootPath = Path.Combine(Path.GetTempPath(), $"pharos-server-{guid}");
        ModsPath = Path.Combine(RootPath, "Mods");
        ConfigPath = Path.Combine(RootPath, "ModConfig");
        SavesPath = Path.Combine(RootPath, "Saves");

        // Create all directories immediately
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ModsPath);
        Directory.CreateDirectory(ConfigPath);
        Directory.CreateDirectory(SavesPath);
    }

    /// <summary>
    /// Creates a server sandbox using an existing directory path.
    /// </summary>
    /// <param name="existingPath">The path to use as the sandbox root.</param>
    /// <param name="deleteOnDispose">Whether to delete the sandbox directory on dispose. Default is false for existing paths.</param>
    /// <exception cref="ArgumentException"><paramref name="existingPath"/> is null or whitespace.</exception>
    public ServerSandbox(string existingPath, bool deleteOnDispose = false)
    {
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(existingPath));
        }

        _deleteOnDispose = deleteOnDispose;
        RootPath = existingPath;
        ModsPath = Path.Combine(RootPath, "Mods");
        ConfigPath = Path.Combine(RootPath, "ModConfig");
        SavesPath = Path.Combine(RootPath, "Saves");

        // Ensure all directories exist
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ModsPath);
        Directory.CreateDirectory(ConfigPath);
        Directory.CreateDirectory(SavesPath);
    }

    /// <summary>
    /// Stages a mod file into the sandbox's Mods directory.
    /// </summary>
    /// <param name="sourcePath">The path to the mod file (.zip or .dll) to stage.</param>
    /// <returns>The destination path of the staged file.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourcePath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    /// <exception cref="ObjectDisposedException">The sandbox has been disposed.</exception>
    public string StageModFile(string sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or whitespace.", nameof(sourcePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Mod file not found.", sourcePath);
        }

        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(ModsPath, fileName);

        File.Copy(sourcePath, destPath, overwrite: true);

        return destPath;
    }

    /// <summary>
    /// Stages a configuration file into the sandbox's ModConfig directory.
    /// </summary>
    /// <param name="key">The configuration key (used as the filename without extension).</param>
    /// <param name="json">The JSON content to write to the configuration file.</param>
    /// <returns>The path of the created configuration file.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> or <paramref name="json"/> is null or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The sandbox has been disposed.</exception>
    public string StageConfigFile(string key, string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON content cannot be null or whitespace.", nameof(json));
        }

        // Sanitize the key to be a valid filename
        string sanitizedKey = SanitizeFileName(key);
        string destPath = Path.Combine(ConfigPath, sanitizedKey + ".json");

        File.WriteAllText(destPath, json);

        return destPath;
    }

    /// <summary>
    /// Stages raw content as a file in a specified subdirectory.
    /// </summary>
    /// <param name="subdirectory">The subdirectory name within the sandbox.</param>
    /// <param name="fileName">The name of the file to create.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <returns>The path of the created file.</returns>
    /// <exception cref="ArgumentException">Any argument is null or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The sandbox has been disposed.</exception>
    public string StageFile(string subdirectory, string fileName, byte[] content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(subdirectory))
        {
            throw new ArgumentException("Subdirectory cannot be null or whitespace.", nameof(subdirectory));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));
        }

        ArgumentNullException.ThrowIfNull(content);

        string dirPath = Path.Combine(RootPath, subdirectory);
        Directory.CreateDirectory(dirPath);

        string filePath = Path.Combine(dirPath, fileName);
        File.WriteAllBytes(filePath, content);

        return filePath;
    }

    /// <summary>
    /// Cleans up the sandbox directory with retry logic for locked files.
    /// </summary>
    /// <param name="maxRetries">Maximum number of deletion attempts. Default is 5.</param>
    /// <param name="initialDelayMs">Initial delay between retries in milliseconds. Default is 100.</param>
    /// <returns>True if cleanup succeeded; false if files remain locked after all retries.</returns>
    /// <remarks>
    /// Uses exponential backoff: delay doubles after each failed attempt.
    /// This handles SQLite locks on VS world files that may persist briefly after server shutdown.
    /// </remarks>
    public bool Cleanup(int maxRetries = 5, int initialDelayMs = 100)
    {
        if (!Directory.Exists(RootPath))
        {
            return true;
        }

        int delayMs = initialDelayMs;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
                return true;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(delayMs);
                delayMs *= 2; // Exponential backoff
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to clean up specific files that may be locked.
    /// </summary>
    /// <param name="pattern">The file pattern to match (e.g., "*.db").</param>
    /// <param name="maxRetries">Maximum number of deletion attempts per file.</param>
    /// <param name="initialDelayMs">Initial delay between retries in milliseconds.</param>
    /// <returns>The number of files that could not be deleted.</returns>
    public int CleanupFiles(string pattern, int maxRetries = 5, int initialDelayMs = 100)
    {
        if (!Directory.Exists(RootPath))
        {
            return 0;
        }

        int failedCount = 0;
        string[] files = Directory.GetFiles(RootPath, pattern, SearchOption.AllDirectories);

        foreach (string file in files)
        {
            if (!TryDeleteFile(file, maxRetries, initialDelayMs))
            {
                failedCount++;
            }
        }

        return failedCount;
    }

    private static bool TryDeleteFile(string path, int maxRetries, int initialDelayMs)
    {
        int delayMs = initialDelayMs;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }

        return false;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = name;

        foreach (char c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        return sanitized;
    }

    /// <summary>
    /// Disposes the sandbox and optionally deletes the directory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_deleteOnDispose)
        {
            // Best-effort cleanup with retries
            Cleanup(maxRetries: 3, initialDelayMs: 50);
        }
    }
}
