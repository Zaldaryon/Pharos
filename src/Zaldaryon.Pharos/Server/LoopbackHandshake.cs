using System;
using System.Reflection;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Static helpers for building correct loopback handshake packets between HeadlessClient and EmbeddedServerHost.
/// </summary>
/// <remarks>
/// <para>
/// The Vintage Story handshake requires a specific two-step sequence:
/// <list type="number">
/// <item>Packet 33 (LoginTokenQuery) - Client requests a login token from the server</item>
/// <item>Packet 1 (ClientIdentification) - Client sends identification with the received token</item>
/// </list>
/// </para>
/// <para>
/// NetworkVersion and ShortGameVersion are read dynamically from the loaded engine assembly
/// to ensure compatibility across Vintage Story versions.
/// </para>
/// <para>
/// Socket slot 0 is reserved by the engine for internal use; additional players use slots 1+.
/// </para>
/// </remarks>
public static class LoopbackHandshake
{
    /// <summary>
    /// The packet ID for LoginTokenQuery (sent before ClientIdentification).
    /// </summary>
    public const int PacketIdLoginTokenQuery = 33;

    /// <summary>
    /// The packet ID for ClientIdentification (sent after receiving token).
    /// </summary>
    public const int PacketIdClientIdentification = 1;

    /// <summary>
    /// The first valid socket slot for player connections (slot 0 is engine-reserved).
    /// </summary>
    public const int FirstPlayerSocketSlot = 1;

    private static int? _cachedNetworkVersion;
    private static string? _cachedShortGameVersion;

    /// <summary>
    /// Gets the network protocol version from the loaded VintagestoryLib assembly.
    /// </summary>
    /// <returns>The network version, or 0 if it could not be determined.</returns>
    /// <remarks>
    /// This method uses reflection to find the NetworkVersion field/property in the
    /// Vintagestory.Common or Vintagestory namespace. The result is cached for performance.
    /// </remarks>
    public static int GetNetworkVersion()
    {
        if (_cachedNetworkVersion.HasValue)
            return _cachedNetworkVersion.Value;

        _cachedNetworkVersion = ResolveNetworkVersion();
        return _cachedNetworkVersion.Value;
    }

    /// <summary>
    /// Gets the short game version string from the loaded VintagestoryLib assembly.
    /// </summary>
    /// <returns>The short game version (e.g., "1.22.7"), or "0.0.0" if it could not be determined.</returns>
    /// <remarks>
    /// This method uses reflection to find the ShortGameVersion field/property in the
    /// GameVersion class. The result is cached for performance.
    /// </remarks>
    public static string GetShortGameVersion()
    {
        if (_cachedShortGameVersion != null)
            return _cachedShortGameVersion;

        _cachedShortGameVersion = ResolveShortGameVersion();
        return _cachedShortGameVersion;
    }

    /// <summary>
    /// Validates that socket slot is valid for player connections.
    /// </summary>
    /// <param name="socketSlot">The socket slot to validate.</param>
    /// <returns>True if the slot is valid (>= 1); false if it uses the reserved slot 0.</returns>
    public static bool IsValidPlayerSocketSlot(int socketSlot)
    {
        return socketSlot >= FirstPlayerSocketSlot;
    }

    /// <summary>
    /// Returns the first valid socket slot for a new player connection.
    /// </summary>
    /// <returns>The first valid player socket slot (1).</returns>
    public static int GetFirstValidPlayerSocketSlot()
    {
        return FirstPlayerSocketSlot;
    }

    /// <summary>
    /// Validates that the handshake packet order is correct (33 before 1).
    /// </summary>
    /// <param name="firstPacketId">The ID of the first packet sent.</param>
    /// <param name="secondPacketId">The ID of the second packet sent.</param>
    /// <returns>True if the order is correct (33 then 1); false otherwise.</returns>
    public static bool IsCorrectHandshakeOrder(int firstPacketId, int secondPacketId)
    {
        return firstPacketId == PacketIdLoginTokenQuery && secondPacketId == PacketIdClientIdentification;
    }

    /// <summary>
    /// Slices a byte array to the specified length, avoiding trailing buffer bytes.
    /// </summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="actualLength">The actual data length within the buffer.</param>
    /// <returns>A new byte array containing exactly the specified number of bytes.</returns>
    /// <remarks>
    /// CitoMemoryStream.ToArray() returns the entire buffer, not just the written portion.
    /// This helper ensures serialized packets have exact wire lengths without trailing garbage.
    /// </remarks>
    public static byte[] SliceToLength(byte[] buffer, int actualLength)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (actualLength < 0)
            throw new ArgumentOutOfRangeException(nameof(actualLength), actualLength, "Length must be non-negative.");

