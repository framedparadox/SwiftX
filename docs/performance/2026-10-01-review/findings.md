# Findings - 2026-10-01 review

Severity: **Critical** process dies or data is lost on a normal action; **High** freeze > 5 s,
unbounded leak, or a crash on a plausible path; **Medium** noticeable waste, nothing breaks;
**Low** hygiene or a hazard with no demonstrated trigger; **Info** an observation.
Status: Fixed (with proof), Open (with reason), Accepted (with reason).

| ID | Severity | Status | Title | Proof |
|---|---|---|---|---|
| PR-001 | Critical | Fixed | "Rate on Store" crashes the portable build (native access violation) | dump identifiers; `NativeCapabilityGuardTests` |
| PR-002 | High | Fixed | Closed windows were never collected (Settings, set-up guide, Add item, every tool window) | heap dump 15/15 live -> 1; soak +7.1 -> +0.7 MB per open; `EventSubscriptionLifetimeTests` |
| PR-003 | High | **Open (framework)** | Custom title bar leaks ~26 GDI objects and ~34 handles per window created | probe on SDK 2.4.0 and 2.5.1; soak |
| PR-004 | High | Fixed | An orbit item on an unreachable share froze the UI for 21 s on every summon | UI-thread stall 21 261 ms -> see report; `NetworkPathHangTests` |
| PR-005 | Medium | Fixed | Settings window burned 19-26% of a core while open and idle | idle 18.6-25.6% -> 0.48%; `HomeIdlePolicyTests` |
| PR-006 | Medium | Fixed | `XmlFormat.Validate` built a whole document tree to check well-formedness | 18.2 MB -> 11.5 KB allocated, x25 -> x8 growth; `AllocationBudgetTests` |
| PR-007 | Low | Fixed | Test runs and measurement launches truncated the user's real `swiftx.log` | `DiagLogLocationTests` |
| PR-008 | Low | Fixed | Failed favicon fetches retried (2 requests) on every summon; site favicon requested on the wrong port | `FaviconMissCacheTests` |
| PR-009 | Low | Fixed | `PickModuleIcon` was the one `async void` UI handler without the project's guard | source review |
| PR-010 | Medium | Open | Opening Settings blocks the UI thread 0.3-0.6 s (window construction) | soak stall list |
| PR-011 | Low | Open | ~1 handle leaked per orbit summon cycle | summon drift |
| PR-012 | Info | Accepted | Hot corner polls at 20 Hz: 0.37% of a core, 20 wakeups per second | idle A/B |
| PR-013 | Info | Documented | `AppWindowTitleBar` setters leak per call in a tight loop (probe only; not seen in the product) | probe; guard reverted |
| PR-014 | Info | Open | Store listing id is still the placeholder `0000000000000` | `StoreFeedback.ProductId` |
| PR-015 | Info | Watch | Debug-configuration scaling ratios sat within 15% of their ceilings in the first baseline (x27.7 and x30.1 against x32) | suite summary |
| PR-016 | Medium | Fixed | A configuration with `null` entries (`"Items": [null]`) stopped the app starting, on every launch | live crash log; 14 of 16 new tests fail without the fix; launch check |
| PR-017 | Low | Open | The first summon after launch can hitch 0.3-0.45 s (cold start; unrelated to the network) | 5 + 3 runs, with and without unreachable items |
| PR-019 | Info | Accepted | A 300-toggle summon storm (25 per second) stalls the UI thread once, 285-319 ms | spike phase, original and fixed alike |
| PR-018 | Low | Open (hazard) | Icon image decode bounds the bytes (2 MB) but not the pixel dimensions | static scan PF04; not reproduced |

---

### PR-001 - "Rate on Store" crashes the portable build  `Critical` `Fixed`

- **Evidence:** the Application event log holds two native access violations,
  `Windows.ApplicationModel.Store.dll` `0xc0000005` at offset `0xb6902`, on 2026-09-15 (v1.0.1.0) and
  2026-09-22 (v2.0.0.0). A string scan of the newer dump (`crash-forensics.ps1`, code identifiers only)
  recovers `SwiftX.ShellWindow.RateOnStore_Click()`, `SwiftX.Services.StoreFeedback.RequestRatingAsync()`
  and `StoreFeedback+<RequestRatingAsync>d__1.MoveNext()` beside the faulting module.
