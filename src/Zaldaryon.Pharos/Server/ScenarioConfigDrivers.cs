using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>Changes a server world's saved configuration on the game thread.</summary>
public sealed class WorldConfigDriver(EmbeddedServerHost host)
{
    /// <summary>Sets one world configuration key using the type declared by its mod.</summary>
    /// <param name="key">The world config key, such as <c>temporalStorms</c>.</param>
    /// <param name="value">The textual value accepted by the world's config type.</param>
    /// <exception cref="ArgumentException">The key is unknown or the value does not match its declared type.</exception>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        host.RunOnGameThread(() =>
        {
            EnsureRunning();
            WorldConfigurationAttribute setting = Find(key);
            ITreeAttribute config = host.Api.World.Config;
            string code = setting.Code;
            switch (setting.DataType)
            {
                case EnumDataType.Bool:
                    if (!bool.TryParse(value, out bool flag)) throw BadValue(code, value, "a boolean");
                    config.SetBool(code, flag);
                    break;
                case EnumDataType.DoubleInput:
                case EnumDataType.DoubleRange:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                        throw BadValue(code, value, "a number");
                    config.SetDouble(code, number);
                    break;
                case EnumDataType.IntInput:
                case EnumDataType.IntRange:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole))
                        throw BadValue(code, value, "a whole number");
                    config.SetInt(code, whole);
                    break;
                case EnumDataType.String:
                case EnumDataType.DropDown:
                case EnumDataType.StringRange:
                    config.SetString(code, value);
                    break;
                default:
                    throw new ArgumentException($"World config key '{code}' has unsupported type {setting.DataType}.", nameof(key));
            }
        });
    }

    private static ArgumentException BadValue(string code, string value, string expected) =>
        new($"World config key '{code}' takes {expected}, not '{value}'.", nameof(value));

    private WorldConfigurationAttribute Find(string key)
    {
        foreach (Mod mod in host.Api.ModLoader.Mods)
        {
            if (mod.WorldConfig is not { } worldConfig) continue;
            WorldConfigurationAttribute? setting = worldConfig.WorldConfigAttributes.FirstOrDefault(attribute =>
                attribute.Code.Equals(key, StringComparison.InvariantCultureIgnoreCase));
            if (setting != null) return setting;
        }
        throw new ArgumentException($"No world configuration key named '{key}' is registered by a loaded mod.", nameof(key));
    }

    private void EnsureRunning()
    {
        if (!host.IsRunning) throw new InvalidOperationException("The server is not running.");
    }
}

/// <summary>Edits the embedded server's live config on the game thread.</summary>
public sealed class ServerConfigDriver(EmbeddedServerHost host)
{
    /// <summary>Applies a change and tells the server to save <c>serverconfig.json</c>.</summary>
    /// <param name="change">The changes to apply to the live config object.</param>
    public void Set(Action<ServerConfig> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        host.RunOnGameThread(() =>
        {
            if (!host.IsRunning) throw new InvalidOperationException("The server is not running.");
            change(host.Server.Config);
            ServerConfigState.RefreshRoles(host.Server.Config);
            host.Server.ConfigNeedsSaving = true;
        });
    }
}

/// <summary>Writes mod configuration files into the embedded server's isolated data folder.</summary>
public sealed class ModConfigDriver(EmbeddedServerHost host)
{
    /// <summary>Writes a JSON config beneath <c>ModConfig</c>. A mod must reload it to apply live changes.</summary>
    /// <param name="fileName">A filename or relative path within <c>ModConfig</c>.</param>
    /// <param name="value">The object serialized as JSON.</param>
    public void Write<T>(string fileName, T value)
    {
        string relative = SafePath(fileName);
        host.RunOnGameThread(() =>
        {
            if (!host.IsRunning) throw new InvalidOperationException("The server is not running.");
            string root = Path.GetFullPath(Path.Combine(host.DataPath, "ModConfig"));
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            string fromRoot = Path.GetRelativePath(root, destination);
            if (Path.IsPathRooted(fromRoot) || fromRoot == ".." || fromRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("A mod config destination must stay inside ModConfig.", nameof(fileName));

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, JsonConvert.SerializeObject(value, Formatting.Indented));
        });
    }

    private static string SafePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string normalized = fileName.Replace('\\', '/');
        if (Path.IsPathRooted(fileName) || normalized.StartsWith('/') || normalized.Contains(':')
            || normalized.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
        {
            throw new ArgumentException("A mod config destination must be a file path inside ModConfig.", nameof(fileName));
        }
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }
}

/// <summary>Captures and restores the public server config while keeping its live object in place.</summary>
internal static class ServerConfigState
{
    public static string Capture(ServerConfig config) => JsonConvert.SerializeObject(config, Formatting.None);

    public static void Restore(ServerConfig config, string json)
    {
        JsonConvert.PopulateObject(json, config, new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        });
        RefreshRoles(config);
    }

    public static void RefreshRoles(ServerConfig config)
    {
        if (config.Roles == null) return;
        config.RolesByCode ??= new Dictionary<string, PlayerRole>(StringComparer.OrdinalIgnoreCase);
        config.RolesByCode.Clear();
        foreach (PlayerRole role in config.Roles)
        {
            if (!string.IsNullOrWhiteSpace(role.Code)) config.RolesByCode[role.Code] = role;
        }
        if (config.RolesByCode.TryGetValue(config.DefaultRoleCode, out PlayerRole? defaultRole))
            ServerConfigInternals.DefaultRole(config) = defaultRole;
    }

    private static class ServerConfigInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "DefaultRole")]
        public static extern ref PlayerRole DefaultRole(ServerConfig config);
    }
}
