# SwiftX profile: the facts a performance review needs

Project-specific knowledge lives here and nowhere else in the skill. To reuse the skill on another
app, replace this file and the publisher list in `scripts/scan-risk-patterns.ps1` (rule CR10).

## What the app is

A tray-resident WinUI 3 launcher (`net10.0-windows10.0.26100.0`, self-contained, **unpackaged by
default**; the Store build is an MSIX produced with `-p:WindowsPackageType=MSIX`). One process, one
UI thread, no taskbar button. ~28 000 lines; `ShellWindow.xaml.cs` alone is ~7 300.

| Piece | Lives in | Perf / crash relevance |
|---|---|---|
| `OrbitManager` | app lifetime | owns everything below; **publishes `ItemsChanged`** (any window subscribing must unsubscribe in `Closed`) |
| `MessageWindow` | hidden top-level HWND, class `SwiftX.MessageWindow.<pid>` | receives tray callbacks, `WM_HOTKEY`, `WM_TIMER`, the show-Settings message; the UI-thread ping target |
| `HotCornerWatcher` | `WM_TIMER` every 50 ms | the only always-on timer; polls the cursor (display rect cached 2 s) |
| `HotkeyService` | `RegisterHotKey`, default `Ctrl+Shift+X`, id 1 | `WM_HOTKEY` is ignored unless the id is registered |
| `OrbitWindow` | hidden owner window kept alive for the whole run | `_outsideClick` timer 40 ms **only while open**; resolves item icons on summon |
| `ShellWindow` | created per open, title **"SwiftX app"** | 163 KB XAML, 7.3 k-line code-behind; the heaviest thing the app builds |
| `OnboardingWindow` | first run only, title "Set up SwiftX" | a config with `Seeded: false` opens this, not Settings |
| Tool windows | `ToolWindowBase`, title = tool name | one window per tool instance; `ToolWindowManager` registers/unregisters |
| `IconService` | static | shell jumbo icon via raw vtable call, favicon over a shared `HttpClient` (10 s timeout) cached under `IconCache` |
| `OrbitStore` / `SecureValueStore` | `%AppData%\SwiftX\orbit.json`, `%LocalAppData%\SwiftX\secure-values.json` | whole-file replace through `AtomicWrite` (retry up to ~300 ms **on the calling thread**) |
| `Diag` | `%TEMP%\swiftx.log` (override: `SWIFTX_LOG_DIR`, set by the test host) | truncated at every process start and at 4 MB; shared by every SwiftX process |

## Entry points the scripts rely on

| Thing | Value |
|---|---|
| Tray-only launch | `SwiftX.exe --startup` (does **not** build `ShellWindow`); no args opens Settings |
| Isolated data | `SWIFTX_DATA_DIR=<dir>` redirects both stores **and** is the single-instance key, so a copy with its own dir runs beside the user's |
| Single-instance key | first 8 bytes of SHA-256 of the lower-cased full data-dir path, upper-case hex (16 chars) |
| Show-Settings message | `RegisterWindowMessage("SwiftX.ShowSettings.v1." + key)`, posted to the message window |
| Summon | post `WM_HOTKEY (0x0312)`, `wParam = 1` to the message window (toggle: summon, then dismiss) |
| Close Settings | post `WM_CLOSE (0x0010)` to the visible window titled "SwiftX app" |
| Config schema | `orbit.json`: `Seeded`, `Hotkey` (`"Ctrl+Alt+Shift+F24"`), `HotkeyEnabled`, `HotCornerEnabled`, `Orbits[].Items[]` (`Kind` by name: Application, File, Folder, WebLink, Shortcut, Group, Tool; `DisplayName`, `Target`) |
| Trace first-chance faults | `SWIFTX_TRACE_EXCEPTIONS=1` |

Property names are case-sensitive PascalCase. `Seeded: true` is required or the app opens the
first-run guide.

## Hard rules learned the expensive way

1. **`dotnet test` destroys real data unless redirected.** `tests/SwiftX.Tests/TestEnvironment.cs`
   sets `SWIFTX_DATA_DIR` from a `[ModuleInitializer]` (it must be one: `OrbitStore.DataDirectory` is
   `static readonly`, resolved once). `run-suite.ps1` refuses to run if that is gone.
2. **The user usually has `SwiftX.exe` running under a debugger**, which locks `src\SwiftX\bin`.
   Build and test in a scratch copy (`run-suite.ps1`, `build-app.ps1`); never kill it. The only
   process a script may stop is one it launched itself.
3. **A live instance needs its own `TEMP`**, or its `Diag` start-up truncates the user's log.
4. **Perf tests need a quiet machine** (`PerformanceBudgetTests` divides small timings by large
   ones). Do not build or run a second `dotnet test` alongside.
5. **A window must not hold a delegate to itself on its own `Activated` / `Closed`** (or any event of
   its own that targets it): the native window then keeps the managed `Window` alive for the life of
   the process, with all its XAML tree, icons and editors. Use a named method, and remove every
   handler - the `Closed` handler too - inside `Closed`. `EventSubscriptionLifetimeTests` enforces it.
6. Editing hazards on this repo (from earlier sessions): use the Edit tool, not `sed -i` (it rewrites
   CRLF to LF and `autocrlf` hides it from `git diff`); use the Write tool for content with
   backslashes (a heredoc collapses `\\`); spot-check any new special character in a `.cs` comment
   with `od -c`; never type a private-use glyph literally, build it from its code point.