- **Root cause:** `RequestRatingAsync` called `StoreContext.GetDefault()` / `RequestRateAndReviewAppAsync()`
  unconditionally. `StoreContext` needs package identity, which only the MSIX build has; from the
  unpackaged portable build it faults natively inside the Store DLL. A native access violation is not
  catchable, so the existing `try/catch` around the call could never help.
- **Fix:** `StoreFeedback.InAppRatingSupported` (= `PackagedRuntime.IsPackaged`) is asked first; an
  unpackaged process goes straight to the Store page / web fallback.
- **Proof:** `NativeCapabilityGuardTests` pins that the test host (unpackaged) reports the dialog as
  unsupported and that `StoreContext` is referenced only inside `StoreFeedback.cs`, only after the guard.
  The crash itself cannot be reproduced in a test (it kills the test host).
- **Residual:** confirm on the maintainer's own debugger setup by pressing Rate on Store in the portable
  build. See PR-014: the fallback URL still carries a placeholder product id.

### PR-002 - Closed windows were never collected  `High` `Fixed`

- **Evidence:** a 40-cycle Settings open/close soak on the Release build: private bytes 197 -> 396 MB,
  handles 1838 -> 3212, GDI objects 492 -> 1576 (+7.1 MB, +49 handles, +39 GDI per open), 48 UI stalls up
  to 1.06 s. A heap dump after a forced full GC following 15 opens held **15 live `ShellWindow`** (and 15
  `HotkeyCaptureButton`, 30 `ColorPicker`), with `gcroot` finding no managed root: the reference is
  native. The same behaviour reproduces in a 150-line WinUI 3 app with no SwiftX code (30 of 30 windows
  alive after a full GC; Windows App SDK 2.4.0 and 2.5.1).
- **Root cause:** `Window` is not a `DependencyObject`, so XAML's reference tracking does not cover it.
  A delegate on the window's **own** `Activated` or `Closed` event that targets the window is held
  strongly by the native window, which therefore never dies. Handlers on child elements (all 85 XAML
  `Click=""`s) and on `AppWindow` do not matter. `ShellWindow`, `OnboardingWindow`, `AddItemWindow` and
  `ToolWindowBase` all did it.
- **Why it took four tries:** the first two fixes - unsubscribing the `OrbitManager.ItemsChanged`
  lambda (a real defect) and stopping the Home animations on close (also real) - looked like the cause
  from the code and moved no number. Forcing a GC, nulling `Content` / `SystemBackdrop` / the title bar,
  nulling every generated element field, and skipping icons / marks / backdrop / chrome each moved
  nothing either. All are recorded in `data/leak-investigation.md`.
- **Impact:** ~250 Settings opens exhausts the process's 10 000-object GDI quota (see PR-003 for what
  remains), and every closed window re-ran `RenderPreview` / `RebuildItemsList` on each edit. For tool
  windows it also meant a closed Formatter kept its editors and document text, and a closed Secure
  values window kept whatever the user had revealed, in memory until exit.
- **Fix:** named handlers, every one removed inside the window's `Closed` handler (the `Closed` handler
  itself included), in all four windows; `ItemsChanged` and the static `OrbitMetrics.Changed` likewise;
  a `_closed` guard for dispatcher callbacks already queued.
- **Proof:** heap dump after 40 opens: **1 live `ShellWindow`** (was 15 of 15), 38 k objects / 3.7 MB
  (was 146 k / 11 MB). Soak (final build, 40 opens): +0.7 MB, +34 handles, +26 GDI per open (+1.1 MB in the first post-fix run); was +7.1 / +49 / +39.
  `EventSubscriptionLifetimeTests` fails if any closeable window leaves a handler of its own on its own
  `Activated` / `Closed`, uses a lambda there, or its `Closed` handler fails to remove itself.

### PR-003 - A custom title bar leaks native resources per window  `High` `Open (framework)`

- **Evidence:** in the probe, `ExtendsContentIntoTitleBar = true` costs **~49 GDI objects and ~57
  handles per window, collected or not**; `SetTitleBar` alone costs nothing; the `AppWindow` form costs
  ~32; even `SetBorderAndTitleBar(true, false)` costs ~26. Reverting the title bar in `Closing` or
  `Closed` (hidden first) does not help. Windows App SDK 2.5.1, the newest release, is identical to 2.4.0.
  In SwiftX the residual is ~26 GDI and ~34 handles per window opened.
