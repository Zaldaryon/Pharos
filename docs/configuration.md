# Configuration

A scenario often depends on configuration: the world's own settings, the server's, or a mod's config
file. Pharos sets it before the server boots, runs one row per value when you ask for a matrix, and
puts it back when a test changes it.

```csharp
[ServerWorld(Preset = WorldPreset.SurvivalFlat)]
[ServerMods("mods/mymod")]
[WorldConfig("temporalStorms", "off")]
[ServerConfig("AllowPvP", false)]
public class MyModConfigTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheServerStartsWithTheConfiguredValues()
    {
        Assert.Equal("off", Server.Api.World.Config.GetString("temporalStorms"));
        Assert.False(Server.Config.AllowPvP);
    }
}
```

`[WorldConfig]` and `[ServerConfig]` take a key and a value. A world key is one the game's mods
declare, such as `temporalStorms`, `creatureHostility` or `playerHealthPoints`. A server key is a
property of `Vintagestory.Server.ServerConfig`, such as `AllowPvP`, `MaxChunkRadius` or
`DefaultRoleCode`; an unknown one is an error that names the key.

Both attributes work on a class or on a test. A test's values win over its class's, and a derived
class's over its base class's. Two attributes with the same key on the same level are an error.

World values are merged into the world's recipe, so the rest of `[ServerWorld]` stays. The game
converts each value to the type its world config declares, and a value it cannot read is left at the
default. Server values are written into the server's `serverconfig.json` before the server reads it.

## Matrices

A matrix runs a theory once per value, and every row gets its own server, because the hosts are
pooled by their configuration.

```csharp
[ServerTheory]
[WorldConfigMatrix("creatureHostility", "aggressive", "passive")]
public void EachHostilityRunsWithItsValue(string value)
{
    Assert.Equal(value, Server.Api.World.Config.GetString("creatureHostility"));
}
```

`[ServerConfigMatrix]` does the same for a server property, and `[ModConfigMatrix]` for a mod's
config file:

```csharp
[ServerTheory]
[ServerConfigMatrix("MaxChunkRadius", "4", "8")]
public void EachChunkRadiusRunsWithItsValue(string value)
{
    Assert.Equal(int.Parse(value), Server.Config.MaxChunkRadius);
}
```

```csharp
[ServerTheory]
[ModConfigMatrix("mymod.json", "Fixtures/easy.json", "Fixtures/hard.json")]
public async Task EachModConfigRunsWithItsFile(string fixture)
{
    CommandResult result = await ExecuteCommandAsync("/mymod");
    Assert.Contains(fixture, result.StatusMessage);
}
```

A matrix attribute is a data attribute, so pair it with `[ServerTheory]`. The method takes one
string argument, the selected value. Values that are valid JSON keep their JSON type, so `false` is
a boolean and `4` a number; anything else, such as `off`, is a string. The selected value of a
mod config matrix is the path of the fixture copied into `ModConfig/` under the destination the
attribute names.

A theory can use only one matrix, because xUnit runs the rows of several data attributes one after
the other instead of combining them.

## Changing configuration during a test

The server's configuration is changed on the game's thread, where the game expects it.

```csharp
[ServerScenario]
public void TheWeatherChangesLive()
{
    Server.WorldConfig.Set("temporalStorms", "veryoften");
    Assert.Equal("veryoften", Server.Api.World.Config.GetString("temporalStorms"));
}
```

`Server.WorldConfig.Set` takes a world key and its value, and converts the value the way the game's
`/worldconfig` command does. An unknown key is an error.

```csharp
[ServerScenario]
public void TheChunkRadiusChangesLive()
{
    Server.ServerConfig.Set(config => config.MaxChunkRadius = 4);
    Assert.Equal(4, Server.Config.MaxChunkRadius);
}
```

`Server.ServerConfig.Set` changes the live `ServerConfig` object and tells the server to save it.
The object stays the same one, so a mod that holds it sees the new value.

```csharp
[ServerScenario]
public void TheModConfigIsRewritten()
{
    Server.ModConfig.Write("mymod.json", new { Enabled = false });
}
```

`Server.ModConfig.Write` writes a JSON file under the server's `ModConfig` folder. A mod reads it
the next time it loads it, not the moment it is written.

## Rollbacks

Between the tests of a class with `Isolation = Isolation.Rollback` (the default), Pharos puts the
configuration back as it was after boot:

- the world's configuration tree is restored in place, so the object mods hold stays the same one;
- the server's configuration is restored into the live object;
- the files under `ModConfig`, and the server's `serverconfig.json`, are put back as they were.

A test that changes the configuration and leaves it changed fails under `StrictIsolation`, the same
way a test that changes the world does.

## What a run records

The effective configuration of each row is part of the run manifest, `pharos-run.json`, under
`configuration`: the world values, the server values and the seeded files with their hashes. A
failing test's `run.json` lists the same values, so the row can be run again with the same
configuration.

## Limits

- A matrix attribute is the only data attribute a scenario theory can use, and the row's
  configuration is fixed when the hosts boot. A test that changes the configuration by hand changes
  it for the next test too, unless the rollback puts it back.
- The server's `serverconfig.json` is written as a partial document with `ConfigVersion` 1.0, which
  makes the game fill in its default player roles. A file without the roles makes the game stop
  with "no such group exists". The game rewrites the file at boot with the whole configuration.
- A mod config matrix replaces the file for that destination, so a class-level `[DataFiles]` for
  the same name does not apply. A test that seeds the same destination with `[DataFiles]` and
  selects it with a matrix is an error. The matrix seeds the server's folder only.
- World values are the world's own configuration, not the server's. `allowFireSpread` and
  `AllowFireSpread` are two different settings, and the world one is what the game's world
  generator reads.
- A mod that reads its config while it starts sees the file as it was staged. A mod that reads the
  server's configuration while it starts sees the staged value too, because the server reads
  `serverconfig.json` during its boot.
