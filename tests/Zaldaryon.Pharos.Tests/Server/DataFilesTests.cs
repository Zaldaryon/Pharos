using System.Net;
using System.Net.Sockets;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Seeded data files read, checked and filled in, before anything boots.</summary>
public sealed class DataFileSetTests : IDisposable
{
    private static readonly EnumAppSide[] Both = [EnumAppSide.Server, EnumAppSide.Client];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-datafiles-" + Guid.NewGuid().ToString("N")[..8]);

    public DataFileSetTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Placeholders_GetOneFreePortPerName_OnBothSides()
    {
        string server = Fixture("server.json", "{\"a\": {{pharos:port:metrics}}, \"b\": {{pharos:port:admin}}}");
        string client = Fixture("client.json", "{\"a\": {{pharos:port:metrics}}}");

        DataFileSet set = DataFileSet.Create(
            [new DataFilesAttribute(server) { To = "ModConfig/x.json", Side = EnumAppSide.Server }, new DataFilesAttribute(client) { To = "ModConfig/x.json", Side = EnumAppSide.Client }],
            [], Both);

        Assert.Equal(["admin", "metrics"], set.Ports.Keys.Order());
        Assert.NotEqual(set.Port("metrics"), set.Port("admin"));
        using (TcpListener listener = new(IPAddress.Any, set.Port("metrics"))) listener.Start();

        string root = Path.Combine(_folder, "data");
        set.WriteTo(root, EnumAppSide.Server);
        Assert.Equal($"{{\"a\": {set.Port("metrics")}, \"b\": {set.Port("admin")}}}", File.ReadAllText(Path.Combine(root, "ModConfig", "x.json")));

        ArgumentException unknown = Assert.Throws<ArgumentException>(() => set.Port("nope"));
        Assert.Contains("admin, metrics", unknown.Message);
    }

    [Fact]
    public void TheKey_IgnoresPorts_ButNotContent()
    {
        string file = Fixture("a.json", "{{pharos:port:p}}");
        DataFilesAttribute[] attributes = [new DataFilesAttribute(file)];

        Assert.Equal(DataFileSet.Create(attributes, [], Both).Key, DataFileSet.Create(attributes, [], Both).Key);
        Assert.Equal("", DataFileSet.Create([], [], Both).Key);

        File.WriteAllText(file, "changed {{pharos:port:p}}");
        Assert.NotEqual(DataFileSet.Create(attributes, [], Both).Key, DataFileSet.Create([new DataFilesAttribute(Fixture("b.json", "{{pharos:port:p}}"))], [], Both).Key);
    }

    [Fact]
    public void ATestsFile_ReplacesTheClasssForTheSameDestination()
    {
        string classFile = Fixture("class.json", "class");
        string methodFile = Fixture("method.json", "method");

        DataFileSet set = DataFileSet.Create(
            [new DataFilesAttribute(classFile) { To = "ModConfig/x.json" }],
            [new DataFilesAttribute(methodFile) { To = "modconfig/X.json", Side = EnumAppSide.Server }],
            Both);

        string root = Path.Combine(_folder, "data");
        set.WriteTo(root, EnumAppSide.Server);
        set.WriteTo(Path.Combine(_folder, "client"), EnumAppSide.Client);
        Assert.Equal("method", File.ReadAllText(Path.Combine(root, "modconfig", "X.json")));
        Assert.Equal("class", File.ReadAllText(Path.Combine(_folder, "client", "ModConfig", "x.json")));
        Assert.Throws<InvalidOperationException>(() => DataFileSet.Create(
            [new DataFilesAttribute(classFile) { To = "ModConfig/x.json" }, new DataFilesAttribute(methodFile) { To = "ModConfig/x.json" }], [], Both));
    }

    [Fact]
    public void ADerivedClassFile_ReplacesItsBaseClasss()
    {
        DataFileSet set = ScenarioAttributes.DataFiles(typeof(DerivedFiles), EnumAppSide.Server);

        string root = Path.Combine(_folder, "levels");
        set.WriteTo(root, EnumAppSide.Server);
        Assert.Equal("derived", File.ReadAllText(Path.Combine(root, "ModConfig", "m.json")));
        Assert.Equal("base", File.ReadAllText(Path.Combine(root, "ModConfig", "kept.json")));
    }

    [DataFiles("Fixtures/DataFilesLevels/base.json", To = "ModConfig/m.json")]
    [DataFiles("Fixtures/DataFilesLevels/base.json", To = "ModConfig/kept.json")]
    private class BaseFiles;

