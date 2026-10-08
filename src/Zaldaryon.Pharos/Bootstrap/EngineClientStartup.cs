using System.Reflection;
using OpenTK.Windowing.Desktop;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.Gui;
using Vintagestory.Client.NoObf;
using Vintagestory.ClientNative;
using Vintagestory.Common;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Runs the vanilla client startup in a headless process, for <see cref="ClientBootMode.Engine"/>.
/// </summary>
/// <remarks>
/// <para>
/// This follows <c>ClientProgram.Start</c>, <c>ScreenManager.Start</c> and the
/// <c>DoGameInitStage1</c> to <c>DoGameInitStage4</c> chain, with three deliberate differences:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Everything runs synchronously on the calling thread. Vanilla loads assets on a background
/// thread and polls for completion from the loading screen, which a test has no use for.
/// </description></item>
/// <item><description>
/// No session key is validated and no version check is requested. The client is marked offline,
/// the same state vanilla falls back to when the auth server is unreachable, so nothing in the
/// startup needs outbound network access. That is what makes this mode usable in CI and in
/// sandboxed cloud runners. <see cref="OfflineSessionPatcher"/> keeps the running game screen
/// from being swapped for the login screen over the missing session key.
/// </description></item>
/// <item><description>
/// No main menu is composed: the running game screen becomes the current screen directly, and
/// <see cref="HeadlessClient.ConnectLoopback(Server.EmbeddedServerHost, string)"/> connects it.
/// </description></item>
/// </list>
/// <para>
/// Everything runs on a dedicated <see cref="EngineThread"/>, which stays the client's main
/// thread for its whole life. <c>ClientMain.Start</c> starts the engine's own worker threads (network processing,
/// tessellation, relighting, chunk visibility, particles). They run exactly as in the game, which
/// is the point of this mode, and stop when the client is disposed.
/// </para>
/// </remarks>
internal static class EngineClientStartup
{
    private const string BridgeAssemblyFile = "Zaldaryon.Pharos.Bridge.dll";

    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo s_currentScreenField =
        typeof(ScreenManager).GetField("CurrentScreen", AnyInstance)
        ?? throw new MissingFieldException(nameof(ScreenManager), "CurrentScreen");

    private static readonly FieldInfo s_runningGameField =
        typeof(GuiScreenRunningGame).GetField("runningGame", AnyInstance)
        ?? throw new MissingFieldException(nameof(GuiScreenRunningGame), "runningGame");

    private static readonly MethodInfo s_loadModsMethod =
        typeof(ScreenManager).GetMethod("loadMods", AnyInstance)
        ?? throw new MissingMethodException(nameof(ScreenManager), "loadMods");

    private static readonly MethodInfo s_renderMethod =
        typeof(ScreenManager).GetMethod("Render", AnyInstance, null, [typeof(float)], null)
        ?? throw new MissingMethodException(nameof(ScreenManager), "Render");

    /// <summary>
    /// Boots the engine-mode client. Paths, audio and screen settings are already configured by
    /// <see cref="HeadlessClientBootstrap.Boot"/>.
    /// </summary>
    public static HeadlessClient Boot(HeadlessClientOptions options, string? tempDataPath)
    {
        // The whole engine lives on its own thread from the first GL call on: the window and its
        // context are created there, and that thread becomes the engine's main thread.
        EngineThread clientThread = new("Pharos client main thread");
        try
        {
            return clientThread.Invoke(() => BootOnClientThread(options, tempDataPath, clientThread));
        }
        catch
        {
            clientThread.Dispose();
            throw;
        }
    }

