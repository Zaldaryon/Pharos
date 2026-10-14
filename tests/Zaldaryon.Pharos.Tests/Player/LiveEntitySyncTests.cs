using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Player;

[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveEntitySyncTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override string PlayerName => "EntitySync";

    [ClientServerScenario]
    public async Task SpawnedEntityAndWatchedAttribute_ArriveAtTheClient()
    {
        Vec3d position = ServerPlayer.Entity!.Pos.XYZ.AddCopy(3, 0, 0);
        Entity entity = await ServerHost!.Entities.SpawnAsync("game:chicken-hen", position);
        ServerHost.RunOnGameThread(() =>
        {
            entity.WatchedAttributes.SetString("pharosSync", "arrived");
            entity.WatchedAttributes.MarkPathDirty("pharosSync");
        });

        bool synced = await Session!.StepUntilAsync(() =>
        {
            try
            {
                ClientServerAssert.EntitySynced(Session, entity.EntityId, tolerance: 0.2);
                ClientServerAssert.WatchedAttributeSynced(Session, entity.EntityId, "pharosSync");
                return true;
            }
            catch (PharosAssertException)
            {
                return false;
            }
        }, maxFrames: 600);

        Assert.True(synced, "The client did not receive the entity state and watched attribute.");
        ClientServerAssert.EntitySynced(Session, entity.EntityId, tolerance: 0.2);
        ClientServerAssert.WatchedAttributeSynced(Session, entity.EntityId, "pharosSync");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            ClientServerAssert.EntitySynced(Session, entity.EntityId, tolerance: double.NaN);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            ClientServerAssert.WatchedAttributeSynced(Session, entity.EntityId, "nested..value");
        });
    }
}
