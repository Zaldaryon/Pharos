using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

public class SurvivalDialogUnitTests
{
    [Fact]
    public void VerticalTabs_AreClickedOnTheRightEdge_BelowTheTabsAndPaddingsAbove()
    {
        int[] widths = [80, 120, 60];
        double[] paddings = [0, 20, 0];

        Assert.Equal((161, 12), GuiTabs.VerticalPoint(200, widths, paddings, 25, 5, 0));
        Assert.Equal((141, 62), GuiTabs.VerticalPoint(200, widths, paddings, 25, 5, 1));
        Assert.Equal((171, 92), GuiTabs.VerticalPoint(200, widths, paddings, 25, 5, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiTabs.VerticalPoint(200, widths, paddings, 25, 5, 3));
        Assert.Throws<InvalidOperationException>(() => GuiTabs.VerticalPoint(200, [0], [0], 25, 5, 0));
    }

    [Fact]
    public void HorizontalTabs_FollowOneAnother_ShiftedByTheScroll()
    {
        int[] widths = [100, 60, 80];

        Assert.Equal((55, 10), GuiTabs.HorizontalPoint(widths, 5, 0, 300, 21, 0));
        Assert.Equal((140, 10), GuiTabs.HorizontalPoint(widths, 5, 0, 300, 21, 1));
        Assert.Equal((95, 10), GuiTabs.HorizontalPoint(widths, 5, 120, 300, 21, 2));
        Assert.Throws<InvalidOperationException>(() => GuiTabs.HorizontalPoint(widths, 5, 0, 150, 21, 2));

        // Under a scroll arrow: the click would scroll.
        Assert.Throws<InvalidOperationException>(() => GuiTabs.HorizontalPoint(widths, 5, 0, 300, 21, 0, leftArrow: 60));
        Assert.Throws<InvalidOperationException>(() => GuiTabs.HorizontalPoint(widths, 5, 0, 230, 21, 2, rightArrow: 20));
        Assert.Equal((215, 10), GuiTabs.HorizontalPoint(widths, 5, 0, 300, 21, 2, leftArrow: 20, rightArrow: 20));
    }

    [Fact]
    public void RichText_JoinsTheDisplayTexts_AndSkipsWhatHasNone()
    {
        RichTextComponent text = Component<RichTextComponent>("Smelting turns ore into metal. ");
        LinkTextComponent link = Component<LinkTextComponent>("a Pharos gizmo");
        RichTextComponentBase other = (RichTextComponentBase)RuntimeHelpers.GetUninitializedObject(typeof(ItemstackTextComponent));

        Assert.Equal("Smelting turns ore into metal. a Pharos gizmo", GuiDriver.RichText([text, other, link]));
        Assert.Equal("", GuiDriver.RichText(null));
    }

    [Fact]
    public void UnknownHandbookCodes_SuggestTheCodesThatContainThem_ThenThoseSharingTheStart()
    {
        string[] codes = ["craftinginfo-starterguide", "craftinginfo-smelting", "item-game:flint", "block-game:soil-low-none"];

        Assert.Equal(["craftinginfo-smelting"], HandbookDriver.Close("smelting", codes));
        Assert.Equal(["craftinginfo-starterguide", "craftinginfo-smelting"], HandbookDriver.Close("craftinginfo-starterguid3", codes));
        Assert.Empty(HandbookDriver.Close("zzz", codes));
    }

    private static T Component<T>(string text) where T : RichTextComponent
    {
        T component = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        component.DisplayText = text;
        return component;
    }
}

/// <summary>
/// The survival handbook, the world map and waypoints on an engine-mode client in a creative
/// world, with <c>TestMods/pharoshandbookmod</c>: a handbook guide that mentions smelting and
/// links to the mod's gizmo item. The world sets <c>allowMap</c>, as the game's play styles do:
/// the server tracks players for the map only then.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharoshandbookmod")]
[ServerWorld(seed: 4242, WorldConfigurationJson = "{\"allowMap\": true}")]
public class SurvivalDialogHandbookAndMapTests : ClientServerScenarioBase
{
    private const string GuideCode = "pharoshandbookmod-guide";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1600,
        Height = 1000,
    };

