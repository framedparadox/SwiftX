# Settings window leak: every hypothesis, measured

Release build, isolated instance, Settings opened and closed by window message (no input injected).
"first -> last" are the process counters at the first and last sampled cycle. The point of keeping
the dead ends is that **four fixes that looked right from reading the code moved nothing**; only the
probe and the heap dump found the cause.

## Baseline (original code, 40 cycles)

private 197 -> 396 MB, handles 1838 -> 3212, GDI objects 492 -> 1576. Per cycle (least squares after
warm-up): **+7.1 MB, +49 handles, +39 GDI**. UI thread: 48 stalls, longest 1064 ms.
Heap dump after a forced full GC, 15 opens: **15 live `ShellWindow`**, 15 `HotkeyCaptureButton`,
30 `ColorPicker`; managed heap only 11 MB for a 223 MB process, so the weight is native.
SOS `gcroot` on a leaked window: **0 managed roots** (the reference is held natively).

## Hypotheses tried in the product (each is a scratch-only switch; first -> last)

| Change | cycles | private MB | handles | GDI | Verdict |
|---|---:|---|---|---|---|
| unsubscribe `OrbitManager.ItemsChanged` (the lambda) | 40 | 196 -> 359 | 1902 -> 3384 | 555 -> 1750 | no change |
| + stop the Home animations (`Mark.Reset`) on close / leaving Home | 15 | 130 -> 221 | 1414 -> 2065 | 186 -> 708 | no change |
| forced `GC.Collect` 400 ms after every close | 15 | 113 -> 219 | 1195 -> 2111 | 95 -> 744 | not garbage waiting for the GC |
| null `Content`, `SystemBackdrop`, title bar on close | 15 | 111 -> 226 | 1213 -> 2415 | 115 -> 1044 | worse |
| skip icon loads | 10 | 104 -> 164 | 1026 -> 1544 | 61 -> 492 | no change |
| skip logo marks | 10 | 106 -> 164 | 1154 -> 1740 | 99 -> 475 | no change |
| skip backdrop | 10 | 112 -> 194 | 1208 -> 1858 | 117 -> 561 | no change |
| skip window chrome (`ApplyChromeTheme`) | 10 | 110 -> 188 | 1178 -> 1611 | 83 -> 309 | GDI roughly halves |
| skip all four | 10 | 96 -> 143 | 961 -> 1261 | 43 -> 274 | still leaks |
| null every generated element field on close (reflection) | 12 | 112 -> 201 | 1191 -> 1712 | 87 -> 367 | GDI/handles down ~40%, windows still alive |

## The probe (a minimal WinUI 3 app, no product code), 30 windows per variant

See `.claude/skills/performance-review/assets/window-lifetime-probe/README.md` for the full tables.
Two independent framework behaviours, both identical on Windows App SDK 2.4.0 and 2.5.1:

1. **A `Window` that subscribes to its own `Activated` / `Closed` is never collected**: 30 / 30 alive
   after a full GC with a lambda or method on either event; 0 / 30 with no subscription, with a
   weak-reference delegate target, or with every handler (the `Closed` handler included) removed inside
   `Closed`. Handlers on child elements (XAML `Click=""`) and on `AppWindow` do not matter.
2. **A custom title bar costs ~45 GDI objects and ~55 handles per window, collected or not**
   (`ExtendsContentIntoTitleBar = true`); `SetTitleBar` alone costs nothing; reverting the title bar in
   `Closing`/`Closed` does not help; the `AppWindow` form costs ~32.

## Fix and verification (named handlers, all removed in `Closed`, four closeable windows)

Heap dump after 40 opens + full GC: **1 live `ShellWindow`** (was 15 of 15), 38 k objects / 3.7 MB
(was 146 k / 11 MB). Per cycle (final build, 40 opens): **+0.7 MB (was +7.1; +1.1 in the first post-fix run), +34 handles (was +49), +26 GDI (was +39)**.
The remaining ~26 GDI and ~34 handles per window are the framework's title-bar cost (finding 2
above) and cannot be removed from application code.

## Not reproduced in the product

`WindowChrome.SetTitleBarTheme` runs on every window activation and its seven `AppWindowTitleBar`
setters leak ~6 GDI per application when applied back to back in the probe (0 / 1 / 2 / 5 / 20
applications: 51 / 59 / 77 / 119 / 166 GDI per window). In the product, 41 real `Activated` events
(orbit summoned and dismissed over Settings) added **0** GDI, and a read-before-write guard changed
the Settings soak by nothing (+25.6 vs +26.8 GDI per open). The guard was reverted: a leak seen only
in isolation is a hazard to record, not a finding to fix.
