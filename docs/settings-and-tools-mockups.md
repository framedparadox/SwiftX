# Settings & Tools Mockups — Logi-Format Restyle

**Status:** mockups only. No app code changed.
**Base:** `framedparadox/windows-app-mx` @ `logiApp/dotnet` (`be474f5`) — a newer branch than the
`logiApp-win` this session extracted before; see [What changed upstream](#what-changed-upstream).
`framedparadox/SwiftX` @ `ca48d97` (branch `claude/swiftx-settings-screen-qamzrj`, pulled current).
**Supersedes nothing** — `settings-screen-logi-alignment-plan.md` and `swiftx-app-shell-plan.md`
stand as written; this restyles Settings in place rather than splitting it into a five-screen shell.

Three asks, three sets of mockups, all under `docs/images/`:

| # | Ask | Files |
| --: | :-- | :-- |
| 1 | Settings in Logi's format, SwiftX's own content | `9`, `10`, `11`, `12` |
| 2 | A new tool window, restyled per Logi | `13` |
| 3 | The same tool embedded as a Settings screen instead | `14`, `15` |

---

## What changed upstream

`logiApp/dotnet` carries 9 commits beyond the `logiApp-win` branch this session read before:
Acrylic and Frosted-Glass-V2 themes, a 5-card theme picker (145px cards, down from 150), a
conditional opacity-slider panel under the grid, and a `ContentDialog` re-entrancy guard on the
feature tour. None of it changes the rail, the type ramp, or the `Settings*` colour tokens the
earlier extraction (`settings-screen-logi-alignment-plan.md` §A) is built on — that document is
still accurate. The one transferable idea: a settings block that only matters for one choice
(the opacity slider) appears **conditionally**, docked right under the control that governs it,
instead of always taking up space. Worth keeping in mind if SwiftX ever grows a setting like that.

`SwiftX`'s designated branch also moved: language support landed for real (`LanguageCatalog`, 19
entries with RTL, a live `LanguageBox`), and About picked up a "float and animate the dock's hub
mark" pass plus Terms/Privacy links. Both are reflected below — the Language row in mockup 9 uses
the actual catalog, not a placeholder.

## Rule followed throughout

**Content = SwiftX, design = Logi.** Every row, card, and label below is a setting that exists in
`SettingsWindow.xaml` today — nothing invented, nothing from Logi ported in name only. Where Logi's
format has no equivalent to map to (a `ColorPicker` swatch, a hotkey-capture button, an
Export/Import pair) the control keeps its native shape but takes Logi's card geometry, spacing, and
type ramp. Where Logi's format *is* a direct fit — its theme picker is a card grid — SwiftX's own
`ThemeBox` becomes a card grid too, styled to Logi's exact 150×130 geometry, sized to 4 entries
(System/Light/Dark/Midnight) instead of Logi's 3.

---

## 1. Settings, Logi format

Rail: 270px, two-line 30px title ("SwiftX" / "Settings"), 44px rows, 4px accent indicator — all
per `settings-screen-logi-alignment-plan.md` §D.2–D.3. One detail these mockups add that the
earlier plan didn't call out: SwiftX's `NavigationView` already splits `MenuItems`
(Home/General/Elements/Tools) from `FooterMenuItems` (Feedback/Privacy/About) with a divider. The
mockups keep that split — it's real structure, not a Logi import.

**`9-settings-general-logi.svg`** — General. Order matches the shipped XAML: Theme, Accent colour,
Language, Invoke, Start with Windows, Backup & restore, Reset. Only Theme changes shape.

**`10-settings-home-logi.svg`** — Home. Ring preview (360×360) with Add item…/Reset defaults on the
canvas, sidebar with Item size / Ring radius / Hot corner / the selected-item info box — all
already colocated here in the real code, not moved in from anywhere.

**`11-settings-about-logi.svg`** — About, cropped to the card for legibility. Hero card only,
matching Logi's hero-card geometry (90/\* columns, 24px padding). Deliberately **doesn't** add
Logi's spec-grid or legal-notices blocks — SwiftX's About doesn't have that content today, and
rule one is content from SwiftX only.

**`12-settings-tools-logi.svg`** — Tools panel, restyled from its current `ListView` rows into a
card grid (4 cards fit the 1040px window at ~160px each). This is also where mockups 2 and 3 pick
up: clicking **OPEN** on Data Formatter is the moment their two answers diverge.

---

## 2. New tool window, Logi-restyled

**`13-tool-window-standalone-logi.svg`** — `ToolWindowBase`, still a separate top-level window
(900×640 default), same as today. What changes is the chrome: the `CommandBar`'s seven actions
(Validate, Format, Minify, Swap, Copy, Save, Clear — from `EditorToolPage`'s real command set)
become a row of 36px circular icon buttons, Logi's own toolbar idiom from the Action Ring's bottom
bar. The options row (indent width, sort keys, escape/unescape, convert-to pills) keeps its shape
but picks up Logi's pill styling. The footer status bar's validity chip becomes a Logi-style pill
instead of plain text. `AppTitleBar`'s glyph+title pattern carries over unchanged — Logi has
nothing to say about a floating utility window's title bar that SwiftX doesn't already do.

---

## 3. Tools embedded in Settings, as screens

Two answers to "where does the rail go while a tool is open," both reachable from mockup 12's
**OPEN**:

**`14-tool-embedded-wide.svg`** (recommended) — the rail is replaced entirely by a breadcrumb
header (`SETTINGS › TOOLS` / "Data Formatter"), the same swap Logi's own `ActionRingPage` uses to
go from its Layout view to its Explore AI view inside one `Grid` — collapse one subtree, show
another, no new window. Full 944px width for the editor panes. **Back** returns to the Tools card
grid.

**`15-tool-embedded-railed.svg`** (alternative) — the rail stays, `TOOLS` still lit, so there's
never a moment where the user isn't sure they're still in Settings. Cost: the editor drops to
690px and the command bar's options row has to wrap to a second line to fit.

Recommend **14**. Losing 944px down to 690px is a real hit to a dual-pane text editor — Logi's own
in-layout Settings view (`ActionRingPage.xaml`'s Layout ↔ Settings swap) sets the precedent for
collapsing navigation chrome when a screen needs the room, and Data Formatter is exactly that kind
of screen. **15** is worth keeping in the comparison because the "stay oriented" argument is real
for a user who came from Tools specifically to jump between two or three tools in a row — that's a
product call, not a layout one, so it's included rather than decided here.

Neither changes how tool windows work when opened from the ring itself (`RingManager.OpenTool` →
`ToolWindowBase`, unaffected) — this is specifically about what happens when a tool is opened *from
inside Settings*, which today just spawns the same floating window. Adopting 14 or 15 means
`SettingsWindow` gets a `ShowToolScreen(ToolKind)` path alongside its existing
`ToolWindowLauncher.Open`, not a replacement for it — opening a tool from the ring should still
open a window; only the Settings-panel entry point changes.

---

## Open question this doesn't answer

Whether Tools (and Elements) belong inside Settings at all, or as their own top-level screens in a
five-screen shell, is what `swiftx-app-shell-plan.md` was for. These mockups assume Tools **stays**
inside Settings — restyled, and in mockup 3's case reworked to open in-place — because that's the
narrower thing asked for here. If the shell direction is still live, decide that first; these
mockups are the two answers for "Tools inside Settings" either way that plan lands.
