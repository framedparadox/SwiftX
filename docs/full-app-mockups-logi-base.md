# SwiftX as a Full App — Logi as the Literal Base

**Status:** mockups only. No app code changed.
**Supersedes the *execution*, not the analysis, of the previous two mockup rounds** — this is the
third and current direction. `settings-screen-logi-alignment-plan.md` and
`swiftx-app-shell-plan.md` still hold the underlying extraction and phased build-out; this document
is what actually got mocked up once the brief tightened to "this is a complete app, colours and
background included."
**Base:** `framedparadox/windows-app-mx` @ `logiApp/dotnet` (`be474f5`).
`framedparadox/SwiftX` @ `87cf0cf` (branch `claude/swiftx-settings-screen-qamzrj`).

## What changed from the last round

1. **Four screens, not seven-inside-a-Settings-window:** Home, Ring, Tools, Settings. Elements is
   not a fifth screen — it folds into Ring, because that's where item management already happens
   in both apps (Logi has no separate elements list either; SwiftX's real `SettingsWindow.xaml`
   already colocates its ring-item canvas with Item size/Radius/Hot corner in one "Home" panel,
   which is what the Ring screen below is built from).
2. **Colours are Logi's literal hex, not SwiftX's.** The previous round kept SwiftX's teal accent
   deliberately (`settings-screen-logi-alignment-plan.md` §D.1) on the reasoning that SwiftX's own
   docs call it teal and users can already repick it. That reasoning is overridden this round —
   every value below is copied from `LogiApp/dotnet/Themes/AppThemeResources.xaml`'s `Dark`
   dictionary, unchanged. That file is identical between `logiApp-win` and `logiApp/dotnet`, so
   nothing here goes stale from the branch move.
3. **This is the app**, not a Settings window with panels. Settings is one of four peer screens,
   reached the way Logi's own `MainPage` reaches it — a gear button that navigates a `Frame`, not
   a rail item.

## Two Logi palettes, kept separate because Logi keeps them separate

Logi's own source uses **two different accents** depending on which surface you're looking at —
this isn't a SwiftX invention, it's what `AppThemeResources.xaml` actually contains:

| Family | Where | bg | card/node | border | accent |
| :-- | :-- | :-- | :-- | :-- | :-- |
| **App chrome** | Home, Tools, Settings | `#2B2B2B` (`AppBackgroundBrush`) | `#333333` (`SettingsCardBackgroundBrush`) | `#444444` | `#8A67FF` violet (`SettingsAccentBrush`) |
| **Ring canvas** | Ring screen, and any tool opened *from* the ring | `#121212` (`ActionRingBackgroundBrush`) | `#1A1A1A` (`ActionRingNodeBackgroundBrush`) | `#333333` | `#00DDB3` teal (`ActionRingAccentBrush`) |

The mockups use this split deliberately: a tool opened **from inside the app** (Tools screen) reads
as app chrome — violet. A tool opened **by clicking its bubble on the ring** reads as the ring —
teal. That's what answers point 5 below without inventing a rule Logi doesn't already have.

## The six mockups

All under `docs/images/`, all Dark theme (Light exists in the source and in the palette table
above's Logi origin, but wasn't re-rendered here — same scope decision as the last two rounds).

**`16-app-home.svg`** — Logi's real `MainPage.xaml`: greeting header (`GreetingHelper`, 30px Bold),
`AppBackgroundBrush` canvas. Three of Logi's four header controls are **kept and repurposed**
rather than deleted: `ThemeToggleButton` stays a theme toggle; `ActionRingButton` (glyph `E9D9`)
now opens the **Ring** screen — its literal job in Logi's own `MainPage.xaml.cs`, just pointed at
SwiftX's ring instead of Logi's; `SettingsButton` (gear, `E713`) opens **Settings**, exactly as
today. Dropped: `AddDeviceButton` and the profile avatar — the two controls with no SwiftX
equivalent (no device pairing, no account). This reverses the very first round's instruction to
drop the Action Ring button; now that Ring is a named top-level screen, routing Logi's own button
to it is the more faithful reading of "use Logi as the base." The content area, empty by design in
Logi, holds two destination cards (Ring, Tools) in the app-chrome violet.

**`17-app-ring.svg`** — full `ActionRingPage.xaml` Layout-view geometry: 68px header, 8 nodes at
45° with outward label pills (44px nodes, 30px pills, matching Logi's canvas measurements from
`settings-screen-logi-alignment-plan.md` §A.7), 60px bottom toolbar, 320px right sidebar — all in
the **Ring canvas** teal palette. The sidebar content is **not** Logi's drag-source accordion
(SwiftX has no marketplace/category browser to populate one with); it's SwiftX's own ring-display
settings — Item size, Ring radius, Hot corner, Hover style — carried over verbatim from the
previous round, because that's what actually exists in `SettingsWindow.xaml` today. The bottom
toolbar's Add item…/Reset pair moved here from the old on-canvas buttons, taking Logi's toolbar
*position* while keeping SwiftX's own action.

**`18-app-tools.svg`** — Tools promoted to its own top-level screen: Logi's back+title header
pattern, the same 4-card grid as the previous round, recoloured from teal to the app-chrome violet.

**`19-app-tools-embedded.svg`** — **Tools opened within the app** (point 5, first variation): the
header collapses to a breadcrumb (`TOOLS` / "Data Formatter") the same way `ActionRingPage.xaml`
swaps its own Layout view for its Explore AI view inside one `Grid`. Full-width dual-pane editor.
Violet chip and pills — app chrome, because it never left the app's own screen.

**`20-tool-standalone-from-ring.svg`** — **the same tool, invoked by clicking its bubble on the
ring** (point 5, second variation): a separate top-level window, exactly `ToolWindowBase` as it
exists today, 900×640. Painted in the ring's own palette — `#121212`/`#1A1A1A`/`#00DDB3` — rather
than app violet, so it visually reads as having come from the ring rather than from Tools. Both 19
and 20 are real, simultaneous paths through the same app, not alternatives to choose between:
opening Data Formatter from the Tools screen goes in-place (19); opening it by clicking its bubble
on the ring still spawns a window (20), matching `RingManager.OpenTool` → `ToolWindowBase`
unchanged.

**`21-app-settings.svg`** — Logi's literal `SettingsPage.xaml` rail: Back button, two-line
"SwiftX / Settings" title, and now genuinely Logi's **four** sections (`GENERAL` /
`FEEDBACK & SUPPORT` / `PRIVACY & DATA` / `ABOUT`) — Home, Elements, and Tools are gone from this
rail because they're top-level screens now, not Settings panels. General's content is unchanged
from the previous round (Theme as 4 cards, Accent colour, Language, Invoke, Start with Windows,
Backup & restore, Reset) with the accent swapped from teal to `#8A67FF`.

## What this doesn't (yet) cover

Feedback, Privacy, and About keep the same content as the previous round's mockups — not
re-rendered here, since the ask this round was about the four-screen structure and the literal
palette, not new content for those three. They'd need the same violet swap if rebuilt. Light theme
is unmocked for the same reason as before. And `SettingsWindow` still needs to actually become
this four-screen shell in code — these are mockups, `swiftx-app-shell-plan.md`'s phased build-out
is still the plan for turning them into `SettingsWindow.xaml` changes.
