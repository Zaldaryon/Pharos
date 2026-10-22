using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The animals of the server: what they breed, how they grow and what a hen box holds.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveAnimalTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheBreedingState_IsRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity sow = await Server.Entities.SpawnAsync("game:pig-eurasian-adult-female", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        AnimalState state = Server.Animals.Of(sow);

        Assert.NotNull(state.Breeding);
        Assert.False(state.Breeding!.IsPregnant);
        Assert.True(state.Breeding.PregnancyDays > 0);
        Assert.True(state.Breeding.PortionsEatenForMultiply > 0);
    }

    [ServerScenario]
    public async Task TheGrowthState_IsRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity chick = await Server.Entities.SpawnAsync("game:chicken-baby", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        AnimalState state = Server.Animals.Of(chick);

        Assert.NotNull(state.Growth);
        Assert.True(state.Growth!.HoursToGrow > 0);
        Assert.True(state.Growth.AdultEntityCodes.Any(code => code.Contains("henpoult")), "The chick grows into no hen.");
    }

    [ServerScenario]
    public async Task TheAnimals_Breed()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Vec3d place = player.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        Entity sow = await Server.Entities.SpawnAsync("game:pig-eurasian-adult-female", place);
        Entity boar = await Server.Entities.SpawnAsync("game:pig-eurasian-adult-male", place.AddCopy(1, 0, 0));

        Entity[] piglets = await Server.Animals.BreedAsync(boar, sow, maxDays: 40);

        Assert.NotEmpty(piglets);
        Assert.Contains(piglets, piglet => piglet.Code.Path.Contains("baby"));
    }

    [ServerScenario]
    public async Task TheChick_GrowsIntoAnAdult()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity chick = await Server.Entities.SpawnAsync("game:chicken-baby", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        Entity? adult = await Server.Animals.GrowAsync(chick, maxDays: 30);

        Assert.NotNull(adult);
        Assert.Contains("henpoult", adult!.Code.Path);
    }

    [ServerScenario]
    public async Task TheHenBox_CountsItsEggs()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, -1, 0);
        Server.SetBlock(pos, "game:henbox");

        HenBoxState box = Server.Animals.HenBox(pos);

        Assert.Equal(0, box.Eggs);
        Assert.False(box.Occupied);
    }
}
