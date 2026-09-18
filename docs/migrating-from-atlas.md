# Migrating from Atlas to Pharos

This guide helps you migrate test suites from Pixnop.Atlas to Zaldaryon.Pharos.XUnit.

## Quick Start

### Option 1: Automatic Migration (Recommended)

Use the built-in CLI migration tool:

```bash
# Dry-run to preview changes
pharos migrate-atlas ./your-test-project --dry-run

# Apply changes
pharos migrate-atlas ./your-test-project
```

### Option 2: Compatibility Shim (Minimal Changes)

Install the compatibility package for a drop-in replacement:

```bash
dotnet add package Zaldaryon.Pharos.AtlasCompat
```

Replace your Atlas package reference:
```diff
- <PackageReference Include="Pixnop.Atlas" Version="..." />
+ <PackageReference Include="Zaldaryon.Pharos.AtlasCompat" Version="0.1.0" />
```

Your existing `using Atlas.Api;` and `using Atlas.XUnit;` statements will continue working.

### Option 3: Full Manual Migration

Replace package references and update all using statements and type references.

## Type Mapping Reference

### Namespace Changes

| Atlas Namespace | Pharos Namespace |
|-----------------|------------------|
| `Atlas.Api` | `Zaldaryon.Pharos.Server`, `Zaldaryon.Pharos.XUnit` |
| `Atlas.XUnit` | `Zaldaryon.Pharos.XUnit` |

### Atlas.Api Types

| Atlas Type | Pharos Type | Notes |
|------------|-------------|-------|
| `IWorldSession` | `EmbeddedServerHost` | Same lifecycle semantics |
| `WorldOptions` | `ServerWorldOptions` | Same properties, record type |
| `ITestPlayer` | `IServerTestPlayer` | Same interface contract |
| `CommandResult` | `CommandResult` | In `Zaldaryon.Pharos.XUnit` namespace |

### Atlas.XUnit Types

| Atlas Type | Pharos Type | Notes |
|------------|-------------|-------|
| `[AtlasScenario]` | `[ServerScenario]` | Fact-based tests |
| `[AtlasTheory]` | `[ServerTheory]` | Theory-based data-driven tests |
| `AtlasScenarioBase` | `ServerScenarioBase` | Abstract base class |
| `[AtlasWorld]` | `[ServerWorld]` | World configuration |
| `[AtlasMods]` | `[ServerMods]` | Mod staging |

## Side-by-Side Examples

### Before (Atlas)

```csharp
using Atlas.Api;
using Atlas.XUnit;

[AtlasWorld(seed: 12345, playStyle: "creativebuilding")]
public class InventoryTests : AtlasScenarioBase
{
    private readonly IWorldSession _session;

    public InventoryTests()
    {
        _session = WorldSession.Create(new WorldOptions { Seed = "12345" });
    }

    [AtlasScenario]
    public async Task Should_GiveItem_ToPlayer()
    {
        var player = await _session.CreateTestPlayer();
        player.GiveItem("game:sword-iron");
        Assert.True(player.HasItem("game:sword-iron"));
    }

    [AtlasTheory]
    [InlineData("game:sword-iron")]
    [InlineData("game:axe-copper")]
    public async Task Should_GiveItem_MultipleTypes(string itemCode)
    {
        var player = await _session.CreateTestPlayer();
        player.GiveItem(itemCode);
        Assert.True(player.HasItem(itemCode));
    }
}
```

### After (Pharos)

```csharp
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

[ServerWorld(seed: 12345, playStyle: "creativebuilding")]
public class InventoryTests : ServerScenarioBase
{
    private readonly EmbeddedServerHost _host;

    public InventoryTests()
    {
        _host = EmbeddedServerHost.Boot(new ServerWorldOptions { Seed = "12345" });
    }

    [ServerScenario]
    public async Task Should_GiveItem_ToPlayer()
    {
        var player = await _host.CreateTestPlayer();
        player.GiveItem("game:sword-iron");
        Assert.True(player.HasItem("game:sword-iron"));
    }

    [ServerTheory]
    [InlineData("game:sword-iron")]
    [InlineData("game:axe-copper")]
    public async Task Should_GiveItem_MultipleTypes(string itemCode)
    {
        var player = await _host.CreateTestPlayer();
        player.GiveItem(itemCode);
        Assert.True(player.HasItem(itemCode));
    }
}
```

## Migration Tool Details

### pharos migrate-atlas

The CLI migration tool automatically transforms your test suite:

```
Usage: pharos migrate-atlas <directory> [options]

Arguments:
  <directory>    Directory containing .csproj files to migrate

Options:
  --dry-run      Preview changes without modifying files
  --verbose      Show detailed progress
  --backup       Create .bak files before modifying (default: true)
```

### What the Tool Changes

1. **Package References** in .csproj files:
   - `Pixnop.Atlas` → `Zaldaryon.Pharos.XUnit`
   - `Pixnop.Atlas.XUnit` → `Zaldaryon.Pharos.XUnit`

