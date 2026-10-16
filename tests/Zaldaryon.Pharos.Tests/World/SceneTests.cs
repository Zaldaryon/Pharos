using Xunit;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Tests.World;

public sealed class SceneTests
{
    [Fact]
    public void Parse_MapsColumnsRowsAndLayers_AndTreatsDotAsAir()
    {
        SceneLayout scene = SceneLayout.Parse(
            new Dictionary<char, string> { ['#'] = "game:rock-granite", ['w'] = "game:water-still" },
            [["#.", "w#"], ["..", ".#"]]);

        Assert.Equal((2, 2, 2), (scene.Width, scene.Depth, scene.Height));
        Assert.Equal("game:rock-granite", scene.Cells.Single(c => (c.X, c.Y, c.Z) == (0, 0, 0)).BlockCode);
        Assert.Null(scene.Cells.Single(c => (c.X, c.Y, c.Z) == (1, 0, 0)).BlockCode);
        Assert.Equal("game:water-still", scene.Cells.Single(c => (c.X, c.Y, c.Z) == (0, 0, 1)).BlockCode);
        Assert.Equal("game:rock-granite", scene.Cells.Single(c => (c.X, c.Y, c.Z) == (1, 1, 1)).BlockCode);
    }

    [Fact]
    public void Parse_RejectsInvalidLayoutsBeforeAnyWorldAccess()
    {
        IReadOnlyDictionary<char, string> legend = new Dictionary<char, string> { ['#'] = "game:rock-granite" };

        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(legend, [[""]]));
        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(legend, [["##", "#"]]));
        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(legend, [["#"], ["##"]]));
        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(legend, [["?"]]));
        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(new Dictionary<char, string> { ['.'] = "game:air" }, [["."]]));
        Assert.Throws<ArgumentException>(() => SceneLayout.Parse(legend, [[new string('#', 65_537)]]));
    }
}
