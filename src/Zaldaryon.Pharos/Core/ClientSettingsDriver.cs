using System.Globalization;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Reads and changes a headless client's settings while it runs, as the settings menu does.
/// </summary>
/// <remarks>
/// Settings are process-wide in the game, like the rest of <c>clientsettings.json</c>. Changing
/// one notifies the systems that watch it: view distance reloads chunks, the GUI scale relayouts
/// the dialogs, and so on. When a graphics setting changes, the frame buffers are rebuilt and the
/// shaders reloaded, as the graphics menu does. <see cref="Apply"/> returns a handle that puts the
/// previous values back, so a test leaves the client as it found it.
/// </remarks>
public sealed class ClientSettingsDriver
{
    // Settings the shaders or frame buffers are built from.
    private static readonly HashSet<string> s_graphicsSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "smoothShadows", "fxaa", "ssaoquality", "wavingStuff", "liquidFoamAndShinyEffect", "bloom",
        "godRays", "shadowMapQuality", "ssaa", "maxDynamicLights", "hdrMode", "instancedGrass",
    };

    private readonly HeadlessClient _client;

    internal ClientSettingsDriver(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>The value of the setting <paramref name="key"/>, or null when the game has no such setting.</summary>
    public object? Get(string key) => _client.RunOnClientThread(() => Read(key));

    /// <summary>Changes the setting <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">The value cannot be converted to the setting's type.</exception>
    public void Set(string key, object value) => Apply(ClientSettingsProfile.Of(key, (key, value)));

    /// <summary>
    /// Changes every setting in <paramref name="profile"/>. Dispose the result to put the previous
    /// values back.
    /// </summary>
    public IDisposable Apply(ClientSettingsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Dictionary<string, object?> previous = _client.RunOnClientThread(() =>
        {
            Dictionary<string, object?> before = profile.Values.Keys.ToDictionary(k => k, Read, StringComparer.OrdinalIgnoreCase);
            Write(profile.Values.ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase));
            return before;
        });

        return new Restore(this, previous);
    }

    private static object? Read(string key)
    {
        ClientSettings settings = ClientSettings.Inst;
        Type? type = settings.GetSettingType(key);
        if (type == typeof(int)) return settings.GetIntSetting(key);
        if (type == typeof(float)) return settings.GetFloatSetting(key);
        if (type == typeof(bool)) return settings.GetBoolSetting(key);
        if (type == typeof(string)) return settings.GetStringSetting(key);
        return null;
    }

    private void Write(IReadOnlyDictionary<string, object?> values)
    {
        ClientSettings settings = ClientSettings.Inst;
        bool graphics = false;

        ShaderRegistry.SupressShaderAndBufferReloads = true;
        try
        {
            foreach ((string key, object? value) in values)
            {
                if (value == null) continue;

                Type type = settings.GetSettingType(key) ?? value.GetType();
                try
                {
                    if (type == typeof(int)) settings.Int[key] = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    else if (type == typeof(float)) settings.Float[key] = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    else if (type == typeof(bool)) settings.Bool[key] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    else if (type == typeof(string)) settings.String[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                    else throw new ArgumentException($"Setting '{key}' has an unsupported type {type.Name}.");
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    throw new ArgumentException($"Setting '{key}' is {type.Name}; '{value}' does not convert to it.", ex);
                }

                graphics |= s_graphicsSettings.Contains(key);
            }
        }
        finally
        {
            ShaderRegistry.SupressShaderAndBufferReloads = false;
        }

        if (graphics && _client.IsEngineMode)
        {
            ScreenManager.Platform.RebuildFrameBuffers();
            ShaderRegistry.ReloadShaders();
            _client.Client.eventManager?.TriggerReloadShaders();
        }
    }

    private sealed class Restore(ClientSettingsDriver driver, Dictionary<string, object?> previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || driver._client.IsDisposed) return;
            driver._client.RunOnClientThread(() => driver.Write(previous));
        }
    }
}
