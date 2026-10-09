using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Translations;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Core;

/// <summary>What a reload did.</summary>
/// <param name="Category">The asset category reloaded.</param>
/// <param name="AssetsReloaded">How many assets the asset manager read again.</param>
/// <param name="Succeeded">False when the engine or a mod reported errors: shaders that did not compile.</param>
public sealed record ReloadResult(AssetCategory Category, int AssetsReloaded, bool Succeeded);

/// <summary>
/// What the game's <c>.reload</c> command does for each asset category, run on the client thread
/// so its results are read directly rather than parsed from a chat line.
/// </summary>
internal static class AssetReloader
{
    /// <summary>The categories <see cref="HeadlessClient.ReloadAllAsync"/> reloads, in order.</summary>
    public static readonly AssetCategory[] All = [AssetCategory.lang, AssetCategory.shapes, AssetCategory.textures, AssetCategory.shaders];

    public static AssetCategory Resolve(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return AssetCategory.categories.TryGetValue(code, out AssetCategory? category)
            ? category
            : throw new ArgumentException($"The game has no asset category '{code}'. It has: {string.Join(", ", AssetCategory.categories.Keys.Order())}.", nameof(code));
    }

    /// <summary>Reloads one category. Client thread.</summary>
    public static ReloadResult Reload(ClientMain game, AssetCategory category, LanguageDriver? lang)
    {
        if (category == AssetCategory.sounds || category == AssetCategory.music)
        {
            throw new NotSupportedException("Reloading sounds and music is not supported: the headless client plays no audio.");
        }

        if (category == AssetCategory.shaders && ShaderRegistry.SupressShaderAndBufferReloads)
        {
            throw new InvalidOperationException("The engine suppresses shader reloads right now (ShaderRegistry.SupressShaderAndBufferReloads).");
        }

        int assets = game.Platform.AssetManager.Reload(category);
        bool succeeded = true;
        if (category == AssetCategory.shaders)
        {
            bool engine = ShaderRegistry.ReloadShaders();
            bool mods = game.eventManager == null || game.eventManager.TriggerReloadShaders();
            succeeded = engine && mods;
        }
        else if (category == AssetCategory.shapes)
        {
            game.eventManager?.TriggerReloadShapes();
        }
        else if (category == AssetCategory.textures)
        {
            game.ReloadTextures();
        }
        else if (category == AssetCategory.lang)
        {
            GameLang.Load(game.Logger, game.AssetManager, Vintagestory.Client.NoObf.ClientSettings.Language);
            lang?.AfterReload();
        }

        return new ReloadResult(category, assets, succeeded);
    }
}
