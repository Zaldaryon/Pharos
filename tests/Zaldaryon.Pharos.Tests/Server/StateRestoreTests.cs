using Vintagestory.API.Datastructures;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>How a rollback puts attribute trees and mod data back, without a game.</summary>
public class StateRestoreTests
{
    [Fact]
    public void TreeInPlace_KeepsTheLiveSubTreeAndPutsItsValuesBack()
    {
        SyncedTreeAttribute live = new();
        TreeAttribute hunger = new();
        hunger.SetFloat("currentsaturation", 1500);
        live["hunger"] = hunger;
        live.SetInt("entityDead", 0);
        TreeAttribute saved = TreeAttribute.CreateFromBytes(live.ToBytes());

        hunger.SetFloat("currentsaturation", 100);
        hunger.SetFloat("added", 1);
        live.SetInt("entityDead", 1);
        live.SetString("mymod:buff", "strong");
        live.MarkClean();

        bool changed = StateRestore.TreeInPlace(live, saved, _ => true);

        Assert.True(changed);
        Assert.Same(hunger, live.GetTreeAttribute("hunger"));
        Assert.Equal(1500, hunger.GetFloat("currentsaturation"));
        Assert.False(hunger.HasAttribute("added"));
        Assert.Equal(0, live.GetInt("entityDead"));
        Assert.False(live.HasAttribute("mymod:buff"));
        Assert.False(live.AllDirty || live.PartialDirty, "The restore marked the tree dirty itself");
    }

    [Fact]
    public void TreeInPlace_LeavesARefusedKeyAsItIs()
    {
        TreeAttribute live = new();
        live.SetInt("positionVersionNumber", 3);
        live.SetString("mymod:keep", "before");
        TreeAttribute saved = TreeAttribute.CreateFromBytes(live.ToBytes());

        live.SetInt("positionVersionNumber", 9);
        live.SetString("mymod:keep", "after");
        live.SetString("mymod:new", "kept");

        StateRestore.TreeInPlace(live, saved, key => key is not ("positionVersionNumber" or "mymod:keep" or "mymod:new"));

        Assert.Equal(9, live.GetInt("positionVersionNumber"));
        Assert.Equal("after", live.GetString("mymod:keep"));
        Assert.Equal("kept", live.GetString("mymod:new"));
    }

    [Fact]
    public void TreeInPlace_ReportsAnUnchangedTree()
    {
        TreeAttribute live = new();
        live.SetFloat("currenthealth", 15);

        Assert.False(StateRestore.TreeInPlace(live, TreeAttribute.CreateFromBytes(live.ToBytes()), _ => true));
    }

    [Fact]
    public void ModDataInPlace_CountsWhatItPutBackAndHonoursTheFilter()
    {
        Dictionary<string, byte[]> saved = new() { ["same"] = [1], ["changed"] = [2], ["removed"] = [3], ["keep"] = [4] };
        Dictionary<string, byte[]> live = new() { ["same"] = [1], ["changed"] = [9], ["added"] = [5], ["keep"] = [8], ["keepnew"] = [7] };
        byte[] same = live["same"];

        int count = StateRestore.ModDataInPlace(live, saved, key => !key.StartsWith("keep", StringComparison.Ordinal));

        Assert.Equal(3, count);
        Assert.Same(same, live["same"]);
        Assert.Equal([2], live["changed"]);
        Assert.Equal([3], live["removed"]);
        Assert.NotSame(saved["removed"], live["removed"]);
        Assert.False(live.ContainsKey("added"));
        Assert.Equal([8], live["keep"]);
        Assert.Equal([7], live["keepnew"]);
    }

    [Fact]
    public void ModDataInPlace_TreatsAKeyStoredWithNullAsAbsent()
    {
        Dictionary<string, byte[]> saved = StateRestore.Copy(new Dictionary<string, byte[]> { ["a"] = [1], ["empty"] = null! });
        Dictionary<string, byte[]> live = new() { ["a"] = [1], ["empty"] = null!, ["stored-null"] = null! };

        Assert.False(saved.ContainsKey("empty"));
        Assert.Equal(0, StateRestore.ModDataInPlace(live, saved, _ => true));
        Assert.Equal(0, StateRestore.ModDataInPlace(live, saved, _ => true));
    }

    [Fact]
    public void IsolationReport_ListsWhatTheRollbackPutBack()
    {
        IsolationReport report = new(IsolationKind.RolledBack, null, 12, 3, TimeSpan.FromMilliseconds(340))
        {
            PlayersRestored = 1,
            ModDataKeysRestored = 2,
            MapRegionsRestored = 1,
        };

        Assert.Equal("rolled back (12 chunks, 1 player, 2 mod data keys, 1 map region, 3 listeners removed, 340 ms)", report.ToString());
    }

    [Fact]
    public void WorldRestoreFilter_PutsBackEveryKeyByDefault()
    {
        Assert.True(WorldRestoreFilter.All.RestoresModData("anything"));
        Assert.True(WorldRestoreFilter.All.RestoresPlayerAttribute("health"));
        Assert.False(new WorldRestoreFilter(ModData: k => k != "x").RestoresModData("x"));
    }
}
