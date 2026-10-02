# Performance review - 2026-10-01

## 1. Verdict

- **Scope:** full (static scan, crash forensics, Debug and Release test runs, live Release app: start-up, idle, summon soak, burst, Settings soak, activation soak, unreachable share)
- **Build:** `852cf87` on `develop`; the review's work is 66 files, staged and **not committed** (the harness counted 55 dirty paths when the final suite ran, before the report files existed); Debug for the full suite, Release for the perf/load tests and for every live number
- **Headline:** The app is fast (orbit summon p95 20 ms, 0.37% of a core idle in the tray) but it was not stable: it had a native crash on a button, leaked a whole window per Settings open, froze for 21 s on a dead network share, and burned 19-26% of a core with Settings open and idle. All of that is fixed and proven except a framework-level title-bar leak (~26 GDI objects and ~34 handles per window opened) that cannot be fixed from application code and needs a decision (PR-003).
- **Findings:** 19 total - 1 Critical, 3 High, 4 Medium, 6 Low, 5 Info; **9 fixed**, 6 open (1 High, 1 Medium, 3 Low, 1 Info), 2 accepted, 2 documented / watch
- **Crashes seen on this machine:** 25 events in 7 buckets over 90 days, last 2026-09-22; the newest is explained and fixed (PR-001), the 17 `0xc000027b` events are from a class fixed on 2026-09-19 and have not recurred; one signature (`coreclr.dll`, a single event) is unexplained
- **Not covered:** see section 8

What you need to do: review and commit the staged changes; decide PR-003; set the real Store product id (PR-014) and press **Rate on Store** once in the portable build and once in the Store build (a native crash cannot be asserted in a test, so that is the one fix proven by the guard rather than by running it).

## 2. Conditions

| | |
|---|---|
| Machine | 13th Gen Intel Core i5-1334U, 12 logical cores, 15.7 GB |
| OS / SDK | Windows 11 Home 10.0.26300 (it was 10.0.26200 at the start of the session: an OS update landed mid-review), .NET SDK 10.0.301 |
| Power | plan `Balanced`, not on battery |
| Quiet gate | CPU 24% before the baseline suite (limit 25%); 16.0% before the final Debug run; 16.7% before the Release run |
| User's SwiftX running | no, at every measurement (the harness never touches `%AppData%\SwiftX` and runs every instance with its own data directory, TEMP and hotkey regardless) |

## 3. Test suite

| Run | Config | Tests | Passed | Failed | Wall time |
|---|---|---:|---:|---:|---|
| baseline | Debug | 796 | 796 | 0 | 3 m 00 s (cold build + tests) |
| after fixes | Debug | 862 | 862 | 0 | 56.9 s (incremental build + tests) |
| perf + load | Release | 30 | 30 | 0 | 54.3 s |

66 tests were added (every new test is described in `test-inventory.md`): allocation budgets 7, config shape 16,
event-subscription lifetime 5, native capability 2, network path hang 19, log location 4, favicon back-off 6,
home idle policy 7. For the fixes with a causal claim the test was shown to **fail without the fix**: the
config-shape tests (14 of 16 fail with the repair removed).

Failures during the review and how each was resolved: two full-suite runs reported 14 failures in
`ConfigShapeResilienceTests`. Both were an artifact of the review's own mutation check: the mutated scratch
build output was newer than the restored source, so the incremental build tested the stale DLL. Nothing was
wrong with the product or the tests; the lesson is recorded in `methodology.md` section 6. No other failure
occurred.

## 4. Performance

### 4.1 Absolute budgets (heavy payloads), Release

| Operation | Payload | Time | Budget |
|---|---|---:|---:|
| json validate / format / minify | 6.1 M chars | 12.9 / 48.0 / 41.5 ms | 10 000 ms |
| json sort keys / canonical round trip | 6.1 M chars | 67.1 / 169.1 ms | 10 000 / 15 000 ms |
| xml validate / format / minify | 3.7 M chars | 7.3 / 69.4 / 59.6 ms | 10 000 / 15 000 ms |
| xml canonical | 3.7 M chars | 125.9 ms | 15 000 ms |
| base64 encode / decode | multi-MB | 4.4 / 14.5 ms | 5 000 ms |
| tokenizers + folds + line index | 1.2 M chars | 0.5-3.3 ms | 2 000-5 000 ms |
| sanitize | 0.7 M chars | 1.4 ms | 5 000 ms |