    private GuiDriver Ui => Client!.Ui!;

    [ClientServerScenario]
    public async Task Handbook_OpensAModsGuideByCode_ReadsIt_SearchesIt_AndListsTheModsPages()
    {
        HandbookDriver handbook = Ui.Handbook;
        Assert.True(handbook.IsAvailable);

        await handbook.OpenAsync();
        Assert.True(handbook.IsOpen);
        Assert.Null(handbook.CurrentPageCode);

        // The tutorials category lists no guide of the mod: the search goes back to everything.
        string tutorials = Client!.RunOnClientThread(() => Vintagestory.API.Config.Lang.Get("handbook-category-tutorials"));
        Ui.ClickTab("GuiDialogSurvivalHandbook", "verticalTabs", tutorials);
        await Session!.StepFramesAsync(1);
        Assert.True(Assert.Single(Ui.Tabs("GuiDialogSurvivalHandbook", "verticalTabs"), t => t.Name == tutorials).Active);

        await handbook.OpenPageAsync(GuideCode);
        Assert.Equal(GuideCode, handbook.CurrentPageCode);
        Assert.Equal("Pharos field guide", handbook.PageTitle);
        Assert.Contains("Smelting turns ore into metal", handbook.PageText);
        Assert.Contains("a Pharos gizmo", handbook.PageText);

        IReadOnlyList<HandbookPageInfo> found = await handbook.SearchAsync("lighthouse");
        Assert.Null(handbook.CurrentPageCode);
        HandbookPageInfo guide = Assert.Single(found, p => p.Code == GuideCode);
        Assert.Equal(new HandbookPageInfo(GuideCode, "Pharos field guide", "guide", "text", "pharoshandbookmod"), guide);

        IReadOnlyList<HandbookPageInfo> mine = await handbook.PagesAsync("pharoshandbookmod");
        Assert.Contains(mine, p => p.Code == GuideCode);
        Assert.Contains(mine, p => p.Code == "item-pharoshandbookmod:gizmo" && p.Kind == "stack" && p.Title == "Pharos gizmo");
        Assert.All(mine, p => Assert.Equal("pharoshandbookmod", p.Domain));

        await handbook.CloseAsync();
        Assert.False(handbook.IsOpen);
    }

    [ClientServerScenario]
    public async Task Handbook_OpensTheVanillaStarterGuide_AndNamesCloseCodesForAnUnknownOne()
    {
        HandbookDriver handbook = Ui.Handbook;

        KeyNotFoundException unknown = await Assert.ThrowsAsync<KeyNotFoundException>(() => handbook.OpenPageAsync("craftinginfo-startergide"));
        Assert.Contains("craftinginfo-starterguide", unknown.Message);
        Assert.False(handbook.IsOpen);

        // Follows the link while the handbook is closed: the link opens it.
        await handbook.OpenPageAsync("craftinginfo-starterguide");
        Assert.True(handbook.IsOpen);
        Assert.Equal("craftinginfo-starterguide", handbook.CurrentPageCode);
        Assert.False(string.IsNullOrWhiteSpace(handbook.PageText));

        await handbook.CloseAsync();
    }