## Existing test inventory (what already protects what)

| Folder | Covers |
|---|---|
| `Performance/` | absolute budgets on 6 MB JSON / 3.7 MB XML / tokenizers / base64; **scaling** (cost grows with the input, not its square); heap retention |
| `Load/` | the format singletons from 24 threads, store save/load under contention, torn-write detection, atomic publish, 300-cycle endurance, mixed-tool load |
| `Resilience/` | regressions for real crashes and hangs (e.g. the 73-second nested-XML freeze) |
| `Security/` | bounded inputs, hostile XML/JSON, secret lifetime |
| `Formats/`, `Models/`, `Services/` | pure logic: formats, geometry, slots, localization, hover, themes, icon handle plumbing |

Added by the 2026-10-01 review: `Performance/AllocationBudgetTests` (garbage per call, load-independent),
`Resilience/` `NativeCapabilityGuardTests`, `EventSubscriptionLifetimeTests` (source rules: no own-event
handlers left on a closeable window), `NetworkPathHangTests`, `ConfigShapeResilienceTests`;
`Services/` `HomeIdlePolicyTests`, `FaviconMissCacheTests`, `DiagLogLocationTests`.

**Not covered by any test, by construction:** anything that needs a live WinUI window (whether a
closed window is actually collected, UI-thread blocking, animation CPU, GDI/handle drift). The source
rules above pin the *cause* of the leak we found, not the symptom. `measure-app.ps1` fills that gap.

## Where things go

- Reports: `docs/performance/<yyyy-MM-dd>-review/` (`REPORT.md`, `findings.md`, `data/`), index in
  `docs/performance/README.md`.
- Scratch workspace, raw logs and run folders: `%TEMP%\swiftx-perf-review\` (never in the repo).

## Crash and finding history (append one row per review)

| Date | Signature | Cause | Fix | Status |
|---|---|---|---|---|
| 2026-08/09 | `0xc000027b` in `Microsoft.UI.Xaml.dll`, 16 events | `out IImageList` as a managed `[ComImport]` interface (`IconService.TryGetJumboIcon`) | raw pointer, `GetIcon` by vtable slot 10; `JumboIconTests` | fixed 2026-09-19, none since |
| 2026-09-15, 09-22 | `0xc0000005` in `Windows.ApplicationModel.Store.dll` @ `0xb6902`, 2 events (latest on v2.0.0.0) | `StoreFeedback.RequestRatingAsync` called `StoreContext` with no package identity (unpackaged build); the 09-22 dump shows `RateOnStore_Click -> RequestRatingAsync` | `StoreFeedback.InAppRatingSupported` gates it on `PackagedRuntime.IsPackaged`; `NativeCapabilityGuardTests` | fixed 2026-10-01; confirm on the user's own setup |
| 2026-10-01 (found by soak, no crash yet) | +7.1 MB private, +49 handles, +39 GDI per Settings open; 15 live `ShellWindow` after 15 opens and a full GC, no managed root | **A `Window` that subscribes to its own `Activated` / `Closed` is never collected** (native reference; related upstream: microsoft-ui-xaml #9960, #7282, #9063). Handlers on child elements (all 85 XAML `Click=""`) and on `AppWindow` do not matter. Four earlier, plausible fixes moved nothing; only the probe (`assets/window-lifetime-probe`) and the heap dump found it | named handlers, all removed inside `Closed` (itself included) in `ShellWindow`, `OnboardingWindow`, `AddItemWindow`, `ToolWindowBase`; `EventSubscriptionLifetimeTests` | fixed 2026-10-01 (PR-002) |
| 2026-10-01 | ~26 GDI + ~34 handles per window created, collected or not | `ExtendsContentIntoTitleBar = true` costs the framework ~49 GDI / ~57 handles per window (probe, SDK 2.4.0 and 2.5.1 alike); cannot be undone from app code | none possible in-app; recommended: upstream issue, window reuse, GDI-threshold recycle | **open** (PR-003) |
| 2026-10-01 | UI thread blocked up to 21 s per summon with an unreachable network item on the orbit | `IconService.LoadIconAsync` called `File/Directory.Exists` inline on the UI thread | probes moved to the pool (`IconService.ProbeAsync`, one per item at a time); network launches on an STA thread (`Launcher`); `NetworkPathHangTests` | fixed 2026-10-01 (PR-004) |
| 2026-10-01 | `NullReferenceException` in `SystemOrbitItems.Strip` out of `App.OnLaunched`, every launch, on an `orbit.json` containing `[null]` | JSON is not bound by C# nullability; nothing repaired the shape before the launch migrations walked it | `AppConfig.RepairShape()` in `OrbitStore.Load` / `ImportFrom`; `ConfigShapeResilienceTests` | fixed 2026-10-01 (PR-016) |
| 2026-10-01 | Settings window 19-26% of a core while open and idle | forever composition animations on the Home marks, never stopped | `HomeIdlePolicy`; `HomeIdlePolicyTests` | fixed 2026-10-01 (PR-005) |
| 2026-09-21 | 73 s freeze on nested XML | indentation quadratic in depth | `XmlFormat.MaxDepth` | fixed |