Every operation is two to three orders of magnitude inside its budget: the budgets catch catastrophes, not drift.

### 4.2 Scaling (cost vs input size), Release, input x8

| Operation | Small | Large | Time ratio | Exponent | Verdict |
|---|---:|---:|---:|---:|---|
| json validate | 2.2 ms | 19.2 ms | x8.8 | 1.04 | linear |
| json format | 8.4 ms | 76.1 ms | x9.0 | 1.06 | linear |
| xml validate | 1.6 ms | 11.1 ms | x6.9 | 0.93 | linear |
| xml format | 7.8 ms | 105.1 ms | x13.5 | 1.25 | linear (under the 1.3 flag) |
| xml canonical | 11.8 ms | 196.0 ms | x16.6 | 1.35 | borderline (over the 1.3 flag, far under the x32 ceiling); x6.9-x16.6 across five runs of the same code, so noise, not a regression |
| tokenize / folds / line index | 0.4-4.2 ms | 3.8-29.0 ms | x3.8-x10.0 | 0.64-1.1 | linear |

Borderline results and what the isolated re-runs showed: the **first** Debug baseline read `xml validate` x27.7 and
`xml format` x30.1 against a ceiling of x32 (CPU gate at 24%, the OS updating). Run back to back with the
original code the same minute, `xml validate` was x25.1 (a real quadratic-leaning cost: it built a document tree
to check well-formedness) and is now x8.3; `xml format` was x16.2 and is x14.2 with identical code, which is the
spread to expect. See `data/after/xml-validate-pair.md`. The suite is sensitive to machine state (untouched
operations ran up to 4x faster in the later runs): compare ratios and allocations, not milliseconds, across sessions.

## 5. Load, stress and endurance

### 5.1 Concurrency and endurance tests

| What ran | Result |
|---|---|
| Every tool under simultaneous load, 24 threads | completed without faulting, 109 ms |
| Format singletons, store save/load under contention, torn-write detection, atomic publish | pass |
| 5 000-item configuration save + load | 248 ms, round-trips intact |
| 300 full formatter cycles | 885 ms, heap 4 MB -> 4 MB |
| 3 000 codec / sanitizer cycles | heap growth 0 MB |
| Repeated format cycles (documents must not be retained) | heap 5 MB -> 8 MB (growth 3 MB, inside its bound) |
| Export / import cycle repeated | stable |
| Config with `null` entries in lists (new) | 16 shapes survive the launch migrations |

### 5.2 Live app (Release build, isolated instance, window messages only - no input injected)

| Measure | Result | Budget | Verdict |
|---|---:|---:|---|
| Tray ready (5 runs, median) | 609 ms (497-968) | < 1 s | pass |
| Settings window visible (5 runs, median) | 1 296 ms (1 080-1 701) | < 1.5 s warm | pass (the first run is cold: 1 701 ms) |
| Idle CPU, tray only | 0.371% of one core | < 1% | pass |
| Idle CPU, Settings open | 0.479% | < 1% | pass (was 25.6%) |
| Hot corner poll cost | 0.371% (0% with it off) | informational | accepted (PR-012) |
| Orbit summon -> visible (p50 / p95 / max) | 15.8 / 19.7 / 30.7 ms | p95 < 250 ms | pass |
| Longest UI-thread stall, summon soak (150 cycles) | none over 250 ms (longest ping 216 ms) | < 250 ms | pass |
| Longest UI-thread stall, Settings soak (40 opens) | 482 ms, 29 stalls | < 250 ms | **fail, open** (PR-010) |
| Longest UI-thread stall, unreachable share | 0 ms in 4 of 5 runs; 316 ms once (cold first summon, also seen with no network item) | < 250 ms | pass (was 21 261 ms) |
| Burst: 300 summon toggles in 12.2 s | 1 stall of 318.9 ms; recovers, no crash | informational | accepted (PR-019) |

Drift per 100 cycles (after warm-up; budgets 1.5 MB private, 4 handles, 2 GDI, 2 USER):

