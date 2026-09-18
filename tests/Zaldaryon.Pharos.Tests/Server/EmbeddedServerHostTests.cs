using System;
using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Headless-safe tests for <see cref="ServerWorldOptions"/> and <see cref="EmbeddedServerHost"/>.
/// </summary>
/// <remarks>
/// These tests validate option defaults, record equality, and public API shape
/// without booting a live server, making them safe for headless CI environments.
/// </remarks>
public class EmbeddedServerHostTests
{
    #region ServerWorldOptions Tests

    [Fact]
    public void ServerWorldOptions_DefaultValues_AreCorrect()
    {
        ServerWorldOptions options = new();

        Assert.Equal("424242", options.Seed);
        Assert.Equal("PharosWorld", options.WorldName);
        Assert.Equal("creativebuilding", options.PlayStyle);
        Assert.Equal("superflat", options.WorldType);
        Assert.Null(options.SaveFileLocation);
        Assert.Equal("{}", options.WorldConfigurationJson);
    }

    [Fact]
    public void ServerWorldOptions_WithInitializer_OverridesDefaults()
    {
        ServerWorldOptions options = new()
        {
            Seed = "12345",
            WorldName = "TestWorld",
            PlayStyle = "surviveandbuild",
            WorldType = "standard",
            SaveFileLocation = "/tmp/test.vcdbs",
            WorldConfigurationJson = "{\"key\":\"value\"}"
        };

        Assert.Equal("12345", options.Seed);
        Assert.Equal("TestWorld", options.WorldName);
        Assert.Equal("surviveandbuild", options.PlayStyle);
        Assert.Equal("standard", options.WorldType);
        Assert.Equal("/tmp/test.vcdbs", options.SaveFileLocation);
        Assert.Equal("{\"key\":\"value\"}", options.WorldConfigurationJson);
    }

    [Fact]
    public void ServerWorldOptions_RecordEquality_WorksCorrectly()
    {
        ServerWorldOptions a = new() { Seed = "4242", WorldName = "Test" };
        ServerWorldOptions b = new() { Seed = "4242", WorldName = "Test" };
        ServerWorldOptions c = new() { Seed = "9999", WorldName = "Test" };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ServerWorldOptions_NullSaveFileLocation_IsDefault()
    {
        ServerWorldOptions options = new();
        Assert.Null(options.SaveFileLocation);
    }

    [Fact]
    public void ServerWorldOptions_EmptyJsonDefault_IsValid()
    {
        ServerWorldOptions options = new();
        Assert.Equal("{}", options.WorldConfigurationJson);
    }

    #endregion

    #region AtlasServerHost Obsolete Tests

    [Fact]
    public void AtlasServerHost_HasObsoleteAttribute()
    {
        Type type = typeof(AtlasServerHost);
        ObsoleteAttribute? attr = type.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(attr);
        Assert.Contains("EmbeddedServerHost", attr.Message);
        Assert.False(attr.IsError);
    }

    [Fact]
    public void AtlasServerHost_ObsoleteMessage_IsCorrect()
    {
        Type type = typeof(AtlasServerHost);
        ObsoleteAttribute? attr = type.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("Use EmbeddedServerHost instead. AtlasServerHost will be removed in a future version.", attr.Message);
    }

    #endregion

    #region EmbeddedServerHost API Tests

    [Fact]
    public void EmbeddedServerHost_ImplementsIDisposable()
    {
        Type type = typeof(EmbeddedServerHost);
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));
    }

    [Fact]
    public void EmbeddedServerHost_ImplementsIAsyncDisposable()
    {
        Type type = typeof(EmbeddedServerHost);
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(type));
    }

    [Fact]
    public void EmbeddedServerHost_HasBootFactoryMethod()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? bootMethod = type.GetMethod("Boot", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(bootMethod);
        Assert.True(bootMethod.IsStatic);
        Assert.Equal(type, bootMethod.ReturnType);
    }

    [Fact]
    public void EmbeddedServerHost_HasTickMethods()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? tickMethod = type.GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? ticksMethod = type.GetMethod("Ticks", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(tickMethod);
        Assert.NotNull(ticksMethod);
    }

    [Fact]
    public void EmbeddedServerHost_HasIsRunningProperty()
    {
        Type type = typeof(EmbeddedServerHost);
        PropertyInfo? isRunningProp = type.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(isRunningProp);
        Assert.Equal(typeof(bool), isRunningProp.PropertyType);
    }

    [Fact]
    public void EmbeddedServerHost_HasOptionsProperty()
    {
        Type type = typeof(EmbeddedServerHost);
        PropertyInfo? optionsProp = type.GetProperty("Options", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(optionsProp);
        Assert.Equal(typeof(ServerWorldOptions), optionsProp.PropertyType);
    }

    #endregion
}
