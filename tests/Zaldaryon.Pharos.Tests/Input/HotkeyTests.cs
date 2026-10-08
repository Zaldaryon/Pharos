using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Input;

/// <summary>Hotkey bindings as values, before anything runs.</summary>
public class HotkeyBindingTests
{
    [Fact]
    public void Bindings_CompareByTheirKeysAndModifiers()
    {
        Assert.Equal(HotkeyBinding.Of(GlKeys.K, ctrl: true, shift: true), new HotkeyBinding((int)GlKeys.K, null, Ctrl: true, Alt: false, Shift: true));
        Assert.NotEqual(HotkeyBinding.Of(GlKeys.K, ctrl: true), HotkeyBinding.Of(GlKeys.K, ctrl: true, shift: true));
    }

    [Fact]
    public void MouseButtonsAndDoubleTaps_AreRecognised()
    {
        HotkeyBinding middle = HotkeyBinding.Of(EnumMouseButton.Middle, ctrl: true);
        HotkeyBinding fly = HotkeyBinding.DoubleTap(GlKeys.Space);

        Assert.True(middle.IsMouseButton);
        Assert.Equal(EnumMouseButton.Middle, middle.MouseButton);
        Assert.False(fly.IsMouseButton);
        Assert.Equal((int)GlKeys.Space, fly.SecondKeyCode);
    }
}

/// <summary>A mod's hotkeys on a real engine-mode client joined to a server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosclientmod")]
public partial class LiveHotkeyTests : ClientServerScenarioBase
{
    private const string Ping = "pharosclientmod:ping";
    private const string Pong = "pharosclientmod:pong";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public void AModsHotkey_IsListedWithItsBinding()
    {
        HotkeyInfo ping = Client!.Hotkeys.Require(Ping);

        Assert.Equal("Pharos ping", ping.Name);
        Assert.Equal(HotkeyType.GUIOrOtherControls, ping.Type);
        Assert.Equal(HotkeyBinding.Of(GlKeys.O, ctrl: true, shift: true), ping.Current);
        Assert.Equal(ping.Default, ping.Current);
        Assert.True(ping.HasHandler);
        Assert.Null(Client.Hotkeys.Get("pharosclientmod:nope"));
        Assert.Throws<KeyNotFoundException>(() => Client.Hotkeys.Require("pharosclientmod:nope"));
        Assert.Contains(Client.Hotkeys.All(), h => h.Code == "inventorydialog");
        Assert.DoesNotContain(Client.Hotkeys.Conflicts(), c => c.Codes.Any(code => code.StartsWith("pharosclientmod:", StringComparison.Ordinal)));
    }

    [ClientServerScenario]
    public async Task Trigger_RunsTheHotkeysHandler()
    {
        (int pings, _) = await PressesAsync();

        HotkeyTriggerResult result = await Client!.Hotkeys.TriggerAsync(Ping);
        Assert.True(result.Fired, result.ToString());
        Assert.True(result.Consumed);
        Assert.Equal(pings + 1, (await PressesAsync()).Pings);
    }

    [ClientServerScenario]
    public async Task Press_GoesThroughLiveInput()
    {
        (int pings, _) = await PressesAsync();

        HotkeyTriggerResult result = await Client!.Hotkeys.PressAsync(Ping);

        Assert.True(result.Fired, result.ToString());
        Assert.Equal(pings + 1, (await PressesAsync()).Pings);
    }

    [ClientServerScenario]
    public async Task Rebind_MovesTheHotkey_UntilDisposed()
    {
        HotkeyBinding j = HotkeyBinding.Of(GlKeys.J, ctrl: true, shift: true);
        (int pings, _) = await PressesAsync();

        using (Client!.Hotkeys.Rebind(Ping, j))
        {
            Assert.Equal(j, Client.Hotkeys.Require(Ping).Current);
            Assert.True((await Client.Hotkeys.PressAsync(Ping)).Fired);
        }

        Assert.Equal(HotkeyBinding.Of(GlKeys.O, ctrl: true, shift: true), Client.Hotkeys.Require(Ping).Current);
        Assert.Equal(pings + 1, (await PressesAsync()).Pings);
    }

