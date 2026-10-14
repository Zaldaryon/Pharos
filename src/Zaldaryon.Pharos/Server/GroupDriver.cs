using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Creates player groups and adds test players to them. Groups a test creates are removed by rollback.</summary>
public sealed class GroupDriver
{
    private readonly EmbeddedServerHost _host;
    private readonly Dictionary<int, PlayerGroup> _created = [];

    internal GroupDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Creates a group owned by one player, with other players as members.</summary>
    /// <param name="name">The group name. It must not match an existing group.</param>
    /// <param name="owner">The player who owns the group.</param>
    /// <param name="members">Players to add as members.</param>
    /// <returns>The group as the server stores it.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="members"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A group with that name already exists, or a player has not joined.</exception>
    public PlayerGroup Create(string name, ServerTestPlayer owner, params ServerTestPlayer[] members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(members);
        return _host.RunOnGameThread(() =>
        {
            IGroupManager groups = Groups;
            if (groups.GetPlayerGroupByName(name) is not null)
            {
                throw new InvalidOperationException($"A group named '{name}' already exists.");
            }

            int uid = groups.PlayerGroupsById.Keys.DefaultIfEmpty(0).Max() + 1;
            PlayerGroup group = new()
            {
                Uid = uid,
                Name = name,
                OwnerUID = owner.PlayerUID,
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Md5Identifier = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(name))),
                JoinPolicy = "",
                OnlinePlayers = [],
            };
            groups.AddPlayerGroup(group);
            _created[uid] = group;
            Join(owner, group, EnumPlayerGroupMemberShip.Owner);
            foreach (ServerTestPlayer member in members) Join(member, group, EnumPlayerGroupMemberShip.Member);
            return group;
        });
    }

    /// <summary>Reads a player's level in a group, or <see cref="EnumPlayerGroupMemberShip.None"/> when the player is not in it.</summary>
    /// <param name="player">The joined test player.</param>
    /// <param name="group">The group.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="group"/> is null.</exception>
    public EnumPlayerGroupMemberShip Membership(ServerTestPlayer player, PlayerGroup group)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(group);
        return _host.RunOnGameThread(() =>
            player.Player?.ServerData.PlayerGroupMemberships.TryGetValue(group.Uid, out PlayerGroupMembership? membership) == true
                ? membership!.Level
                : EnumPlayerGroupMemberShip.None);
    }

    /// <summary>Finds a group by name.</summary>
    /// <param name="name">The group name.</param>
    public PlayerGroup? Find(string name) => _host.RunOnGameThread(() => Groups.GetPlayerGroupByName(name));

    /// <summary>Records the groups the driver has created so far, to be removed by <see cref="Baseline.Restore"/>.</summary>
    internal Baseline Capture() => new(this);

    private IGroupManager Groups => ((ICoreServerAPI)_host.Server.Api).Groups;

    private void Join(ServerTestPlayer player, PlayerGroup group, EnumPlayerGroupMemberShip level)
    {
        if (player.Player is null) throw new InvalidOperationException($"{player.PlayerUID} has not joined the server.");
        player.Player.ServerData.PlayerGroupMemberships[group.Uid] = new PlayerGroupMembership
        {
            Level = level,
            GroupName = group.Name,
            GroupUid = group.Uid,
        };
    }

    private void RemoveCreated()
    {
        foreach (PlayerGroup group in _created.Values) Groups.RemovePlayerGroup(group);
        _created.Clear();
    }

    /// <summary>The groups the world started with: none. A restore removes the ones a test created.</summary>
    internal sealed class Baseline
    {
        private readonly GroupDriver _driver;

        internal Baseline(GroupDriver driver) => _driver = driver;

        /// <summary>Removes every group the driver created since the world started.</summary>
        public void Restore() => _driver._host.RunOnGameThread(_driver.RemoveCreated);
    }
}
