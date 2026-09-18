using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for xUnit server scenario attributes and related types.
/// </summary>
public class ServerScenarioAttributeTests
{
    [Fact]
    public void ServerScenarioAttribute_InheritsFactAttribute()
    {
        Assert.Equal(typeof(Xunit.FactAttribute), typeof(ServerScenarioAttribute).BaseType);
    }

    [Fact]
    public void ServerTheoryAttribute_InheritsTheoryAttribute()
    {
        Assert.Equal(typeof(Xunit.TheoryAttribute), typeof(ServerTheoryAttribute).BaseType);
    }

    [Fact]
    public void WorldIsolation_Rollback_IsDefault()
    {
        Assert.Equal(WorldIsolation.Rollback, default(WorldIsolation));
    }

    [Fact]
    public void WorldIsolation_HasThreeValues()
    {
        var values = Enum.GetValues<WorldIsolation>();
        Assert.Equal(3, values.Length);
        Assert.Contains(WorldIsolation.Rollback, values);
        Assert.Contains(WorldIsolation.Restart, values);
        Assert.Contains(WorldIsolation.Recycle, values);
    }

    [Fact]
    public void ServerScenarioAttribute_DefaultIsolation_IsRollback()
    {
        var attr = new ServerScenarioAttribute();
        Assert.Equal(WorldIsolation.Rollback, attr.Isolation);
    }

    [Fact]
    public void ServerScenarioAttribute_DefaultTimeout_Is120Seconds()
    {
        var attr = new ServerScenarioAttribute();
        Assert.Equal(120_000, attr.TimeoutMs);
        Assert.Equal(120_000, ServerScenarioAttribute.DefaultTimeoutMs);
    }

    [Fact]
    public void ServerScenarioAttribute_TimeoutMs_CanBeSet()
    {
        var attr = new ServerScenarioAttribute { TimeoutMs = 60_000 };
        Assert.Equal(60_000, attr.TimeoutMs);
    }

    [Fact]
    public void ServerTheoryAttribute_DefaultIsolation_IsRollback()
    {
        var attr = new ServerTheoryAttribute();
        Assert.Equal(WorldIsolation.Rollback, attr.Isolation);
    }

    [Fact]
    public void ServerTheoryAttribute_DefaultTimeout_Is120Seconds()
    {
        var attr = new ServerTheoryAttribute();
        Assert.Equal(120_000, attr.TimeoutMs);
        Assert.Equal(120_000, ServerTheoryAttribute.DefaultTimeoutMs);
    }

    [Fact]
    public void ServerWorldAttribute_DefaultValues()
    {
        var attr = new ServerWorldAttribute();
        Assert.Equal(0, attr.Seed);
        Assert.Equal("creativebuilding", attr.PlayStyle);
        Assert.Equal("superflat", attr.WorldType);
        Assert.Null(attr.WorldConfigurationJson);
        Assert.Equal(WorldIsolation.Rollback, attr.Isolation);
    }

    [Fact]
    public void ServerWorldAttribute_ConstructorWithParameters()
    {
        var attr = new ServerWorldAttribute(seed: 12345, playStyle: "surviveandbuild", worldType: "standard");
        Assert.Equal(12345, attr.Seed);
        Assert.Equal("surviveandbuild", attr.PlayStyle);
        Assert.Equal("standard", attr.WorldType);
    }

    [Fact]
    public void ServerWorldAttribute_WorldConfigurationJson_CanBeSet()
    {
        var attr = new ServerWorldAttribute { WorldConfigurationJson = """{"test": true}""" };
        Assert.Equal("""{"test": true}""", attr.WorldConfigurationJson);
    }

    [Fact]
    public void ServerWorldAttribute_CanApplyToClassOrMethod()
    {
        var attr = typeof(ServerWorldAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attr);
        Assert.True(attr.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(attr.ValidOn.HasFlag(AttributeTargets.Method));
    }

    [Fact]
    public void ServerModsAttribute_EmptyByDefault()
    {
        var attr = new ServerModsAttribute();
        Assert.NotNull(attr.ModPaths);
        Assert.Empty(attr.ModPaths);
    }

    [Fact]
    public void ServerModsAttribute_AcceptsSinglePath()
    {
        var attr = new ServerModsAttribute("/path/to/mod");
        Assert.Single(attr.ModPaths);
        Assert.Equal("/path/to/mod", attr.ModPaths[0]);
    }

    [Fact]
    public void ServerModsAttribute_AcceptsMultiplePaths()
    {
        var attr = new ServerModsAttribute("/path/to/mod1", "/path/to/mod2", "/path/to/mod3.zip");
        Assert.Equal(3, attr.ModPaths.Count);
        Assert.Equal("/path/to/mod1", attr.ModPaths[0]);
        Assert.Equal("/path/to/mod2", attr.ModPaths[1]);
        Assert.Equal("/path/to/mod3.zip", attr.ModPaths[2]);
    }

    [Fact]
    public void ServerModsAttribute_CanApplyToClassOrAssembly()
    {
        var attr = typeof(ServerModsAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attr);
        Assert.True(attr.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(attr.ValidOn.HasFlag(AttributeTargets.Assembly));
        Assert.True(attr.AllowMultiple);
    }

    [Fact]
    public void ServerScenarioAttribute_HasMethodOnlyUsage()
    {
        var attr = typeof(ServerScenarioAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(AttributeTargets.Method, attr.ValidOn);
        Assert.False(attr.AllowMultiple);
    }

    [Fact]
    public void ServerTheoryAttribute_HasMethodOnlyUsage()
    {
        var attr = typeof(ServerTheoryAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(AttributeTargets.Method, attr.ValidOn);
        Assert.False(attr.AllowMultiple);
    }
}
