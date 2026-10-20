# Comparing Before and After

`pharos compare` runs the same selection of tests against a baseline and against your working tree,
alternates the runs so machine noise lands on both, and reports what each metric did. It answers
one question: what got better, what got worse, and is it real or noise.

## Quick start

```text
pharos compare tests/MyMod.Tests --baseline origin/main
```

That builds `origin/main` in a temporary worktree, builds the working tree, and runs the selected
tests five times on each side, alternating in pairs (baseline, candidate, candidate, baseline, and
so on). Both sides run on the same machine, with the same filter, the same workers and the same
world.

```text
pharos compare tests/MyMod.Tests --baseline origin/main --repeat 7 --filter "Forge*"
pharos compare tests/MyMod.Tests --baseline mymod@1.4.2 --budgets pharos.compare.json
pharos compare tests/MyMod.Tests --baseline pharos-run/baseline/pharos-run.json --md summary.md
```

## Baselines

`--baseline` takes one of five things.

| Form | What it does |
|---|---|
| A git ref, such as `origin/main` or `v1.4.2` | Checks the ref out into a temporary worktree and builds it there. The baseline tests run from that build, in that checkout. |
| A `pharos-run.json`, or a folder holding one | Reuses a run you already have. Nothing is rebuilt or run for the baseline. |
| A mod zip or dll | Stages that build in place of the one the tests declare, and runs the candidate tests against it. |
| `<id>@<version>` | The same, with the zip taken from the local mod cache. |
| Nothing else | Rejected with the reason, so a typo does not silently compare a checkout against itself. |

The local mod cache is where Pharos keeps released mod builds:

```text
~/.local/share/Pharos/mods/<id>/<version>/<id>.zip
~/.local/share/Pharos/mods/<id>-<version>.zip
```

Put a released zip there and `--baseline mymod@1.4.2` finds it, or pass the zip directly with
`--baseline path/to/mymod.zip`.

A mod zip or folder is staged through `PHAROS_MOD_OVERRIDE`, a directory whose entries replace
declared mod paths by file name. Only `pharos compare` sets it, and only for the baseline runs, so
the candidate runs stage their own build as usual. It is what lets one test project run against two
builds of the same mod without touching either tree.

## The runs

Every run is a normal Pharos run: the same `--filter`, `--parallel`, `--shard`, `--game`,
`--configuration`, `--no-build` and `--xvfb` apply to both sides. Each run writes its artifacts and
manifest under `--results-dir` (default `pharos-compare`), one folder per side and repetition, so
you can open any single run when a number looks wrong.

`--repeat` sets how many runs each side gets, five by default. Alternating in pairs means a machine
that gets busier over the comparison slows both sides about equally, and a slow first boot does not
land on one side only.

## The report

Metrics are aggregated by name across the tests and runs of each side:

- an exact metric (a count, a byte total, a tick count) adds up, and its per-run totals are compared;
- a sampled metric (a frame, tick or stopwatch time) contributes each run's median, giving one value
  per run for the statistics.

Each row is a metric, its values, the change, the confidence interval and a verdict.

| Verdict | Meaning |
|---|---|
| improved | The change is significant and in the better direction. |
| regressed | The change is significant and in the worse direction. |
| changed | A metric with no preferred direction moved. |
| unchanged | No significant change, or a sampled change smaller than `--min-effect`. |
| inconclusive | Fewer than two runs per side, or an interval too wide to call. |

Exact metrics are compared directly and have no p-value; their clock says whether a difference can
be noise at all, and a difference in one is always called by its direction, because there is no test
to run. Sampled metrics go through a Mann-Whitney U test (exact while the samples are few,
the normal approximation with a tie correction above that) plus a percentile bootstrap interval for
the median difference, seeded so the same samples give the same interval. A change is only called
when the p-value is below `--alpha` (0.05 by default) and the relative change is at least
`--min-effect` (1% by default), so a tiny but statistically clean difference is not reported as a
regression.

Metrics and tests that only one side has are listed as added or removed. A test that exists on one
side only runs there.

Outcomes are compared as well, so a change in behaviour is visible even when every metric is the
same: new failures, fixed tests, tests failing on both sides, and tests that only exist on the
baseline or the candidate. A new failure says which phase it failed in. Behaviour never decides the
exit code: budgets do, so a comparison can report a failing test without failing a lane that is
meant to measure.

## Budgets

`--budgets` names a JSON file of thresholds. A metric that moves in the worse direction past its
threshold makes the command exit 1.

```json
{
  "budgets": [
    { "metric": "tick.modsystem.mymod:*", "limit": "+5%" },
    { "metric": "net.channel.mymod.bytes", "limit": "+0%" }
  ]
}
```

```json
{
  "net.channel.mymod.bytes": "+0%",
  "gl.drawcalls.*": "+10%"
}
```

A limit with a percent sign is relative to the baseline value; a bare number is an absolute change
in the metric's own unit. `+0%` allows no worsening at all. Patterns match the whole metric name,
with `*` for any run of characters and `?` for one. The first matching rule applies, and a neutral
metric is never over budget. Metrics without a matching rule are reported but never fail the run.

## Output

The console report is the default. `--md <path>` writes the same table as Markdown, ready for a
pull request comment or `$GITHUB_STEP_SUMMARY`, and `--json <path>` writes a machine-readable
report (schema version 1) with every metric, the behaviour changes and the exit code.

```text
pharos compare tests/MyMod.Tests --baseline origin/main --md compare.md --json compare.json
```

Exit codes: 0 nothing over budget, 1 a metric is over budget, 2 bad input or a run that did not
start.

## A single run on each side

`pharos diff` compares two runs you already have. When both inputs are run manifests
(`pharos-run.json`, or a TRX file with `--before-manifest` and `--after-manifest`), it also prints
the same metric table, as the single-run, no-repeat form of `pharos compare`. Sampled metrics are
inconclusive there, because one value per side cannot carry a test; exact metrics are compared
directly. For significance, use `pharos compare`.

## In CI

```yaml
- name: Compare against main
  run: pharos compare tests/MyMod.Tests --baseline origin/main --repeat 5 --budgets pharos.compare.json --md compare.md
- name: Publish the summary
  if: always()
  run: cat compare.md >> "$GITHUB_STEP_SUMMARY"
```

`--no-build` skips both builds when a lane has already built the tree, and the JSON report records
the exit code so a job can publish a summary and still fail.