    [ClientServerScenario]
    public async Task WorldMap_TogglesTheMinimap_ListsTheLayers_AndHidesAGroupByItsTab()
    {
        WorldMapDriver map = Ui.WorldMap;
        Assert.True(map.IsAvailable);
        object? minimapSetting = Client!.Settings.Get("showMinimapHud");
        bool minimap = map.IsMinimapShown;
        try
        {
            await map.SetMinimapAsync(!minimap);
            Assert.Equal(!minimap, map.IsMinimapShown);
            Assert.Equal(!minimap, Client.Settings.Get("showMinimapHud"));
            await map.SetMinimapAsync(minimap);
            Assert.Equal(minimap, map.IsMinimapShown);

            IReadOnlyList<MapLayerInfo> layers = map.Layers();
            Assert.Contains(layers, l => l.Code == "chunks" && l.GroupCode == "terrain");
            Assert.Contains(layers, l => l.Code == "players" && l.GroupCode == "terrain" && l.DataSide == "Client");
            Assert.Contains(layers, l => l.Code == "waypoints" && l.GroupCode == "waypoints" && l.DataSide == "Server");
            await Assert.ThrowsAsync<InvalidOperationException>(() => map.SetLayerGroupActiveAsync("waypoints", false));

            await map.SetSizeAsync(900, 600);
            await map.OpenAsync();
            Assert.True(map.IsOpen);
            Assert.False(map.IsMinimapShown);
            await Assert.ThrowsAsync<InvalidOperationException>(() => map.SetMinimapAsync(true));
            Assert.True(await StepUntilAsync(() => map.Markers("players").Any(m => m.Label == PlayerName), maxFrames: 120), "The map never showed the player");

            await map.SetLayerGroupActiveAsync("waypoints", false);
            Assert.False(Assert.Single(map.Layers(), l => l.Code == "waypoints").Active);
            Assert.True(Assert.Single(map.Layers(), l => l.Code == "chunks").Active);
            await map.SetLayerGroupActiveAsync("waypoints", true);
            Assert.True(Assert.Single(map.Layers(), l => l.Code == "waypoints").Active);
            await Assert.ThrowsAsync<ArgumentException>(() => map.SetLayerGroupActiveAsync("nosuchgroup", true));

            await map.CloseAsync();
            Assert.False(map.IsOpen);
        }
        finally
        {
            await map.CloseAsync();
            await map.SetMinimapAsync(minimap);
            await map.SetSizeAsync(1200, 800);
        }

        Assert.Equal(minimapSetting, Client.Settings.Get("showMinimapHud"));
    }

    [ClientServerScenario]
    public async Task Waypoints_AreAddedAndRemovedThroughTheGamesDialogs_AndTheServerFollows()
    {
        WorldMapDriver map = Ui.WorldMap;
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName).Player;
        ICoreServerAPI sapi = (ICoreServerAPI)Server.Api;
        Vec3d at = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ).Add(12.5, 0, -7.25);

        Assert.Contains("home", map.WaypointIcons);
        string color = map.WaypointColors[3];
        ArgumentException badIcon = await Assert.ThrowsAsync<ArgumentException>(() => map.AddWaypointAsync(at, "Nope", icon: "nosuchicon"));
        Assert.Contains("home", badIcon.Message);

        WaypointInfo added = await map.AddWaypointAsync(at, "Pharos camp", icon: "home", color: color, pinned: true);

        Assert.Equal("Pharos camp", added.Title);
        Assert.Equal("home", added.Icon);
        Assert.Equal(color, added.Color, ignoreCase: true);
        Assert.True(added.Position.Equals(at, 0.01), $"Added at {added.Position}, not {at}");
        Assert.True(added.Pinned);
        Assert.False(Ui.IsOpen("GuiDialogAddWayPoint"));
        Assert.Contains(map.Markers("waypoints"), m => m.Label == "Pharos camp");

        WaypointInfo onServer = Assert.Single(ServerHost!.RunOnGameThread(() => ServerWaypoints.Of(sapi, player.PlayerUID)));
        Assert.Equal(("Pharos camp", "home"), (onServer.Title, onServer.Icon));
        Assert.Equal(color, onServer.Color, ignoreCase: true);
        Assert.True(onServer.Position.Equals(at, 0.01));
        Assert.Equal(player.PlayerUID, onServer.OwnerUid);
        Assert.True(onServer.Pinned);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => map.RemoveWaypointAsync("No such camp"));
        await map.RemoveWaypointAsync("Pharos camp");

        Assert.Empty(map.Waypoints);
        Assert.Empty(ServerHost.RunOnGameThread(() => ServerWaypoints.Of(sapi, player.PlayerUID)));
        Assert.DoesNotContain(Ui.OpenDialogs(), d => d.Type == "GuiDialogEditWayPoint");
    }
}

