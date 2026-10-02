# Performance reviews

One folder per review, newest last. Each holds a `REPORT.md` (verdict first, evidence after),
`findings.md` (every finding with evidence, root cause, fix and proof), `test-inventory.md` (every
test, what it covers, whether it passed) and `data/` (the measurements the report is made of, with
machine identity scrubbed).

| Review | Scope | Headline |
|---|---|---|
| [2026-10-01](2026-10-01-review/REPORT.md) | Full: Debug + Release suites, live Release app (startup, idle, summon / Settings / spike / activation soak, unreachable share), crash forensics, static scan | Fast (summon p95 20 ms, 0.37% idle) but not stable: 19 findings, 9 fixed (a native crash on Rate on Store, a window leak per Settings open, a 21 s freeze on a dead share, 25% idle CPU in Settings, a launch crash on a `null` config entry), 6 open - chiefly a WinUI title-bar GDI leak that needs a decision. 862 tests, all passing. Fixes are staged, not committed. |

## How a review is run

The reviews are produced by the project skill at
[`.claude/skills/performance-review`](../../.claude/skills/performance-review/SKILL.md). From a
Claude Code session in this repository:

```
/performance-review            # full review, fixes included
/performance-review quick      # static scan + forensics + Debug suite, no live app
/performance-review crash      # crash and hang evidence only
/performance-review --no-fix   # measure and report, change nothing
```

Or run the pieces by hand (PowerShell 7, from the repository root):

```powershell
& .claude/skills/performance-review/scripts/run-suite.ps1 -Label baseline-debug           # build + all tests, isolated
& .claude/skills/performance-review/scripts/build-app.ps1 -Configuration Release          # the app, for live measurement
& .claude/skills/performance-review/scripts/measure-app.ps1 -Phase startup,idle,summon,settings,activation,spike,unreachable
& .claude/skills/performance-review/scripts/crash-forensics.ps1                           # event log + dump identifiers
& .claude/skills/performance-review/scripts/scan-risk-patterns.ps1                        # static crash/hang/leak triage
```

Everything runs in a scratch copy of the working tree under `%TEMP%\swiftx-perf-review`, with its
own data directory, `TEMP` and hotkey: it never touches `%AppData%\SwiftX`, never fights a running
SwiftX or its debugger for file locks, and only ever stops processes it started itself.

## Budgets the reviews judge against

Tray ready < 1 s; Settings visible < 1.5 s warm; orbit summon p95 < 250 ms; **no UI-thread stall
> 250 ms**; idle CPU < 1% of a core; per-100-cycle drift < 1.5 MB private, < 4 handles, < 2 GDI /
USER objects. The reasoning and the statistics rules are in the skill's
[methodology](../../.claude/skills/performance-review/references/methodology.md).
