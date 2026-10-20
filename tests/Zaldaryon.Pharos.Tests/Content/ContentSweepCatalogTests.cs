using Xunit;
using Zaldaryon.Pharos.Content;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Content;

public sealed class ContentSweepCatalogTests
{
    [Fact]
    public void DiscoverAssetCodes_ExpandsVariantGroupsAndAppliesFilters()
    {
        IReadOnlyList<ContentCode> codes = ContentSweepCatalog.DiscoverAssetCodes(
            typeof(ContentSweepCatalogTests),
            "pharositemmod",
            SweepSteps.All,
            [],
            []);

        Assert.Contains(codes, code => code.Kind == ContentKind.Block && code.Code == "pharositemmod:crate");
        Assert.Contains(codes, code => code.Kind == ContentKind.Item
            && code.Code == "pharositemmod:sword-iron"
            && code.Variants == "metal=iron");

        IReadOnlyList<ContentCode> filtered = ContentSweepCatalog.DiscoverAssetCodes(
            typeof(ContentSweepCatalogTests),
            "pharositemmod",
            SweepSteps.ItemHeldIdle,
            ["pharositemmod:crate"],
            ["metal=iron"]);
        ContentCode only = Assert.Single(filtered);
        Assert.Equal(ContentKind.Item, only.Kind);
        Assert.Equal("pharositemmod:sword-iron", only.Code);
        Assert.Equal("metal=iron", only.Variants);
        Assert.Equal(SweepSteps.ItemHeldIdle, only.Steps);
    }
}
