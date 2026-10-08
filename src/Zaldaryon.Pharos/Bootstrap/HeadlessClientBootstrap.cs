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
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;

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

            // A client a scenario kept for its next test owns a window too. Two clients cannot
            // live side by side in one process: disposing one later tears down the other's
            // window. Whoever boots a client now no longer needs the kept one.
            ScenarioHostPool.Clear();
            HashSet<object> settingsWatchers = ClientSettingsWatchers.Snapshot();

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
            // Before any mod code is compiled, so nothing inlines the paths it rewrites.
            Server.SideDataPaths.Patch();
            // Logs, Saves, Cache and the rest: the engine opens files under all of them.
            GamePaths.EnsurePathsExist();

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

            if (options.BootMode == ClientBootMode.Engine)
            {
                if (options.DisableAudio || options.UseNullAudioDevice)
                {
                    // The engine skips every sound while the sound level is 0. Nothing reaches a
                    // speaker through the null device anyway, so an engine-mode client keeps the
                    // levels up and its sounds are played, and recorded, as in the game.
                    ClientSettings.MasterSoundLevel = 100;
                    ClientSettings.SoundLevel = 100;
                    ClientSettings.EntitySoundLevel = 100;
                    ClientSettings.AmbientSoundLevel = 100;
                    ClientSettings.WeatherSoundLevel = 100;
                    ClientSettings.MusicLevel = 100;
                }

                HeadlessClient engineClient = EngineClientStartup.Boot(options, tempDataPath);
                engineClient.SettingsWatchersAtBoot = settingsWatchers;
                return engineClient;
            }

            // 4. Create offscreen GLFW window with attached FBO
            HeadlessWindow window = new(options);

            // 5. Initialize platform
            ClientLogger logger = new();
            LogCapture logs = new(EnumAppSide.Client);
            logs.Attach(logger);
            ClientPlatformWindows platform = new(logger);
            platform.window = window.NativeWindow;
            platform.XPlatInterface.Window = (GameWindow)(object)window.NativeWindow;
            platform.WindowSize.Width = options.Width;
            platform.WindowSize.Height = options.Height;

            FieldInfo? amField = typeof(ClientPlatformWindows).GetField("assetManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (amField?.GetValue(platform) == null)
            {
                var am = new AssetManager(GamePaths.AssetsPath, EnumAppSide.Client);
                if (Directory.Exists(GamePaths.AssetsPath))
                {
                    try
                    {
                        am.InitAndLoadBaseAssets(logger, "textures");
                    }
                    catch
                    {
                        // Fall back to manual collections if base asset discovery fails
                    }
                }

                if (am.Origins == null || am.Origins.Count == 0)
                {
                    am.Origins = new List<IAssetOrigin>();
                    am.Assets = new Dictionary<AssetLocation, IAsset>();
                    typeof(AssetManager).GetField("assetsByCategory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(am, new Dictionary<string, List<IAsset>>());
                }

                AssetLocation cubeLoc = new("shapes/block/basic/cube.json");
                if (!am.Assets.ContainsKey(cubeLoc))
                {
                    const string basicCubeJson = """
                    {
                        "textures": { "all": "unknown" },
                        "elements": [
                            {
                                "name": "Cube",
                                "from": [ 0.0, 0.0, 0.0 ],
                                "to": [ 16.0, 16.0, 16.0 ],
                                "faces": {
                                    "north": { "texture": "#north", "uv": [ 0.0, 0.0, 16.0, 16.0 ] },
                                    "east": { "texture": "#east", "uv": [ 0.0, 0.0, 16.0, 16.0 ] },
                                    "south": { "texture": "#south", "uv": [ 0.0, 0.0, 16.0, 16.0 ] },
                                    "west": { "texture": "#west", "uv": [ 0.0, 0.0, 16.0, 16.0 ] },
                                    "up": { "texture": "#up", "uv": [ 0.0, 0.0, 16.0, 16.0 ] },
                                    "down": { "texture": "#down", "uv": [ 0.0, 0.0, 16.0, 16.0 ] }
                                }
                            }
                        ]
                    }
                    """;
                    IAssetOrigin origin = am.Origins.FirstOrDefault() ?? new PathOrigin("game", GamePaths.AssetsPath ?? AppContext.BaseDirectory);
                    Asset cubeAsset = new(System.Text.Encoding.UTF8.GetBytes(basicCubeJson), cubeLoc, origin)
                    {
                        FilePath = "shapes/block/basic/cube.json"
                    };
                    am.Assets[cubeLoc] = cubeAsset;
                }

                typeof(AssetManager).GetField("allAssetsLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.SetValue(am, true);

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

            HeadlessClient headless = new(client, platform, screenManager, runningGameScreen, window, options, tempDataPath)
            {
                Logs = logs,
                SettingsWatchersAtBoot = settingsWatchers,
            };

            // A fixture-mode client never joins: it has booted once it is built.
            logs.CompleteBoot();
            return headless;
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