    [ClientServerScenario]
    public async Task OnAClash_TheFirstHotkeyWins_UnlessItLetsTheKeyPass()
    {
        using (Client!.Hotkeys.Rebind(Pong, Client.Hotkeys.Require(Ping).Current))
        {
            Assert.Equal([Ping, Pong], Client.Hotkeys.ConflictWith(Ping)!.Codes);

            HotkeyTriggerResult taken = await Client.Hotkeys.TriggerAsync(Pong);
            Assert.False(taken.Fired);
            Assert.Equal(Ping, taken.FiredCode);

            await Client.Commands.ExecuteSuccessAsync(".pharoscmd pingpasses true");
            try
            {
                HotkeyTriggerResult passed = await Client.Hotkeys.TriggerAsync(Pong);
                Assert.True(passed.Fired, passed.ToString());
            }
            finally
            {
                await Client.Commands.ExecuteSuccessAsync(".pharoscmd pingpasses false");
            }
        }

        Assert.Null(Client.Hotkeys.ConflictWith(Ping));
    }

    [ClientServerScenario]
    public async Task WhileChatHasTheKeyboard_AHotkeyDoesNotFire()
    {
        (int pings, _) = await PressesAsync();

        await Client!.Hotkeys.TriggerAsync("beginchat");
        try
        {
            HotkeyTriggerResult result = await Client.Hotkeys.TriggerAsync(Ping);

            Assert.False(result.Fired);
            Assert.Null(result.FiredCode);
            Assert.True(result.Consumed);
        }
        finally
        {
            Client.Input.PressKey(GlKeys.Escape);
            await Session!.StepFramesAsync(2);
        }

        Assert.True((await Client.Hotkeys.TriggerAsync(Ping)).Fired);
        Assert.Equal(pings + 1, (await PressesAsync()).Pings);
    }

    [ClientServerScenario]
    public async Task DoubleTapsAndModifiedMouseButtons_CanBePressed()
    {
        using (Client!.Hotkeys.Rebind(Ping, HotkeyBinding.DoubleTap(GlKeys.O)))
        {
            (int pings, _) = await PressesAsync();

            // Back to back: each press fires once, not the second one twice.
            Assert.True((await Client.Hotkeys.PressAsync(Ping)).Fired);
            Assert.True((await Client.Hotkeys.PressAsync(Ping)).Fired);
            Assert.Equal(pings + 2, (await PressesAsync()).Pings);

            Assert.True((await Client.Hotkeys.TriggerAsync(Ping)).Fired);
        }

        // A binding whose key is itself a modifier, as the controls menu can capture.
        using (Client.Hotkeys.Rebind(Ping, new HotkeyBinding((int)GlKeys.RShift, Ctrl: true)))
        {
            Assert.True((await Client.Hotkeys.PressAsync(Ping)).Fired);
            Assert.True((await Client.Hotkeys.TriggerAsync(Ping)).Fired);
        }

        using (Client.Hotkeys.Rebind(Ping, HotkeyBinding.Of(EnumMouseButton.Button4, ctrl: true)))
        {
            Assert.True((await Client.Hotkeys.PressAsync(Ping)).Fired);
            Assert.True((await Client.Hotkeys.TriggerAsync(Ping)).Fired);
        }
    }

    private async Task<(int Pings, int Pongs)> PressesAsync()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteSuccessAsync(".pharoscmd presses");
        Match match = PressesPattern().Match(result.Message ?? "");
        Assert.True(match.Success, result.ToString());
        return (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"ping=(\d+) pong=(\d+)")]
    private static partial Regex PressesPattern();
}