    private static HeadlessClient BootOnClientThread(HeadlessClientOptions options, string? tempDataPath, EngineThread clientThread)
    {
        // Logs, Saves, Mods, Cache and the rest: the engine opens files under all of them.
        GamePaths.EnsurePathsExist();
        OfflineSessionPatcher.Patch();

        HeadlessWindow window = new(options);

        ClientLogger logger = new();
        LogCapture logs = new(EnumAppSide.Client);
        logs.Attach(logger);
        ClientPlatformWindows platform = new(logger);

        // ClientMain.Start registers a crash handler on the platform, which dereferences this.
        // It is never started, so it does not install a process-wide exception handler.
        platform.crashreporter = new CrashReporter(EnumAppSide.Client);
        CrashReporter.SetLogger(logger);

        EngineFocusPatcher.Register(platform);
        Inspection.ClientInspectionPatches.Install();
        platform.window = window.NativeWindow;
        platform.XPlatInterface.Window = (GameWindow)(object)window.NativeWindow;
        platform.WindowSize.Width = options.Width;
        platform.WindowSize.Height = options.Height;

        lock (ScreenManager.MainThreadTasks)
        {
            ScreenManager.MainThreadTasks.Clear();
        }

        ScreenManager screenManager = new(platform);
        ScreenManager.ParsedArgs = new ClientProgramArgs();
        GuiStyle.DecorativeFontName = ClientSettings.DecorativeFontName;
        GuiStyle.StandardFontName = ClientSettings.DefaultFontName;

        // The launcher preloads translations here; hotkey registration and the screens look up
        // their labels before the mods load the full set. A server booted earlier in the process
        // would have loaded them too, so without this a client that boots first fails.
        Lang.PreLoad(logger, GamePaths.AssetsPath, ClientSettings.Language);
        ModSafetyCheck.Disable();
        StartScreenManager(screenManager, platform);

        // Default frame buffers (primary, transparency, post processing) and the minimal GUI
        // shader. The engine draws into these and blits the result to the window.
        platform.Start();

        platform.LoadAssets();
        PrepareShaderRegistry();
        ShaderRegistry.Load();
        ScreenManager.hotkeyManager.RegisterDefaultHotKeys();

        // ClientSettings is a process-wide singleton, created with whatever data path was current
        // the first time it was touched, so the mod folders are set explicitly for every boot.
        ClientSettings.ModPaths = ["Mods", GamePaths.DataPathMods, .. options.ModPaths, .. BridgeModPaths(options)];

        screenManager.ClientIsOffline = true;
        s_loadModsMethod.Invoke(screenManager, null);

        GuiScreenRunningGame runningGameScreen = new(screenManager, null);
        s_currentScreenField.SetValue(screenManager, runningGameScreen);

        ClientMain client = (ClientMain)s_runningGameField.GetValue(runningGameScreen)!;
        client.Start();

        return new HeadlessClient(client, platform, screenManager, runningGameScreen, window, options, tempDataPath, ClientBootMode.Engine, clientThread) { Logs = logs };
    }

    /// <summary>
    /// Runs one vanilla <c>ScreenManager.Render</c> pass: the game's own render to primary,
    /// post processing, final composition, blit and GUI passes.
    /// </summary>
    public static void Render(ScreenManager screenManager, float dt)
    {
        try
        {
            s_renderMethod.Invoke(screenManager, [dt]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static void PrepareShaderRegistry()
    {
        // ShaderRegistry is static and its programs are created once per process, in its static
        // constructor. Load compiles them and registers their custom samplers, so loading the
        // same instances a second time throws on the duplicate sampler name. That happens for a
        // second engine client, and for the first one when a fixture-mode client already compiled
        // the shaders in this process. The old instances hold GL names from a context that may be
        // gone, so they are dropped rather than disposed: deleting those names here could delete
        // objects the new context has since handed out under the same numbers. Fresh default
        // programs are registered in their place, exactly as ShaderRegistry.ReloadShaders does.
        if (typeof(ShaderRegistry).GetField("shaderPrograms", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is ShaderProgram?[] programs)
        {
            Array.Clear(programs);
        }

        typeof(ShaderRegistry).GetMethod("registerDefaultShaderProgramsPre", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, null);
    }

    /// <summary>
    /// The folder the bridge mod is staged in, when <see cref="HeadlessClientOptions.LoadBridge"/>
    /// is on and the bridge assembly is next to the Pharos assembly.
    /// </summary>
    /// <remarks>
    /// The game loads every assembly in a mod folder as a mod, so the bridge is copied into a
    /// folder of its own under the data path. The game loads it into the process's default
    /// context, where a test that references the bridge already has it, so both see the same
    /// <c>BridgeChannel</c>.
    /// </remarks>
    private static IEnumerable<string> BridgeModPaths(HeadlessClientOptions options)
    {
        if (!options.LoadBridge) yield break;

        string? pharosFolder = Path.GetDirectoryName(typeof(EngineClientStartup).Assembly.Location);
        if (string.IsNullOrEmpty(pharosFolder)) yield break;

        string bridge = Path.Combine(pharosFolder, BridgeAssemblyFile);
        if (!File.Exists(bridge)) yield break;

        string staged = Path.Combine(GamePaths.DataPath, "PharosBridge");
        Directory.CreateDirectory(staged);
        File.Copy(bridge, Path.Combine(staged, BridgeAssemblyFile), overwrite: true);
        yield return staged;
    }

    private static void StartScreenManager(ScreenManager screenManager, ClientPlatformWindows platform)
    {
        // The parts of ScreenManager.Start a running game depends on. The loading screen, the
        // main menu, the background asset thread and the newest-version request are left out.
        screenManager.api = new MainMenuAPI(screenManager);
        ScreenManager.GuiComposers = new GuiComposerManager(screenManager.api);
        ScreenManager.MainThreadId = Environment.CurrentManagedThreadId;
        RuntimeEnv.MainThreadId = ScreenManager.MainThreadId;

        // Keyboard and mouse events reach the current screen through these, which is what lets
        // input injected on the platform travel the same path as real input.
        platform.RegisterKeyboardEvent(screenManager);
        platform.RegisterMouseEvent(screenManager);

        screenManager.registerSettingsWatchers();
        platform.GlToggleBlend(on: true);
    }
}
