# Failure artifacts and the scenario watchdog

When a scenario fails, Pharos saves what you need to understand the failure while the client and
the server are still up. It also stops a scenario body that hangs. This covers tests marked
`[ClientScenario]`, `[ClientTheory]`, `[ServerScenario]`, `[ServerTheory]` or
`[ClientServerScenario]` in a class derived from `ClientScenarioBase`, `ServerScenarioBase` or
`ClientServerScenarioBase`.

## What is saved

Each failed or timed-out test gets its own folder, `<root>/<TestClass>/<TestMethod>`. A theory
row adds a short hash of its arguments to the method name, and a folder that already exists gets
`-2`, `-3` and so on, so an earlier run is never overwritten.

| File | What it holds | Saved when |
|------|---------------|------------|
| `screenshot.png` | The last frame the client rendered. | The scenario has a client. |
| `client.log`, `server.log` | What the client and the server logged during the test. | `Logs` |
| `client-logs/`, `server-logs/` | The game's own log files (`client-main.log`, `server-main.log` and the rest), up to their last 20 MB each. | `Logs` |
| `packets.json` | The client-server traffic of the test, in the `PacketRecorder` format. | `Packets`, which is off by default |
| `run.json` | The test, the outcome (`failed` or `timedOut`), the exception, the game and Pharos versions, the seed, world type and play style, the isolation, the client's boot mode, the frames and server ticks the test stepped, the run id, the time, the OS and the files written. | `RunInfo` |

The failure message itself starts with the original exception's type and message. It then says
where the artifacts went and lists the first 20 errors the client and the server logged. On a
freshly booted host that means since boot. On a host reused from the previous test
(`WorldIsolation.Rollback` or `Recycle`), the logs are cleared between tests, so it means during
this test. The stack trace is the original one.

A scenario that fails before its body runs, because the server does not boot or the player never
joins, saves what it has too, and its message says so.

## Choosing what to save

Override `Artifacts` on the scenario class:

```csharp
protected override FailureArtifacts Artifacts => FailureArtifacts.All;
```

- `FailureArtifacts.Default` is everything but the packets.
- `FailureArtifacts.Packets` starts the client's packet recorder when the test body starts, if the
  test has not started it itself. The recorder keeps every packet in memory, which is why it is off
  by default. A server scenario has no client, so `Packets` does nothing there. Under
  `WorldIsolation.Rollback` the recorder is stopped and cleared between tests.

To add files of your own, override `OnFailure`. It runs while the client and server are still up:

```csharp
protected override void OnFailure(string directory) =>
    File.WriteAllText(Path.Combine(directory, "inventory.txt"), DumpInventory());
```

Every step has a time limit of 15 seconds, so a wedged game cannot hang the report. A step that
fails or runs out of time is listed under "Not saved" in the failure message, and the other steps
still run.

## Where it goes

| Variable | Effect |
|----------|--------|
| `PHAROS_ARTIFACTS` | The root folder. Defaults to `TestResults/pharos` next to the test assembly, for example `bin/Release/net10.0/TestResults/pharos`. |
| `PHAROS_KEEP_SANDBOX=1` | Keeps the failed test's server sandbox and the client's temporary data folder instead of deleting them. Their paths are in the message and in `run.json`. A kept host is not reused by the next test. |
| `PHAROS_RUN_ID` | Names the run in `run.json`. Defaults to `GITHUB_RUN_ID-GITHUB_RUN_ATTEMPT` on GitHub Actions, otherwise to one id per test process. |

On CI, point `PHAROS_ARTIFACTS` into the workspace and upload it when the job fails. This
repository's workflow does this:

```yaml
- name: Test
  env:
    PHAROS_ARTIFACTS: ${{ github.workspace }}/artifacts/pharos
  run: dotnet test

- name: Upload failure artifacts
  if: failure() || cancelled()
  uses: actions/upload-artifact@v4
  with:
    name: pharos-failures-${{ github.run_attempt }}
    path: ${{ github.workspace }}/artifacts/pharos
    if-no-files-found: ignore
```

`cancelled()` matters: a job stopped by `timeout-minutes` counts as cancelled, not failed.

## The watchdog

Each scenario attribute has a `TimeoutMs`: 120 seconds for client and server scenarios, 180
seconds for client-server ones, and 0 turns it off. It limits the test body only, not the boot
before it or the teardown after it. xUnit's own `Timeout`, when set, takes its place. The
watchdog is for hangs, not for performance: set `PHAROS_TIMEOUT_SCALE` (for example `2`) on slow
machines rather than tightening the limits.

When a body runs out of time:

1. It is asked to stop. From then on, every call it makes that steps the client or the server, or
   runs work on their game threads, throws `ScenarioAbortedException`.
2. If it stops within 15 seconds, the failure artifacts are saved, the test fails with a
   `TestTimeoutException`, and the client and server are torn down rather than reused.
3. If it does not stop, for example because it blocks without calling into Pharos, or because the
   game's own thread is stuck, its client and server are left alone, never disposed or reused. The
   artifacts that do not need the game (logs, `run.json`) are still saved. Every later scenario in
   the same test process then fails at once, saying which scenario got stuck, instead of waiting
   for it.

## What is not covered

- Tests marked with a plain `[Fact]` or `[Theory]` in a scenario class save no artifacts and have
  no watchdog. That includes the `[Theory, ClientSettingsMatrix(...)]` pattern: use
  `[ClientTheory, ClientSettingsMatrix(...)]` instead. Their `FailOnLoggedErrors` check still runs
  in `DisposeAsync`, as before.
- In a scenario that the pipeline runs, `FailOnLoggedErrors` is checked when the body ends, before
  teardown, so its failure gets artifacts too. Errors logged by a `BeforeAfterTestAttribute.After`
  or during teardown do not count.