- **Why it is open:** the leak is created inside the framework when the window is created; no
  application-level call frees it. Every window SwiftX shows uses the custom title bar.
- **Impact:** the 10 000-object GDI quota allows ~380 window creations per process lifetime
  (Settings, Add item, tool windows combined). A tray app that stays up for weeks and opens a few
  tool windows a day will reach it; windows then fail to create, which is a plausible route to the
  `0xc000027b` fail-fast this app has crashed with before.
- **Recommended, in order of value for effort:** (1) report it upstream with the probe
  (`assets/window-lifetime-probe`; draft in the report); (2) recycle the process at a quiet moment when
  GDI objects pass ~7 000 and no window is open (a watchdog checked at window close; relaunch with
  `--startup` and wait for the old PID); (3) keep one Settings window alive hidden instead of
  recreating it, which removes the most frequently opened window from the count; (4) log GDI/USER
  object counts when they pass 5 000 so the failure has a visible precursor. None was applied: each is
  a behaviour change with its own risk and deserves a decision.

### PR-004 - An unreachable share froze the UI for 21 s per summon  `High` `Fixed`

- **Evidence:** `Directory.Exists(@"\\192.0.2.1\share\folder")` takes **21.04 s** to answer "no" on this
  machine (two independent unroutable addresses, 21.02 s and 21.04 s). On the original build, summoning the
  orbit with one unreachable folder and one unreachable application on it stalled the UI thread for
  **21 261 ms** (`measure-app.ps1 -Phase unreachable`, original `HEAD`).
- **Root cause:** `IconService.LoadIconAsync` made its `File.Exists` / `Directory.Exists` calls inline.
  Callers start it on the UI thread and an async method runs synchronously up to its first `await`, so
  the checks ran on the UI thread; a failed resolution is retried on every summon. `Launcher.Launch`
  made the same kind of check before launching a clicked item.
- **Fix:** the checks run on the thread pool (`IconService.ProbeAsync`, at most one per item at a
  time); `Launcher` runs launches whose target is on the network (`PathLocality`) on a dedicated STA
  thread.
- **Proof:** `NetworkPathHangTests` (the checks run on a pool thread; a slow answer leaves the caller
  free; concurrent summons share one probe; locality classification) and the live phase: **0 stalls in
  4 of 5 runs, one 316 ms first-summon hitch that also occurs with no network items (PR-017)**, against
  21 261 ms on the original build; start-up with the dead shares on the orbit: 0 stalls.
- **Instrument note:** the first baseline read "0 stalls". A stall still in progress is invisible to a
  pinger that records a ping only when it returns. `UiPinger` now reports the outstanding ping and the
  phase waits for the UI to answer again; every earlier phase benefits.
- **Residual:** `OrbitItemFactory.Classify` / `TryReadTextTarget` still probe the path a user types or
  pastes into the Add dialog on the UI thread. Dropped network folders are reachable by construction, and
  skipping the check would mis-classify them, so this was left (Accepted).

### PR-005 - Settings burned 19-26% of a core while open and idle  `Medium` `Fixed`

- **Evidence:** Release build, window open and untouched: **25.6%** of one core (first run), **18.6%** in the
  A/B control. Scratch-only switches: ring only 13.9%, tile-spin only 8.4%, neither **0.32%**. The process
  never became CPU-quiet in 25 s.
- **Root cause:** the Home screen's Orbit mark turns on a `Forever` composition animation and the Tools
  mark replays a tile-spin every 5 s; nothing ever stopped either while the window stayed open, and
  `StopHomeOrbitMark` only stopped the replay timer. The animations also ignored Windows' "Show animations"
  accessibility setting.
- **Fix:** `HomeIdlePolicy`: the marks play only while Home is showing, the window is active, motion is not
  reduced, and the person arrived or interacted within 20 s; otherwise they rest at the static logo.
  `StopHomeOrbitMark` now resets both marks.
- **Proof:** idle with Settings open **0.48%** of a core (was 18.6-25.6%); the process is CPU-quiet 23 s after
  launch (never before). `HomeIdlePolicyTests` pin each input and the 20 s boundary.
- **Behaviour change to be aware of:** the marks now rest after 20 s without interaction and resume on
  the next pointer move or key press.

### PR-006 - `XmlFormat.Validate` built a document tree  `Medium` `Fixed`