        if (actualLength > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(actualLength), actualLength, $"Length ({actualLength}) exceeds buffer size ({buffer.Length}).");

        if (actualLength == buffer.Length)
            return buffer;

        byte[] result = new byte[actualLength];
        Array.Copy(buffer, result, actualLength);
        return result;
    }

    /// <summary>
    /// Clears cached version values, forcing re-resolution on next access.
    /// </summary>
    /// <remarks>
    /// Useful for testing or when assemblies might be reloaded.
    /// </remarks>
    public static void ClearCache()
    {
        _cachedNetworkVersion = null;
        _cachedShortGameVersion = null;
    }

    private static int ResolveNetworkVersion()
    {
        // Try Vintagestory.Common.GameVersion.NetworkVersion first
        Type? gameVersionType = Type.GetType("Vintagestory.Common.GameVersion, VintagestoryLib")
            ?? Type.GetType("Vintagestory.GameVersion, VintagestoryLib")
            ?? Type.GetType("Vintagestory.API.Config.GameVersion, VintagestoryAPI");

        if (gameVersionType != null)
        {
            // Try static field
            FieldInfo? field = gameVersionType.GetField("NetworkVersion", BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(int))
            {
                return (int)(field.GetValue(null) ?? 0);
            }

            // Try static property
            PropertyInfo? prop = gameVersionType.GetProperty("NetworkVersion", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.PropertyType == typeof(int) && prop.CanRead)
            {
                return (int)(prop.GetValue(null) ?? 0);
            }
        }

        // Scan loaded assemblies for GameVersion type
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!assembly.FullName?.Contains("Vintagestory") ?? true)
                    continue;

                foreach (Type type in assembly.GetTypes())
                {
                    if (type.Name == "GameVersion")
                    {
                        FieldInfo? field = type.GetField("NetworkVersion", BindingFlags.Public | BindingFlags.Static);
                        if (field != null && field.FieldType == typeof(int))
                        {
                            return (int)(field.GetValue(null) ?? 0);
                        }

                        PropertyInfo? prop = type.GetProperty("NetworkVersion", BindingFlags.Public | BindingFlags.Static);
                        if (prop != null && prop.PropertyType == typeof(int) && prop.CanRead)
                        {
                            return (int)(prop.GetValue(null) ?? 0);
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection errors on individual assemblies
            }
        }

        return 0;
    }

    private static string ResolveShortGameVersion()
    {
        // Try Vintagestory.Common.GameVersion.ShortGameVersion first
        Type? gameVersionType = Type.GetType("Vintagestory.Common.GameVersion, VintagestoryLib")
            ?? Type.GetType("Vintagestory.GameVersion, VintagestoryLib")
            ?? Type.GetType("Vintagestory.API.Config.GameVersion, VintagestoryAPI");

        if (gameVersionType != null)
        {
            // Try static field
            FieldInfo? field = gameVersionType.GetField("ShortGameVersion", BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(string))
            {
                return (string?)field.GetValue(null) ?? "0.0.0";
            }

            // Try static property
            PropertyInfo? prop = gameVersionType.GetProperty("ShortGameVersion", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead)
            {
                return (string?)prop.GetValue(null) ?? "0.0.0";
            }
        }

        // Scan loaded assemblies for GameVersion type
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!assembly.FullName?.Contains("Vintagestory") ?? true)
                    continue;

                foreach (Type type in assembly.GetTypes())
                {
                    if (type.Name == "GameVersion")
                    {
                        FieldInfo? field = type.GetField("ShortGameVersion", BindingFlags.Public | BindingFlags.Static);
                        if (field != null && field.FieldType == typeof(string))
                        {
                            return (string?)field.GetValue(null) ?? "0.0.0";
                        }

                        PropertyInfo? prop = type.GetProperty("ShortGameVersion", BindingFlags.Public | BindingFlags.Static);
                        if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead)
                        {
                            return (string?)prop.GetValue(null) ?? "0.0.0";
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection errors on individual assemblies
            }
        }

        return "0.0.0";
    }
}
