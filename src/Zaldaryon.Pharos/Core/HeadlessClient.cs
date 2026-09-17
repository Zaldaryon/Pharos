using OpenTK.Windowing.Desktop;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Bootstrap;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Headless Vintage Story client instance running offscreen in-process.
/// </summary>
public sealed class HeadlessClient : IDisposable
{
    private bool _disposed;
    private readonly string? _tempDataPath;

    public ClientMain Client { get; }
    public ClientPlatformWindows Platform { get; }
    public ScreenManager ScreenManager { get; }
    public GuiScreenRunningGame RunningGameScreen { get; }
    public HeadlessWindow Window { get; }
    public HeadlessFramebuffer Framebuffer => Window.Framebuffer;
    public HeadlessClientOptions Options { get; }
    public bool IsDisposed => _disposed;

    internal HeadlessClient(
        ClientMain client,
        ClientPlatformWindows platform,
        ScreenManager screenManager,
        GuiScreenRunningGame runningGameScreen,
        HeadlessWindow window,
        HeadlessClientOptions options,
        string? tempDataPath)
    {
        Client = client;
        Platform = platform;
        ScreenManager = screenManager;
        RunningGameScreen = runningGameScreen;
        Window = window;
        Options = options;
        _tempDataPath = tempDataPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Client.Dispose();
        }
        catch
        {
            // Ignore client shutdown errors
        }

        try
        {
            if (Platform.Logger is IDisposable disposableLogger)
            {
                disposableLogger.Dispose();
            }
        }
        catch
        {
            // Ignore logger disposal errors
        }

        try
        {
            Window.Dispose();
        }
        catch
        {
            // Ignore teardown errors during test shutdown
        }

        if (!string.IsNullOrEmpty(_tempDataPath) && Directory.Exists(_tempDataPath))
        {
            try
            {
                Directory.Delete(_tempDataPath, recursive: true);
            }
            catch
            {
                // Best effort temporary cleanup
            }
        }
    }
}
