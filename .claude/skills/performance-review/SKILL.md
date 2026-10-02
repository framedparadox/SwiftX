---
name: performance-review
description: Run a full performance, load and crash review of the SwiftX desktop app. Executes the isolated test suite, measures the real Release app (startup, idle CPU, UI-thread stalls, per-cycle leak drift under summon and Settings load, unreachable-network stalls), hunts crash and hang causes from WER dumps, the event log and a static risk scan, writes dated reports to docs/performance, then fixes every confirmed issue with a regression test and re-measures. Use for a performance review, performance or load test, soak or leak hunt, crash or freeze investigation, or "is the app fast and stable".
argument-hint: "[full | quick | perf | load | crash | live | report] [--no-fix]"
compatibility: Windows 10/11, PowerShell 7+, the .NET SDK the repo targets, run from inside the SwiftX repository. Live phases open and close real windows on the desktop.
metadata:
  version: "1.0"
---

# Performance review

Arguments: `$ARGUMENTS` (empty means `full`).

A review is **measure, find, fix, re-measure, document**, in that order, and a claim without a number
or a dump identifier behind it does not go in the report. The methodology, budgets and severity
rubric are in [references/methodology.md](references/methodology.md) - read it before judging any
result. Crash and hang technique is in [references/crash-hunting.md](references/crash-hunting.md);
what is specific to SwiftX (entry points, hard rules, test inventory, crash history) is in
[references/swiftx-profile.md](references/swiftx-profile.md).

| Scope | Runs | Fixes |
|---|---|---|
| `full` | phases 0-7 | yes |
| `quick` | 0, 1 (Debug suite only), 2, 7 - no live app | trivial only |
| `perf` | 0, 1 incl. Release perf tests + scaling probes, 7 | yes |
| `load` | 0, load tests, live `summon` + `settings` soak, 7 | yes |
| `crash` | 0, 2, live `unreachable`, 7 | yes |
| `live` | 0, 3, 7 | yes |
| `report` | 7 only, from the latest data | no |

`--no-fix` stops after phase 4 and reports. Otherwise phase 5 is part of the job: the review is not
done while a confirmed issue is open.

## Ground rules (these override convenience)

1. **Never touch the user's real data.** Tests run only through `run-suite.ps1`, which refuses unless
   `tests/SwiftX.Tests/TestEnvironment.cs` still redirects `SWIFTX_DATA_DIR`. A live instance runs
   only through `measure-app.ps1`, which gives it its own data dir, its own `TEMP` and its own hotkey.
2. **Never kill or lock the user's SwiftX.** They usually have it running under a debugger. Build
   and test in the scratch workspace (the scripts do). Scripts stop only processes they started.
   Attaching read-only to the user's process (`-Phase attach -AttachPid`) is allowed; driving it is not.
3. **One heavy thing at a time.** The scaling tests fail falsely beside a build. Do not run builds,
   a second `dotnet test`, or live phases concurrently with timing work. Light reading and writing
   are fine.
4. **Announce live phases first.** They open and close windows and the orbit takes foreground for a
   few minutes. They inject no keyboard or mouse input, but say so in one line before starting.
