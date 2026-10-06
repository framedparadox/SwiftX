# Methodology: what to measure, how, and what counts as a finding

Read this before interpreting any number. Every rule below exists because the opposite was tried
on this codebase and produced a wrong conclusion.

## 1. Test types, for a tray-resident desktop app

| Type | Question it answers | SwiftX instrument |
|---|---|---|
| **Smoke** | Does it build, start and survive one cycle? | `run-suite.ps1` (build + 796 tests), `measure-app.ps1 -Phase startup` |
| **Performance** (latency, throughput) | Is one operation fast enough, and does its cost grow with input the way the algorithm says? | `Performance/PerformanceBudgetTests`, scaling-exponent probes, startup / summon latency |
| **Load** (concurrency) | Is shared state correct when several things happen at once? | `Load/ConcurrencyAndEnduranceTests`, `Formats/ToolLoadAndStressTests`, `Services/SecureValuesAndCertStressTests` |
| **Stress** (beyond limits) | What happens at 10x the realistic input, with hostile input, or with a dependency gone? | `Security/BoundedInputTests`, `Resilience/CrashAndHangRegressionTests`, `measure-app.ps1 -Phase unreachable` |
| **Soak / endurance** | Does anything grow, per cycle, without bound? | `measure-app.ps1 -Phase summon / settings`, heap-retention tests |
| **Spike** | Does a burst (rapid summon/dismiss) break ordering or lose state? | summon phase (250 ms cadence), hotkey toggle races |
| **Recovery / fault injection** | After a failure, is the app usable and is the data intact? | unreachable share, locked files, torn writes, malformed config |

A desktop app has no request rate. Its load axes are **many at once** (tool windows + background
icon loads), **many in a row** (the orbit is summoned hundreds of times a day and restarted
rarely), and **big** (a pasted multi-megabyte document). Pick cycle counts that make a per-cycle
leak visible, not request counts that mean nothing here.

## 2. Budgets (defaults; override per project and write the override in the report)

| Metric | Budget | Notes |
|---|---|---|
| Tray ready (hotkey works) | warm < 1 s | process start to message window |
| Settings window visible | warm < 1.5 s, cold < 3 s | first run after a build is cold |
| Orbit summon -> visible | p95 < 250 ms | `measure-app.ps1` summonMs |
| **UI-thread stall** | **no single stall > 250 ms** | WM_NULL ping; Windows shows "Not Responding" at ~5 s |
| Idle CPU, tray only | < 1% of one core | hot corner polls at 20 Hz; measure its cost separately |
| Idle CPU, Settings open | < 1% of one core | animated surfaces show up only here |
| Private bytes drift | < 1.5 MB per 100 cycles | after 20% warm-up |
| Handle drift | < 4 per 100 cycles | |
| GDI / USER object drift | < 2 per 100 cycles | the per-process limit is 10 000: crossing it makes window creation fail |
| Thread drift | < 2 per 100 cycles | |
| Managed heap after N cycles | growth < 64 MB (tests) | coarse leak detector, not an allocation budget |
| Heavy payload (6 MB JSON) | every operation < 10 s, interactive ones < 1 s | budgets are 10x measured: they catch catastrophes, not drift |
| Crashes / hangs | zero | any is Critical until explained |

## 3. Statistics rules

1. **Minimum of N, never the mean.** Noise (a scheduler preemption, a GC pause) only ever makes a
   sample slower, so the minimum is the best estimate of true cost. Use N >= 5.
2. **Warm up with a fixed pass count, equal for every size.** Tier-0 JIT code, first-touch
   allocation and cold caches belong to the process, not the operation. A warm-up *time budget*
   gives the small size many more passes than the large one and biases the ratio.
3. **Measure one size at a time and collect between them.** Holding two payloads alive bills one
   size for collecting the other.
4. **Scaling is the real assertion; absolute budgets are only a net.** Time the same operation at
   sizes N and 8N. Linear costs x8, quadratic x64. Report the **empirical exponent**
   `log(t2/t1) / log(n2/n1)`; flag > 1.3 for anything that should be linear. A single ratio against
   a baseline under ~1 ms is timer granularity, not data.
