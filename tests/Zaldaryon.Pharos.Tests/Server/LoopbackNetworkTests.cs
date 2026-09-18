using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Headless-safe tests for <see cref="LoopbackHandshake"/> and <see cref="ClientServerLoopbackSession"/>
/// loopback network binding functionality.
/// </summary>
/// <remarks>
/// These tests validate handshake packet ordering, socket slot logic, version reading,
/// buffer slicing, and API shape without booting a live server, making them safe for
/// headless CI environments.
/// </remarks>
public class LoopbackNetworkTests
{
    #region Handshake Packet Order Tests

    [Fact]
    public void LoopbackHandshake_PacketIdLoginTokenQuery_Is33()
    {
        Assert.Equal(33, LoopbackHandshake.PacketIdLoginTokenQuery);
    }

    [Fact]
    public void LoopbackHandshake_PacketIdClientIdentification_Is1()
    {
        Assert.Equal(1, LoopbackHandshake.PacketIdClientIdentification);
    }

    [Fact]
    public void LoopbackHandshake_IsCorrectHandshakeOrder_TrueWhen33Before1()
    {
        bool result = LoopbackHandshake.IsCorrectHandshakeOrder(33, 1);
        Assert.True(result);
    }

    [Fact]
    public void LoopbackHandshake_IsCorrectHandshakeOrder_FalseWhen1Before33()
    {
        bool result = LoopbackHandshake.IsCorrectHandshakeOrder(1, 33);
        Assert.False(result);
    }

    [Fact]
    public void LoopbackHandshake_IsCorrectHandshakeOrder_FalseForWrongPackets()
    {
        Assert.False(LoopbackHandshake.IsCorrectHandshakeOrder(1, 2));
        Assert.False(LoopbackHandshake.IsCorrectHandshakeOrder(33, 33));
        Assert.False(LoopbackHandshake.IsCorrectHandshakeOrder(0, 0));
        Assert.False(LoopbackHandshake.IsCorrectHandshakeOrder(32, 1));
        Assert.False(LoopbackHandshake.IsCorrectHandshakeOrder(33, 2));
    }

    [Fact]
    public void LoopbackHandshake_PacketConstants_MatchEngineValues()
    {
        // Packet 33 is LoginTokenQuery and must come before Packet 1 (ClientIdentification)
        // This validates the handshake order requirement from the VS protocol
        Assert.Equal(33, LoopbackHandshake.PacketIdLoginTokenQuery);
        Assert.Equal(1, LoopbackHandshake.PacketIdClientIdentification);
        Assert.True(LoopbackHandshake.PacketIdLoginTokenQuery > LoopbackHandshake.PacketIdClientIdentification);
    }

    #endregion

    #region Socket Slot Tests

    [Fact]
    public void LoopbackHandshake_FirstPlayerSocketSlot_Is1()
    {
        Assert.Equal(1, LoopbackHandshake.FirstPlayerSocketSlot);
    }

    [Fact]
    public void LoopbackHandshake_GetFirstValidPlayerSocketSlot_Returns1()
    {
        int slot = LoopbackHandshake.GetFirstValidPlayerSocketSlot();
        Assert.Equal(1, slot);
    }

    [Fact]
    public void LoopbackHandshake_IsValidPlayerSocketSlot_FalseForSlot0()
    {
        // Slot 0 is reserved by the engine
        bool result = LoopbackHandshake.IsValidPlayerSocketSlot(0);
        Assert.False(result);
    }

    [Fact]
    public void LoopbackHandshake_IsValidPlayerSocketSlot_TrueForSlot1()
    {
        bool result = LoopbackHandshake.IsValidPlayerSocketSlot(1);
        Assert.True(result);
    }

    [Fact]
    public void LoopbackHandshake_IsValidPlayerSocketSlot_TrueForHigherSlots()
    {
        Assert.True(LoopbackHandshake.IsValidPlayerSocketSlot(2));
        Assert.True(LoopbackHandshake.IsValidPlayerSocketSlot(10));
        Assert.True(LoopbackHandshake.IsValidPlayerSocketSlot(100));
    }

    [Fact]
    public void LoopbackHandshake_IsValidPlayerSocketSlot_FalseForNegativeSlots()
    {
        Assert.False(LoopbackHandshake.IsValidPlayerSocketSlot(-1));
        Assert.False(LoopbackHandshake.IsValidPlayerSocketSlot(-100));
    }

    #endregion