5. **Debug is not what ships.** Startup, memory, idle CPU and drift come from a Release build only.
6. **Evidence or it did not happen.** Every finding carries a measurement, a dump identifier, a log
   line or a file:line. "Could not reproduce" is never evidence of a fix (the icon crash reproduced
   only under the user's debugger). **A suspected cause is a hypothesis until a measurement flips.**
   Reading the code and finding a plausible leak is how a "fix" ships that changes nothing: in this
   repo an unsubscribed event handler and a never-stopped animation both looked like the cause of a
   leak, were fixed, and moved no number; the real cause was elsewhere. Fix, re-measure, and report
   what did *and did not* move.
7. **Dumps are memory.** Extract code identifiers only; never print or copy other strings.
8. **Edit hazards in this repo:** Edit tool, not `sed -i` (rewrites CRLF); Write tool for anything
   containing backslashes; check any new special character in a `.cs` comment with `od -c`; build
   private-use glyphs from code points. Do not commit or push unless asked; leave the work in the
   tree and say so.

Scripts live in `${CLAUDE_SKILL_DIR}/scripts`; every one prints a compact summary and writes full
data to disk, so read the files, not the console, for detail. Raw logs and the scratch workspace
go to `%TEMP%\swiftx-perf-review` (`-WorkRoot` overrides); only summarised data goes into the repo.

## Phase 0 - Preflight

1. Say what you are about to do. Read the three reference files.
2. `git status`, branch and short SHA; note uncommitted paths (they are measured, so they count).
3. Is the user's SwiftX running (`Get-Process SwiftX`)? Note the PID; do not touch it.
4. Create the report folder `docs/performance/<yyyy-MM-dd>-review/` with a `data/` subfolder.
   If a previous review exists in `docs/performance/`, read its `REPORT.md`: it is your baseline.

## Phase 1 - Test suite, performance and load

```powershell
& "${CLAUDE_SKILL_DIR}/scripts/run-suite.ps1" -Label baseline-debug                       # everything, ~3 min
& "${CLAUDE_SKILL_DIR}/scripts/run-suite.ps1" -Label perf-release -Configuration Release `
    -Filter "FullyQualifiedName~Performance|FullyQualifiedName~Load|FullyQualifiedName~ToolLoadAndStress|FullyQualifiedName~StressTests"
```

Each run writes `summary.json` / `summary.md` (totals, failures with messages, slowest tests, and
every millisecond figure the tests print). Copy the summaries into `data/`.

- A failure is a finding, not an annoyance. A **timing** failure or a ratio within ~25% of its
  ceiling: re-run that class alone three times (methodology section 4) before deciding.
- Compare the numbers with the previous review. Same spread = noise; consistently worse = regression.
- Compute the empirical scaling exponent for any operation that should be linear; report > 1.3.

## Phase 2 - Crash forensics and static scan (both cheap, run first)

```powershell
& "${CLAUDE_SKILL_DIR}/scripts/crash-forensics.ps1" -OutDir docs/performance/<date>-review/data
& "${CLAUDE_SKILL_DIR}/scripts/scan-risk-patterns.ps1" -OutDir docs/performance/<date>-review/data
```

Forensics buckets the crash history by (module, code, offset) and string-scans the newest dumps for
identifiers. Every bucket needs an explanation: *fixed since (date, how)*, *open*, or *unknown*.
The scan is triage: read every unguarded hit in context and class it **confirmed / false positive /
accepted**, and record the count of each. A rule that is wrong gets fixed in the script.

## Phase 3 - Live app

```powershell
& "${CLAUDE_SKILL_DIR}/scripts/build-app.ps1" -Configuration Release        # ~30 s, scratch workspace
& "${CLAUDE_SKILL_DIR}/scripts/measure-app.ps1" -Phase startup,idle,summon,spike,settings,activation,unreachable `
    -StartupRuns 5 -SummonCycles 150 -SettingsCycles 40 -ActivationPairs 150
```

Phases: `startup` (tray-ready and Settings-visible times, settle time, footprint), `idle` (CPU of
one core: tray only, hot corner off, Settings open; `-IdleVariants` picks), `summon` (orbit
summon/dismiss cycles), `spike` (300 irregular toggles 5-60 ms apart, then it must still work),
`settings` (Settings open/close cycles), `activation` (summon/dismiss the orbit over an open Settings
window: Activated/Deactivated churn), `unreachable` (UI-thread stall with unroutable network targets
on the orbit; `-NoUnreachableItems` is the control that separates cold-start cost from the network),
`attach` (read-only sampling of an existing PID). While cycling, a pinger
records every UI-thread stall and counters are fit for **drift per 100 cycles**. The script prints
a `FINDINGS:` list against the budgets in methodology section 2; `live-app.json` holds everything.
`-OmitWebLink` takes the favicon path out of a soak; `-KeepRunning` leaves the last instance alive
for heap tooling (it is yours to stop afterwards).

If a phase reports a crash, a failed cycle or a stall, that is a finding: collect the instance's
`swiftx.log` (in the run folder's `tmp`) and any new dump in `%LOCALAPPDATA%\CrashDumps`.

**When drift says something grows, go to [references/leak-hunting.md](references/leak-hunting.md)**:
heap dump after a forced GC to count live objects, SOS `gcroot`, window-HWND enumeration, bisecting
with switches in the scratch copy, and the throwaway window-lifetime probe in
`assets/window-lifetime-probe/` that isolates a framework-level cause in seconds.

## Phase 4 - Triage

Write `findings.md` from [assets/findings-template.md](assets/findings-template.md): one entry per
finding with severity (rubric in methodology section 5), evidence, root cause (mechanism, not
symptom), planned fix and a proof plan. Merge duplicates. Order by severity. Confirm each suspected
root cause by reading the code path **and** by a measurement or dump; if only one is available, say
"suspected". Then verify every suspicion that is cheap to verify before fixing anything.

## Phase 5 - Fix every confirmed issue

For each finding, highest severity first:

1. **Prove it fails first.** Add a regression test that fails today (a pure-logic seam if the code is
   UI-bound: extract the decision, test the decision), or write down the live measurement that will
   flip. No fix without a way to see it work.
2. Smallest correct change at the root cause. Match the surrounding code's idiom and comment density;
   comment only the non-obvious *why*. No drive-by refactors.
3. Run the affected tests (`run-suite.ps1 -Filter ...`), then the live phase that found it.
4. Update the finding: status, proof (test name, before -> after numbers).

Things not worth "fixing": accepted trade-offs with a documented reason, and noise. Record them as
*Accepted* with the reason; do not leave them silent.

## Phase 6 - Re-verify

Full Debug suite once more (alone on the machine), the Release perf/load tests, Release rebuild, and
the live phases again with the same parameters. Re-measure any claim that straddles a change in the
machine (an OS update, a different quiet-gate reading) as an **A/B pair back to back**, and run a
**mutation check** for each regression test (undo the fix in the scratch copy, watch it fail; see
methodology section 6 for the timestamp trap). A "GROWING" flag from few samples is a lead: lengthen
the run and A/B it against the original build. Build the **before / after table**. Anything not improved or newly worse is
reported as such. Check `git diff --stat` for accidental line-ending rewrites.

## Phase 7 - Document

Fill [assets/report-template.md](assets/report-template.md) into
`docs/performance/<date>-review/REPORT.md`, keep `findings.md` and `data/` beside it, and add a row
to `docs/performance/README.md`. Update the crash-history table in
`references/swiftx-profile.md`. State limits honestly (architectures, DPI, multi-monitor, packaged
build, soak length not covered). If something durable was learned that is not derivable from the
repo, save it to memory.

**Final message:** the verdict in a few lines, the findings table (severity, status, proof), what
changed in the tree (uncommitted), what the user must do themselves (e.g. confirm a crash fix on
their debugger setup), and the one command to re-run the review.

## Files

- `scripts/run-suite.ps1` isolated build + test + TRX summary, data-dir guard, quiet-machine gate
- `scripts/build-app.ps1` scratch-workspace app build (Release by default)
- `scripts/measure-app.ps1` + `Win32Probe.cs` live driver, UI-stall pinger, drift analysis
- `scripts/crash-forensics.ps1` event log + dump identifier scan + app log
- `scripts/scan-risk-patterns.ps1` static crash / hang / leak triage
- `scripts/test-inventory.ps1` every test, what it covers, whether it passed (from a run's `tests.csv`)
- `scripts/publish-data.ps1` copies measurement files into the report's `data/` with the machine's identity scrubbed
- `scripts/_common.ps1` shared helpers
- `references/` methodology, crash hunting, leak hunting, SwiftX profile
- `assets/` report and finding templates, and `window-lifetime-probe/` (throwaway WinUI 3 app: open
  and close windows of a chosen variant, report how many survive a full GC)
