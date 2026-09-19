# SwiftX App Shell — Logi-Based Layout Plan

**Status:** plan only. No code changed.
**Supersedes:** the scope of `settings-screen-logi-alignment-plan.md`, which planned Settings alone.
That document's extraction (§A: Logi metrics, type ramp, colour tokens) still stands and is the
reference for everything visual here; this one restructures the whole app around it.
**Base reference:** `framedparadox/windows-app-mx` @ `e82997e`, project `LogiApp/`, plus three
supplied product screenshots.
**Target:** `framedparadox/SwiftX` @ `56cfe64`.

---

## The headline problem

**SwiftX has no main window.** `App.OnLaunched` loads config, starts `RingManager`, and returns —
it never creates a window (`src/SwiftX/App.xaml.cs:33-42`). The only windows that exist are
`RingWindow` (the overlay), `SettingsWindow`, `AddItemWindow`, and `ToolWindowBase`. SwiftX is a
tray-resident app.

The requested layout — Home, Ring, Tools, Elements, Settings — is a **five-screen main window**.
That window has to be created. It is the single biggest piece of work in this plan and everything
else hangs off it.

**Recommended approach:** promote `SettingsWindow` into that shell rather than adding a sixth window
class beside it. It already owns everything the shell needs — Mica backdrop, custom `AppTitleBar`,
theme and accent plumbing, `WindowChrome` sizing/rounding/centring, the ring preview canvas with
drag-reorder, the elements list, the tools list, and `RingManager` wiring. Rebuilding that in a new
`MainWindow` and leaving `SettingsWindow` behind means maintaining two copies of the theme engine.
Renaming it `ShellWindow` (or `MainWindow`) and re-homing its panels as screens is the smaller,
safer change. `RingManager.OpenSettings()` becomes `OpenShell()`, and the tray menu's "Settings"
entry opens the shell on the Home screen.

---

## Reference and target wireframes

Committed under `docs/images/`. **The supplied screenshots could not be committed as files** — they
arrived as vision input and never reached this session's filesystem, so there were no bytes to add
to the branch. These are redrawn, annotated wireframes of them instead; they carry the measurements
the plan depends on, which the raw PNGs do not. If the original PNGs are wanted in the repo, attach
them as files and they can be added directly.

| File | What it shows |
| :-- | :-- |
| `1-ref-logi-home.svg` | Reference: Logi home header, with the three ignored controls struck through |
| `2-ref-logi-cards-dark.svg` | Reference: the card gallery, dark — card geometry and internals |
| `3-ref-logi-cards-light.svg` | Reference: the same gallery, light |
| `4-swiftx-home.svg` | Target: Home — greeting header + three portrait nav boxes |
| `5-swiftx-ring.svg` | Target: Ring — Logi ring canvas + ring-settings sidebar |
| `6-swiftx-tools.svg` | Target: Tools — four tool cards |
| `7-swiftx-elements.svg` | Target: Elements — element cards |
| `8-swiftx-settings-general.svg` | Target: Settings › General, after the removals and moves |

---

## 1. Home screen

### Reference
Logi's header (`LogiApp/MainPage.xaml`): greeting `TextBlock` at 30px Bold in the
`Segoe UI, Cambria, Georgia, 'Times New Roman'` stack, header `Padding="40,20,20,20"`, right-aligned
toolbar. **Logi's content row is empty by design** — the device grid in the screenshot is product
art, not source. The card grid comes from screenshots 2/3 instead.

### Header — what survives
Per the spec, three controls are dropped: `AddDeviceButton`, `ActionRingButton`, and the avatar
`Ellipse`. What remains, in Logi's own order:

| Keep | From | Detail |
| :-- | :-- | :-- |
| Greeting | `GreetingText` | 30px Bold, `< 12` → "Good Morning", `< 17` → "Good Afternoon", else "Good Evening". Port `GreetingHelper` as `Services/Greeting.cs` |
| Theme toggle | `ThemeToggleButton` | 36×36, `CornerRadius=18`, glyph `E793`. Cycles Light → Dark → Midnight → System, or opens a flyout |
| Settings gear | `SettingsButton` | 36×36, `CornerRadius=18`, glyph `E713` → Settings screen |

The screenshot shows a check-in-circle where the source has the moon; the source is authoritative.

### The three nav boxes

