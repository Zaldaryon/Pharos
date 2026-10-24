# Game-state snapshots with Verify

`Zaldaryon.Pharos.Verify` integrates [Verify](https://github.com/VerifyTests/Verify) with Pharos.
It depends on the runner-neutral `Verify` **31.12.5**, not either xUnit runner. Choose the matching
adapter in your test project; do not install both adapters in one project:

| Pharos scenarios | Verify adapter |
| --- | --- |
| `Zaldaryon.Pharos.XUnit` (xUnit 2.9.3) | `Verify.Xunit` **31.12.5**, the final legacy release |
| `Zaldaryon.Pharos.XUnit.V3` | `Verify.XunitV3` **31.12.5** |

```sh
dotnet add package Zaldaryon.Pharos.Verify --version 0.7.0
# Pick one:
dotnet add package Verify.Xunit --version 31.12.5
# dotnet add package Verify.XunitV3 --version 31.12.5
```

The common Verify dependency is pinned to that version. This avoids mixing incompatible Verify
majors through transitive runner dependencies. It does not require a sponsorship-exemption property.

## Initialize before verifying

Call `PharosVerify.Initialize()` before the assembly's first Verify call. A module initializer works
with both adapters. Initialization is thread-safe and idempotent; each test process initializes once.

```csharp
using Zaldaryon.Pharos.Verify;

public class SnapshotSetup
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Initialize() => PharosVerify.Initialize();
}
```

## Tooltips and game versions

Use the fluent extension on the **unawaited** Verify call. It uses the loaded game API's version,
not the version against which the Pharos package was compiled.

```csharp
using Zaldaryon.Pharos.Verify;

[ClientServerScenario]
public async Task Tooltip_unchanged()
{
    await VerifyXunit.Verifier.Verify(Client.Items.Tooltip(Client.Items.Stack("game:grain-spelt")))
        .UniqueForGameVersion();
}
```

This produces `ScenarioContainer.Tooltip_unchanged.1.22.7.verified.txt` on 1.22.7. The suffix
changes with the loaded game. The test's class, directory and `UseParameters(...)` remain in the
name. Verify 31 has no public arbitrary-suffix API, so this extension sets its method-name component.
Call `UniqueForGameVersion("custom_name")` if you already customize that component. `UseFileName`
replaces the whole basename and should not be combined with this helper. The same extension is
available on `VerifySettings`.

## What converters preserve

- `TreeAttribute` / `ITreeAttribute`: recursively typed values, arrays and ordinally sorted keys.
  Shared subtrees are allowed; cycles and depth over 128 containers fail instead of hiding data.
- `ItemStack`: collectible code, quantity and attributes, including stacks nested in attributes.
- `AssetLocation`: full `domain:path`. `BlockPos`: X/Y/Z and dimension. `Vec3d`: X/Y/Z.
- `Entity`: code, position and its watched-attribute tree, not the entire cyclic engine object.
- `GuiSnapshot`: dialog/HUD names, modal state and complete captured layouts. Live
  `Client.Gui.Snapshot()` now captures those layouts on the client thread. Older manually constructed
  name-only snapshots have empty `Layouts` until supplied with element data.
- `DialogLayout`: window size, GUI scale, composer bounds, element keys/types, bounds, text,
  interactivity and clipping. Array order remains significant.
- `ItemTooltip`: title and the game's plain-text lines, not markup noise.
- `ModMessage` / `ServerModMessage`: direction, channel, transport, message ID/type and **all payload
  bytes** as hexadecimal; server messages retain position. Run-specific frame/tick counters,
  observation sequence and connection/player identifiers are omitted. For field-level scrubbing
  inside a protobuf payload, verify its decoded message as well rather than removing payload bytes.
- `HudReadout`: HUD dialog layouts and element text/bounds from `Client.Ui.HudReadout()`.
- `IGameCalendar`: a stable date marker plus calendar configuration (hours/day, days/month/year,
  speed and season override), not elapsed game time or date-dependent astronomy.

Floating-point **numbers** round to four decimal places, culture-independently. Numeric-looking
strings, block/item codes, integer quantities and arbitrary long numbers are not rounded or masked.
Snapshot raw entity/tree data on its owning game thread, after a tick/frame-bounded wait, rather
than while the engine is mutating it.

## Regions and the scene builder

```csharp
using Zaldaryon.Pharos.Verify;

[ServerScenario]
public async Task Structure_unchanged()
{
    var origin = new BlockPos(160, 100, 160);
    var max = origin.AddCopy(1, 0, 1);
    await Server.EnsureChunksLoadedAsync(origin, max);
    Scene.Build(Server, origin, new Dictionary<char, string> { ['#'] = "game:rock-granite" }, "##", "#.");
    await VerifyXunit.Verifier.Verify(Server.Snapshot.Region(origin, max)).UniqueForGameVersion();
}
```

`Server.Snapshot.Region(min, max)` reads on the server thread, even when called from a test thread.
Bounds are inclusive. Columns increase X, rows increase Z, layers increase Y, exactly as `Scene.Build`.
It shares Scene's loaded-chunk/world-bound checks and 65,536-cell limit. Reversed, oversized or
non-main-dimension bounds fail explicitly. Load chunks first; capturing never generates them.

The text contains solid and fluid block-code grids followed by block entities in Y/Z/X order.
Each block entity includes its type, relative cell position and sorted typed attribute tree.
Full codes separated by ` | ` avoid a finite symbol alphabet or a separately maintained legend.
`ServerSnapshot.Region(host, min, max)` remains as a compatibility delegate to this API.

## Scrubbing runtime values

Known members named `entityId` or `playerUID` (case-insensitive) are scrubbed, including inside tree
attributes. A custom field such as `ownerPlayerUID` is not guessed. Supply explicit per-verification
replacements for such fields, dates embedded in text, or IDs appearing in strings.

`[DataFiles]` scenarios expose `DataFilePorts`: names from `{{pharos:port:NAME}}` and their actual
assigned ports. Pass that map to the scrubber; no growing process-global port registry is used.

```csharp
using Zaldaryon.Pharos.Verify;

[ServerScenario]
public async Task Runtime_values_unchanged()
{
    var uid = "the-known-runtime-player-uid";
    await VerifyXunit.Verifier.Verify(new { Port = DataFilePort("metrics"), Player = uid, Count = 123456 })
        .ScrubDataFilePorts(DataFilePorts)
        .ScrubGameValue(uid, "Player")
        .UniqueForGameVersion();
}
```

For that example, declare a `[DataFiles]` fixture containing `{{pharos:port:metrics}}` as described
in [data files](data-files.md). Ports become `{Port:metrics}`. This deliberately replaces supplied
standalone port tokens, including endpoint ports, wherever they occur in that verification; keep
unrelated equal-valued numbers in a separate verification when they must remain visible. Codes
like `mod:item-54321`, longer integers and decimal fractions are not port tokens.

`ScrubGameValue(calendarDateText, "Calendar")` handles a date rendered into GUI/text without
assuming an English date format. All scrubber extensions also work on `VerifySettings`.

## Received / verified workflow

1. First run fails and writes `*.received.txt` (strict JSON uses `.received.json`).
2. Read the entire received file. Check codes, quantities, payloads, element text and bounds; remove
   only justified nondeterminism, not the behavior being tested.
3. Accept intentionally through Verify's IDE/diff tooling, or copy that reviewed file to the matching
   `*.verified.txt`. Commit the verified baseline with the test.
4. Re-run: matching snapshots pass; mismatches fail and leave received output beside the untouched
   verified baseline. Never enable automatic acceptance in normal tests or CI.

Ignore `*.received.*` in Git, **not** `*.verified.*`. A new game version gets separate reviewed
baselines; do not rename the old version's files to suppress a regression. See the shared executable
v2/v3 examples and committed baselines under `tests/Snapshots/`.