| Phase | Private MB | Handles | GDI | USER | Threads |
|---|---:|---:|---:|---:|---:|
| summon x150 | -1.4 (flat) | +92.7 (**over**, ~0.9 per summon, PR-011) | 0 | -0.1 | -9.7 |
| settings x40 | +70.1 (0.70 per open) | +3 359 (34 per open) | +2 581 (26 per open) | +0.3 | -4.2 |
| activation x150 (original vs fixed, two runs each) | sawtooth 117-154-126 MB, identical in both builds | settles 1 500-1 550 in both | flat | flat | falling |

The settings row is the framework title-bar cost (PR-003), not application memory: private bytes per open fell
from +7.1 MB to +0.7 MB, and a heap dump after 40 opens holds one live `ShellWindow` (was 15 of 15).

## 6. Crash and hang analysis

| Faulting module | Code | Offset | Count | First | Last | Versions | Explanation | Status |
|---|---|---|---:|---|---|---|---|---|
| `Windows.ApplicationModel.Store.dll` | `0xc0000005` | `0xb6902` | 2 | 09-15 | 09-22 | 1.0.1.0, 2.0.0.0 | `StoreContext` called without package identity from "Rate on Store" | **fixed** (PR-001), confirm by pressing it |
| `Microsoft.UI.Xaml.dll` | `0xc000027b` | `0x3a9c5d` | 11 | 09-13 | 09-19 | 1.0.1.0 | managed `[ComImport]` `IImageList` through WinRT (`IconService.TryGetJumboIcon`) | fixed 09-19 (raw vtable call), none since |
| `CoreMessagingXP.dll` | `0xc000027b` | `0x97de6` | 1 | 09-17 | 09-17 | 1.0.1.0 | same stowed-exception class | fixed 09-19, none since |
| `Microsoft.UI.Xaml.dll` | `0xc000027b` | `0x3a6d95` | 5 | 08-08 | 08-12 | 1.0.0.0 | same signature class on the first build; the project's history attributes it to the same icon interop, not independently verified here | none since |
| `KERNELBASE.dll` | `0xc000041d` / `0xe0434352` | `0xc187a` | 3 + 2 | 08-08 | 08-08 | 1.0.0.0 | an unhandled exception in a callback / a CLR exception, on the first build only; not investigated further | none since |
| `coreclr.dll` | `0xc0000005` | `0x356fef` | 1 | 08-30 | 08-30 | 1.0.0.0 | **unexplained**: a single event on the first build; only the three newest dumps were scanned, none of them is this one; no recurrence in a month | open, watch |

Dump evidence (code identifiers only; a dump holds whatever the app held, and SwiftX holds secrets, so nothing
but type and method names was extracted): the 09-22 dump contains `ShellWindow.RateOnStore_Click`,
`StoreFeedback.RequestRatingAsync` and its state machine beside `Windows.ApplicationModel.Store`; the 09-19 dump
contains `IconService.TryGetJumboIcon`, `SHGetImageList` and `InterfaceMarshaler.ConvertToManaged`. A crash is
confirmed fixed only when the measurement that exposed it no longer shows it and a test would fail if it came
back; the `0xc000027b` crash reproduced only under a debugger and never in ~15 scripted launches, so
"none since 09-19" is consistent with the fix, not proof of it.

Crashes found by this review beyond the history: a **null entry in `orbit.json`** killed the app on every
launch (PR-016, fixed); and **no crash, hang or dump occurred in any live phase** (`crashed False` in every phase;
no new dumps).

Static scan: 63 hits, 36 unguarded; every one read - 5 false positives, 30 accepted with a reason, 1 hazard
(PR-018), 0 new defects (`data/risk-scan-triage.md`).

## 7. Findings and fixes

Summary table (details, evidence and proof in `findings.md`):