    #region Version Reading Tests

    [Fact]
    public void LoopbackHandshake_GetNetworkVersion_ReturnsNonNegative()
    {
        LoopbackHandshake.ClearCache();
        int version = LoopbackHandshake.GetNetworkVersion();
        Assert.True(version >= 0, $"NetworkVersion should be non-negative, got {version}");
    }

    [Fact]
    public void LoopbackHandshake_GetShortGameVersion_ReturnsNonEmpty()
    {
        LoopbackHandshake.ClearCache();
        string version = LoopbackHandshake.GetShortGameVersion();
        Assert.False(string.IsNullOrEmpty(version), "ShortGameVersion should not be null or empty");
    }

    [Fact]
    public void LoopbackHandshake_GetShortGameVersion_HasVersionFormat()
    {
        LoopbackHandshake.ClearCache();
        string version = LoopbackHandshake.GetShortGameVersion();
        
        // Should contain at least one dot (e.g., "1.22.7" or "0.0.0")
        Assert.Contains(".", version);
    }

    [Fact]
    public void LoopbackHandshake_VersionCaching_ReturnsSameValue()
    {
        LoopbackHandshake.ClearCache();
        
        int networkV1 = LoopbackHandshake.GetNetworkVersion();
        int networkV2 = LoopbackHandshake.GetNetworkVersion();
        
        string gameV1 = LoopbackHandshake.GetShortGameVersion();
        string gameV2 = LoopbackHandshake.GetShortGameVersion();
        
        Assert.Equal(networkV1, networkV2);
        Assert.Equal(gameV1, gameV2);
    }

    [Fact]
    public void LoopbackHandshake_ClearCache_AllowsReReading()
    {
        int v1 = LoopbackHandshake.GetNetworkVersion();
        string g1 = LoopbackHandshake.GetShortGameVersion();
        
        LoopbackHandshake.ClearCache();
        
        int v2 = LoopbackHandshake.GetNetworkVersion();
        string g2 = LoopbackHandshake.GetShortGameVersion();
        
        // Values should be equal (from same assembly) but cache was cleared
        Assert.Equal(v1, v2);
        Assert.Equal(g1, g2);
    }

    #endregion

    #region Buffer Slicing Tests