    [DataFiles("Fixtures/DataFilesLevels/derived.json", To = "ModConfig/m.json")]
    private sealed class DerivedFiles : BaseFiles;

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.json")]
    [InlineData("ModConfig/../../outside.json")]
    [InlineData("ModConfig/")]
    [InlineData("Mods/mymod.zip")]
    [InlineData("clientsettings.json")]
    [InlineData("C:/outside.json")]
    [InlineData("Mods./mymod.zip")]
    [InlineData("ModConfig/x.json ")]
    [InlineData("ModConfig/CON.json")]
    [InlineData("Saves/world.vcdbs")]
    [InlineData("serverconfig.json")]
    public void Destinations_StayInsideTheDataFolder_AndOutOfWhatTheGameOwns(string to)
    {
        string file = Fixture("a.json", "{}");

        Assert.Throws<ArgumentException>(() => DataFileSet.Create([new DataFilesAttribute(file) { To = to }], [], Both));
    }

    [Fact]
    public void MissingFilesAndBadPlaceholders_AreReported()
    {
        FileNotFoundException missing = Assert.Throws<FileNotFoundException>(() => DataFileSet.Create([new DataFilesAttribute("Fixtures/nope.json")], [], Both));
        Assert.Contains("CopyToOutputDirectory", missing.Message);

        string bad = Fixture("bad.json", "{\n  \"port\": {{pharos:prot:metrics}}\n}");
        FormatException format = Assert.Throws<FormatException>(() => DataFileSet.Create([new DataFilesAttribute(bad)], [], Both));
        Assert.Contains("line 2", format.Message);

        string misspelt = Fixture("misspelt.json", "{{ Pharos:port:metrics }}");
        Assert.Throws<FormatException>(() => DataFileSet.Create([new DataFilesAttribute(misspelt)], [], Both));

        string other = Fixture("other.json", "{{mustache}}");
        Assert.Empty(DataFileSet.Create([new DataFilesAttribute(other)], [], Both).Ports);
    }

    [Fact]
    public void TheBaseline_PutsBackWhatATestChanged()
    {
        string file = Fixture("x.json", "seeded");
        DataFileSet set = DataFileSet.Create([new DataFilesAttribute(file) { To = "ModConfig/x.json", Side = EnumAppSide.Server }], [], Both);
        string root = Path.Combine(_folder, "data");
        set.WriteTo(root, EnumAppSide.Server);
        File.WriteAllText(Path.Combine(root, "ModConfig", "other.json"), "a mod's own");
        Directory.CreateDirectory(Path.Combine(root, "Logs"));

        DataFileBaseline baseline = DataFileBaseline.Capture(root, set, EnumAppSide.Server)!;
        Assert.Null(DataFileBaseline.Capture(root, set, EnumAppSide.Client));

        File.WriteAllText(Path.Combine(root, "ModConfig", "x.json"), "changed");
        File.Delete(Path.Combine(root, "ModConfig", "other.json"));
        File.WriteAllText(Path.Combine(root, "ModConfig", "added.json"), "new");
        File.WriteAllText(Path.Combine(root, "Logs", "server.log"), "kept");

        Assert.Equal(3, baseline.Restore());
        Assert.Equal("seeded", File.ReadAllText(Path.Combine(root, "ModConfig", "x.json")));
        Assert.Equal("a mod's own", File.ReadAllText(Path.Combine(root, "ModConfig", "other.json")));
        Assert.False(File.Exists(Path.Combine(root, "ModConfig", "added.json")));
        Assert.True(File.Exists(Path.Combine(root, "Logs", "server.log")));
        Assert.Equal(0, baseline.Restore());
    }

    private string Fixture(string name, string content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }
}