/// <summary>A world whose configuration turns the map off.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, WorldConfigurationJson = "{\"allowMap\": false}")]
public class SurvivalDialogNoMapTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1024,
        Height = 768,
    };

    [ClientServerScenario]
    public async Task WorldMap_IsUnavailable_AndOpeningItThrows()
    {
        WorldMapDriver map = Client!.Ui!.WorldMap;

        Assert.False(map.IsAvailable);
        Assert.False(map.IsOpen);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => map.OpenAsync());
        Assert.Contains("allowMap", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => map.AddWaypointAsync(new Vec3d(1, 2, 3), "Nowhere"));
    }
}

/// <summary>The character dialog of a survival player.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild", worldType: "superflat")]
public class SurvivalDialogCharacterTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1600,
        Height = 1000,
    };

    [ClientServerScenario]
    public async Task CharacterDialog_SwitchesTabs_ShowsStats_AndEquipsArmourFromTheHotbar()
    {
        CharacterDriver character = Client!.Ui!.Character;
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName).Player;
        const string armour = "game:armor-body-improvised-wood";
        ServerHost!.RunOnGameThread(() =>
        {
            ItemSlot slot = player.InventoryManager.GetHotbarInventory()![2]!;
            slot.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation(armour)), 1);
            slot.MarkDirty();
        });
        Assert.True(await StepUntilAsync(() => Client.Inventory.GetSlotContents(2).ItemCode == armour, maxFrames: 120), "The armour never reached the hotbar");

        await character.OpenAsync();
        Assert.True(character.IsOpen);
        string characterTab = Client.RunOnClientThread(() => Vintagestory.API.Config.Lang.Get("charactertab-character"));
        string traitsTab = Client.RunOnClientThread(() => Vintagestory.API.Config.Lang.Get("charactertab-traits"));
        Assert.Equal([characterTab, traitsTab], character.Tabs.Select(t => t.Name));
        Assert.Equal(characterTab, character.CurrentTab);

        IReadOnlyDictionary<string, string> stats = character.Stats();
        Assert.Equal("100%", character.Stat("walkspeed"));
        Assert.Contains("health", stats.Keys);
        Assert.Throws<KeyNotFoundException>(() => character.Stat("nosuchstat"));

        await character.SwitchTabAsync(traitsTab);
        Assert.Equal(traitsTab, character.CurrentTab);
        await Assert.ThrowsAsync<InvalidOperationException>(() => character.EquipAsync(2, EnumCharacterDressType.ArmorBody));
        await character.SwitchTabAsync(0);
        Assert.Equal(characterTab, character.CurrentTab);

        await character.EquipAsync(2, EnumCharacterDressType.ArmorBody);

        Assert.Equal(armour, character.Gear(EnumCharacterDressType.ArmorBody)?.Collectible.Code.ToString());
        Assert.True(await StepUntilAsync(
            () => ServerHost.RunOnGameThread(() => player.InventoryManager.GetOwnInventory("character")![(int)EnumCharacterDressType.ArmorBody]!.Itemstack?.Collectible.Code.ToString() == armour),
            maxFrames: 120), "The server never put the armour in the body armour slot");
        Assert.Null(Client.Inventory.GetSlotContents(2).ItemCode);

        // A second piece swaps with the first, which goes back into the hotbar slot.
        const string jerkin = "game:armor-body-jerkin-leather";
        ServerHost.RunOnGameThread(() =>
        {
            ItemSlot slot = player.InventoryManager.GetHotbarInventory()![3]!;
            slot.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation(jerkin)), 1);
            slot.MarkDirty();
        });
        Assert.True(await StepUntilAsync(() => Client.Inventory.GetSlotContents(3).ItemCode == jerkin, maxFrames: 120), "The jerkin never reached the hotbar");

        await character.EquipAsync(3, EnumCharacterDressType.ArmorBody);

        Assert.Equal(jerkin, character.Gear(EnumCharacterDressType.ArmorBody)?.Collectible.Code.ToString());
        Assert.True(await StepUntilAsync(() => Client.Inventory.GetSlotContents(3).ItemCode == armour, maxFrames: 120), "The old armour did not go back to the hotbar");
        Assert.True(Client.RunOnClientThread(() => Client.Client.player.InventoryManager.MouseItemSlot.Empty));

        await character.CloseAsync();
        Assert.False(character.IsOpen);
    }
}
