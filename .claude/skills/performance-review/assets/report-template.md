# Performance review - <yyyy-MM-dd>

<!-- One page of verdict first; everything after it is evidence. Write numbers with units and the
     conditions they were taken under. Delete this comment. -->

## 1. Verdict

- **Scope:** full | quick | perf | load | crash | live
- **Build:** commit `<sha>` on `<branch>`, <n> uncommitted paths (measured), Debug for the suite and Release for the live app
- **Headline:** <one sentence: is it fast and stable?>
- **Findings:** <n> total - <c> Critical, <h> High, <m> Medium, <l> Low; <fixed> fixed, <open> open
- **Crashes seen on this machine:** <n> events in <b> buckets, last <date>; <explained / unexplained>
- **Not covered:** see section 8

## 2. Conditions

| | |
|---|---|
| Machine | <cpu>, <cores> logical cores, <ram> GB |
| OS / SDK | <os>, .NET SDK <ver> |
| Power | plan `<plan>`, on battery: <yes/no> |
| Quiet gate | CPU <n>% before the suite (limit <n>%), quiet: <yes/no> |
| User's SwiftX running | <PIDs or no> |

## 3. Test suite

| Run | Config | Tests | Passed | Failed | Wall time |
|---|---|---:|---:|---:|---|
| baseline | Debug | | | | |
| perf + load | Release | | | | |

Failures and how each was resolved: <list or "none">.

## 4. Performance

### 4.1 Absolute budgets (heavy payloads)

| Operation | Payload | Time | Budget |
|---|---|---:|---:|

### 4.2 Scaling (cost vs input size)

| Operation | Small | Large | Time ratio | Size ratio | Exponent | Verdict |
|---|---|---|---:|---:|---:|---|

Borderline results and what the isolated re-runs showed: <...>

## 5. Load, stress and endurance

### 5.1 Concurrency and endurance tests
<table or bullets: what ran, result, notes>

### 5.2 Live app (Release build, isolated instance)

| Measure | Result | Budget | Verdict |
|---|---:|---:|---|
| Tray ready (warm, median) | | < 1 s | |
| Settings window visible (cold / warm median) | | < 3 s / < 1.5 s | |
| Idle CPU, tray only | | < 1% of a core | |
| Idle CPU, Settings open | | < 1% of a core | |
| Hot corner poll cost | | informational | |
| Orbit summon -> visible (p50 / p95) | | p95 < 250 ms | |
| Longest UI-thread stall, summon soak | | < 250 ms | |
| Longest UI-thread stall, Settings soak | | < 250 ms | |
| Longest UI-thread stall, unreachable share | | < 250 ms | |

Drift per 100 cycles (after warm-up):

| Phase | Private MB | Handles | GDI | USER | Threads |
|---|---:|---:|---:|---:|---:|
| summon x<n> | | | | | |
| settings x<n> | | | | | |

## 6. Crash and hang analysis

| Faulting module | Code | Offset | Count | First | Last | Versions | Explanation | Status |
|---|---|---|---:|---|---|---|---|---|

Dump evidence (code identifiers only): <...>

Static scan: <n> hits, <u> unguarded; confirmed <c>, false positive <f>, accepted <a>.

## 7. Findings and fixes

Summary table (details in `findings.md`):

| ID | Severity | Title | Status | Proof |
|---|---|---|---|---|

### Before / after

| Metric | Before | After |
|---|---:|---:|

## 8. Limits of this review

What was not measured and why (architectures, DPI / multi-monitor, packaged build, soak longer than
the cycle count used, network conditions, antivirus effects).

## 9. Reproduce

```powershell
& .claude/skills/performance-review/scripts/run-suite.ps1 -Label baseline-debug
& .claude/skills/performance-review/scripts/build-app.ps1 -Configuration Release
& .claude/skills/performance-review/scripts/measure-app.ps1 -Phase startup,idle,summon,settings,unreachable
& .claude/skills/performance-review/scripts/crash-forensics.ps1
& .claude/skills/performance-review/scripts/scan-risk-patterns.ps1
```

## 10. Follow-ups
