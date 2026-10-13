using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// The client's copy of a player's survival state, as the server last sent it. Read it to assert
/// that a client shows what the server decided; see <see cref="VitalsReading"/> and
/// <c>ClientServerAssert.VitalsSynced</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client's player entity keeps health, oxygen and body temperature but no hunger: the
/// survival player holds satiety and the nutrient levels on the server only. A reading here
/// therefore has nulls where the server's has values, and a comparison looks only at what both
/// sides carry.
/// </para>
/// <para>
/// Every read runs on the client thread, so a test can call it from any thread.
/// </para>
/// </remarks>
public sealed class ClientVitals
{
    private readonly System.Func<EntityPlayer?> _entity;
    private readonly System.Func<EnumAppSide> _side;

    internal ClientVitals(System.Func<ClientMain> client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _side = () => client().EntityPlayer?.World?.Side ?? EnumAppSide.Client;
        _entity = () => client().EntityPlayer;
    }

    /// <summary>Whether the client has a player entity to read.</summary>
    public bool IsAvailable => _entity() is not null;

    /// <summary>
    /// Takes a reading of every value at once. Costs one walk of the entity's behaviours, so take
    /// one reading and keep it rather than reading again per assertion.
    /// </summary>
    public VitalsReading Read()
    {
        EntityPlayer? entity = _entity();
        return entity is null ? VitalsReading.Empty : VitalsBehaviors.Read(entity);
    }

    /// <summary>Health points, from 0 up.</summary>
    public float? Health => Read().Health;

    /// <summary>The most health points the player can have right now.</summary>
    public float? MaxHealth => Read().MaxHealth;

    /// <summary>Satiety, from 0 up. Null: the client does not keep it.</summary>
    public float? Saturation => Read().Saturation;

    /// <summary>The five nutrient levels. Null: the client does not keep them.</summary>
    public Nutrition? Nutrition => Read().Nutrition;

    /// <summary>Oxygen in milliseconds of breathing left.</summary>
    public float? Oxygen => Read().Oxygen;

    /// <summary>The most oxygen the player can hold, in milliseconds.</summary>
    public float? MaxOxygen => Read().MaxOxygen;

    /// <summary>Whether the player has air to breathe.</summary>
    public bool? HasAir => Read().HasAir;

    /// <summary>Body temperature, from 31 up.</summary>
    public float? BodyTemperature => Read().BodyTemperature;

    /// <summary>How wet the player is, from 0 up.</summary>
    public float? Wetness => Read().Wetness;

    /// <summary>Whether the player is alive.</summary>
    public bool IsAlive => Read().IsAlive;

    /// <summary>
    /// Which side the reading came from, so a test can tell a server reading from a client one.
    /// </summary>
    public EnumAppSide Side => _side();
}