- **Evidence:** back to back on one machine, Debug: `xml validate` at 80 000 rows 98.5 ms (growth x25.1 for
  an x8 input, exponent ~1.55) -> 13.5 ms (x8.3). Allocation per call on a 3.7 MB document: 18.2 MB ->
  **11.5 KB**. `Validate` runs on every debounced keystroke in the Formatter and on every clipboard read in
  Quick Format.
- **Root cause:** well-formedness was checked by loading an `XDocument` and discarding it.
- **Fix:** read the token stream through the same hardened, depth-limited reader; same refusals, same
  diagnostics.
- **Proof:** `AllocationBudgetTests` asserts validate allocates under one byte per input character and under
  1/20 of what the tree costs - a thread-local, load-independent measure - plus the refusals (mismatched tag,
  two roots, empty, duplicate attribute, DTD) and the depth ceiling at and past `MaxDepth`.

### PR-007 - Tests truncated the real diagnostic log  `Low` `Fixed`

`%TEMP%\swiftx.log` is shared by every SwiftX process and truncated at each start, and the test run's
redirect covered the stores but not the log: the 796-test baseline ended with the user's `swiftx.log`
holding test output. Added `SWIFTX_LOG_DIR` (default unchanged), set by `TestEnvironment` and by
`measure-app.ps1`'s own `TEMP`. `DiagLogLocationTests`.

### PR-008 - Favicon retries and the wrong port  `Low` `Fixed`

A web link whose icon could not be fetched made two HTTP requests (the site, then DuckDuckGo, which is
told the host each time) on **every summon**; a localhost dev server's favicon was requested on port 80
and two servers on one host shared a cached icon. A per-host back-off (30 s, 1, 2, 5, 10 min; success
forgets) keeps the "appears when the network returns" behaviour; the site request now uses the URL's
authority. `FaviconMissCacheTests`.

### PR-009 - `PickModuleIcon` unguarded  `Low` `Fixed`

The only `async void` UI handler that called a picker (which throws if one is already open) without the
`try/catch` the project added to its siblings. Guarded the same way.

### PR-010 - Settings opens with a UI hitch  `Medium` `Open`

Settings window creation (163 KB of XAML plus the Loaded work) blocks the UI thread. Final build, 40
opens: window visible p50 238 ms, interactive p50 611 ms, 29 stalls over the 250 ms budget, longest
482 ms (original: visible 280 ms, interactive 761 ms, 48 stalls, longest 1 064 ms - the leak made every
later open slower). Fixing the remainder means building the heavy screens lazily, a refactor of a
7 000-line file that was out of scope for a review.

### PR-011 - ~1 handle per summon  `Low` `Open`

150 summon/dismiss cycles add ~105 handles (1459 -> 1565; 92.7 per 100 cycles after the fix, 106.7 before)
with private bytes, GDI and USER flat. Not chased further: at ~1 handle per summon a kernel-handle leak
takes years of daily use to matter (the process limit is ~16 million). Not changed by this review.

### PR-012 - Hot corner polling  `Info` `Accepted`

The 50 ms `WM_TIMER` costs 0.37-0.63% of one core and 20 wakeups per second, and is the whole tray-idle
cost (0% with the hot corner off). Within budget; a low-level hook was rejected by the original authors.

### PR-013 - Title-bar setters leak per call in a tight loop  `Info`

Probe only: 0 / 1 / 2 / 5 / 20 applications of `SetTitleBarTheme`'s seven setters on one window cost
51 / 59 / 77 / 119 / 166 GDI objects. Not reproduced in the product: 41 real `Activated` events added no
GDI, and a read-before-write guard changed the Settings soak by nothing, so it was reverted rather than
shipped unproven.

### PR-014 - Store product id is a placeholder  `Info`

`StoreFeedback.ProductId = "0000000000000"` ("Placeholder until the listing is live"). With the crash
fixed, the portable build's Rate button opens `ms-windows-store://review/?ProductId=0000000000000` and,
failing that, a web URL with the same id. Set the real id before shipping.

### PR-016 - A configuration with null entries stopped the app starting  `Medium` `Fixed`

- **Evidence:** found by accident. A measurement script seeded `"Items": [..., null]`, and the instance
  exited inside a second. Its log:
  `UNHANDLED: System.NullReferenceException at SystemOrbitItems.Strip (SystemOrbitItems.cs:72) <-
  OrbitManager.Start (OrbitManager.cs:95) <- App.OnLaunched (App.xaml.cs:90)`.
