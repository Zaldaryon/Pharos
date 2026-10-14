using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Creates, reads and removes land claims on the server. Claims a test adds are removed when its world is rolled back.</summary>
public sealed class LandClaimDriver
{
    private readonly EmbeddedServerHost _host;

    internal LandClaimDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Every land claim on the server, read on the game thread.</summary>
    public IReadOnlyList<LandClaim> All => _host.RunOnGameThread(() => ClaimApi.All.ToArray());

    /// <summary>Claims a box for a player, as if the player had marked it in game.</summary>
    /// <param name="owner">The player who owns the claim.</param>
    /// <param name="area">The area to claim, in block coordinates.</param>
    /// <returns>The claim as the server stores it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="area"/> is null.</exception>
    public LandClaim Add(ServerTestPlayer owner, Cuboidi area)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(area);
        return _host.RunOnGameThread(() =>
        {
            LandClaim claim = new()
            {
                Areas = [area],
                OwnedByPlayerUid = owner.PlayerUID,
                LastKnownOwnerName = owner.PlayerName ?? owner.PlayerUID,
            };
            ClaimApi.Add(claim);
            return claim;
        });
    }

    /// <summary>Returns the claims that cover a block position.</summary>
    /// <param name="pos">The block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    public LandClaim[] At(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() => ClaimApi.Get(pos));
    }

    /// <summary>Removes a claim from the server.</summary>
    /// <param name="claim">The claim to remove.</param>
    /// <returns>True when the server had the claim and removed it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="claim"/> is null.</exception>
    public bool Remove(LandClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return _host.RunOnGameThread(() => ClaimApi.Remove(claim));
    }

    /// <summary>Copies the claims the server has now, to be put back by <see cref="LandClaimBaseline.Restore"/>.</summary>
    internal LandClaimBaseline Capture() =>
        new(_host, _host.RunOnGameThread(() => ClaimApi.All.Select(claim => claim.Clone()).ToArray()));

    private ILandClaimAPI ClaimApi => ((ICoreServerAPI)_host.Server.Api).World.Claims;

    /// <summary>The claims the world started with.</summary>
    internal sealed class LandClaimBaseline
    {
        private readonly EmbeddedServerHost _host;
        private readonly LandClaim[] _claims;

        internal LandClaimBaseline(EmbeddedServerHost host, LandClaim[] claims)
        {
            _host = host;
            _claims = claims;
        }

        /// <summary>Removes every claim the server has and adds back the baseline claims.</summary>
        /// <returns>The number of baseline claims put back.</returns>
        public int Restore() => _host.RunOnGameThread(() =>
        {
            ILandClaimAPI claims = ((ICoreServerAPI)_host.Server.Api).World.Claims;
            foreach (LandClaim claim in claims.All.ToArray()) claims.Remove(claim);
            foreach (LandClaim claim in _claims) claims.Add(claim.Clone());
            return _claims.Length;
        });
    }
}
