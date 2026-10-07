using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Server;

/// <summary>A chat line the server sent a headless player.</summary>
/// <param name="Message">The line as sent, with any formatting markup.</param>
/// <param name="GroupId">The chat group (channel) it was sent on.</param>
/// <param name="ChatType">The engine's chat type: notification, own message, others' message, command result, and so on.</param>
public sealed record ReceivedChatLine(string Message, int GroupId, int ChatType);

/// <summary>An entity the server started tracking for a headless player.</summary>
/// <param name="EntityId">The entity id, unique within the world.</param>
/// <param name="EntityType">The entity code, such as <c>game:player</c> or <c>game:chicken-hen</c>.</param>
public sealed record ReceivedEntity(long EntityId, string EntityType);

/// <summary>An entity the server stopped tracking for a headless player.</summary>
/// <param name="EntityId">The entity id.</param>
/// <param name="Reason">The engine's despawn reason: death, out of range, removed, and so on.</param>
public sealed record ReceivedEntityDeparture(long EntityId, int Reason);

/// <summary>A particle spawn the server sent a headless player.</summary>
/// <param name="ProviderClass">The particle property provider class the client would instantiate.</param>
/// <param name="Data">The provider's serialized parameters.</param>
public sealed record ReceivedParticles(string ProviderClass, byte[] Data);

/// <summary>A sound the server told a headless player to play.</summary>
/// <param name="Name">The sound asset location.</param>
/// <param name="Position">Where it plays, in block coordinates.</param>
/// <param name="Range">The audible range in blocks.</param>
/// <param name="Volume">The volume, 0 to 1.</param>
public sealed record ReceivedSound(string Name, Vec3d Position, float Range, float Volume);

/// <summary>A packet a mod sent a headless player on one of its network channels.</summary>
/// <param name="Channel">The channel name, or the channel id as text when the name was never announced.</param>
/// <param name="MessageId">The index of the message type within the channel, in registration order.</param>
/// <param name="Data">The message, serialized with protobuf.</param>
public sealed record ReceivedModPacket(string Channel, int MessageId, byte[] Data);

/// <summary>Player data the server sent a headless player, about itself or another player.</summary>
/// <param name="PlayerUid">The player's uid.</param>
/// <param name="PlayerName">The player's name.</param>
/// <param name="EntityId">The player's entity id.</param>
/// <param name="GameMode">The engine's game mode: survival, creative, spectator, guest.</param>
public sealed record ReceivedPlayerData(string PlayerUid, string? PlayerName, long EntityId, int GameMode);

/// <summary>A block highlight the server sent a headless player.</summary>
/// <param name="SlotId">The highlight slot. Each slot holds one set of highlighted blocks.</param>
/// <param name="Blocks">The highlighted positions. Empty when the slot was cleared.</param>
/// <param name="Colors">The colors, one per block or one for all.</param>
public sealed record ReceivedHighlight(int SlotId, IReadOnlyList<BlockPos> Blocks, IReadOnlyList<int> Colors);

/// <summary>An in-game error or discovery message the server showed a headless player.</summary>
/// <param name="Code">The message code.</param>
/// <param name="Message">The message text.</param>
public sealed record ReceivedIngameMessage(string Code, string? Message);

/// <summary>A block change the server sent a headless player, alone or in a batch.</summary>
/// <param name="Position">The changed position.</param>
/// <param name="BlockId">The new block id.</param>
public sealed record ReceivedBlockChange(BlockPos Position, int BlockId);

/// <summary>
/// Everything the server has sent one headless player so far, decoded: what a real client would
/// have received and acted on.
/// </summary>
/// <remarks>
/// The server sends to the player over its in-memory socket, uncompressed. Every packet is
/// counted by id; the kinds a test usually asserts on are decoded into the lists below. Reads are
/// safe from any thread.
/// </remarks>
public sealed class ReceivedPackets
{
    private const int ChatLineId = 8, SetBlockId = 7, SoundId = 18, EntitySpawnId = 34, EntityDespawnId = 36, EntitiesId = 40,
        PlayerDataId = 41, HighlightBlocksId = 52, CustomPacketId = 55, NetworkChannelsId = 56, SpawnParticlesId = 61,
        IngameErrorId = 68, IngameDiscoveryId = 69, SetBlocksId = 47, SetBlocksNoRelightId = 63;

    private readonly object _lock = new();
    private readonly Dictionary<int, int> _counts = [];
    private readonly Dictionary<int, string> _channelNames = [];
    private readonly HashSet<long> _knownEntities = [];
    private readonly List<ReceivedChatLine> _chat = [];
    private readonly List<ReceivedEntity> _entityArrivals = [];
    private readonly List<ReceivedEntityDeparture> _entityDepartures = [];
    private readonly List<ReceivedParticles> _particles = [];
    private readonly List<ReceivedSound> _sounds = [];
    private readonly List<ReceivedModPacket> _modPackets = [];
    private readonly List<ReceivedPlayerData> _playerData = [];
    private readonly List<ReceivedHighlight> _highlights = [];
    private readonly List<ReceivedIngameMessage> _errors = [];
    private readonly List<ReceivedIngameMessage> _discoveries = [];
    private readonly List<ReceivedBlockChange> _blockChanges = [];
    private long _total;
    private long _undecodable;