5. **Drift is a slope, not a difference.** Fit least squares of each counter against the cycle number
   after discarding the first 20% as warm-up; also compare early and late medians so the GC's
   sawtooth cannot fake or hide a trend. A leak is monotonic. State both numbers. A "GROWING" flag
   built from a handful of samples is a lead, not a finding: in this review a 60-pair run flagged
   +36 MB private bytes, and a 150-pair A/B of the original and fixed builds drew the same sawtooth
   (117 -> 153 -> 121 -> 126 MB) because the first run ended on the peak. Re-run longer and against
   the original build before believing it, and list the raw samples beside the slope.
6. **One run proves nothing.** A flaky result is re-run in isolation at least three times and the
   spread is reported. Reproduce before you believe; reproduce again after the fix.
7. **Quiet machine.** The timing tests fail falsely if anything heavy runs alongside (a build, a second
   `dotnet test`, a browser tab loading). `run-suite.ps1` gates on total CPU and records the
   reading; when the gate fails, say so in the report and treat borderline ratios as inconclusive.
8. **Debug is not what ships.** Unoptimised code distorts exactly the hot loops being measured.
   Calibrate and gate perf tests in whatever configuration CI uses, but take startup, memory,
   idle-CPU and drift numbers from a **Release** build only.
9. **Record the conditions with the numbers**: CPU model, cores, RAM, power plan, battery, commit,
   dirty file count, whether the user's own SwiftX was running. Numbers without conditions cannot
   be compared next month.

## 4. Interpreting a failed or borderline timing test

1. Re-run just that test class on an idle machine, three times, with the console logger at
   `detailed` (the tests print their millisecond numbers and ratios).
2. Compare to the previous report's numbers (in `docs/performance/`), not to the budget.
3. Same ratio within the earlier spread -> noise or contention; say so, do not "fix" the product.
4. Ratio consistently higher than every earlier sample -> real regression: bisect with `git log` on
   the code path, add a larger-size or exponent probe, fix the algorithm. Raising a ceiling is the
   last resort and needs a measured justification in the test's comment.

## 5. Severity rubric

| Severity | Meaning | Examples |
|---|---|---|
| **Critical** | Process dies or data is lost for a normal user action | native fail-fast, access violation, store overwritten |
| **High** | Freeze > 5 s, unbounded leak, or a crash on an uncommon-but-plausible path | UI thread blocked on a dead network share; per-open window leak |
| **Medium** | Noticeable slowness or waste that does not break anything | idle CPU above budget, quadratic path on capped input |
| **Low** | Hygiene, hazard without a demonstrated trigger, flaky-test risk | diagnostics clobbered by tests, missing decode size |
| **Info** | Observation worth recording | cold-start number, accepted trade-off |

Every finding records: **evidence** (a number, a dump identifier, a log line, a file:line),
**root cause** (the mechanism, not the symptom), **fix**, **proof** (the test or measurement that
flipped), **status** (Fixed / Mitigated / Accepted / Deferred, with a reason for the last two).

## 6. What "fixed" means

A finding is fixed when the measurement that exposed it, run the same way, no longer shows it, **and**
a regression test (or a re-runnable measurement for UI-only behaviour) would fail if it came back.
"I could not reproduce it" is not evidence of a fix: the icon crash here reproduced only under the
user's debugger, never in ~15 scripted launches. Match the dump or the trace instead.

"Would fail if it came back" is checked, not assumed: undo the fix **in the scratch copy**, run the new
test, and watch it fail (the config-shape tests: 14 of 16 failed with the repair removed). Then restore
the file and **touch the repo's copy** (`(Get-Item <file>).LastWriteTime = Get-Date`; contents are
unchanged, git sees nothing). `run-suite.ps1` mirrors with preserved timestamps and overwrites the scratch
copy with the repo's, so a restored file that is older than the mutated build output is skipped by the
incremental build and the stale, mutated DLL is tested - this review lost two full-suite runs to exactly
that, with 14 failures that looked like a regression and were not. A suite that finishes in about a minute
when a build was expected is the tell.