2. **Using Statements** in .cs files:
   - `using Atlas.Api;` → `using Zaldaryon.Pharos.Server; using Zaldaryon.Pharos.XUnit;`
   - `using Atlas.XUnit;` → `using Zaldaryon.Pharos.XUnit;`

3. **Type References** in .cs files:
   - `IWorldSession` → `EmbeddedServerHost`
   - `WorldSession` → `EmbeddedServerHost`
   - `WorldOptions` → `ServerWorldOptions`
   - `[AtlasScenario]` → `[ServerScenario]`
   - `[AtlasTheory]` → `[ServerTheory]`
   - `AtlasScenarioBase` → `ServerScenarioBase`
   - `[AtlasWorld]` → `[ServerWorld]`
   - `[AtlasMods]` → `[ServerMods]`

### Example Output

```
$ pharos migrate-atlas ./MyTestProject --dry-run

Scanning: ./MyTestProject
Found 3 .csproj files
Found 12 .cs files

Changes (dry-run):
  MyTestProject.csproj:
    - Replace: Pixnop.Atlas → Zaldaryon.Pharos.XUnit

  Tests/InventoryTests.cs:
    - Line 1: using Atlas.Api; → using Zaldaryon.Pharos.Server;
    - Line 2: using Atlas.XUnit; → using Zaldaryon.Pharos.XUnit;
    - Line 5: AtlasScenarioBase → ServerScenarioBase
    - Line 12: [AtlasScenario] → [ServerScenario]

  Tests/WorldTests.cs:
    - Line 1: using Atlas.Api; → using Zaldaryon.Pharos.Server;
    - Line 8: IWorldSession → EmbeddedServerHost
    - Line 10: WorldOptions → ServerWorldOptions

3 files would be modified (12 replacements)
Run without --dry-run to apply changes.
```

## AtlasCompat Shim Details

The `Zaldaryon.Pharos.AtlasCompat` package provides type aliases in the original Atlas namespaces:

### Atlas.Api Namespace

```csharp
namespace Atlas.Api
{
    // Wraps EmbeddedServerHost
    public interface IWorldSession { ... }
    public class WorldSession : IWorldSession { ... }
    
    // Alias for ServerWorldOptions
    public record WorldOptions { ... }
    
    // Wraps IServerTestPlayer  
    public interface ITestPlayer { ... }
    public class TestPlayerAdapter : ITestPlayer { ... }
    
    // Wraps Pharos CommandResult
    public class CommandResult { ... }
}
```

### Atlas.XUnit Namespace

```csharp
namespace Atlas.XUnit
{
    // Equivalent to ServerScenarioAttribute
    [AttributeUsage(AttributeTargets.Method)]
    public class AtlasScenarioAttribute : FactAttribute { ... }
    
    // Equivalent to ServerTheoryAttribute
    [AttributeUsage(AttributeTargets.Method)]
    public class AtlasTheoryAttribute : TheoryAttribute { ... }
    
    // Equivalent to ServerScenarioBase
    public abstract class AtlasScenarioBase : ServerScenarioBase { }
    
    // Equivalent to ServerWorldAttribute
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AtlasWorldAttribute : Attribute { ... }
    
    // Equivalent to ServerModsAttribute
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly)]
    public class AtlasModsAttribute : Attribute { ... }
}
```

All shim types are marked `[Obsolete]` to encourage eventual migration to native Pharos types.

### Converting Between Types

```csharp
using Atlas.Api;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

// Atlas → Pharos
ITestPlayer atlasPlayer = ...;
IServerTestPlayer pharosPlayer = atlasPlayer.AsPharosPlayer();
EmbeddedServerHost host = worldSession.Host;
ServerWorldOptions options = worldOptions.ToServerWorldOptions();

// Pharos → Atlas
IServerTestPlayer pharosPlayer = ...;
ITestPlayer atlasPlayer = pharosPlayer.AsAtlasPlayer();
WorldOptions options = WorldOptions.FromServerWorldOptions(serverOptions);
```

## Troubleshooting

### Build Errors After Migration

**Error: `CS0246: The type or namespace name 'IWorldSession' could not be found`**

You're using the full migration but haven't replaced all type references. Either:
- Run `pharos migrate-atlas` again to catch missed references
- Add `using Zaldaryon.Pharos.Server;` at the top of the file
- Or use `Zaldaryon.Pharos.AtlasCompat` instead

**Error: `CS0433: The type 'AtlasScenarioAttribute' exists in both assemblies`**

You have both Atlas and AtlasCompat packages referenced. Remove the original `Pixnop.Atlas` reference.

### Runtime Errors

**InvalidCastException when using `AsPharosPlayer()`**

Only `TestPlayerAdapter` instances can be unwrapped. If you created a custom `ITestPlayer` implementation, you'll need to update it to implement `IServerTestPlayer` directly.

## Migration Checklist

- [ ] Choose migration strategy (automatic, shim, or manual)
- [ ] Backup your project
- [ ] Update package references
- [ ] Update using statements  
- [ ] Update type references
- [ ] Run build and fix any remaining errors
- [ ] Run tests to verify behavior
- [ ] Remove `[Obsolete]` warning suppression if using shim
- [ ] Eventually migrate from shim to native types
