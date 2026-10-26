using Vintagestory.API.Client;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Input;

/// <summary>
/// Clipboard driver that integrates with the game's actual clipboard API when available,
/// falling back to Pharos-owned in-memory storage for headless operation.
/// </summary>
public sealed class ClipboardDriver
{
    private readonly HeadlessClient? _client;
    private string _fallbackText = string.Empty;

    /// <summary>Creates a standalone clipboard for headless operation without a game client.</summary>
    public ClipboardDriver() => _client = null;

    /// <summary>Creates a clipboard integrated with the game client's actual clipboard API.</summary>
    internal ClipboardDriver(HeadlessClient client) => _client = client;

    /// <summary>The clipboard's current text.</summary>
    public string Text
    {
        get => Get();
        set => Set(value);
    }

    /// <summary>Sets the clipboard text.</summary>
    public void Set(string text)
    {
        text ??= string.Empty;

        if (_client != null && !_client.IsDisposed)
        {
            try
            {
                _client.RunOnClientThread(() =>
                {
                    var api = _client.Client?.api;
                    if (api?.Input != null)
                    {
                        api.Input.ClipboardText = text;
                    }
                    else
                    {
                        // Game API not yet available, use fallback
                        _fallbackText = text;
                    }
                });
                return;
            }
            catch (ObjectDisposedException)
            {
                // Client disposed during operation, fall through to fallback
            }
        }

        _fallbackText = text;
    }

    /// <summary>Gets the clipboard text.</summary>
    public string Get()
    {
        if (_client != null && !_client.IsDisposed)
        {
            try
            {
                return _client.RunOnClientThread(() =>
                {
                    var api = _client.Client?.api;
                    if (api?.Input != null)
                    {
                        return api.Input.ClipboardText ?? string.Empty;
                    }
                    return _fallbackText;
                });
            }
            catch (ObjectDisposedException)
            {
                // Client disposed during operation, fall through to fallback
            }
        }

        return _fallbackText;
    }

    /// <summary>Clears the clipboard.</summary>
    public void Clear() => Set(string.Empty);

    /// <summary>Whether the clipboard contains any text.</summary>
    public bool HasText => !string.IsNullOrEmpty(Get());
}