- **Root cause:** JSON is not bound by C# nullability. `"Items": [null]`, `"Items": null`,
  `"Children": [null]`, `"Orbits": [null]`, `"HotCornerOrbit": null` and a null `Id` / `Target` /
  `DisplayName` / `Hotkey` all deserialize without complaint, and the first code to walk one throws.
  On the launch path that is an unhandled exception out of `OnLaunched`: the app dies on **every**
  launch and has no in-app recovery - the person has to find and delete `orbit.json`, which holds all their
  orbit items. The existing malformed-file tests cover the file that does not parse, not the file that
  parses and is wrong.
- **Trigger plausibility:** the app never writes a null entry itself (`WhenWritingNull`, atomic publish);
  it takes a hand edit, a merge conflict in a synced folder, or a tool that rewrites the file. Hence Medium,
  not High: unlikely trigger, total failure.
- **Fix:** `AppConfig.RepairShape()`, called from `OrbitStore.Load` and `ImportFrom` after
  deserialization: drops null entries, turns a null list into an empty one and a null string into its
  default, keeps everything readable. An import with no usable orbit is still refused ("keep what you had").
- **Proof:** `ConfigShapeResilienceTests` (16 cases, each shape taken through the same migrations
  `OrbitManager.Start` runs): **14 fail with the repair removed** (mutation check, `NullReferenceException`),
  16 pass with it. Live: the Release build launched on a config with `null` orbit, item, child and hot-corner
  entries stays up (alive after 8 s, nothing in the log); the same file killed the previous build.
- **Residual:** other collections that are not part of the configuration (secure-values file, legacy
  config upgrade) were not shape-fuzzed; the secure-values loader already has a malformed-file test.

### PR-017 - The first summon after launch can hitch  `Low` `Open`

- **Evidence:** longest UI-thread stall in the first summon of a fresh process: original build 463 ms;
  fixed build with unreachable items 316 / 0 / 0 / 0 / 0 ms; the **same phase with no unreachable items at
  all** 447 / 0 / 280 ms. Every later summon in a 150-cycle soak: 0 stalls, longest ping 216 ms.
- **Reading:** cold first use (JIT, first XAML realisation of the orbit's items), not anything to do with
  the network - it is there with nothing on the orbit but local items. Over the 250 ms budget about half the
  time, once per process lifetime, and invisible in a tray app unless the first summon follows the launch
  closely.
- **Not fixed:** the remedy is to realise the orbit once, hidden, shortly after start-up when the machine is
  idle. That changes start-up work and memory, so it wants a decision and a before/after of its own.

### PR-018 - Icon decode has a byte cap but no pixel cap  `Low` `Open (hazard)`

`IconService.DecodeAsync` hands a picked image or a fetched favicon to a `BitmapImage` without a decode
size. The bytes are capped (`InputLimits.MaxIconBytes`, `MaxFaviconBytes` = 2 MB) and the GDI icon path has
a 2 048 px ceiling (`MaxIconPixelDimension`), but a 2 MB PNG can describe an image far larger than that
once decoded (a solid-colour PNG compresses by orders of magnitude). **Not reproduced**: no trigger was
demonstrated, so this is a hazard and not a finding. If it is ever worth closing, set `DecodePixelWidth`
from the item size or reject an image whose header declares more than `MaxIconPixelDimension` before
decoding it. From the static scan (`data/risk-scan-triage.md`).

### PR-019 - A summon storm stalls the UI thread once  `Info` `Accepted`

300 `WM_HOTKEY` toggles in 12.2 s (25 per second, about ten times what a person can press) on the fixed
build: one UI stall of 318.9 ms; the original build, 285.4 ms. The orbit recovers (open 10.2 ms, close
466.6 ms, no crash, no dump) and nothing accumulates. Beyond human input rates, same on both builds, so
neither fixed nor chased.

### PR-015 - Scaling ratios near their ceilings in the first baseline  `Info`

`xml validate` x27.7 and `xml format` x30.1 against a x32 ceiling, with the quiet gate at 24% CPU. Re-run
back to back under quiet conditions they were x25.1 (validate, fixed by PR-006) and x14-16 (format). The
suite is sensitive to machine state: the second and third runs of identical code were up to 4x faster
for operations nothing touched. Compare ratios, not milliseconds, across sessions.