/// <summary>A mod's config seeded on each side of a real client-server pair.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosconfigmod")]
[DataFiles("Fixtures/DataFiles/pharosconfig-server.json", To = "ModConfig/pharosconfig.json", Side = EnumAppSide.Server)]
[DataFiles("Fixtures/DataFiles/pharosconfig-client.json", To = "ModConfig/pharosconfig.json", Side = EnumAppSide.Client)]
public class LiveDataFilesClientServerTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

    // Whether the test before this one on the same hosts changed both configs.
    private static bool s_changed;

    [ClientServerScenario]
    public async Task EachSide_StartsWithItsOwnConfig_AndTheSamePort()
    {
        CheckPutBack();
        int port = DataFilePort("metrics");

        Assert.Equal($"label=server port={port}", await ServerCommandAsync("/pharosconfig"));
        Assert.Equal($"label=client port={port}", (await Client!.Commands.ExecuteSuccessAsync(".pharosconfig")).Message);
        using TcpListener listener = new(IPAddress.Any, port);
        listener.Start();
    }

    [ClientServerScenario]
    public Task ChangedConfigs_ArePutBack_First() => ChangesAndFindsTheSeededFilesAsync();

    [ClientServerScenario]
    public Task ChangedConfigs_ArePutBack_Second() => ChangesAndFindsTheSeededFilesAsync();

    [ClientServerScenario]
    [DataFiles("Fixtures/DataFiles/pharosconfig-alternate.json", To = "ModConfig/pharosconfig.json", Side = EnumAppSide.Server)]
    public async Task ATestsOwnFile_BootsItsOwnHosts()
    {
        s_changed = false;
        Assert.Equal("label=alternate port=1", await ServerCommandAsync("/pharosconfig"));
        Assert.StartsWith("label=client", (await Client!.Commands.ExecuteSuccessAsync(".pharosconfig")).Message);
        Assert.NotEqual(IsolationKind.RolledBack, Isolation!.Kind);
    }

    // Each side reads its own file again, though the client booted after the server: server mods
    // keep their own data folder.
    private async Task ChangesAndFindsTheSeededFilesAsync()
    {
        CheckPutBack();
        Assert.StartsWith("label=server", await ServerCommandAsync("/pharosconfig reload"));
        Assert.StartsWith("label=client", (await Client!.Commands.ExecuteSuccessAsync(".pharosconfig reload")).Message);

        await ServerCommandAsync("/pharosconfig set changed");
        await Client.Commands.ExecuteSuccessAsync(".pharosconfig set changed");
        s_changed = true;
        Assert.StartsWith("label=changed", await ServerCommandAsync("/pharosconfig reload"));
        Assert.StartsWith("label=changed", (await Client.Commands.ExecuteSuccessAsync(".pharosconfig reload")).Message);
    }

    // After a test that changed both files, the rollback rewrote them.
    private void CheckPutBack()
    {
        if (Isolation!.Kind == IsolationKind.RolledBack && s_changed) Assert.Equal(2, Isolation.DataFilesRestored);
        s_changed = false;
    }

    private Task<string?> ServerCommandAsync(string command)
    {
        TaskCompletionSource<string?> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerHost!.RunOnGameThread(() => ((Vintagestory.API.Server.ICoreServerAPI)Server!.Api).ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs { Caller = ServerScenarioBase.ConsoleCaller() },
            result => done.TrySetResult(result.StatusMessage)));
        return done.Task;
    }
}

/// <summary>A mod's config seeded into a server scenario.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosconfigmod")]
[DataFiles("Fixtures/DataFiles/pharosconfig-server.json", To = "ModConfig/pharosconfig.json", Side = EnumAppSide.Server)]
[DataFiles("Fixtures/DataFiles/pharosconfig-client.json", To = "ModConfig/client-only.json", Side = EnumAppSide.Client)]
public class LiveDataFilesServerTests : ServerScenarioBase
{
    private static bool s_changed;

    [ServerScenario]
    public async Task TheServer_StartsWithItsConfig_AndClientFilesStayOut()
    {
        CheckPutBack();
        Assert.Equal($"label=server port={DataFilePort("metrics")}", (await ExecuteCommand("/pharosconfig")).StatusMessage);
        Assert.False(File.Exists(Path.Combine(Host!.DataPath!, "ModConfig", "client-only.json")));
    }

    [ServerScenario]
    public Task ChangedConfigs_ArePutBack_First() => ChangesAndFindsTheSeededFileAsync();

    [ServerScenario]
    public Task ChangedConfigs_ArePutBack_Second() => ChangesAndFindsTheSeededFileAsync();

    private async Task ChangesAndFindsTheSeededFileAsync()
    {
        CheckPutBack();
        Assert.StartsWith("label=server", (await ExecuteCommand("/pharosconfig reload")).StatusMessage);
        await ExecuteSuccess("/pharosconfig set changed");
        s_changed = true;
        Assert.StartsWith("label=changed", (await ExecuteCommand("/pharosconfig reload")).StatusMessage);
    }

    private void CheckPutBack()
    {
        if (Isolation!.Kind == IsolationKind.RolledBack && s_changed) Assert.Equal(1, Isolation.DataFilesRestored);
        s_changed = false;
    }
}
