using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for <see cref="ServerWorldHelpers"/> extension methods.
/// </summary>
public class ServerWorldHelpersTests
{
    [Fact]
    public void SetBlock_ByCode_ThrowsOnNullScenario()
    {
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.SetBlock(null!, pos, "game:stone"));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void SetBlock_ByCode_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.SetBlock(null!, "game:stone"));
        Assert.Equal("pos", ex.ParamName);
    }

    [Fact]
    public void SetBlock_ByCode_ThrowsOnNullBlockCode()
    {
        var scenario = new ConcreteServerScenario();
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.SetBlock(pos, (string)null!));
        Assert.Equal("blockCode", ex.ParamName);
    }

    [Fact]
    public void SetBlock_ById_ThrowsOnNullScenario()
    {
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.SetBlock(null!, pos, 1));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void SetBlock_ById_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.SetBlock(null!, 1));
        Assert.Equal("pos", ex.ParamName);
    }

    [Fact]
    public void GetBlock_ThrowsOnNullScenario()
    {
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.GetBlock(null!, pos));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void GetBlock_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.GetBlock(null!));
        Assert.Equal("pos", ex.ParamName);
    }

    [Fact]
    public void GetBlockEntity_ThrowsOnNullScenario()
    {
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.GetBlockEntity(null!, pos));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void GetBlockEntity_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.GetBlockEntity(null!));
        Assert.Equal("pos", ex.ParamName);
    }

    [Fact]
    public void SpawnEntity_ThrowsOnNullScenario()
    {
        Vec3d pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.SpawnEntity(null!, "game:drifter", pos));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void SpawnEntity_ThrowsOnNullEntityCode()
    {
        var scenario = new ConcreteServerScenario();
        Vec3d pos = new(0, 64, 0);

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.SpawnEntity(null!, pos));
        Assert.Equal("entityCode", ex.ParamName);
    }

    [Fact]
    public void SpawnEntity_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.SpawnEntity("game:drifter", null!));
        Assert.Equal("pos", ex.ParamName);
    }

    [Fact]
    public async Task WaitForChunkLoadedAsync_ThrowsOnNullScenario()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => ServerWorldHelpers.WaitForChunkLoadedAsync(null!, 0, 0, 0));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public async Task WaitForEntitySpawnAsync_ThrowsOnNullScenario()
    {
        Vec3d pos = new(0, 64, 0);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => ServerWorldHelpers.WaitForEntitySpawnAsync(null!, "game:drifter", 10, pos));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public async Task WaitForEntitySpawnAsync_ThrowsOnNullEntityCode()
    {
        var scenario = new ConcreteServerScenario();
        Vec3d pos = new(0, 64, 0);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.WaitForEntitySpawnAsync(null!, 10, pos));
        Assert.Equal("entityCode", ex.ParamName);
    }

    [Fact]
    public async Task WaitForEntitySpawnAsync_ThrowsOnNullPos()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.WaitForEntitySpawnAsync("game:drifter", 10, null!));
        Assert.Equal("aroundPos", ex.ParamName);
    }

    [Fact]
    public async Task WaitForConditionAsync_ThrowsOnNullScenario()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => ServerWorldHelpers.WaitForConditionAsync(null!, () => { }, () => true));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public async Task WaitForConditionAsync_ThrowsOnNullTrigger()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.WaitForConditionAsync(null!, () => true));
        Assert.Equal("trigger", ex.ParamName);
    }

    [Fact]
    public async Task WaitForConditionAsync_ThrowsOnNullCondition()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.WaitForConditionAsync(() => { }, null!));
        Assert.Equal("condition", ex.ParamName);
    }

    [Fact]
    public void Tick_ThrowsOnNullScenario()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.Tick(null!));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void Ticks_ThrowsOnNullScenario()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.Ticks(null!, 10));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public async Task TickUntilAsync_ThrowsOnNullScenario()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => ServerWorldHelpers.TickUntilAsync(null!, () => true));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public async Task TickUntilAsync_ThrowsOnNullPredicate()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.TickUntilAsync(null!));
        Assert.Equal("predicate", ex.ParamName);
    }

    [Fact]
    public void GetBlockId_ThrowsOnNullScenario()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.GetBlockId(null!, "game:stone"));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void GetBlockId_ThrowsOnNullBlockCode()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.GetBlockId(null!));
        Assert.Equal("blockCode", ex.ParamName);
    }

    [Fact]
    public void GetEntityId_ThrowsOnNullScenario()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ServerWorldHelpers.GetEntityId(null!, "game:drifter"));
        Assert.Equal("scenario", ex.ParamName);
    }

    [Fact]
    public void GetEntityId_ThrowsOnNullEntityCode()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<ArgumentNullException>(
            () => scenario.GetEntityId(null!));
        Assert.Equal("entityCode", ex.ParamName);
    }

    [Fact]
    public void SetBlock_WhenApiUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.SetBlock(pos, "game:stone"));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void GetBlock_WhenApiUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.GetBlock(pos));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void GetBlockEntity_WhenApiUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();
        BlockPos pos = new(0, 64, 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.GetBlockEntity(pos));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void SpawnEntity_WhenApiUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();
        Vec3d pos = new(0, 64, 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.SpawnEntity("game:drifter", pos));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task WaitForChunkLoadedAsync_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.WaitForChunkLoadedAsync(0, 0, 0));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task WaitForEntitySpawnAsync_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();
        Vec3d pos = new(0, 64, 0);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.WaitForEntitySpawnAsync("game:drifter", 10, pos));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task WaitForConditionAsync_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.WaitForConditionAsync(() => { }, () => true));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void Tick_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.Tick());
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void Ticks_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.Throws<InvalidOperationException>(
            () => scenario.Ticks(10));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task TickUntilAsync_WhenHostUnavailable_ThrowsInvalidOperationException()
    {
        var scenario = new ConcreteServerScenario();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.TickUntilAsync(() => true));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public void GetBlockId_WhenApiUnavailable_ReturnsZero()
    {
        var scenario = new ConcreteServerScenario();

        int blockId = scenario.GetBlockId("game:stone");
        Assert.Equal(0, blockId);
    }

    [Fact]
    public void GetEntityId_WhenApiUnavailable_ReturnsZero()
    {
        var scenario = new ConcreteServerScenario();

        int entityId = scenario.GetEntityId("game:drifter");
        Assert.Equal(0, entityId);
    }

    // Interface structure tests
    [Fact]
    public void ServerWorldHelpers_HasSetBlockByCodeMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "SetBlock",
            new[] { typeof(ServerScenarioBase), typeof(BlockPos), typeof(string) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void ServerWorldHelpers_HasSetBlockByIdMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "SetBlock",
            new[] { typeof(ServerScenarioBase), typeof(BlockPos), typeof(int) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void ServerWorldHelpers_HasGetBlockMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "GetBlock",
            new[] { typeof(ServerScenarioBase), typeof(BlockPos) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Block), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasGetBlockEntityMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "GetBlockEntity",
            new[] { typeof(ServerScenarioBase), typeof(BlockPos) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(BlockEntity), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasSpawnEntityMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "SpawnEntity",
            new[] { typeof(ServerScenarioBase), typeof(string), typeof(Vec3d) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Entity), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasWaitForChunkLoadedAsyncMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod("WaitForChunkLoadedAsync");

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasWaitForEntitySpawnAsyncMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod("WaitForEntitySpawnAsync");

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<Entity>), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasWaitForConditionAsyncMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod("WaitForConditionAsync");

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasTickMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "Tick",
            new[] { typeof(ServerScenarioBase) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void ServerWorldHelpers_HasTicksMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "Ticks",
            new[] { typeof(ServerScenarioBase), typeof(int) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void ServerWorldHelpers_HasTickUntilAsyncMethod()
    {
        var methods = typeof(ServerWorldHelpers).GetMethods()
            .Where(m => m.Name == "TickUntilAsync");

        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.True(m.IsStatic));
    }

    [Fact]
    public void ServerWorldHelpers_HasGetBlockIdMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "GetBlockId",
            new[] { typeof(ServerScenarioBase), typeof(string) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(int), method.ReturnType);
    }

    [Fact]
    public void ServerWorldHelpers_HasGetEntityIdMethod()
    {
        var method = typeof(ServerWorldHelpers).GetMethod(
            "GetEntityId",
            new[] { typeof(ServerScenarioBase), typeof(string) });

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(int), method.ReturnType);
    }

    /// <summary>
    /// Concrete subclass of <see cref="ServerScenarioBase"/> for testing purposes.
    /// </summary>
    private sealed class ConcreteServerScenario : ServerScenarioBase
    {
    }
}