| Property | Value |
| :-- | :-- |
| Box | `268 × 340`, `CornerRadius=8`, `BorderThickness=1` on `SwiftXCardStrokeBrush` |
| Box shadow | Explicitly requested. WinUI has no CSS box-shadow — use `ThemeShadow` on a `Border` with a small `Translation` Z, or a `Composition` `DropShadow` via `ElementCompositionPreview`. `ThemeShadow` is the smaller change and respects theme |
| Gutter | 34px, three boxes centred in the content area |
| Icon medallion | `Ellipse` ⌀88 in `SwiftXNavPaneBrush`, glyph at 36px in the accent |
| Title | 22px Bold, centred |
| Description | 12px, `SettingsSecondaryTextBrush`, two lines, centred |
| Meta pill | 22px tall, `CornerRadius=11`, `SwiftXCardBackgroundBrush` step, 10px label |
| Footer bar | 1px divider then a centred `OPEN` at 11px Bold `CharacterSpacing=80` in the accent — Logi's `ADD` bar |

| Box | Title | Description | Meta | Goes to |
| :-- | :-- | :-- | :-- | :-- |
| 1 | Ring | Arrange the ring, set its size, radius and hot corner | `8 slots · 6 user` | Ring screen |
| 2 | Tools | Open the built-in developer tools and choose which show | `4 tools` | Tools screen |
| 3 | Elements | Apps, files, folders, links and groups on the ring | `add · hide · remove` | Elements screen |

Whole box is the click target (a `Button` with a templated `ContentPresenter`, not `Tapped` on a
`Border` — keyboard and Narrator, per `docs/windows-app-design-standards.md` §7.3).

---

## 2. Ring screen

Ports `LogiApp/ActionRingPage.xaml`'s **Layout view**, with the right sidebar's contents replaced.

### Shell — measured from the reference

| Element | Value |
| :-- | :-- |
| Columns | `* / 320` |
| Left rows | `68` header / `*` canvas / `60` toolbar |
| Header padding | `24,16,24,0`; back button 36×36 `CornerRadius=18` glyph `E72B`; title 20px Bold |
| Sidebar | `Border` with `BorderThickness="1,0,0,0"`, `Padding="16,18,16,16"`, on `ActionRingSidebarBackgroundBrush` → SwiftX's `SwiftXNavPaneBrush` |
| Bottom toolbar | undo/redo left (`E7A7`/`E7A6`), actions right, all 36×36 `CornerRadius=18` at 15px in secondary |
| Canvas scale | `ScaleTransform` with `CenterX=280 CenterY=250` — Logi's zoom hook, reusable for Small/Medium/Large |

### Ring geometry — the part that maps exactly

Logi's canvas is `560 × 500`, hub ⌀28 at `(280, 250)`, and **eight** action nodes at 45° intervals:
⌀44 buttons, `CornerRadius=22`, 1px border, 15px glyph, each with a label pill (`Height=30`,
`CornerRadius=6`, `Padding="12,4"`, 12px SemiBold) pushed outward. Measured node radius is 120 on
the cardinal axes and ~127 on the diagonals — hand-tuned, effectively ~126.

**SwiftX's ring is also 8 slots** (`RingMetrics.SlotCount = 8`, `UserSlotCount = 6`, the last two
reserved for Settings and Close). Logi's canvas is a 1:1 fit. And ~126 sits inside SwiftX's
`MinRadius 80 … MaxRadius 220`, so the reference layout is reachable from the existing slider —
though `DefaultRadius` is 100, so the default renders tighter than the reference.

Port the visual treatment (node chrome, outward label pills, hub cross) onto the **existing**
`RenderPreview()` in `SettingsWindow.xaml.cs:525`. That method already computes slot positions,
handles drag-reorder with reflow animation, group expansion, and remove badges. Do not rewrite it —
restyle what it draws, and add the label pills it currently lacks. System slots 7–8 should read as
fixed: accent border, no drag.

### Sidebar — ring settings replacing System Actions

Four blocks, in the requested order. Logi's in-layout settings idiom (`ActionRingPage.xaml:779-886`)
is the right one here: 15px Bold item titles, 12px secondary descriptions, real `RadioButton`s.

| Block | Control | Backing |
| :-- | :-- | :-- |
| **Item size** | 3 radio buttons — Small / Medium (default) / Large | `AppConfig.ItemSize`, `RingManager.SetItemSize` |
| **Ring radius** | Slider 80–220, live value readout | `AppConfig.RingRadius`, `RingManager.SetRadius`. Reuse `RadiusSlider` and `NumberBoxScroll` |
| **Hot corner** | 2×2 corner picker + enable toggle + Ring/Quadrant segmented control | `HotCornerEnabled`, `HotCorner`, `HotCornerLayout`, `RingManager.ApplyHotCorner` |
| **Hover style** | Invert / Zoom / Tint pills — **moved out of General**. Zoom and Tint are split buttons: selected, the pill splits 70/30 into its name and a side pill (Zoom: In/Out; Tint: a hover-colour swatch that opens a picker) | `AppConfig.HoverStyle`, `HoverZoomDirection`, `HoverTintColorHex` |

