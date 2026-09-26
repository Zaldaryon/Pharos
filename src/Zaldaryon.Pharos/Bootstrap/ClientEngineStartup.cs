using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Zaldaryon.Pharos.Core;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Fills in the engine startup state that <see cref="HeadlessClientBootstrap"/> deliberately
/// skips, so that <c>GuiScreenRunningGame.RenderToPrimary</c> can run in a fixture-mode client.
/// </summary>
/// <remarks>
/// <para>
/// The headless bootstrap constructs <see cref="ClientMain"/> and <see cref="GuiScreenRunningGame"/>
/// directly instead of going through <c>ScreenManager.Start</c> and <c>GuiScreenRunningGame.Start</c>.
/// That is what keeps the harness free of the seven engine worker threads, but it also leaves
/// four pieces of startup state unset, and <c>ClientMain.MainRenderLoop</c> dereferences all of
/// them:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>ScreenManager.Start</c> loads the language table and registers the default hotkeys.
/// <c>ClientMain.MainRenderLoop</c> calls <c>UpdateFreeMouse</c>, which reads
/// <c>ScreenManager.hotkeyManager.HotKeys["togglemousecontrol"]</c>.
/// </description></item>
/// <item><description>
/// <c>ClientSystemStartup.HandleLevelInitialize</c> fills in the world map chunk and region sizes
/// from the server. Without them <c>ClientWorldMap.MapRegionSizeInChunks</c> divides by zero, and
/// <c>DefaultShaderUniforms.Update</c> reads the climate map through it.
/// </description></item>
/// <item><description>
/// <c>SystemCalendar.OnBlockTexturesLoaded</c> creates the <c>ClientGameCalendar</c>. The render
/// loop writes <c>GameWorldCalendar.Timelapse</c> and the sky and terrain shaders read its sun and
/// daylight values.
/// </description></item>
/// <item><description>
/// The server assigns the own player entity, which the render loop needs for the camera
/// position, the frustum culler, and the fog uniforms.
/// </description></item>
/// </list>
/// <para>
/// Everything here is opt-in. <see cref="Prepare"/> is never called by the bootstrap, so
/// fixture-mode chunk tests keep running against the exact state they were written for.
/// </para>
/// <para>
/// This is a partial shim. It opens the render gate and gets
/// <c>ClientMain.MainRenderLoop</c> as far as dispatching <c>EnumRenderStage.Before</c>. It does
/// not make the render pass complete: that additionally needs the mod config layer, the block
/// texture atlas, and a shader compile.
/// </para>
/// </remarks>
public static class ClientEngineStartup
{
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo? s_calendarField =
        typeof(ClientMain).GetField("GameWorldCalendar", AnyInstance);

    private static readonly FieldInfo? s_quantityAtlassesField =
        typeof(ChunkTesselator).GetField("quantityAtlasses", AnyInstance);

    private static readonly FieldInfo? s_playerWorldDataField =
        typeof(ClientPlayer).GetField("worlddata", AnyInstance);

    private static readonly FieldInfo? s_playerUidField =
        typeof(ClientWorldPlayerData).GetField("PlayerUID", AnyInstance);

    private static bool s_languageLoaded;
    private static void EnsureShaders(HeadlessClient client)
    {
        // ShaderRegistry builds the ShaderProgram instances, but they stay inert until
        // api.Shader.ReloadShaders(), which normally runs from ClientSystemStartup once the
        // server signals readiness. ChunkRenderer.RenderOpaque dereferences them.
        //
        // The compiled programs are named objects in the live GL context, so this is tracked per
        // client rather than once per process: a later client in the same test process gets a
        // fresh context and has to compile again.
        if (s_shadersCompiled.Contains(client.Client)) return;

        client.Client.api.Shader.ReloadShaders();
        s_shadersCompiled.Add(client.Client);
    }

    private static readonly HashSet<ClientMain> s_shadersCompiled = [];