| ID | Severity | Title | Status | Proof |
|---|---|---|---|---|
| PR-001 | Critical | "Rate on Store" crashes the portable build (native AV) | Fixed | dump identifiers; `NativeCapabilityGuardTests` |
| PR-002 | High | Closed windows never collected | Fixed | heap dump 15/15 -> 1 live; soak +7.1 -> +0.7 MB/open; `EventSubscriptionLifetimeTests` |
| PR-003 | High | Custom title bar leaks ~26 GDI / ~34 handles per window | **Open (framework)** | probe, SDK 2.4.0 and 2.5.1 |
| PR-004 | High | Unreachable share froze the UI 21 s per summon | Fixed | 21 261 ms -> 0 ms; `NetworkPathHangTests` |
| PR-005 | Medium | Settings burned 19-26% of a core idle | Fixed | 25.6% -> 0.48%; `HomeIdlePolicyTests` |
| PR-006 | Medium | `XmlFormat.Validate` built a document tree | Fixed | 18.2 MB -> 11.5 KB; `AllocationBudgetTests` |
| PR-010 | Medium | Opening Settings hitches the UI 0.3-0.5 s | Open | soak stall list |
| PR-016 | Medium | `null` entries in `orbit.json` stop the app launching | Fixed | live crash log; 14/16 tests fail without fix |
| PR-007 | Low | Tests truncated the real `swiftx.log` | Fixed | `DiagLogLocationTests` |
| PR-008 | Low | Failed favicon fetches retried on every summon; wrong port | Fixed | `FaviconMissCacheTests` |
| PR-009 | Low | `PickModuleIcon` unguarded `async void` | Fixed | source |
| PR-011 | Low | ~1 handle per summon | Open | drift |
| PR-017 | Low | First summon after launch can hitch 0.3-0.45 s | Open | 8 runs, with and without network items |
| PR-018 | Low | Icon decode caps bytes, not pixels | Open (hazard) | static scan |
| PR-012 | Info | Hot corner polls at 20 Hz (0.37% of a core) | Accepted | A/B |
| PR-019 | Info | A 25-per-second summon storm stalls once (285-319 ms) | Accepted | spike phase |
| PR-013 | Info | `AppWindowTitleBar` setters leak in a tight loop (probe only) | Documented | probe; guard reverted |
| PR-014 | Info | Store listing id is a placeholder | Open | `StoreFeedback.ProductId` |
| PR-015 | Info | Debug scaling ratios near their ceilings in the first baseline | Watch | suite summaries |

### Before / after

| Metric | Before | After |
|---|---:|---:|
| Idle CPU, Settings open | 25.6% of a core (18.6% in a control run) | 0.48% |
| Settings open: private MB per open | +7.1 | +0.7 |
| Settings open: handles per open | +49 | +34 |
| Settings open: GDI objects per open | +39 | +26 |
| Live `ShellWindow` objects after N opens + full GC | 15 of 15 | 1 (after 40) |
| Settings soak: longest UI stall (count) | 1 064 ms (48) | 482 ms (29) |
| Settings visible / interactive, p50 | 280 / 761 ms | 238 / 611 ms |
| Summon with an unreachable share on the orbit: UI freeze | 21 261 ms | 0 ms (4 of 5 runs) |
| "Rate on Store" in the portable build | native crash (2 events) | guarded |
| `orbit.json` containing `[null]` | process exits within 1 s, every launch | starts |
| `Validate` XML allocation, 3.7 MB | 18.2 MB | 11.5 KB |
| XML validate growth for an x8 input (same minute, Debug) | x25.1 | x8.3 |
| Favicon requests per summon for an unreachable host | 2, every summon | 0 inside a 30 s-10 min back-off |
| Test run clobbers the user's `swiftx.log` | yes | no |
| Tests | 796 | 862, all passing |
| Tray idle CPU | 0.63% | 0.37% (within run-to-run noise; no claim) |
| Tray ready / Settings visible (median) | 456 ms (3 runs) / 1 253 ms | 609 ms / 1 296 ms (5 runs; no claim, different OS build) |

## 8. Limits of this review

- **One machine, one session.** An OS update (26200 -> 26300) landed between the baseline and the final runs, so every live number that is claimed as a before/after was either re-measured as a back-to-back A/B in the same minute (activation, unreachable, XML validate) or rests on a mechanism-level proof (heap dump, source rules, allocation counts). Start-up times and the tray idle figure are reported without a claim for that reason.
- **Unpackaged build only.** The MSIX / Store build, packaged `StoreContext`, and the Store's own rating dialog were not run. PR-001 is proven by a guard, not by pressing the button.
- **No input injection, by design.** Pointer and keyboard paths (hover animations, drag and drop, the hot corner actually firing, typing in the editors) were not exercised live; the harness posts window messages only so it can never type into the person's desktop.
- **Windows measured live:** Settings and the orbit. The Add item window, set-up guide and every tool window got the same fix and are covered by the source rule and by the same code shape, but their leak was **not measured** live.
- **Soak lengths are minutes, not days:** 150 summons, 40 Settings opens, 150 activation pairs, 300 burst toggles. The "~380 windows to the GDI quota" figure in PR-003 is arithmetic from a measured per-window cost, not an observed exhaustion.
- **Not covered:** ARM64, multi-monitor / DPI change while open, high-contrast, screen readers, antivirus interference, battery power plan, Windows App SDK versions other than 2.4.0 (2.5.1 in the probe only), network failure shapes other than a silent unroutable address (DNS failure, a disconnected mapped drive, an authenticated share that rejects).
- **The 0xc000027b crash was never reproduced;** the history is evidence from the event log and one dump.
- **Not fuzzed:** the secure-values file and the legacy config upgrader beyond their existing malformed-file tests.

