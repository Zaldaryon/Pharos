# Run reports

`pharos report` reads what a run already wrote and turns it into one page, one short summary and a
timings file. It does not run a test and never changes a result.

```bash
pharos run tests/MyMod.Tests --parallel 4 --report artifacts/report
pharos report pharos-run/pharos.trx
pharos report pharos-run/pharos-run.json --md "$GITHUB_STEP_SUMMARY"
```

## What it reads

- the merged TRX that `pharos run` writes, or the `pharos-run.json` manifest beside it;
- the run manifest, which adds the run id, the worker groups, the attempt phases and the artifact
  folders;
- the failure artifacts, which are the folders holding `screenshot.png`, `server.log`, `client.log`
  and `run.json`.

By default the command looks for `pharos-run.json` and `artifacts` beside the TRX. The options
`--manifest` and `--artifacts` point somewhere else.

## What it writes

- `report.html`: the summary, every failure first with its message, its stack trace, its screenshot
  embedded in the page and the last lines of its logs, the slowest tests, every test and the skipped
  ones. It opens on its own, without a server and without the artifacts beside it.
- `report.md`: the same summary and failure table, shortened for `$GITHUB_STEP_SUMMARY` and pull
  request comments.
- `timings.json`: the outcome and duration of each test, and the setup, body and teardown attempts
  the manifest recorded, for balancing shards later.

The command prints where the three files went. A report of a failing run exits 0, because writing a
report is not a test; a missing or unreadable run exits 2.

## Limits

- The input is a merged TRX or a `pharos-run.json`; the JSON lines that `pharos run --json` prints
  to standard output are not read yet.
- A screenshot is embedded only when it is at most 4 MB. Larger ones stay linked from the page.
- A run that wrote no manifest still gets a report, with the TRX durations and without the attempt
  phases and artifact folders.