    /// <summary>
    /// Prepares the client so the vanilla render pass can start. Safe to call more than once.
    /// </summary>
    /// <param name="client">The headless client to prepare.</param>
    /// <param name="playerPosition">
    /// Where to place the synthesized player. Must be inside the mock world bounds.
    /// </param>
    /// <returns>
    /// The first startup step that could not be completed, or <see langword="null"/> when all
    /// four succeeded. A <see langword="null"/> return does not mean the render pass runs to
    /// completion: the mod config layer (<c>ClientMain.ColorMaps</c>, the block texture atlas)
    /// and the shader compile are still absent, so <c>AmbientManager.UpdateAmbient</c> throws on
    /// the first <c>EnumRenderStage.Before</c> dispatch.
    /// </returns>
    public static string? Prepare(HeadlessClient client, Vec3d? playerPosition = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        EnsureMainThreadIdentity();
        EnsureLanguageAndHotkeys(client);
        EnsureWorldMapSizing(client.Client);
        EnsurePlayer(client.Client, playerPosition ?? DefaultPlayerPosition(client));
        EnsureWorldCalendar(client.Client);
        EnsureColorMaps(client.Client);
        EnsureShaderUniformsWiring(client);
        EnsureBlockTextureAtlas(client);
        EnsureShaders(client);
        EnsureFrameBuffers(client);
        EnsureRenderSystems(client.Client);
        client.Client.Reset3DProjection();
        return null;
    }

    private static void EnsureMainThreadIdentity()
    {
        // ClientPlatformWindows.LoadTexture, TextureAtlasManager.RuntimeCreateNewAtlas and
        // friends refuse to touch GL unless RuntimeEnv.MainThreadId matches the current thread.
        // ScreenManager.Start assigns it, and the headless bootstrap never calls that, so it
        // stays 0 and every texture or atlas creation throws.
        if (RuntimeEnv.MainThreadId != Environment.CurrentManagedThreadId)
        {
            RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
        }
    }

    private static void EnsureRenderSystems(ClientMain client)
    {
        // HeadlessClientBootstrap replaces clientSystems with a single mod handler, so
        // ClientEventManager.renderersByStage stays empty for the draw-capable stages and
        // MainRenderLoop dispatches nothing that issues a GL draw call. ClientMain.Start is the
        // only vanilla place that builds the array, and it also spawns seven worker threads.
        List<ClientSystem> systems = [.. client.clientSystems ?? Array.Empty<ClientSystem>()];

        foreach (string name in DrawCapableRenderSystems)
        {
            ClientSystem? system = ConstructRenderSystem(client, name);
            if (system == null) continue;
            systems.Add(system);

            // SystemRenderSkyColor.OnLevelFinalize loads the sky and sun glow textures, and
            // SystemRenderSunMoon.OnLevelFinalize loads the celestial bodies.
            typeof(ClientSystem)
                .GetMethod("OnLevelFinalize", AnyInstance, null, Type.EmptyTypes, null)
                ?.Invoke(system, null);
        }

        client.clientSystems = systems.ToArray();
    }

    private static readonly string[] DrawCapableRenderSystems =
    [
        "Vintagestory.Client.NoObf.SystemRenderSkyColor",
        "Vintagestory.Client.NoObf.SystemRenderSunMoon",
        // SystemRenderTerrain draws the chunk mesh pools through the multi-draw overload.
        // It needs game.chunkRenderer, which HeadlessClient.InitializeMockWorld already builds,
        // so its own OnBlockTexturesLoaded is deliberately not called here.
        "Vintagestory.Client.NoObf.SystemRenderTerrain",
    ];

