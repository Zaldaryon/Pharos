# Parallel runs

The game keeps its state in statics, so one process holds one engine-mode client at a time and
`dotnet test` runs live scenarios one after another. `pharos run` runs them in several worker
processes at once:

```bash
pharos run tests/MyMod.Tests --filter "Category=Live" --parallel 4 --trx results.trx
pharos run tests/MyMod.Tests --list
```

It lists the tests without running them, splits them into groups, and runs each group in a
`dotnet vstest` process of its own, `--parallel` at a time. xUnit runs each group exactly as
`dotnet test` would: fixtures, collections, Pharos's scenario runner, timeouts and skips all
behave the same. The runner then merges every worker's results into one TRX and one summary.

`run` needs no game install of its own. Its workers find the game the way your tests do, through
`VINTAGE_STORY`, or the install `--game` names.

## What runs where

- **Groups.**
  - Each test class in `Category=Live` (`[Trait(PharosTraits.Category, PharosTraits.Live)]`) gets
    a worker of its own.
  - Every other test shares one worker: those tests are quick, and a worker each would spend
    more time starting than testing.
  - When no test is in `Category=Live`, every class gets a worker of its own.
- **`--group collection`** gives each xUnit collection a worker instead of each class. Pharos's
  own live classes all sit in the `Sequential` collection, which only keeps them apart inside
  one process. Grouping by collection would run them in a single worker, so class is the
  default.
- **Order.** The longest groups start first, going by the durations in the last run's merged
  TRX when there is one; otherwise the larger groups start first. A group the last run did not
  have starts before the rest.
- **Filters.** `--filter` takes the same expressions as `dotnet test --filter`, and every worker
  applies it.
  - Listing evaluates fully qualified names and traits.
  - With a filter on `DisplayName` or `Name`, every test is listed and grouped, and the workers
    leave out the ones the filter does not match.

## What each worker gets

- **Its own data.** Each worker gets:
  - a client data folder, a server sandbox, and world and mod staging folders: temporary
    folders, as in any Pharos run;
  - its own ports: loopback sessions use in-memory networks, and servers that listen or
    `[DataFiles]` placeholders take free ports. A port is probed, then released for its user,
    so another process could take it in between. That is rare, but not impossible.
- **A display.** On Linux with no display (`DISPLAY` and `WAYLAND_DISPLAY` unset) and `xvfb-run`
  installed, each worker runs under a virtual display of its own. `--xvfb always` insists on
  one, and `--xvfb never` shares whatever display the runner has. Under a CI job already wrapped
  in `xvfb-run`, workers share that display; Pharos's clients do not need focus, so that works.
- **A share of the cores.** Mesa's software renderer uses every core for each client. Each worker
  gets `LP_NUM_THREADS` = cores ÷ workers, unless you set it.
  - Live tests also use the CPU heavily for the game itself: more workers than cores slows every
    one of them, and timing-sensitive watchdogs may trip.
  - Raise `PHAROS_TIMEOUT_SCALE` when you push the count up.
  - Each engine client also takes several hundred MB of memory.
- **Failure artifacts** under `<results-dir>/artifacts/worker-<n>`, or under `PHAROS_ARTIFACTS`
  when it is set. Every worker's `run.json` carries the same run id.

Resources outside the machine are shared: a remote dedicated server (`PHAROS_REMOTE_SERVER`) or
one online account. Keep the tests that use one of them in a single class, so they run in one
worker.

## Crashes and hangs

- **Crashes.** A worker whose test host crashes, or that stops before writing its results, fails
  its group with a result named `<group> (worker aborted)`. This also covers a crash after the
  last test, while fixtures are disposed. When listing applied the filter, every listed test the
  worker never reported fails too, with the end of the worker's output.
- **Hangs.** vstest stops a test host whose current test has run longer than `--test-timeout`
  (30 minutes, scaled by `PHAROS_TIMEOUT_SCALE`). The results it had are kept. Pharos's own
  scenario watchdogs normally fail a hung test long before.
- **Whole workers.** A worker that runs longer than `--worker-timeout` (60 minutes, scaled) is
  stopped with its whole process tree.
- **Stopping the run.** Ctrl+C, or SIGTERM from a CI runner, stops every worker.

A crash or a hang never hangs the run, and never passes.

## Output

- **Text.** The count of test methods listed (a theory once), a line for each finished group with
  its results (each theory row counted), then the failed tests with the start of their
  messages, the slowest groups, and a summary in the style of `dotnet test`. Each worker's full
  output is in `<results-dir>/workers/<n>.log`, beside its TRX and settings.
- **`--trx`.** One TRX with every result from every worker, and the errors vstest recorded. The
  default is `<results-dir>/pharos.trx`. Attachments stay where each worker wrote them, under
  `<results-dir>/workers/<n>`.
- **`--json`.** JSON lines on standard output instead of text:
  - `listed`;
  - `started`;
  - `groupStarted`;
  - `test`, one per result;
  - `groupFinished`, with `crashed` and `aborted`;
  - `summary`.
- **Exit codes.**
  - 0: every test passed, or none matched;
  - 1: a test failed, or a worker crashed or hung;
  - 2: the arguments were wrong, or the tests could not be built or listed.

## In CI

```yaml
- name: Live tests
  run: pharos run tests/MyMod.Tests --no-build --filter "Category=Live" --parallel 2 --trx live.trx
  env:
    VINTAGE_STORY: ${{ github.workspace }}/vs
    PHAROS_TIMEOUT_SCALE: "2"
- uses: actions/upload-artifact@v4
  if: failure()
  with:
    name: pharos-run
    path: pharos-run/
```

GitHub's standard Linux runners have four cores: two workers is a good start.

## Options

| Option | Default | |
|---|---|---|
| `--filter <expr>` | | A `dotnet test` filter. |
| `-p`, `--parallel <n>` | 1 | Workers at once. |
| `--group class\|collection` | class | How live tests are split. |
| `--list` | | List the groups and tests; run nothing. |
| `--trx <path>` | `<results-dir>/pharos.trx` | The merged results. |
| `--results-dir <dir>` | `pharos-run` | Worker logs, TRX files, artifacts. |
| `--test-timeout <min>` | 30 | Per test, before vstest stops the test host. |
| `--worker-timeout <min>` | 60 | Per worker, before it is stopped. |
| `--json` | | JSON lines instead of text. |
| `-c`, `--configuration <c>` | Release | A project's build configuration. |
| `--no-build` | | Use a project's last build. |
| `--game <path>` | `VINTAGE_STORY` | The game install workers use. |
| `--xvfb auto\|always\|never` | auto | A virtual display per worker. |
| `-v`, `--verbose` | | Print each worker's command and filter. |
