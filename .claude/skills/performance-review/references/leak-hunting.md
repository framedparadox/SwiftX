# Leak hunting: from "it drifts" to "this line roots it"

Use this when `measure-app.ps1` reports a counter as GROWING. The sequence matters: each step
removes a whole class of explanation, and the first two cost seconds.

## 1. Which resource, and how fast

Read the drift table before anything else; the *kind* of growth names the suspect.

| Grows | Typical cause |
|---|---|
| Private bytes (managed heap stays flat) | native objects behind managed wrappers: XAML trees, composition visuals, bitmaps |
| Private bytes **and** managed heap | managed retention: static caches, event handlers, closures |
| Handles only | undisposed kernel objects: events, timers, sockets, file streams, WinRT streams |
| **GDI objects** | bitmaps, fonts, regions, DCs; the quota is **10 000 per process**, so ~35 per open is a crash after ~280 opens |
| USER objects | windows, menus, icons, cursors that were created and never destroyed |
| Threads | a thread per operation that never ends |

Normalise to **per cycle** (`slopePer100 / 100`). A leak of 4 MB / 35 handles / 30 GDI per cycle is
invisible in one run and fatal after a few hundred.

## 2. Is it a leak, or garbage waiting for the GC?

Native memory is invisible to the GC's budget, so a pile of dead wrappers can sit uncollected
for a long time and *look* like a leak. A forced GC settles it. Either:

- take a heap dump, which forces a full GC first (below); or
- in a scratch build, run `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` shortly
  after each close and watch the counters. If they do not move, it is real.

## 3. Name the objects: a type histogram after a full GC

```powershell
dotnet tool install dotnet-gcdump --tool-path <scratch>\tools     # never -g
measure-app.ps1 -Phase settings -SettingsCycles 15 -KeepRunning   # prints keptRunningPid in live-app.json
<scratch>\tools\dotnet-gcdump.exe collect -p <pid> -o x.gcdump
<scratch>\tools\dotnet-gcdump.exe report x.gcdump > x.txt
```

Look for **a type whose count equals the cycle count**: `SwiftX.ShellWindow  15` after 15 opens means
no window was ever freed. Types whose counts are multiples of it (`HotkeyCaptureButton 15`,
`ColorPicker 30`) show the whole visual tree is alive with it. A managed heap of ~10 MB beside a
process at 220 MB says the weight is native and held *by* those objects.
Stop the kept instance yourself afterwards (it is yours: `Stop-Process -Id <pid>`).

## 4. Find the root: SOS `gcroot`

```powershell
dotnet tool install dotnet-dump --tool-path <scratch>\tools
<scratch>\tools\dotnet-dump.exe collect -p <pid> --type Heap -o x.dmp
<scratch>\tools\dotnet-dump.exe analyze x.dmp -c "dumpheap -stat -type SwiftX.ShellWindow" -c exit   # get the MT
<scratch>\tools\dotnet-dump.exe analyze x.dmp -c "dumpheap -mt <MT>" -c exit                        # instance addresses
<scratch>\tools\dotnet-dump.exe analyze x.dmp -c "gcroot <address>" -c exit
<scratch>\tools\dotnet-dump.exe analyze x.dmp -c gchandles -c exit
```

- A chain naming a static, a `DispatcherTimer`, an event delegate: that is the root. Fix it.
- **"Found 0 unique roots"** while a full-GC dump still lists the object: the roots are *native*
  (a COM reference held by WinUI or the compositor, reached through a ref-counted handle). Managed
  tools cannot show those; go to step 5.
- `gchandles` summarises the handle table. Thousands of **Ref Count** and **Dependent** handles is
  the normal footprint of WinRT interop; it is the *count growing per cycle* that matters.

Check **native window destruction** independently, because it separates "the window object leaked"
from "the HWND leaked": enumerate the process's top-level windows after N cycles
(`[Win32Probe]::Windows($pid)`). If only the long-lived ones remain, the HWNDs are being destroyed
and what leaks is the native object graph behind them.

## 5. Bisect with switches in the **scratch** workspace

When the root is not nameable, remove suspects one at a time, in one build, with environment
switches that exist **only in the scratch copy** (the repo is never touched):

```csharp
// scratch only: ShellWindow.xaml.cs
if ((Environment.GetEnvironmentVariable("SWIFTX_EXPERIMENT") ?? "").Contains("x-noicons")) return;
```

