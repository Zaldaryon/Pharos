using Vintagestory.API.Common;
using Vintagestory.Common;
using Zaldaryon.Pharos.Mods;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Which mod supplied an asset, and whether a patch changed it.</summary>
/// <remarks>
/// The target domain is the asset's own location, such as <c>game</c> or a mod ID. The provider is
/// the mod whose folder holds the bytes: a compatibility asset copied over another mod's asset
/// keeps the providing mod as its origin. See <c>docs/mod-loading.md</c>.
/// </remarks>
public sealed class AssetInspector
{
    private readonly ICoreAPI _api;
    private readonly ModLoadReport? _mods;
    private readonly Dictionary<string, string> _modsByAssetsFolder;

    internal AssetInspector(ICoreAPI api, ModLoadReport? mods = null)
    {
        _api = api;
        _mods = mods;
        _modsByAssetsFolder = [];
        foreach (Mod mod in api.ModLoader.Mods)
        {
            if (mod is not ModContainer container || string.IsNullOrEmpty(container.FolderPath)) continue;
            _modsByAssetsFolder[Normalize(Path.Combine(container.FolderPath, "assets"))] = mod.Info.ModID;
        }
    }

    /// <summary>The source of the asset at <paramref name="path"/>, or null when it does not exist.</summary>
    public AssetSource? TryOrigin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetLocation location = new(path);
        IAsset? asset = _api.Assets.TryGet(location, loadAsset: false);
        if (asset == null) return null;

        string requested = location.ToString();
        string provider = ProviderOf(asset);
        bool compatibility = !string.Equals(provider, location.Domain, StringComparison.OrdinalIgnoreCase);
        if (asset.IsPatched && !compatibility && _mods != null)
        {
            // A JSON patch keeps the original asset's origin. The patch is what supplied the final
            // bytes, so name its mod instead of the mod that supplied the pre-patch file.
            PatchResult? patch = _mods.Patches.LastOrDefault(candidate =>
                candidate.Outcome == PatchOutcome.Applied
                && string.Equals(candidate.Target, requested, StringComparison.OrdinalIgnoreCase));
            if (patch != null && !string.IsNullOrEmpty(patch.SourceMod)) provider = patch.SourceMod;
        }

        return new AssetSource(
            requested,
            location.Domain,
            provider,
            asset.Origin?.OriginPath,
            asset.IsPatched,
            compatibility);
    }

    /// <summary>The source of the asset at <paramref name="path"/>.</summary>
    /// <exception cref="KeyNotFoundException">No asset exists at <paramref name="path"/>.</exception>
    public AssetSource Origin(string path) =>
        TryOrigin(path) ?? throw new KeyNotFoundException($"No asset is loaded at '{path}'.");

    private string ProviderOf(IAsset asset)
    {
        if (asset.Origin is PathOrigin pathOrigin && !string.IsNullOrEmpty(pathOrigin.Domain))
        {
            if (string.Equals(pathOrigin.Domain, "game", StringComparison.OrdinalIgnoreCase)) return "game";
        }

        string? originPath = asset.Origin?.OriginPath;
        if (!string.IsNullOrEmpty(originPath)
            && _modsByAssetsFolder.TryGetValue(Normalize(originPath), out string? modId))
        {
            return modId;
        }

        return asset.Location.Domain;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
}

/// <summary>Where one asset came from.</summary>
public sealed record AssetSource(
    string Path,
    string Domain,
    string ModId,
    string? OriginPath,
    bool Patched,
    bool IsCompatibility);
