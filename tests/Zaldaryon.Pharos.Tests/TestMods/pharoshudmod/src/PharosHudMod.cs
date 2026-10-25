using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PharosHudMod;

public sealed class PharosHudModSystem : ModSystem
{
    public override void Start(ICoreAPI api) => api.RegisterBlockClass("PharosHudProbe", typeof(BlockHudProbe));
}

public sealed class BlockHudProbe : Block
{
    public int Temperature = 1100;
    public int ApplyCalls;

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        ApplyCalls++;
        return "Pharos probe";
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        ApplyCalls++;
        return "<font color=\"#ff0000\">Temperature: " + Temperature + "</font>\nObserved mod text";
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
        new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "pharoshudmod:add-fuel",
                HotKeyCode = "sneak",
                MouseButton = EnumMouseButton.Right,
                ShouldApply = (wi, block, entity) => { ApplyCalls++; return true; },
            },
            new WorldInteraction
            {
                ActionLangCode = "pharoshudmod:hidden",
                ShouldApply = (wi, block, entity) => { ApplyCalls++; return false; },
            },
            new WorldInteraction
            {
                ActionLangCode = "pharoshudmod:hidden",
                Itemstacks = new[] { new ItemStack(world.GetItem(new AssetLocation("game:stick"))) },
                GetMatchingStacks = (wi, block, entity) => { ApplyCalls++; return new ItemStack[0]; },
            },
        };
}