## 9. Reproduce

```powershell
& .claude/skills/performance-review/scripts/run-suite.ps1 -Label baseline-debug
& .claude/skills/performance-review/scripts/run-suite.ps1 -Label perf-load-release -Configuration Release -Filter "FullyQualifiedName~SwiftX.Tests.Performance|FullyQualifiedName~SwiftX.Tests.Load"
& .claude/skills/performance-review/scripts/build-app.ps1 -Configuration Release
& .claude/skills/performance-review/scripts/measure-app.ps1 -Phase startup,idle,summon,spike,settings,activation,unreachable
& .claude/skills/performance-review/scripts/measure-app.ps1 -Phase unreachable -NoUnreachableItems   # the cold-summon control
& .claude/skills/performance-review/scripts/crash-forensics.ps1
& .claude/skills/performance-review/scripts/scan-risk-patterns.ps1
```

Leak probe (no SwiftX code): `.claude/skills/performance-review/assets/window-lifetime-probe/README.md`.

## 10. Follow-ups

In order of value:

1. **Decide PR-003** (the framework title-bar leak). Options, cheapest first: file the upstream issue below; log GDI / USER counts past 5 000 so the failure has a precursor; keep one Settings window alive hidden instead of re-creating it; recycle the process at a quiet moment past ~7 000 GDI objects with no window open. Each is a behaviour change.
2. **Press "Rate on Store"** in the portable and the Store build, and set the real `StoreFeedback.ProductId` (PR-014).
3. **Review and commit.** The changes are staged, not committed: 66 files (14 in `src/`, 10 in `tests/`, 21 in `.claude/skills/performance-review/`, 21 in `docs/performance/`).
4. PR-010 / PR-017: build Settings' heavy screens lazily; realise the orbit once, hidden, shortly after start-up. Each wants its own before / after.
5. PR-018 if image icons from untrusted sources matter to you.
6. Re-run this review after the next release and compare ratios and allocations, not milliseconds.

### Appendix: upstream issue draft (microsoft/microsoft-ui-xaml or WindowsAppSDK)

> **A `Window` that subscribes to its own `Activated` (or `Closed`) event is never collected.**
> Windows App SDK 2.4.0 and 2.5.1, unpackaged, .NET 10, WinUI 3. Create a `Window`, subscribe a method of that
> window (or a lambda capturing it) to its own `Activated` or `Closed`, show it, close it, release every
> reference, `GC.Collect()` twice with `WaitForPendingFinalizers()`: a `WeakReference` to the window is still
> alive (30 of 30 in the attached repro). With no subscription, with a weak-reference delegate target, or with
> every handler removed inside `Closed` (the `Closed` handler included), 0 of 30 survive. Handlers on child
> elements and on `AppWindow` do not matter. `Window` is not a `DependencyObject`, so XAML's reference
> tracking does not cover it (see #9960, #7282, #9063).
>
> **Separately,** `ExtendsContentIntoTitleBar = true` costs ~49 GDI objects and ~57 handles per window,
> whether or not the window is later collected; `SetTitleBar` alone costs nothing; `AppWindowTitleBar`
> setters called back to back leak ~6 GDI objects per application (0 / 1 / 2 / 5 / 20 applications of seven
> setters: 51 / 59 / 77 / 119 / 166 GDI objects). Per-process GDI quota is 10 000, so a tray app that opens
> windows over weeks reaches it. Repro: `assets/window-lifetime-probe` (one small WinUI 3 project, variants
> selectable on the command line).