`measure-app.ps1` hands its environment to the child, so set `$env:SWIFTX_EXPERIMENT` and run
`-Phase settings -SettingsCycles 10` once per variant (about 45 s each). Do not use `build-app.ps1`
for the patched build: it re-mirrors the repo over the scratch tree. Build the scratch project
directly. Candidates worth a switch: icon loads, logo/animation marks, backdrop and window chrome,
timers, big controls (ColorPicker, ListView), event wiring. Run the **all switches on** variant
early: if the leak survives it, the leak is in the window/XAML itself, not in anything you
switched off.

Rules for the experiment: change one thing per variant; keep the control in the same session;
record every variant's before/after in the findings, **including the ones that did nothing** - a
hypothesis that was measured and ruled out is the evidence that stops the next person repeating it.

## 6. Take the product out of the picture: the window-lifetime probe

When a cause is not nameable, reproduce it **without the product**. `assets/window-lifetime-probe/`
is a ~150-line WinUI 3 app that opens and closes N windows of a chosen variant and reports how many
survive a full GC plus the GDI / handle / private-byte cost per window. One build, 30 windows, 20
seconds per variant. Run it first on `none` (the control), then add the one thing you suspect.

It is also how to decide **whether upgrading the dependency would help**: copy the probe, change the
`Microsoft.WindowsAppSDK` version, rerun the same variants. (Here: 2.5.1 behaves exactly like 2.4.0,
so a bump was ruled out in minutes instead of being tried in the product.)

Method rules that paid for themselves:

- Vary **one** thing per variant; keep a control in the same run.
- Report **per window**, not totals: GDI / window, handles / window, private MB / window, and
  `alive after full GC / cycles`. Tables of those four numbers make the cause obvious.
- A leak that survives a forced GC *and* is not rooted by any managed GC handle (`gcroot` finds none)
  is held on the native side; stop reading product code and bisect in the probe.
- Test **repetition and timing** separately: the same call 20x in a tight loop leaked per call, while
  the same call spread over real activations in the running app did not. Whether a leak is per window,
  per call, or per frame decides what the fix can be.

## 7. Hazards this codebase has already hit

- **A `Window` that subscribes a handler of its own to its own events (`Activated`, `Closed`) is never
  collected** (`Window` is not a `DependencyObject`, so the XAML reference tracking does not cover
  it). Handlers on child elements and on `AppWindow` do not matter. Named handlers, all removed
  inside `Closed` - `Closed` included - fix it. `EventSubscriptionLifetimeTests` pins the rule for
  every closeable window. 30 / 30 windows alive versus 0 / 30.
- **`ExtendsContentIntoTitleBar = true` leaks ~45 GDI objects and ~55 handles per window**, collected
  or not, on Windows App SDK 2.4.0 and 2.5.1 alike; reverting it in `Closing`/`Closed` does not help
  (`SetBorderAndTitleBar(true, false)` alone leaks ~26). Not fixable in app code. The process quota
  is 10 000 GDI objects, so budget roughly 200 window creations per process lifetime; the options
  are reusing windows, reducing how many are created, or recycling the process at a quiet moment.
- `AppWindowTitleBar` property setters leak per call when applied back to back in a tight loop (about
  6 GDI + 6 handles per application of seven, probe only). **Not observed in the product**: 41 real
  `Activated` events over Settings added no GDI, and a read-before-write guard changed the Settings
  soak by nothing (+25.6 vs +26.8 GDI per open), so it was reverted. A reminder that a leak
  reproduced in isolation is a hazard, not a finding, until the product shows it.
- Touching a closed window's `AppWindow` / `XamlRoot` from a dispatcher callback throws; with no
  handler it is a stowed-exception fail-fast, **`0xC000027B` - the same code this app has crashed
  with**. Guard late callbacks with a `_closed` flag.
- A subscription to an app-lifetime event written as a lambda cannot be unsubscribed
  (`_manager.ItemsChanged`). `EventSubscriptionLifetimeTests` pins the rule.
- A `Forever` composition animation never ends by itself (`AppLogo.Mark.PlayOrbitDock`;
  `Mark.Reset()` stops it): stop it when the screen or window goes away.
- Static `Dictionary<hwnd, delegate>` registries keyed by window handle
  (`WindowChrome.NoEraseSubclassCallbacks`) grow by one entry per window ever created.
- `IconService` allocates HICON / HBITMAP: every path must `DestroyIcon` / `DeleteObject`.

## 8. Prove the fix the same way you found the bug

Same parameters, same build configuration, same machine state: drift verdict `stable`, the type
count in a fresh heap dump back to 0-1, GDI flat. Then add the guard that would have failed: a
source-structure test for a rule ("every subscription has its unsubscribe"), a unit test for a
seam, or, for UI-only lifetime, the drift measurement recorded in the report as the regression check.
