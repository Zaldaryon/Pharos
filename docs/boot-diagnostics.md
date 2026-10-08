# Boot diagnostics

Most of what breaks a mod release shows up as a warning while the game boots, before any test
runs. A texture or shape that is not found, a recipe with an unknown ingredient, a missing
dependency or a JSON patch that matched nothing all end up there. Pharos keeps those warnings
apart, and a strict scenario class fails when its client or server logged one it did not expect.

## What is collected

`EmbeddedServerHost.BootDiagnostics` and `HeadlessClient.BootDiagnostics` list every warning,
error and fatal error the server or client logged while it booted, oldest first. Each entry has
its `Side`, its level (`Type`), its `Message` and its `Source`.

The boot ends at a different point for each host:

| Host | Boot ends | Why |
|------|-----------|-----|
| Server | When `EmbeddedServerHost.Boot` returns. | The world is loaded and the mods have started. |
| Engine-mode client | When it has joined a server, the first time `IsJoined` is true. In a `ClientScenarioBase`, which does not join it, when the scenario has booted it, even if a test joins it to a server later. | Its mods only start during the join. |
| Fixture-mode client | When it has booted. | It never joins. |

Until then `IsComplete` is false and the list keeps growing. An engine-mode client in a
`ClientScenarioBase` never joins a server, so its list only covers what it logged before its mods
would have started. A `ClientServerScenarioBase` that sets `WaitForPlayerJoinOnInit` to false is
checked before its client has joined; the check notes it, and what the client logs during the
later join is not enforced. What the game logs later does not count, for example a shape it only tessellates when it
first renders, or a warning the server logs when a client joins.

`LogCapture.Clear()`, which runs between pooled tests, leaves the boot list alone. The list holds
at most 10,000 entries; past that it is marked `IsTruncated`.

### Source

`Source` is the logger that wrote the entry:
- A mod's own logger (`Mod.Logger`) prefixes its messages with `[modid] `, so `Source` is that mod
  id. Vanilla's mods show up as `game`, `survival` or `creative`.
- Everything else, including the engine's own bracketed prefixes such as `[Mod API]`, is `game`.

`Source` names who logged the entry, not whose fault it is. The engine reports a mod's missing
asset under `game`, naming the mod in the message (`Texture asset 'mymod:block/x' not found`).
To allow those, put the domain in the pattern instead of setting `Source`.

## Strict boots

```csharp
[StrictBoot]
[AllowBootDiagnostic("Texture asset 'mymod:.*' not found")]
public class MyModLoadsCleanly : ClientServerScenarioBase
{
    [ClientServerScenario]
    public void NoWarnings() => Assert.True(UnexpectedBootDiagnostics.Passed, UnexpectedBootDiagnostics.Describe());
}
```

`[StrictBoot]` on a class, a base class or the assembly fails the boot of every host the class
boots fresh when that host logged an entry no allowance matches. A host reused from the previous
test of the class was checked when it booted.

When the check fails:
- The test that booted the host fails with a `BootDiagnosticsException` that lists the unexpected
  entries and the unmet allowances. The host is disposed, never pooled.
- The test's [failure artifacts](failure-artifacts.md) include `boot-diagnostics.txt`, beside the
  logs and `run.json`.
- Every later test of the class fails at once with the same list and names the first test,
  without booting again. One boot warning therefore fails the whole class, which is the point:
  fix the warning or allow it.

## Allowances

`[AllowBootDiagnostic(pattern)]` allows matching entries. It goes on a class, a base class or the
assembly, as many times as needed.

| Property | Meaning |
|----------|---------|
| `pattern` | A regular expression searched for anywhere in the message, ignoring case. `AllowedLoggedErrors` takes plain fragments instead. |
| `Level` | `Warning`, `Error` or `Fatal`, matched exactly. Any of them when not set. The engine logs `Fatal(exception)` at the error level. |
| `Source` | The logger, compared ignoring case. Any when not set. |
| `Count` | How many matches there must be, exactly. Any number when not set. A count above 0 makes the entry required. |
| `Required` | The entry must appear at least once. A warning that has been fixed then shows up as a stale allowance, so the allowance can be removed. |

An entry is unexpected when no allowance matches it. An entry that matches several allowances
counts toward each of them. An allowance that cannot work fails every test of the class before
anything boots, with a message naming it: an invalid pattern, a level other than `Warning`,
`Error` or `Fatal`, a negative count other than -1, or `Count = 0` with `Required = true`. A
pattern that takes more than a second on an entry counts as not matching it, and the result
says so.

Pharos itself allows one entry wherever it applies: the server's `Server overloaded. A tick took
...` warning, which depends on how busy the machine is rather than on the mods under test.

## Without strict mode

Any scenario class can read `UnexpectedBootDiagnostics`, which judges its hosts' boot diagnostics
against its allowances, and assert on it:

```csharp
Assert.Empty(UnexpectedBootDiagnostics.Unexpected);
```

`BootDiagnostics.Check(allowances)` does the same for a host booted directly, with the allowances
given and Pharos's own.

## Interplay with `FailOnLoggedErrors`

When a `[StrictBoot]` class's fresh boot passes the check, its hosts' logs are cleared, so
`FailOnLoggedErrors` judges only what each test logs. Any other class keeps the old behaviour: the
first test after a fresh boot also sees the boot's errors.

## The mod safety check

On its first boot in a process, the game fetches the list of blocked mods from its website. Offline
or on a slow network, that waits up to three ten-second timeouts and logs a warning for each
attempt, at a moment that depends on the network and on which test booted first. Pharos fills the
list in empty before the game asks for it, so hosts booted by Pharos neither wait for it nor warn
about it, and no mod is blocked.
