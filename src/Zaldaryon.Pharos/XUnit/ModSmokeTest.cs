using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A ready-made smoke test for a mod: boots a server and an engine-mode client with the mods,
/// joins, runs the commands, walks and looks around, and fails on a crash, a disconnect, a
/// timeout, a failed command or an error logged on either side. See <c>docs/smoke-test.md</c>.
/// </summary>
/// <remarks>
/// <para>Inherit it with no body, and name the mods as for any scenario:</para>
/// <code>
/// [ServerMods("../../../../MyMod/bin/Release/mymod.zip")]
/// [StrictBoot]
/// public class Smoke : ModSmokeTest;
/// </code>
/// <para>
/// <see cref="StrictBootAttribute"/> also fails the boot on warnings. The test fails before booting
/// when no mod is named: a smoke test that boots nothing but vanilla would pass for every mod.
/// </para>
/// </remarks>
public abstract class ModSmokeTest : ClientServerScenarioBase
{
    /// <summary>How long the smoke test may play: five minutes, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</summary>
    public const int SmokeTimeoutMs = 300_000;

    /// <summary>How many frames the client plays, the server ticking once per frame. 600 by default, ten seconds of game time.</summary>
    protected virtual int SmokeFrames => 600;

    /// <summary>Server console commands to run once the player has joined. Each must succeed.</summary>
    protected virtual IReadOnlyList<string> SmokeCommands => [];

    /// <inheritdoc />
    protected override bool FailOnLoggedErrors => true;

    /// <inheritdoc />
    protected override WorldIsolation WorldIsolation => WorldIsolation.Restart;

    /// <inheritdoc />
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

    /// <summary>
    /// The smoke test: commands, then a walk, a turn, a jump and a look up and down, checking
    /// after each step that the client is still in the world and the server still running.
    /// </summary>
    [ClientServerScenario(TimeoutMs = SmokeTimeoutMs)]
    public async Task ModBootsJoinsAndPlays()
    {
        AssertAlive("joining");

        foreach (string command in SmokeCommands)
        {
            await RunCommandAsync(command).ConfigureAwait(false);
            AssertAlive($"running {command}");
        }

        int frames = Math.Max(SmokeFrames, 8);
        int walk = frames / 3;
        int look = frames / 6;

        await Session!.HoldAsync(PlayerAction.Forward, walk).ConfigureAwait(false);
        AssertAlive("walking forward");

        await TurnAsync(yawPerFrame: (float)(Math.PI / look), pitchPerFrame: 0, look).ConfigureAwait(false);
        AssertAlive("turning around");

        await Session.HoldAsync(PlayerAction.Jump, 10).ConfigureAwait(false);
        AssertAlive("jumping");

        await TurnAsync(yawPerFrame: 0, pitchPerFrame: 0.6f / (look / 2f), look / 2).ConfigureAwait(false);
        await TurnAsync(yawPerFrame: 0, pitchPerFrame: -0.6f / (look / 2f), look / 2).ConfigureAwait(false);
        AssertAlive("looking up and down");

        int rest = frames - walk - look - 10 - (look / 2) * 2;
        if (rest > 0)
        {
            await Session.StepFramesAsync(rest).ConfigureAwait(false);
        }

        AssertAlive("playing");
    }

    /// <summary>Refuses a smoke test without mods before booting anything.</summary>
    public override Task InitializeAsync()
    {
        if (ServerModPaths.Count == 0 && ClientModPaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} names no mod: add [ServerMods(...)] or [PharosMods(...)], or it only smoke-tests vanilla.");
        }

        return base.InitializeAsync();
    }

    private async Task TurnAsync(float yawPerFrame, float pitchPerFrame, int frames)
    {
        IClientTestPlayer player = Player ?? throw new InvalidOperationException("The client has no player.");
        for (int i = 0; i < frames; i++)
        {
            Client!.RunOnClientThread(() =>
            {
                player.Camera.Yaw += yawPerFrame;
                player.Camera.Pitch = Math.Clamp(player.Camera.Pitch + pitchPerFrame, -1.4, 1.4);
            });
            await Session!.StepFramesAsync(1).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a server console command and steps the game until it has finished: a deferred command
    /// only completes as the server ticks.
    /// </summary>
    private async Task RunCommandAsync(string command)
    {
        ICoreServerAPI api = (ICoreServerAPI)Server!.Api;
        string normalized = command.TrimStart('/');
        TaskCompletionSource<TextCommandResult> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool deferred = false;
        // ExecuteUnparsed takes the text as typed: it drops the leading slash itself.
        ServerHost!.RunOnGameThread(() => api.ChatCommands.ExecuteUnparsed(
            "/" + normalized,
            new TextCommandCallingArgs { Caller = ServerScenarioBase.ConsoleCaller() },
            result =>
            {
                // A deferred result is followed by the real one once the command has finished.
                if (result.Status == EnumCommandStatus.Deferred) deferred = true;
                else done.TrySetResult(result);
            }));

        bool finished = await Session!.StepUntilAsync(() => done.Task.IsCompleted, maxFrames: 600).ConfigureAwait(false);
        if (!finished)
        {
            // A command that deferred and never reported back is taken as accepted, as
            // CommandResult.Ok takes it.
            if (deferred) return;
            throw new TimeoutException($"The command '{command}' did not finish within 600 frames.");
        }

        TextCommandResult outcome = await done.Task.ConfigureAwait(false);
        if (outcome.Status != EnumCommandStatus.Success)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"The command '{command}' failed with status {outcome.Status}: {outcome.StatusMessage ?? "(no message)"}"));
        }
    }

    /// <summary>Fails when the client has left the world or the server has stopped.</summary>
    private void AssertAlive(string doing)
    {
        EmbeddedServerHost server = ServerHost ?? throw new InvalidOperationException("The server is gone.");
        List<string> problems = [];

        if (!server.IsRunning) problems.Add("the server stopped");
        if (Session!.IsLinkSevered) problems.Add("the connection to the server was cut");
        if (Client!.DisconnectSimulator.IsDisconnected) problems.Add("the client was disconnected");
        if (!Client.IsJoined) problems.Add("the client is no longer in the world");
        if (server.IsRunning && server.RunOnGameThread(() => server.Server.GetClientByPlayername(PlayerName) == null))
        {
            problems.Add("the server no longer knows the player");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"After {doing}: {string.Join(", ", problems)}.");
        }
    }
}