Every one of these already exists in `SettingsWindow`'s Home and General panels with working
handlers. This is a move, not new logic — which is what makes the Ring screen the cheapest of the
four despite looking like the biggest.

---

## 3. Tools screen

Card grid per screenshots 2/3, one card per entry in `ToolCatalog.All`.

**There are four tools, not six.** `ToolCatalog.All` holds Data Formatter, Secure values, Base64,
Certificate — JSON and XML were merged into Data Formatter. The comment at
`SettingsWindow.xaml:558` still says "six fixed built-in tools" and is stale; fix it in passing.

| Card element | Value | Source |
| :-- | :-- | :-- |
| Card | `226 × 268` at 4 columns (or `258 × 268` at 3), `CornerRadius=8`, 1px stroke, box shadow | screenshots |
| Icon | ⌀44 medallion, `ToolDefinition.Glyph` | `ToolCatalog` |
| Toggle | Top-right `ToggleSwitch`, blank On/Off content — "show on the ring" | `ShowDataTool` / `ShowSecureValuesTool` / `ShowBase64Tool` / `ShowCertificateTool` |
| Title | 18px Bold, wraps to 2 lines | `DisplayName` |
| Description | 11.5px secondary, 2 lines | `Description` |
| Pill | Category chip, 22px, `CornerRadius=11` | new, static per tool |
| Footer | Divider + centred `OPEN` in accent | screenshots |

`OPEN` calls the existing `RingManager.OpenTool(ToolKind)`. Drag-to-reorder currently lives on the
`ToolsList` `ListView` (`CanReorderItems`); a card grid needs `GridView` with the same flag, or the
reorder affordance moves to the Ring screen. **Decide this** — it is the one behaviour the card
layout puts at risk.

---

## 4. Elements screen

Same card geometry, 3 columns, one card per user item in `Config.Primary.Items`.

| Card element | Source |
| :-- | :-- |
| Icon | `RingItem.IconImage` when resolved, else `RingItem.Glyph` |
| Kind badge | `RingItemKind` — Application / File / Folder / WebLink / Shortcut / Group / Tool |
| Title | `DisplayName` |
| Subtitle | Kind + truncated `Target`; for a group, `"Group · N items"` |
| Toggle | `RingItem.Hidden` (inverted) |
| Footer | `EDIT` → `AddItemWindow` in edit mode |

Items with `IsSystem` (Settings, Close) are excluded, as the current Elements panel already does.
Groups need an affordance to show children — reuse the existing group-expansion model rather than
inventing a nested grid. An `Add item…` action goes in the screen header, as it does today.

---

## 5. Settings

Settings becomes one screen among five, keeping the Logi rail from
`settings-screen-logi-alignment-plan.md` §D.2 — 270px pane, 44px rows, 4px accent indicator, 11px
Bold caps labels — but with a **shorter list**, because four of its seven panels have left:

| Was | Now |
| :-- | :-- |
| Home | → Ring screen |
| General | **stays** (revised below) |
| Elements | → Elements screen |
| Dev Tools | → Tools screen |
| Feedback | stays |
| About | stays |
| Privacy | stays |

The spec details General only; Feedback, About and Privacy are assumed to stay as they are, with the
About rebuild from the earlier plan's Phase 5 still applying. Say so if they should also move or go.

### General — revised

**Removed** (all update/version UI):

- the `"Logi Options+ version …"` line and SwiftX's `VersionText` equivalent
- `CHECK FOR UPDATE` and `See release notes`
- `Automatically install software updates`

Version information still belongs on About, which reads it from the assembly — this removes it from
General only.

**Kept, in this order:**

| # | Setting | Control | Backing |
| --: | :-- | :-- | :-- |
| 1 | **Language** | `ComboBox`, 170–260 wide, `CornerRadius=6` | ⚠ see below |
| 2 | **Theme** | 4 theme cards — System / Light / Dark / Midnight | `AppConfig.Theme` |
| 3 | **Invoke** | `HotkeyCaptureButton` + enable `ToggleSwitch` | `Hotkey`, `HotkeyEnabled` |
| 4 | **Start with Windows** | `ToggleSwitch` | `StartupService` |
| 5 | **Backup & restore** | Export… / Import… | existing handlers |
| 6 | **Reset** | Reset button | existing handler |

