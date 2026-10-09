using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>A schematic placed in a client-server scenario reaches the client, meshed.</summary>
[Collection("Sequential")]
public class LiveSchematicTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task PlaceSchematicAsync_TheClientHasTheBlocksTheChestAndTheMeshes()
    {
        BlockPos player = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.AsBlockPos);
        BlockPos origin = player.AddCopy(4, -1, 4);

        SchematicPlacement placement = await this.PlaceSchematicAsync(SchematicTests.Room, origin);

        Assert.Equal("game:chest-east", Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(origin.AddCopy(1, 1, 1)).Code.ToString()));
        Assert.NotNull(Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlockEntity(origin.AddCopy(1, 1, 1))));
        Assert.All(placement.Chunks, chunk => Assert.True(Client.FrameController.IsChunkMeshed(chunk), $"Chunk {chunk} is not meshed"));
        Assert.True(await Session!.WaitForChunkMeshedAsync(origin));
    }
}