    private static ClientSystem? ConstructRenderSystem(ClientMain client, string typeName)
    {
        Type? type = typeof(ClientMain).Assembly.GetType(typeName);
        ConstructorInfo? ctor = type?.GetConstructor(AnyInstance, null, [typeof(ClientMain)], null);
        if (ctor == null) return null;

        try
        {
            return ctor.Invoke([client]) as ClientSystem;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <c>ClientMain.MainRenderLoop</c> would get past the player null check today.
    /// This mirrors the gate in <c>DeterministicFrameController.Step</c>.
    /// </summary>
    public static bool IsRenderGateOpen(HeadlessClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Client.Player?.Entity?.Pos != null;
    }

    private static void EnsureLanguageAndHotkeys(HeadlessClient client)
    {
        if (!s_languageLoaded)
        {
            Lang.Load(client.Platform.Logger, client.Platform.AssetManager, "en");
            s_languageLoaded = true;
        }

        if (ScreenManager.hotkeyManager.HotKeys.Count == 0)
        {
            ScreenManager.hotkeyManager.RegisterDefaultHotKeys();
        }
    }

    private static void EnsureWorldMapSizing(ClientMain client)
    {
        // ClientSystemStartup.HandleLevelInitialize copies these off Packet_LevelInitialize.
        // Only the defaults are filled in when nothing has set them yet, so a real
        // login still wins.
        if (client.WorldMap.ServerChunkSize <= 0) client.WorldMap.ServerChunkSize = 32;
        if (client.WorldMap.MapChunkSize <= 0) client.WorldMap.MapChunkSize = 32;
        if (client.WorldMap.regionSize <= 0) client.WorldMap.regionSize = 512;
        if (client.WorldMap.MaxViewDistance <= 0) client.WorldMap.MaxViewDistance = 8;
    }

    private static void EnsurePlayer(ClientMain client, Vec3d position)
    {
        if (client.EntityPlayer != null)
        {
            client.EntityPlayer.Pos.SetPos(position);
            client.EntityPlayer.CameraPos.Set(position);
            client.MainCamera?.OnPlayerPhysicsTick(0f, position);
            return;
        }

        ClientPlayer player = new(client);
        EntityPlayer entity = new()
        {
            Api = client.api,
            World = client,
        };
        entity.Pos.SetPos(position);
        entity.CameraPos.Set(position);
        entity.WatchedAttributes.SetString("playerUID", RenderPassPlayerUid);

        // Entity.Properties has a protected setter, so its backing field is used instead. The
        // base Entity.Initialize path is not usable here: it needs a populated class registry,
        // which only a real mod load provides.
        SetEntityProperties(entity, new EntityProperties { Tags = new TagSetFast() });

        // EntityPlayer.canStandUp reads SelectionBox during the camera's before-render pass, and
        // the swim offset and frustum sphere read it too. Entity.Initialize would normally set
        // these from EntityProperties.CollisionBoxSize.
        entity.SetCollisionBox(PlayerBoxWidth, PlayerBoxHeight);
        entity.SetSelectionBox(PlayerBoxWidth, PlayerBoxHeight);

        object? worldData = s_playerWorldDataField?.GetValue(player);
        worldData?.GetType()
            .GetProperty("EntityPlayer", AnyInstance)
            ?.SetValue(worldData, entity);

        s_playerUidField?.SetValue(worldData, RenderPassPlayerUid);
        client.PlayersByUid[RenderPassPlayerUid] = player;
        client.player = player;

        client.MainCamera?.OnPlayerPhysicsTick(0f, position);
    }

    private const float PlayerBoxWidth = 0.7f;
    private const float PlayerBoxHeight = 1.8f;

    private const string RenderPassPlayerUid = "pharos-render-pass";

    private static void SetEntityProperties(EntityPlayer entity, EntityProperties properties)
    {
        FieldInfo? backing = typeof(Entity).GetField("<Properties>k__BackingField", AnyInstance)
            ?? typeof(Entity).GetField("Properties", AnyInstance);
        backing?.SetValue(entity, properties);
    }

    private static void EnsureWorldCalendar(ClientMain client)
    {
        if (s_calendarField?.GetValue(client) != null) return;

        Type? calendarType = typeof(ClientMain).Assembly
            .GetType("Vintagestory.Common.ClientGameCalendar");

        foreach (ConstructorInfo ctor in calendarType?.GetConstructors(AnyInstance) ?? Array.Empty<ConstructorInfo>())
        {
            ParameterInfo[] parameters = ctor.GetParameters();
            if (parameters.Length == 0) continue;

            object?[] arguments = new object?[parameters.Length];
            bool resolved = true;

            for (int i = 0; i < parameters.Length; i++)
            {
                string name = parameters[i].ParameterType.Name;
                if (name.Contains("WorldAccessor")) arguments[i] = client;
                else if (name.Contains("IAsset")) arguments[i] = client.Platform.AssetManager?.Get(CalendarTexturePath);
                else if (name.Contains("Int32")) arguments[i] = 0;
                else if (name.Contains("Int64")) arguments[i] = 28000L;
                else { resolved = false; break; }
            }

            if (!resolved || arguments.Any(a => a == null)) continue;

            s_calendarField?.SetValue(client, ctor.Invoke(arguments));
            return;
        }
    }

    private const string CalendarTexturePath = "textures/environment/sunlight.png";

    private static void EnsureColorMaps(ClientMain client)
    {
        // ClientCoreAPI.RegisterColorMap is normally driven by each mod's config/colormaps.json
        // during mod load. The harness loads no mods, so ClientMain.ColorMaps stays empty and
        // AmbientManager.UpdateAmbient throws KeyNotFoundException on "climateWaterTint".
        if (client.ColorMaps is { Count: > 0 }) return;

        int registered = 0;
        foreach (AssetLocation location in ColorMapAssetLocations())
        {
            registered += RegisterColorMaps(client, location);
        }

        if (registered == 0) return;

        // Fills ColorMap.Pixels and OuterSize from the PNGs. LoadIntoBlockTextureAtlas is off,
        // because the block texture atlas is not built in fixture mode; ApplyColorMapOnRgba
        // only reads Pixels and OuterSize.
        client.WorldMap.LoadColorMaps();

        // Allocates shUniforms.ColorMapRects4, a 40 vec4 array. Every shader that includes
        // colormap.vsh calls Uniforms4("colorMapRects", 40, ColorMapRects4) from
        // ShaderProgramBase.Use, and OpenTK marshals a null array into glUniform4fv as a null
        // pointer with a count of 40. The driver then dereferences it and the process takes a
        // 0xC0000005 access violation, which is how ChunkRenderer.RenderOpaque used to die.
        // Chunkopaque is the first draw-capable shader to include colormap.vsh, which is why
        // the sky rendered and the terrain did not.
        client.WorldMap.PopulateColorMaps();
    }

    private static void EnsureShaderUniformsWiring(HeadlessClient client)
    {
        // ShaderProgramBase.Use reads the uniforms from ScreenManager.Platform.ShaderUniforms,
        // which ClientPlatformWindows initialises to its own DefaultShaderUniforms instance.
        // ClientMain.Start is the only vanilla code that replaces it with ClientMain.shUniforms.
        // Without that swap, PopulateColorMaps fills one object and Use() reads another, so
        // colorMapRects stays null on the instance that matters.
        client.Platform.ShaderUniforms = client.Client.shUniforms;
    }

    private static IEnumerable<AssetLocation> ColorMapAssetLocations()
    {
        string? assetsPath = GamePaths.AssetsPath;
        if (string.IsNullOrEmpty(assetsPath) || !Directory.Exists(assetsPath)) yield break;

        foreach (string domain in Directory.GetDirectories(assetsPath))
        {
            string name = Path.GetFileName(domain);
            if (!File.Exists(Path.Combine(domain, "config", "colormaps.json"))) continue;
            yield return new AssetLocation(name, "config/colormaps.json");
        }
    }

    private static int RegisterColorMaps(ClientMain client, AssetLocation location)
    {
        IAsset? asset;
        try
        {
            asset = client.Platform.AssetManager?.Get(location);
        }
        catch (Exception)
        {
            // AssetManager.Get throws for assets outside this app side. A server-only mod can
            // still ship a config/colormaps.json on disk.
            return 0;
        }

        if (asset?.Data == null) return 0;

        JArray? entries;
        try
        {
            // Vintage Story config files use unquoted property names, which Newtonsoft's
            // reader accepts.
            using StringReader stringReader = new(Encoding.UTF8.GetString(asset.Data));
            using JsonTextReader jsonReader = new(stringReader) { DateParseHandling = DateParseHandling.None };
            entries = JToken.ReadFrom(jsonReader) as JArray;
        }
        catch (Exception)
        {
            return 0;
        }

        if (entries == null) return 0;

        int count = 0;
        foreach (JToken entry in entries)
        {
            if (entry is not JObject obj) continue;

            string? code = obj["code"]?.Value<string>();
            string? textureBase = obj["texture"]?["base"]?.Value<string>();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(textureBase)) continue;

            ColorMap map = new()
            {
                Code = code,
                Padding = obj["padding"]?.Value<int>() ?? 0,
                ExtraFlags = obj["extraFlags"]?.Value<int>() ?? 0,
                LoadIntoBlockTextureAtlas = false,
                Texture = new CompositeTexture(new AssetLocation(location.Domain, textureBase)),
            };

            client.api.RegisterColorMap(map);
            count++;
        }

        return count;
    }

    private static void EnsureBlockTextureAtlas(HeadlessClient client)
    {
        // ChunkTesselator.UpdateForAtlasses sets quantityAtlasses from the block texture atlas
        // count, and every emit loop iterates quantityAtlasses times. With no atlas the loops run
        // zero times, populateTesselatedChunkPart returns empty parts, and the chunk reaches the
        // render pool carrying no MeshData at all, so the pools stay empty even though the block
        // is drawable. Only ChunkTesselator.ReloadTextures and
        // RuntimeCreateNewBlockTextureAtlas call UpdateForAtlasses, and neither is reachable in a
        // headless client, so it is invoked here.
        BlockTextureAtlasManager atlas = client.Client.BlockAtlasManager;
        EnsureAtlasTextureEntry(atlas, client);

        ChunkTesselator? tesselator = client.Client.TerrainChunkTesselator;
        if (tesselator == null) return;
        if ((int)(s_quantityAtlassesField?.GetValue(tesselator) ?? 0) > 0) return;

        // Runs after FixtureBlockRegistry has populated FastBlockTextureSubidsByBlockAndFace,
        // because UpdateForAtlasses caches that array by reference.
        tesselator.ReloadTextures();
    }

    private static void EnsureAtlasTextureEntry(BlockTextureAtlasManager atlas, HeadlessClient client)
    {
        if (atlas.TextureAtlasPositionsByTextureSubId is { Length: > 0 } positions
            && positions[0] != null
            && atlas.AtlasTextures is { Count: > 0 })
        {
            return;
        }

        if (atlas.AtlasTextures == null)
        {
            atlas.AtlasTextures = [];
        }

        if (atlas.AtlasTextures.Count == 0)
        {
            int textureId = OpenTK.Graphics.OpenGL.GL.GenTexture();
            atlas.AtlasTextures.Add(new LoadedTexture(client.Client.api, textureId, 16, 16));
        }

        TextureAtlasPosition[]? slots = atlas.TextureAtlasPositionsByTextureSubId;
        if (slots == null || slots.Length == 0 || slots[0] == null)
        {
            // BleedingCubeTesselator indexes this array directly, so subid 0 must be non-null.
            // Only the rectangle is read on the terrain path, so pixels are irrelevant.
            slots = slots is { Length: > 0 } ? slots : new TextureAtlasPosition[1];
            atlas.TextureAtlasPositionsByTextureSubId = slots;

            slots[0] = new TextureAtlasPosition
            {
                atlasTextureId = atlas.AtlasTextures[0].TextureId,
                atlasNumber = 0,
                x1 = 0f,
                y1 = 0f,
                x2 = 1f,
                y2 = 1f,
            };
        }
    }

    private static void EnsureFrameBuffers(HeadlessClient client)
    {
        // ChunkRenderer.RenderOpaque reads Platform.FrameBuffers[5].DepthTextureId, and
        // GuiScreenRunningGame.RenderAfterPostProcessing unloads EnumFrameBuffer.Transparent.
        if (client.Platform.FrameBuffers is { Count: > 0 }) return;

        // ClientPlatformWindows.RebuildFrameBuffers disposes the previous list before
        // installing the new one, and the headless bootstrap never creates the first one, so
        // the field is still null and the dispose pass throws.
        FieldInfo? field = typeof(ClientPlatformWindows).GetField(
            "frameBuffers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field?.SetValue(client.Platform, new List<FrameBufferRef>());

        client.Platform.RebuildFrameBuffers();
    }

    private static Vec3d DefaultPlayerPosition(HeadlessClient client)
    {
        // One block above sea level, well inside the default mock world.
        return new Vec3d(client.Client.WorldMap.MapSizeX / 2.0, 70, client.Client.WorldMap.MapSizeZ / 2.0);
    }
}
