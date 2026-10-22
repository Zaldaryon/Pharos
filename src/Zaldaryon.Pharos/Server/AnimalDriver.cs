using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>What an animal's breeding state says.</summary>
/// <param name="IsPregnant">Whether the animal is pregnant.</param>
/// <param name="PregnancyStartDays">The day the pregnancy began.</param>
/// <param name="PregnancyDays">How long the pregnancy lasts.</param>
/// <param name="LastBirthDays">The day of the last birth.</param>
/// <param name="CooldownUntilDays">The day the animal may breed again.</param>
/// <param name="PortionsEatenForMultiply">How much the animal has to eat to breed.</param>
/// <param name="PortionsLeftToEat">How much the animal still has to eat.</param>
public sealed record AnimalBreedingState(
    bool IsPregnant,
    double PregnancyStartDays,
    double PregnancyDays,
    double LastBirthDays,
    double CooldownUntilDays,
    float PortionsEatenForMultiply,
    float PortionsLeftToEat);

/// <summary>What an animal's growth says.</summary>
/// <param name="HoursToGrow">How many hours the animal needs to grow.</param>
/// <param name="AdultEntityCodes">The codes the animal grows into.</param>
public sealed record AnimalGrowthState(float HoursToGrow, IReadOnlyList<string> AdultEntityCodes);

/// <summary>What an animal is and what it is about to do.</summary>
public sealed class AnimalState
{
    internal AnimalState(AnimalBreedingState? breeding, AnimalGrowthState? growth)
    {
        Breeding = breeding;
        Growth = growth;
    }

    /// <summary>The breeding state, when the animal breeds.</summary>
    public AnimalBreedingState? Breeding { get; }

    /// <summary>The growth state, when the animal grows.</summary>
    public AnimalGrowthState? Growth { get; }
}

/// <summary>What a hen box holds.</summary>
/// <param name="Eggs">How many eggs are in the box.</param>
/// <param name="Occupied">Whether an animal sits in the box.</param>
public sealed record HenBoxState(int Eggs, bool Occupied);

/// <summary>
/// The animals of the server: what they breed, how they grow, what a hen box holds and how a
/// calf becomes an adult.
/// </summary>
public sealed class AnimalDriver
{
    private const string MultiplyBehavior = "EntityBehaviorMultiply";
    private const string MultiplyBaseBehavior = "EntityBehaviorMultiplyBase";
    private const string GrowBehavior = "EntityBehaviorGrow";
    private const string HenBoxBlockEntity = "BlockEntityHenBox";

    private readonly EmbeddedServerHost _host;

    internal AnimalDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// The state of an animal: its breeding and its growth.
    /// </summary>
    /// <param name="entity">The animal.</param>
    public AnimalState Of(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return _host.RunOnGameThread(() => new AnimalState(BreedingOf(entity), GrowthOf(entity)));
    }

    /// <summary>
    /// Feeds an animal, the way the game counts the food it eats.
    /// </summary>
    /// <param name="entity">The animal to feed.</param>
    /// <param name="portions">How many portions to give it.</param>
    public void Feed(Entity entity, float portions)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegative(portions);

