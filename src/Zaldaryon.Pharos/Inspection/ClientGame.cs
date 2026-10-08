using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

internal static class ClientGame
{
    /// <summary>The game of <paramref name="client"/>, which must be an engine-mode client.</summary>
    /// <exception cref="InvalidOperationException">The client was booted in fixture mode.</exception>
    public static ClientMain Require(HeadlessClient client) =>
        client.IsEngineMode
            ? client.Client
            : throw new InvalidOperationException(
                "Client internals are inspected on an engine-mode client: a fixture-mode client runs no render loop, tick listeners, particles or highlights.");
}
