# Data files

Many mods read a config from `ModConfig/` when they start, and some open a port named in it. To
test them, use `[DataFiles]`. It copies a file into the server's or the client's data folder
before they boot, so the mod starts with it.

```csharp
[ServerWorld(seed: 42)]
[ServerMods("mods/mymod.zip")]
[DataFiles("Fixtures/mymod-server.json", To = "ModConfig/mymod.json", Side = EnumAppSide.Server)]
[DataFiles("Fixtures/mymod-client.json", To = "ModConfig/mymod.json", Side = EnumAppSide.Client)]
public class MyModConfigTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public async Task TheMetricsEndpointAnswers()
    {
        int port = DataFilePort("metrics");
        using HttpClient http = new();
        Assert.True((await http.GetAsync($"http://127.0.0.1:{port}/metrics")).IsSuccessStatusCode);
    }
}
```

`Fixtures/mymod-server.json`:

```json
{
  "Enabled": true,
  "MetricsPort": {{pharos:port:metrics}}
}
```

## Where files come from and go

- **`source`** is relative to the test's working folder, which is its output folder under
  `dotnet test`, or else to the test assembly's folder. It can also be absolute. Copy your
  fixtures to the output folder:

  ```xml
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  ```

- **`To`** is relative to the data folder, with `/` between folders. It defaults to
  `ModConfig/` and the file's name. It cannot leave the data folder (no rooted paths and no `..`).
  It cannot go into `Mods/` or `ModsByServer/`, nor `ClientMods/` on the server: stage mods with
  `[ServerMods]` or `[PharosMods]`. It cannot go into the folders the game keeps open or writes
  itself (`Saves/`, `BackupSaves/`, `Backups/`, `OldSaves/`, `Logs/`, `Cache/`, `Playerdata/`),
  nor replace `serverconfig.json` (use `[ServerWorld]`) or `clientsettings.json` (use the client
  options). Destinations compare without case, as on Windows, so `modconfig/X.json` and
  `ModConfig/x.json` are the same file; spell them the way the mod does.
- **`Side`** chooses the server's data folder, the client's, or both. Both is the default.
  `ServerScenarioBase` classes have no client, so their client files are ignored. Client-only
  scenarios do not support `[DataFiles]`, and client files need the client's own temporary
  folder: leave `ClientOptions.DataPath` unset.

## Ports

Each `{{pharos:port:NAME}}` in a file is replaced with a port nothing on the machine listens on.
`NAME` is letters, digits, `.`, `_` and `-`. The same name gets the same port in every file of
the test, on both sides. Different names get different ports. Read a port back with
`DataFilePort("NAME")` once the hosts have booted. An unknown name throws, listing the ones the
test has.

The ports come from the system's ephemeral range and are free when the hosts boot. Another
process could take one between then and the mod's use of it, but that is rare.

Any other `{{pharos:...}}` token is an error that names its line. Everything else in the file is
copied byte for byte.

## On a class or on a test

On the class, the files apply to every test, and its tests share the class's hosts as usual.

A derived class's files add to its base class's, and replace them for the same side and
destination. On a test, they add to the class's files or replace them in the same way. Because the hosts must boot with the test's files, such
a test gets hosts of its own, and the class's tests go on sharing theirs.

Two files with the same side and destination on the same level are an error.

## Rollbacks

Hosts are pooled by their files, so tests never share a host seeded with different files.
Between the tests of a class with `Isolation = WorldIsolation.Rollback` (the default), Pharos puts the data files back as they were
after boot:
- files the test changed are rewritten;
- files it deleted are recreated;
- files added under `ModConfig/` are deleted.

`Isolation.DataFilesRestored` counts them, and the test's isolation line shows it. Other
isolation modes boot fresh hosts or keep the files as the last test left them. A mod that
keeps its config in memory only reads the restored file the next time it loads it.

## The server's own folder

A server and a client in one process share the game's `GamePaths.DataPath` field, and the
client's boot points it at the client's folder. Pharos therefore makes the mod API answer with
each side's own folder. This covers `api.DataBasePath`, `api.GetOrCreateDataPath`,
`api.LoadModConfig` and `api.StoreModConfig`, in their plain and generic forms, so a server mod
that reads or writes its config at run time gets the server's file.

The generic forms follow the server's folder on its game thread, where mods start, tick and run
commands. On a thread of the mod's own, and for a mod that reads `GamePaths.DataPath` or
`GamePaths.ModConfig` itself, the shared field applies.