    /// <summary>How many packets arrived in total.</summary>
    public long Total { get { lock (_lock) return _total; } }

    /// <summary>How many packets could not be parsed as a server packet.</summary>
    public long Undecodable { get { lock (_lock) return _undecodable; } }

    /// <summary>How many packets arrived per packet id.</summary>
    public IReadOnlyDictionary<int, int> CountsById { get { lock (_lock) return new Dictionary<int, int>(_counts); } }

    /// <summary>Chat lines, in arrival order.</summary>
    public IReadOnlyList<ReceivedChatLine> Chat => Snapshot(_chat);

    /// <summary>The chat lines' text, in arrival order.</summary>
    public IReadOnlyList<string> ChatMessages => Snapshot(_chat).Select(c => c.Message).ToList();

    /// <summary>Entities the server started tracking for the player, in arrival order.</summary>
    public IReadOnlyList<ReceivedEntity> EntityArrivals => Snapshot(_entityArrivals);

    /// <summary>Entities the server stopped tracking for the player, in arrival order.</summary>
    public IReadOnlyList<ReceivedEntityDeparture> EntityDepartures => Snapshot(_entityDepartures);

    /// <summary>Particle spawns, in arrival order.</summary>
    public IReadOnlyList<ReceivedParticles> Particles => Snapshot(_particles);

    /// <summary>Sounds the server told the player to play, in arrival order.</summary>
    public IReadOnlyList<ReceivedSound> Sounds => Snapshot(_sounds);

    /// <summary>Mod channel packets, in arrival order.</summary>
    public IReadOnlyList<ReceivedModPacket> ModPackets => Snapshot(_modPackets);

    /// <summary>Player data packets, in arrival order.</summary>
    public IReadOnlyList<ReceivedPlayerData> PlayerData => Snapshot(_playerData);

    /// <summary>Block highlights, in arrival order.</summary>
    public IReadOnlyList<ReceivedHighlight> Highlights => Snapshot(_highlights);

    /// <summary>In-game error messages, in arrival order.</summary>
    public IReadOnlyList<ReceivedIngameMessage> IngameErrors => Snapshot(_errors);

    /// <summary>In-game discovery messages, in arrival order.</summary>
    public IReadOnlyList<ReceivedIngameMessage> IngameDiscoveries => Snapshot(_discoveries);

    /// <summary>Block changes, single or batched, in arrival order.</summary>
    public IReadOnlyList<ReceivedBlockChange> BlockChanges => Snapshot(_blockChanges);

    /// <summary>Whether the server ever started tracking the entity for the player.</summary>
    public bool HasReceivedEntity(long entityId)
    {
        lock (_lock) return _entityArrivals.Any(e => e.EntityId == entityId);
    }

    /// <summary>Whether the server tracks the entity for the player right now.</summary>
    public bool KnowsEntity(long entityId)
    {
        lock (_lock) return _knownEntities.Contains(entityId);
    }

    /// <summary>Whether the server sent player data for the player with this uid.</summary>
    public bool HasReceivedPlayerData(string playerUid)
    {
        lock (_lock) return _playerData.Any(p => p.PlayerUid == playerUid);
    }

    /// <summary>
    /// The highlight currently shown in <paramref name="slotId"/>: the last one sent for it, or an
    /// empty list when it was never set or was cleared.
    /// </summary>
    public IReadOnlyList<BlockPos> HighlightedBlocks(int slotId)
    {
        lock (_lock) return _highlights.LastOrDefault(h => h.SlotId == slotId)?.Blocks ?? [];
    }

    /// <summary>
    /// The packets of message type <typeparamref name="T"/> a mod sent on <paramref name="channel"/>,
    /// deserialized.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="messageId">
    /// The index of <typeparamref name="T"/> among the channel's message types, in registration
    /// order. <see cref="EmbeddedServerHost.ModPackets{T}"/> looks it up for you.
    /// </param>
    public IReadOnlyList<T> ModPacketsOf<T>(string channel, int messageId) =>
        Snapshot(_modPackets)
            .Where(p => p.Channel == channel && p.MessageId == messageId)
            .Select(p => SerializerUtil.Deserialize<T>(p.Data))
            .ToList();