        _host.RunOnGameThread(() =>
        {
            // The hunger tree is made by the client and sent to the server, so a server without a
            // client has none. The driver makes it, the way the client does.
            ITreeAttribute? hunger = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hunger == null)
            {
                entity.WatchedAttributes.SetAttribute("hunger", new TreeAttribute());
                hunger = entity.WatchedAttributes.GetTreeAttribute("hunger");
            }

            if (hunger == null) return;
            hunger.SetFloat("saturation", hunger.GetFloat("saturation", 0) + portions);
        });
    }

    /// <summary>
    /// Breeds two animals, the way the game does, and returns the offspring.
    /// </summary>
    /// <param name="male">The male, which has to be near the female.</param>
    /// <param name="female">The female, which has to have eaten enough.</param>
    /// <param name="maxDays">The most days the pregnancy may take.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The offspring, which is empty when the animals did not breed.</returns>
    public async Task<Entity[]> BreedAsync(Entity male, Entity female, double maxDays = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(male);
        ArgumentNullException.ThrowIfNull(female);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);

        object? multiply = _host.RunOnGameThread(() => GameMembers.FindBehavior(female, MultiplyBehavior));
        if (multiply == null) return [];

        // The female has to have eaten enough, and the male has to be near her and fed himself.
        MoveNear(male, female);
        ClearCooldown(multiply);

        HashSet<long> before = EntityIdsAround(female, radius: 8);

        // The game asks every few seconds, so the driver asks too until the female is pregnant.
        // Every question eats the food the female gathered, so she is fed again each time.
        for (int attempt = 0; attempt < 200; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsPregnant(multiply)) break;

            Feed(female, PortionsNeeded(multiply));
            Feed(male, 1);

            _host.RunOnGameThread(() => GameMembers.Call(multiply, "CheckMultiply", 0f));
            _host.Tick();
            await Task.Yield();
        }

        if (!IsPregnant(multiply)) return [];

        // The pregnancy lasts a few days of game time.
        double pregnancyDays = PregnancyDays(multiply);
        for (double day = 0; day < maxDays; day += 0.25)
        {
            ct.ThrowIfCancellationRequested();
            _host.Calendar.Advance(TimeSpan.FromHours(6));
            _host.Tick();
            _host.RunOnGameThread(() => GameMembers.Call(multiply, "CheckMultiply", 0f));
            await Task.Yield();
            if (!IsPregnant(multiply)) break;
        }

        List<Entity> offspring = [];
        foreach (Entity entity in EntitiesAround(female, radius: 8))
        {
            if (!before.Contains(entity.EntityId)) offspring.Add(entity);
        }

        return [.. offspring];
    }

    /// <summary>
    /// Grows a young animal into its adult, the way the game does, and returns the adult.
    /// </summary>
    /// <param name="calf">The young animal.</param>
    /// <param name="maxDays">The most days the growth may take.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The adult, which is null when the animal did not grow.</returns>
    public async Task<Entity?> GrowAsync(Entity calf, double maxDays = 30, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(calf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);

        object? grow = _host.RunOnGameThread(() => GameMembers.FindBehavior(calf, GrowBehavior));
        if (grow == null) return null;

        HashSet<long> before = EntityIdsAround(calf, radius: 8);

        for (double day = 0; day < maxDays; day += 0.25)
        {
            ct.ThrowIfCancellationRequested();
            _host.Calendar.Advance(TimeSpan.FromHours(6));
            _host.Tick();
            _host.RunOnGameThread(() => GameMembers.Call(grow, "CheckGrowth", 0f));
            await Task.Yield();

            if (!calf.Alive) break;
        }

        // The growth replaces the young animal with the adult, so the adult is a new entity.
        foreach (Entity entity in EntitiesAround(calf, radius: 8))
        {
            if (!before.Contains(entity.EntityId)) return entity;
        }

        return null;
    }

    /// <summary>
    /// Reads what a hen box holds.
    /// </summary>
    /// <param name="pos">The hen box.</param>
    public HenBoxState HenBox(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            object? box = GameMembers.FindBlockEntity(_host.Api.World.BlockAccessor, pos, HenBoxBlockEntity);
            if (box == null) return new HenBoxState(0, false);

            int eggs = (int)(GameMembers.Call(box, "CountEggs") ?? 0);
            bool occupied = GameMembers.Get(box, "occupier") is Entity occupier && occupier.Alive;
            return new HenBoxState(eggs, occupied);
        });
    }

    private AnimalBreedingState? BreedingOf(Entity entity)
    {
        object? multiply = GameMembers.FindBehavior(entity, MultiplyBehavior);
        if (multiply == null) return null;

        object? baseBehavior = GameMembers.FindBehavior(entity, MultiplyBaseBehavior);
        float portions = baseBehavior == null ? 0f : (float)(GameMembers.Get(baseBehavior, "PortionsEatenForMultiply") ?? 0f);
        float left = baseBehavior == null ? 0f : (float)(GameMembers.Get(baseBehavior, "PortionsLeftToEat") ?? 0f);

        return new AnimalBreedingState(
            (bool)(GameMembers.Get(multiply, "IsPregnant") ?? false),
            (double)(GameMembers.Get(multiply, "TotalDaysPregnancyStart") ?? 0d),
            (double)(GameMembers.Get(multiply, "PregnancyDays") ?? 0d),
            (double)(GameMembers.Get(multiply, "TotalDaysLastBirth") ?? 0d),
            baseBehavior == null ? 0d : (double)(GameMembers.Get(baseBehavior, "TotalDaysCooldownUntil") ?? 0d),
            portions,
            left);
    }

    private AnimalGrowthState? GrowthOf(Entity entity)
    {
        object? grow = GameMembers.FindBehavior(entity, GrowBehavior);
        if (grow == null) return null;

        float hours = (float)(GameMembers.Get(grow, "HoursToGrow") ?? 0f);
        List<string> adults = [];
        if (GameMembers.Get(grow, "AdultEntityCodes") is object[] codes)
        {
            foreach (object code in codes) adults.Add(code?.ToString() ?? "");
        }

        return new AnimalGrowthState(hours, adults);
    }

    private void ClearCooldown(object multiply)
    {
        _host.RunOnGameThread(() =>
        {
            if (GameMembers.Get(multiply, "TotalDaysCooldownUntil") is double cooldown)
            {
                GameMembers.Set(multiply, "TotalDaysCooldownUntil", 0d);
            }

            object? baseBehavior = GameMembers.FindBehavior(((EntityBehavior)multiply).entity, MultiplyBaseBehavior);
            if (baseBehavior != null) GameMembers.Set(baseBehavior, "TotalDaysCooldownUntil", 0d);
        });
    }

    private float PortionsNeeded(object multiply)
    {
        object? baseBehavior = GameMembers.FindBehavior(((EntityBehavior)multiply).entity, MultiplyBaseBehavior);
        if (baseBehavior == null) return 0f;
        return (float)(GameMembers.Get(baseBehavior, "PortionsEatenForMultiply") ?? 0f);
    }

    private static bool IsPregnant(object multiply) => (bool)(GameMembers.Get(multiply, "IsPregnant") ?? false);

    private static double PregnancyDays(object multiply) => (double)(GameMembers.Get(multiply, "PregnancyDays") ?? 3d);

    private void MoveNear(Entity male, Entity female)
    {
        _host.RunOnGameThread(() =>
        {
            Vec3d target = female.Pos.XYZ;
            male.Pos.SetPos(new Vec3d(target.X + 1, target.Y, target.Z));
        });
    }

    private HashSet<long> EntityIdsAround(Entity entity, int radius)
    {
        HashSet<long> ids = [];
        foreach (Entity found in EntitiesAround(entity, radius)) ids.Add(found.EntityId);
        return ids;
    }

    private List<Entity> EntitiesAround(Entity entity, int radius)
    {
        List<Entity> found = [];
        _host.RunOnGameThread(() =>
        {
            BlockPos center = entity.Pos.AsBlockPos;
            BlockPos from = center.AddCopy(-radius, -radius, -radius);
            BlockPos to = center.AddCopy(radius, radius, radius);
            foreach (Entity other in _host.Api.World.GetEntitiesInsideCuboid(from, to))
            {
                if (other.EntityId != entity.EntityId) found.Add(other);
            }
        });

        return found;
    }
}
