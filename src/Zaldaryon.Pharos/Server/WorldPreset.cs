using System.Security.Cryptography;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Server;

/// <summary>A reusable world recipe available to server scenarios.</summary>
public enum WorldPreset
{
    /// <summary>Create a new world from <see cref="ServerWorldOptions"/>.</summary>
    None,
    /// <summary>A survival play-style superflat world.</summary>
    SurvivalFlat,
    /// <summary>A survival standard world bounded to 512 by 512 blocks.</summary>
    SurvivalStandardSmall,
}

/// <summary>Generates and resolves immutable, versioned scenario world saves.</summary>
public static class WorldPresetCache
{
    private const int Schema = 1;
    private const int SmallDimension = 512;

    /// <summary>Resolves a preset to its cached save file. Cache misses give the command that creates it.</summary>
    public static ServerWorldOptions Resolve(ServerWorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Preset == WorldPreset.None) return options;
        if (options.Preset is not (WorldPreset.SurvivalFlat or WorldPreset.SurvivalStandardSmall))
            throw new ArgumentOutOfRangeException(nameof(options), options.Preset, "Unknown world preset.");
        if (options.SaveFile != null || options.SaveFileLocation != null)
            throw new ArgumentException("A world preset owns its save file and cannot be combined with SaveFile or SaveFileLocation.", nameof(options));

        PresetRecipe recipe = Recipe(options.Preset);
        ValidateOption(options.Seed, "424242", recipe.Seed, nameof(options.Seed));
        ValidateOption(options.PlayStyle, "creativebuilding", recipe.PlayStyle, nameof(options.PlayStyle));
        ValidateOption(options.WorldType, "superflat", recipe.WorldType, nameof(options.WorldType));
        ValidateOption(options.WorldConfigurationJson, "{}", recipe.WorldConfigurationJson, nameof(options.WorldConfigurationJson));
        string? save = TryCurrentSave(options.Preset, InstalledGame.Version);
        if (save == null)
            throw new FileNotFoundException($"Cached {options.Preset} preset is missing or invalid. Create it first with `pharos fixture --preset {CliName(options.Preset)} --game \"$VINTAGE_STORY\"`.", save);