    [Fact]
    public void LoopbackHandshake_SliceToLength_SlicesCorrectly()
    {
        byte[] buffer = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        byte[] sliced = LoopbackHandshake.SliceToLength(buffer, 5);
        
        Assert.Equal(5, sliced.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, sliced);
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_ReturnsSameArrayWhenEqualLength()
    {
        byte[] buffer = new byte[] { 1, 2, 3, 4, 5 };
        byte[] sliced = LoopbackHandshake.SliceToLength(buffer, 5);
        
        Assert.Same(buffer, sliced);
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_HandlesZeroLength()
    {
        byte[] buffer = new byte[] { 1, 2, 3 };
        byte[] sliced = LoopbackHandshake.SliceToLength(buffer, 0);
        
        Assert.Empty(sliced);
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_ThrowsOnNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => 
            LoopbackHandshake.SliceToLength(null!, 5));
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_ThrowsOnNegativeLength()
    {
        byte[] buffer = new byte[] { 1, 2, 3 };
        
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            LoopbackHandshake.SliceToLength(buffer, -1));
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_ThrowsWhenLengthExceedsBuffer()
    {
        byte[] buffer = new byte[] { 1, 2, 3 };
        
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            LoopbackHandshake.SliceToLength(buffer, 10));
    }

    [Fact]
    public void LoopbackHandshake_SliceToLength_PreservesBytesAtStart()
    {
        // Simulate CitoMemoryStream buffer with trailing garbage
        byte[] buffer = new byte[100];
        buffer[0] = 0xAA;
        buffer[1] = 0xBB;
        buffer[2] = 0xCC;
        // Rest is zeros (trailing buffer bytes)
        
        byte[] sliced = LoopbackHandshake.SliceToLength(buffer, 3);
        
        Assert.Equal(3, sliced.Length);
        Assert.Equal(0xAA, sliced[0]);
        Assert.Equal(0xBB, sliced[1]);
        Assert.Equal(0xCC, sliced[2]);
    }

    #endregion

    #region API Shape Tests

    [Fact]
    public void LoopbackHandshake_IsStaticClass()
    {
        Type type = typeof(LoopbackHandshake);
        Assert.True(type.IsAbstract && type.IsSealed, "LoopbackHandshake should be a static class");
    }

    [Fact]
    public void LoopbackHandshake_HasExpectedPublicConstants()
    {
        Type type = typeof(LoopbackHandshake);
        
        FieldInfo? loginTokenField = type.GetField("PacketIdLoginTokenQuery", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? clientIdField = type.GetField("PacketIdClientIdentification", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? socketSlotField = type.GetField("FirstPlayerSocketSlot", BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(loginTokenField);
        Assert.NotNull(clientIdField);
        Assert.NotNull(socketSlotField);
        
        Assert.True(loginTokenField.IsLiteral);
        Assert.True(clientIdField.IsLiteral);
        Assert.True(socketSlotField.IsLiteral);
    }

    [Fact]
    public void LoopbackHandshake_HasExpectedPublicMethods()
    {
        Type type = typeof(LoopbackHandshake);
        
        Assert.NotNull(type.GetMethod("GetNetworkVersion", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("GetShortGameVersion", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("IsValidPlayerSocketSlot", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("GetFirstValidPlayerSocketSlot", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("IsCorrectHandshakeOrder", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("SliceToLength", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static));
    }

    #endregion

    #region ClientServerLoopbackSession API Tests

    [Fact]
    public void ClientServerLoopbackSession_HasNativeServerProperty()
    {
        Type type = typeof(ClientServerLoopbackSession);
        PropertyInfo? prop = type.GetProperty("NativeServer", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(EmbeddedServerHost), prop.PropertyType);
    }

    [Fact]
    public void ClientServerLoopbackSession_HasIsNativeSessionProperty()
    {
        Type type = typeof(ClientServerLoopbackSession);
        PropertyInfo? prop = type.GetProperty("IsNativeSession", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
    }

    [Fact]
    public void ClientServerLoopbackSession_ServerPropertyIsObsolete()
    {
        Type type = typeof(ClientServerLoopbackSession);
        PropertyInfo? prop = type.GetProperty("Server", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(prop);
        ObsoleteAttribute? attr = prop.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(attr);
        Assert.Contains("NativeServer", attr.Message);
    }

    [Fact]
    public void ClientServerLoopbackSession_ImplementsIDisposable()
    {
        Type type = typeof(ClientServerLoopbackSession);
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));
    }

    #endregion

    #region HeadlessClient ConnectLoopback API Tests

    [Fact]
    public void HeadlessClient_HasConnectLoopbackWithEmbeddedServerHost()
    {
        Type type = typeof(HeadlessClient);
        MethodInfo? method = type.GetMethod("ConnectLoopback", new[] { typeof(EmbeddedServerHost), typeof(string) });
        
        Assert.NotNull(method);
        Assert.Equal(typeof(ClientServerLoopbackSession), method.ReturnType);
    }

    [Fact]
    public void HeadlessClient_AtlasServerHostOverloadIsObsolete()
    {
        Type type = typeof(HeadlessClient);
        MethodInfo? method = type.GetMethod("ConnectLoopback", new[] { typeof(AtlasServerHost), typeof(string) });
        
        Assert.NotNull(method);
        ObsoleteAttribute? attr = method.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(attr);
        Assert.Contains("EmbeddedServerHost", attr.Message);
    }

    [Fact]
    public void HeadlessClient_ConnectLoopbackOverloads_HaveDefaultPlayerName()
    {
        Type type = typeof(HeadlessClient);
        
        // EmbeddedServerHost overload
        MethodInfo? embeddedMethod = type.GetMethod("ConnectLoopback", new[] { typeof(EmbeddedServerHost), typeof(string) });
        Assert.NotNull(embeddedMethod);
        ParameterInfo[] embeddedParams = embeddedMethod.GetParameters();
        Assert.Equal(2, embeddedParams.Length);
        Assert.True(embeddedParams[1].HasDefaultValue);
        Assert.Equal("PharosTest", embeddedParams[1].DefaultValue);
        
        // AtlasServerHost overload
        MethodInfo? atlasMethod = type.GetMethod("ConnectLoopback", new[] { typeof(AtlasServerHost), typeof(string) });
        Assert.NotNull(atlasMethod);
        ParameterInfo[] atlasParams = atlasMethod.GetParameters();
        Assert.Equal(2, atlasParams.Length);
        Assert.True(atlasParams[1].HasDefaultValue);
        Assert.Equal("PharosTest", atlasParams[1].DefaultValue);
    }

    #endregion
}
