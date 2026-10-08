using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Commands;

/// <summary>How a client command's text is split, before anything runs.</summary>
public class ClientCommandParseTests
{
    [Theory]
    [InlineData(".pharoscmd status", "pharoscmd", "status")]
    [InlineData("pharoscmd status", "pharoscmd", "status")]
    [InlineData("  .clientconfig viewDistance 64 ", "clientconfig", "viewDistance 64")]
    [InlineData(".debug", "debug", "")]
    public void Parse_SplitsTheNameFromTheArguments(string command, string name, string args)
    {
        Assert.Equal((name, args), ClientCommandDriver.Parse(command));
    }

    [Theory]
    [InlineData("/time set day", "server command")]
    [InlineData(".", "no name")]
    [InlineData("   ", "")]
    public void Parse_RefusesServerCommandsAndEmptyText(string command, string message)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ClientCommandDriver.Parse(command));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Result_DescribesItself()
    {
        ClientCommandResult result = new(".x", EnumCommandStatus.Error, "broken", "oops", null, []);

        Assert.False(result.Succeeded);
        Assert.False(result.Ok);
        Assert.True(result with { Status = EnumCommandStatus.Deferred } is { Ok: true, Succeeded: false });
        Assert.Equal(".x: Error (oops): broken", result.ToString());
    }
}

/// <summary>Client commands run on a real engine-mode client joined to a server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosclientmod")]
public class LiveClientCommandTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task AModCommand_ReturnsItsStatusAndMessage_AndShowsItInChat()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteAsync(".pharoscmd status");

        Assert.Equal(EnumCommandStatus.Success, result.Status);
        Assert.Equal("ready", result.Message);
        Assert.Contains(result.ChatLines, line => line.Message == "ready");
        Assert.True(Client.Commands.Exists("pharoscmd"));
        Assert.Contains("pharoscmd", Client.Commands.Names());
    }

    [ClientServerScenario]
    public async Task AFailingCommand_ReportsItsError_AndExecuteSuccessThrows()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteAsync("pharoscmd fail");

        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("pharosfail", result.ErrorCode);

        ClientCommandException error = await Assert.ThrowsAsync<ClientCommandException>(() => Client.Commands.ExecuteSuccessAsync(".pharoscmd fail"));
        Assert.Contains("pharoscmd failed on purpose", error.Message);
    }

    [ClientServerScenario]
    public async Task WhatACommandShowsInChat_IsRecorded()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteSuccessAsync(".pharoscmd say hello from pharos");

        Assert.Contains(result.ChatLines, line => line.Message == "hello from pharos");
    }

    [ClientServerScenario]
    public async Task ACommandWhoseArgumentsAreLookedUpLater_IsWaitedFor()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteSuccessAsync(".pharoscmd later pharos");

        Assert.Equal(EnumCommandStatus.Success, result.Status);
        Assert.Equal("done later: pharos", result.Message);
    }

    [ClientServerScenario]
    public async Task WithNoFramesToWait_TheFirstResultIsReturned()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteAsync(".pharoscmd later pharos", maxFrames: 0);

        Assert.Equal(EnumCommandStatus.Deferred, result.Status);
        Assert.True(result.Ok);

        // The real result still comes, a few frames later: let it, so it does not reach the next test.
        await Session!.StepFramesAsync(10);
    }

    [ClientServerScenario]
    public async Task AVanillaCommand_ChangesWhatItSays()
    {
        object? before = Client!.Settings.Get("viewDistance");
        try
        {
            ClientCommandResult result = await Client.Commands.ExecuteSuccessAsync(".clientconfig viewDistance 64");

            Assert.Equal(64, Convert.ToInt32(Client.Settings.Get("viewDistance"), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains(result.ChatLines, line => line.Message.Contains("viewDistance", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.ChatLines, line => line.Message.Length == 0);
            Assert.Equal(1, Client.Commands.Names().Count(n => n == "clientconfig"));
        }
        finally
        {
            if (before != null) Client.Settings.Set("viewDistance", before);
        }
    }

    [ClientServerScenario]
    public async Task ACommandThatThrows_FailsTheTestWithItsException()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client!.Commands.ExecuteAsync(".pharoscmd boom"));

        Assert.Equal("pharoscmd boom", error.Message);
    }

    [ClientServerScenario]
    public async Task AnUnknownCommand_IsNoSuchCommand()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteAsync(".pharosnosuchcommand");

        Assert.Equal(EnumCommandStatus.NoSuchCommand, result.Status);
        Assert.Contains(result.ChatLines, line => line.Message.Contains("No such command", StringComparison.OrdinalIgnoreCase));
        Assert.False(Client.Commands.Exists("pharosnosuchcommand"));
    }
}