        return options with
        {
            SaveFile = save,
            SaveFileLocation = null,
            Seed = recipe.Seed,
            PlayStyle = recipe.PlayStyle,
            WorldType = recipe.WorldType,
            WorldConfigurationJson = recipe.WorldConfigurationJson,
        };
    }

    private static void ValidateOption(string? actual, string defaultValue, string recipeValue, string property)
    {
        if (actual != defaultValue && actual != recipeValue)
            throw new ArgumentException($"{property} conflicts with the selected world preset; preset recipes own this setting.", "options");
    }

    /// <summary>Creates or verifies a preset in the cache, using an isolated server sandbox.</summary>
    /// <remarks>Run this from a standalone Pharos process. Generation is refused while a game session is active.</remarks>
    public static async Task<string> GenerateAsync(WorldPreset preset, CancellationToken cancellationToken = default)
    {
        if (preset == WorldPreset.None) throw new ArgumentOutOfRangeException(nameof(preset), "None has no cached save.");
        if (GameSessionActivity.Active != 0) throw new InvalidOperationException("World preset generation cannot run while a client or server session is active; run `pharos fixture --preset ...` in a standalone process.");
        string gameVersion = InstalledGame.Version;
        if (string.IsNullOrWhiteSpace(gameVersion) || gameVersion == "unknown") throw new InvalidOperationException("Could not read the loaded Vintage Story version for the world preset cache key.");

        string root = PresetRoot(preset, gameVersion);
        Directory.CreateDirectory(root);
        await using FileStream cacheLock = await AcquireLockAsync(Path.Combine(root, ".lock"), cancellationToken).ConfigureAwait(false);
        if (TryCurrentSave(preset, gameVersion) is { } currentSave) return currentSave;

        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        ServerSandbox sandbox = new(Path.Combine(staging, "sandbox"));
        EmbeddedServerHost? host = null;
        try
        {
            PresetRecipe recipe = Recipe(preset);
            host = EmbeddedServerHost.Boot(sandbox, recipe.Options);
            BlockPos spawn = host.RunOnGameThread(() => host.Server.DefaultSpawnPosition.AsBlockPos);
            await host.EnsureChunksLoadedAsync(spawn, spawn, ct: cancellationToken).ConfigureAwait(false);

            Vec3i mapSize = host.RunOnGameThread(() => host.Server.MapSize);
            if (preset == WorldPreset.SurvivalStandardSmall && (mapSize.X != SmallDimension || mapSize.Z != SmallDimension))
                throw new InvalidOperationException($"SurvivalStandardSmall generated a {mapSize.X}x{mapSize.Z} world; expected {SmallDimension}x{SmallDimension}.");

            string stagedSave = Path.Combine(staging, "world.vcdbs");
            await host.SaveWorldAsync(stagedSave, cancellationToken).ConfigureAwait(false);
            await host.DisposeAsync().ConfigureAwait(false);
            host = null;
            sandbox.Dispose();

            string hash = await HashFileAsync(stagedSave, cancellationToken).ConfigureAwait(false);
            PresetManifest manifest = new(Schema, preset, gameVersion, RecipeId(preset, gameVersion), hash, mapSize.X, mapSize.Y, mapSize.Z);
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);

            string generation = ChooseGenerationId(root, preset, gameVersion, hash);
            string published = Path.Combine(root, generation);
            if (Directory.Exists(published)) Directory.Delete(staging, recursive: true);
            else Directory.Move(staging, published);
            string pointerTemp = Path.Combine(root, ".current-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(pointerTemp, JsonSerializer.Serialize(new PresetPointer(generation), JsonOptions), cancellationToken).ConfigureAwait(false);
            File.Move(pointerTemp, Path.Combine(root, "current.json"), overwrite: true);
            return Path.Combine(published, "world.vcdbs");
        }
        catch
        {
            if (host != null) await host.DisposeAsync().ConfigureAwait(false);
            sandbox.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>The root folder used for cached world presets.</summary>
    public static string CacheRoot
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("PHAROS_CACHE");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdg)) return Path.Combine(xdg, "pharos");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(string.IsNullOrWhiteSpace(local) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache") : local, "Pharos");
        }
    }

    private static string PresetRoot(WorldPreset preset, string version) => Path.Combine(CacheRoot, "world-presets", Safe(version), preset.ToString());
    private static string? TryCurrentSave(WorldPreset preset, string version)
    {
        string root = PresetRoot(preset, version);
        return TryCurrentSaveAt(root, preset, version);
    }

    internal static string? TryCurrentSaveAt(string root, WorldPreset preset, string version)
    {
        string pointerPath = Path.Combine(root, "current.json");
        try
        {
            PresetPointer? pointer = JsonSerializer.Deserialize<PresetPointer>(File.ReadAllText(pointerPath));
            if (string.IsNullOrWhiteSpace(pointer?.Generation) || pointer.Generation != Path.GetFileName(pointer.Generation)) return null;
            string save = Path.Combine(root, pointer.Generation, "world.vcdbs");
            return TryReadValid(save, preset, version, out _) ? save : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return null; }
    }

    internal static string ChooseGenerationId(string root, WorldPreset preset, string version, string hash)
    {
        string canonical = "generation-" + hash[..16];
        string canonicalPath = Path.Combine(root, canonical);
        if (!Directory.Exists(canonicalPath)) return canonical;

        string canonicalSave = Path.Combine(canonicalPath, "world.vcdbs");
        if (TryReadValid(canonicalSave, preset, version, out PresetManifest? manifest)
            && string.Equals(manifest?.Sha256, hash, StringComparison.OrdinalIgnoreCase))
            return canonical;

        string unique;
        do unique = canonical + "-" + Guid.NewGuid().ToString("N");
        while (Directory.Exists(Path.Combine(root, unique)));
        return unique;
    }
    private static string Safe(string value) => string.Concat(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
    private static string CliName(WorldPreset preset) => preset switch { WorldPreset.SurvivalFlat => "survival-flat", WorldPreset.SurvivalStandardSmall => "survival-standard-small", _ => "none" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static PresetRecipe Recipe(WorldPreset preset) => preset switch
    {
        WorldPreset.SurvivalFlat => new("pharos-survival-flat-v1", "surviveandbuild", "superflat", "{}", new ServerWorldOptions
        {
            Seed = "pharos-survival-flat-v1", PlayStyle = "surviveandbuild", WorldType = "superflat", WorldConfigurationJson = "{}",
        }),
        WorldPreset.SurvivalStandardSmall => new("pharos-survival-standard-small-v1", "surviveandbuild", "standard",
            "{\"worldWidth\":\"512\",\"worldLength\":\"512\"}", new ServerWorldOptions
            {
                Seed = "pharos-survival-standard-small-v1", PlayStyle = "surviveandbuild", WorldType = "standard",
                WorldConfigurationJson = "{\"worldWidth\":\"512\",\"worldLength\":\"512\"}",
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    private static string RecipeId(WorldPreset preset, string version)
    {
        PresetRecipe recipe = Recipe(preset);
        byte[] input = System.Text.Encoding.UTF8.GetBytes($"{Schema}\n{version}\n{preset}\n{recipe.Seed}\n{recipe.PlayStyle}\n{recipe.WorldType}\n{recipe.WorldConfigurationJson}");
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static bool TryReadValid(string savePath, WorldPreset preset, string version, out PresetManifest? manifest)
    {
        manifest = null;
        string manifestPath = Path.Combine(Path.GetDirectoryName(savePath)!, "manifest.json");
        if (!File.Exists(savePath) || !File.Exists(manifestPath)) return false;
        try
        {
            manifest = JsonSerializer.Deserialize<PresetManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || manifest.Schema != Schema || manifest.Preset != preset || manifest.GameVersion != version || manifest.RecipeId != RecipeId(preset, version)) return false;
            if (preset == WorldPreset.SurvivalStandardSmall && (manifest.MapX != SmallDimension || manifest.MapZ != SmallDimension)) return false;
            return string.Equals(manifest.Sha256, HashFileAsync(savePath, CancellationToken.None).GetAwaiter().GetResult(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private sealed record PresetRecipe(string Seed, string PlayStyle, string WorldType, string WorldConfigurationJson, ServerWorldOptions Options);
    private sealed record PresetPointer(string Generation);
    private sealed record PresetManifest(int Schema, WorldPreset Preset, string GameVersion, string RecipeId, string Sha256, int MapX, int MapY, int MapZ);
}