    /// <summary>Forgets everything received so far. Entities still tracked stay known.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _counts.Clear();
            _total = 0;
            _undecodable = 0;
            _chat.Clear();
            _entityArrivals.Clear();
            _entityDepartures.Clear();
            _particles.Clear();
            _sounds.Clear();
            _modPackets.Clear();
            _playerData.Clear();
            _highlights.Clear();
            _errors.Clear();
            _discoveries.Clear();
            _blockChanges.Clear();
        }
    }

    internal void Add(byte[] data)
    {
        Packet_Server packet;
        try
        {
            packet = Packet_ServerSerializer.DeserializeBuffer(data, data.Length, new Packet_Server());
        }
        catch
        {
            lock (_lock)
            {
                _total++;
                _undecodable++;
            }

            return;
        }

        lock (_lock)
        {
            _total++;
            _counts[packet.Id] = _counts.GetValueOrDefault(packet.Id) + 1;

            try
            {
                Decode(packet);
            }
            catch
            {
                // A packet kind decoded here only for convenience must never break the player.
            }
        }
    }

    private void Decode(Packet_Server packet)
    {
        switch (packet.Id)
        {
            case ChatLineId when packet.Chatline is { } chat:
                _chat.Add(new ReceivedChatLine(chat.Message ?? "", chat.Groupid, chat.ChatType));
                break;

            case EntitySpawnId when packet.EntitySpawn is { } spawn:
                AddEntities(spawn.Entity, spawn.EntityCount);
                break;

            case EntitiesId when packet.Entities is { } entities:
                AddEntities(entities.Entities, entities.EntitiesCount);
                break;

            case EntityDespawnId when packet.EntityDespawn is { } despawn:
                for (int i = 0; i < despawn.EntityIdCount; i++)
                {
                    long id = despawn.EntityId[i];
                    int reason = i < despawn.DespawnReasonCount ? despawn.DespawnReason[i] : 0;
                    _knownEntities.Remove(id);
                    _entityDepartures.Add(new ReceivedEntityDeparture(id, reason));
                }

                break;

            case SpawnParticlesId when packet.SpawnParticles is { } particles:
                _particles.Add(new ReceivedParticles(particles.ParticlePropertyProviderClassName ?? "", particles.Data ?? []));
                break;

            case SoundId when packet.Sound is { } sound:
                _sounds.Add(new ReceivedSound(
                    sound.Name ?? "",
                    new Vec3d(CollectibleNet.DeserializeFloat(sound.X), CollectibleNet.DeserializeFloat(sound.Y), CollectibleNet.DeserializeFloat(sound.Z)),
                    CollectibleNet.DeserializeFloat(sound.Range),
                    CollectibleNet.DeserializeFloat(sound.Volume)));
                break;

            case NetworkChannelsId when packet.NetworkChannels is { } channels:
                for (int i = 0; i < channels.ChannelIdsCount && i < channels.ChannelNamesCount; i++)
                {
                    _channelNames[channels.ChannelIds[i]] = channels.ChannelNames[i];
                }

                break;

            case CustomPacketId when packet.CustomPacket is { } custom:
                _modPackets.Add(new ReceivedModPacket(
                    _channelNames.TryGetValue(custom.ChannelId, out string? name) ? name : custom.ChannelId.ToString(),
                    custom.MessageId,
                    custom.Data ?? []));
                break;

            case PlayerDataId when packet.PlayerData is { } player:
                _playerData.Add(new ReceivedPlayerData(player.PlayerUID ?? "", player.PlayerName, player.EntityId, player.GameMode));
                break;

            case HighlightBlocksId when packet.HighlightBlocks is { } highlight:
                BlockPos[] blocks = highlight.Blocks is { Length: > 0 } packed ? BlockTypeNet.UnpackBlockPositions(packed) : [];
                int[] colors = new int[highlight.ColorsCount];
                if (colors.Length > 0) Array.Copy(highlight.Colors, colors, colors.Length);
                _highlights.Add(new ReceivedHighlight(highlight.Slotid, blocks, colors));
                break;

            case IngameErrorId when packet.IngameError is { } error:
                _errors.Add(new ReceivedIngameMessage(error.Code ?? "", error.Message));
                break;

            case IngameDiscoveryId when packet.IngameDiscovery is { } discovery:
                _discoveries.Add(new ReceivedIngameMessage(discovery.Code ?? "", discovery.Message));
                break;

            case SetBlockId when packet.SetBlock is { } set:
                _blockChanges.Add(new ReceivedBlockChange(new BlockPos(set.X, set.Y, set.Z), set.BlockType));
                break;

            case SetBlocksId or SetBlocksNoRelightId:
                Packet_ServerSetBlocks? batch = packet.Id == SetBlocksId ? packet.SetBlocks : packet.SetBlocksNoRelight;
                if (batch?.SetBlocks is { Length: > 0 } packedBlocks)
                {
                    KeyValuePair<BlockPos[], int[]> changes = BlockTypeNet.UnpackSetBlocks(packedBlocks, out _);
                    for (int i = 0; i < changes.Key.Length; i++)
                    {
                        _blockChanges.Add(new ReceivedBlockChange(changes.Key[i], changes.Value[i]));
                    }
                }

                break;
        }
    }

    private void AddEntities(Packet_Entity[]? entities, int count)
    {
        if (entities == null) return;

        for (int i = 0; i < count && i < entities.Length; i++)
        {
            Packet_Entity entity = entities[i];
            if (_knownEntities.Add(entity.EntityId))
            {
                _entityArrivals.Add(new ReceivedEntity(entity.EntityId, entity.EntityType ?? ""));
            }
        }
    }

    private List<T> Snapshot<T>(List<T> list)
    {
        lock (_lock) return [.. list];
    }
}
