using System.Reflection;
using OpenTK.Windowing.Desktop;
using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Zaldaryon.Pharos.Audio;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Orchestrates the bootstrap of an offscreen, headless Vintage Story client.
/// </summary>
public static class HeadlessClientBootstrap
{
    private static readonly object _bootLock = new();

    /// <summary>
    /// Bootstraps a headless ClientMain instance with an offscreen GLFW context and attached FBO.
    /// </summary>
    public static HeadlessClient Boot(HeadlessClientOptions? options = null)
    {
        lock (_bootLock)
        {
            options ??= new HeadlessClientOptions();

            // 1. Initialize resolver and locate game installation
            HeadlessPlatformResolver.Initialize(options);
            string gamePath = HeadlessPlatformResolver.ResolveGamePath(options.GameInstallPath);

            // 2. Configure isolated data and asset paths
            string? tempDataPath = null;
            string dataPath = options.DataPath ?? string.Empty;
            if (string.IsNullOrEmpty(dataPath))
            {
                tempDataPath = Path.Combine(Path.GetTempPath(), "pharos-test-" + Guid.NewGuid().ToString("N")[..8]);
                dataPath = tempDataPath;
            }

            GamePaths.DataPath = dataPath;
            GamePaths.EnsurePathExists(dataPath);

            string assetsPath = options.AssetsPath ?? Path.Combine(gamePath, "assets");
            if (Directory.Exists(assetsPath))
            {
                SetAssetsPath(assetsPath);
            }

            // 3. Configure audio subsystem and client settings
            if (options.DisableAudio || options.UseNullAudioDevice)
            {
                NullAudioPatcher.Patch();

                ClientSettings.MasterSoundLevel = 0;
                ClientSettings.SoundLevel = 0;
                ClientSettings.EntitySoundLevel = 0;
                ClientSettings.AmbientSoundLevel = 0;
                ClientSettings.WeatherSoundLevel = 0;
                ClientSettings.MusicLevel = 0;
            }

            ClientSettings.ScreenWidth = options.Width;
            ClientSettings.ScreenHeight = options.Height;
            ClientSettings.VsyncMode = 0;
            ClientSettings.GameWindowMode = 0;

            // 4. Create offscreen GLFW window with attached FBO
            HeadlessWindow window = new(options);

            // 5. Initialize platform
            ClientLogger logger = new();
            ClientPlatformWindows platform = new(logger);
            platform.window = window.NativeWindow;
            platform.XPlatInterface.Window = (GameWindow)(object)window.NativeWindow;
            platform.WindowSize.Width = options.Width;
            platform.WindowSize.Height = options.Height;

            FieldInfo? amField = typeof(ClientPlatformWindows).GetField("assetManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (amField?.GetValue(platform) == null)
            {
                var am = new AssetManager(GamePaths.AssetsPath, EnumAppSide.Client);
                am.Origins = new List<IAssetOrigin>();
                am.Assets = new Dictionary<AssetLocation, IAsset>();
                typeof(AssetManager).GetField("assetsByCategory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.SetValue(am, new Dictionary<string, List<IAsset>>());
                amField?.SetValue(platform, am);
            }

            // 6. Wire ScreenManager and instantiate ClientMain via GuiScreenRunningGame
            lock (ScreenManager.MainThreadTasks)
            {
                ScreenManager.MainThreadTasks.Clear();
            }

            ScreenManager.Platform = platform;
            ScreenManager.ParsedArgs ??= new ClientProgramArgs();
            ScreenManager screenManager = new(platform);
            GuiScreenRunningGame runningGameScreen = new(screenManager, null);
            typeof(ScreenManager).GetField("CurrentScreen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(screenManager, runningGameScreen);

            FieldInfo? field = typeof(GuiScreenRunningGame).GetField("runningGame", BindingFlags.NonPublic | BindingFlags.Instance);
            ClientMain? client = (ClientMain?)field?.GetValue(runningGameScreen);
            if (client == null)
            {
                client = new ClientMain(runningGameScreen, platform);
                field?.SetValue(runningGameScreen, client);
            }

            client.modHandler ??= new SystemModHandler(client);
            client.clientSystems ??= new ClientSystem[] { client.modHandler };
            client.TerrainChunkTesselator ??= new ChunkTesselator(client);

            return new HeadlessClient(client, platform, screenManager, runningGameScreen, window, options, tempDataPath);
        }
    }

    private static void SetAssetsPath(string assetsPath)
    {
        PropertyInfo? prop = typeof(GamePaths).GetProperty("AssetsPath", BindingFlags.Public | BindingFlags.Static);
        if (prop != null)
        {
            MethodInfo? setter = prop.GetSetMethod(nonPublic: true);
            setter?.Invoke(null, [assetsPath]);
        }
    }
}