Accent colour is not in the requested list. It has shipped and has a `ColorPicker` and persisted
`AppConfig.AccentColor`; recommend keeping it on General — silently dropping a shipped setting
strands saved values. Flagging rather than deciding.

**Theme cards, not a ComboBox** — four of them, because SwiftX has Midnight where Logi has only
three. Geometry per `settings-screen-logi-alignment-plan.md` §A.2. Keep the `Black` tag verbatim:
it persists by name in `ring.json` and renaming it strands existing configs.

### ⚠ Language has nothing behind it

`Services/Loc.cs` is a hard-coded English-only `Dictionary` — no `.resw` files, no
`ResourceLoader`, no satellite assemblies. A language picker is therefore either inert or a
localisation project in its own right.

The spec says keep it, so the plan keeps it. Three ways to land it, cheapest first:

1. **Single-entry picker** — one item, *English (United States)*, disabled, with a
   "More languages coming" caption. Honest, ~10 lines, no dead control.
2. **System-language passthrough** — *Use system language* + *English (United States)*, persisted to
   a new `AppConfig.Language`, read at startup. Real plumbing, still one language.
3. **Full localisation** — migrate `Loc` to `.resw` + `ResourceLoader`, add locales. A project, not
   a settings row.

**Recommend (1) now and (2) alongside the first real second language.** Shipping Logi's six-language
list against a single-language string table would be a dead control of exactly the kind §F.1 of the
earlier plan warns against.

---

## Screen-to-code map

| Screen | Reuses | New |
| :-- | :-- | :-- |
| Shell | `SettingsWindow` chrome, theme + accent engine, `WindowChrome`, `AppTitleBar` | Screen router (`Frame` + pages, or `Visibility` switching like `Nav_SelectionChanged` today) |
| Home | — | Greeting header, 3 nav boxes, shadow treatment |
| Ring | `RenderPreview`, drag/reorder/group logic, radius + item-size + hot-corner + hover handlers | Node/pill restyle, 320px sidebar, 60px toolbar, 68px header |
| Tools | `RebuildToolsList`, `ToolCatalog`, `OpenTool`, the four `Show*Tool` flags | Card template, `GridView`, reorder decision |
| Elements | `RebuildItemsList`, `RingItem` model, `AddItemWindow` | Card template, group affordance |
| Settings | Feedback / About / Privacy panels, all General handlers except the removed ones | Logi rail, theme cards, Language row |

## Phases

| # | Phase | Depends on | Size |
| --: | :-- | :-- | :-- |
| 0 | Tokens — Phase 1 of `settings-screen-logi-alignment-plan.md`, unchanged | — | S |
| 1 | Shell: rename `SettingsWindow` → shell, add the router, five screens as empty pages, retarget `RingManager.OpenSettings` and the tray menu | 0 | **L** |
| 2 | Move General's ring settings to the Ring screen; move the Home panel's preview across; trim General per §5 | 1 | M |
| 3 | Ring screen visuals: 68/​*/60 rows, 320px sidebar, node + label-pill restyle | 2 | L |
| 4 | Card template as a reusable control; Tools screen | 1 | M |
| 5 | Elements screen on the same template | 4 | M |
| 6 | Home screen: greeting header, nav boxes, shadows | 1 | M |
| 7 | Settings rail + theme cards + Language row + About rebuild | 0, 1 | M |
| 8 | Docs: update `DESIGN.md`, the standards §9 matrix, and fix the stale six-tools comment | all | S |

Phase 1 is the gate. Phases 4–6 are parallelisable once it lands.

## Open questions

1. **Does the shell replace `SettingsWindow`, or sit beside it?** Plan assumes replace.
2. **Tool reorder** — keep drag-to-reorder in a `GridView`, or move it to the Ring screen? (§3)
3. **Accent colour** — keep on General, or drop? (§5)
4. **Feedback / About / Privacy** — stay under Settings, as assumed?
5. **Language** — option 1, 2, or 3? (§5)
6. **Does the tray still quit to nothing?** A main window changes what closing means. Recommend:
   closing the shell hides it, the app stays in the tray, Quit is still tray-only.
7. **Window size** — 1040×700 fits the card grids at 3 columns; the Ring screen's `* / 320` leaves
   720px for a 560-wide canvas, which works. Confirm the shell is still fixed-size and
   non-maximisable, or make it resizable now that it hosts grids.

## Non-goals

Ring overlay behaviour, tool page internals, the `RingWindow` render path, security/privacy copy,
and anything in `tests/` — no test references `SettingsWindow` or any settings `AutomationId`, so
this restructure has no test coverage and no test breakage.
