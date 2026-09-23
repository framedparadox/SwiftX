# SwiftX Settings — Logi Options+ Alignment Plan

**Status:** proposal, superseded in scope by [`swiftx-app-shell-plan.md`](swiftx-app-shell-plan.md)
**Scope note:** this document planned Settings alone. The app is now planned as a five-screen
shell (Home / Ring / Tools / Elements / Settings), which moves four of the seven panels below
out of Settings entirely. Part A's extraction — Logi's layout metrics, type ramp and colour
tokens — is still the visual reference for all of it, and Phase 1 (tokens) is unchanged.
Read the shell plan for what goes where; read this one for the measurements.
**Base reference:** `framedparadox/windows-app-mx` @ `e82997e` (branch `claude/swiftx-settings-screen-qamzrj`), project `LogiApp/`
**Target:** `framedparadox/SwiftX` @ `56cfe64`, `src/SwiftX/SettingsWindow.xaml(.cs)` + `src/SwiftX/Themes/Colors.xaml`

This document extracts **every** detail of the Logi app's settings screen from its source, states what
SwiftX has today, and lays out the work to bring SwiftX's Settings to the same design. It supersedes
nothing in `docs/logi_options_design_features.md` — that file is a prose analysis of the *shipping*
Logi Options+ product; this one is measured off the actual `LogiApp` source in this org, and where
the two disagree, this one is the buildable truth (see §C.4).

---

## Sources read

| Repo | File | What it contributed |
| :-- | :-- | :-- |
| windows-app-mx | `LogiApp/SettingsPage.xaml` (798 ln) | The whole settings screen: shell, nav rail, 4 sections |
| windows-app-mx | `LogiApp/SettingsPage.xaml.cs` (195 ln) | Section switching, theme-card selection, transient button states |
| windows-app-mx | `LogiApp/Themes/AppThemeResources.xaml` (160 ln) | `Settings*` brush tokens, Light + Dark |
| windows-app-mx | `LogiApp/App.xaml.cs`, `MainWindow.xaml.cs` | `App.SetTheme` / `GetCurrentTheme`, title-bar repaint on theme change |
| windows-app-mx | `LogiApp/ActionRingPage.xaml` (ln 687–890) | The *second*, in-layout settings surface (§A.7) |
| windows-app-mx | `LogiApp/MainPage.xaml(.cs)` | Entry point: gear button → `Frame.Navigate(typeof(SettingsPage))` |
| SwiftX | `src/SwiftX/SettingsWindow.xaml` (884 ln) | Current NavigationView shell + 7 panels |
| SwiftX | `src/SwiftX/SettingsWindow.xaml.cs` (2533 ln) | Window chrome, theme/accent plumbing, ring preview, panel switching |
| SwiftX | `src/SwiftX/Themes/Colors.xaml` (323 ln) | `Settings*` / `SwiftX*` brushes across Dark / Light / Default |
| SwiftX | `src/SwiftX/Models/AppConfig.cs` | What is persistable today |
| SwiftX | `docs/windows-app-design-standards.md` | House rules the redesign must not break |

---

# Part A — Extraction: the Logi settings screen, in full

## A.1 Shell and navigation model

Logi's settings is a **`Page` hosted in `MainWindow`'s `Frame`**, reached from the gear button on
`MainPage` (`MainPage.xaml.cs:24`). It is not a separate window.

Top-level structure is a **two-column `Grid`**:

```
Grid
├── Col 0  Width="270"   Left sidebar   (Back arrow, editorial title, 4 nav buttons)
└── Col 1  Width="*"     ScrollViewer   (4 stacked sections, exactly one Visible)
```

The sidebar is *not* a `NavigationView`. It is four hand-rolled `Button`s, each containing a
3-column `Grid` (`4 / 28 / *`): a 4px accent `Border` indicator in col 0, empty gutter in col 1,
an all-caps `TextBlock` in col 2. Selection is expressed by **two** simultaneous changes — the
indicator's `Visibility` and the label's `Foreground` — never by a background fill.

Content sections are four sibling `StackPanel`s in a single `Grid`; switching sets
`Visibility` on all four. There is no `Frame`, no page transition, no scroll-position memory.

**Nav order and labels (verbatim):**

1. `GENERAL`
2. `FEEDBACK & SUPPORT`
3. `PRIVACY & DATA`
4. `ABOUT`

Nav items carry **no icons**. Default selection is `GENERAL`.

## A.2 Layout metrics — every measured value

### Sidebar (col 0)

| Element | Value |
| :-- | :-- |
| Column width | `270` |
| Sidebar padding | `32,24,0,32` |
| Back button | `36×36`, `CornerRadius="18"`, transparent bg + border, `Padding="0"` |
| Back glyph | `&#xE72B;` (Back), `FontSize="18"`, `SettingsHeaderBrush` |
| Back tooltip | `"Back to home"` |
| Title block margin | `0,4,0,0`, sits `16` below the back button (`StackPanel Spacing="16"`) |
| Title lines | Two `TextBlock`s: `"Application"` / `"Settings"` |
| Title font | `Segoe UI, Cambria, Georgia`, `FontSize="30"`, `Bold`, `LineHeight="34"` |
| Nav stack margin | `-32,48,0,0` — the negative left margin **cancels the sidebar's 32px padding** so nav rows bleed to the window edge and the 4px indicator sits flush at x=0 |
| Nav row height | `44` (on both the `Button` and its inner `Grid`) |
| Nav row columns | `4 / 28 / *` |
| Indicator | `Border Width="4"`, `SettingsNavIndicatorBrush`, full 44px height |
| Nav label | `FontSize="11"`, `Bold`, `CharacterSpacing="100"`, `VerticalAlignment="Center"` |
| Nav button chrome | `Background`/`BorderBrush` transparent, `Padding="0"`, `CornerRadius="0"`, `HorizontalContentAlignment="Left"` |

### Content area (col 1)

| Element | Value |
| :-- | :-- |
| ScrollViewer padding | `48,28,64,48` |
| Horizontal scroll | `Disabled`; vertical `Auto` |
| Content grid | `MaxWidth="780"`, `HorizontalAlignment="Left"` |
| Section header | `FontSize="22"`, `Bold`, `SettingsHeaderBrush`, `Margin="0,0,0,28"` |
| Setting-block bottom margins | `28` (General items), `24` (Feedback/Privacy items), `32` (before a section break) |
| Two-column setting row | `Grid` with `* / Auto` — label left, control right |
| Description max width | `560` on the copyright block; otherwise governed by the 780 cap |

### Theme picker cards

| Element | Value |
| :-- | :-- |
| Card grid columns | `150 / 16 / 150 / 16 / 150` (3 cards, 16px gutters) |
| Card size | `150 × 130` |
| Card corner radius | `8` |
| Card border | `1.5` thick, `SettingsCardBorderBrush` → `SettingsCardSelectedBorderBrush` when picked |
| Card rows | `82` (preview graphic) / `46` (label strip) |
| Preview corner radius | `7,7,0,0` (inner, so the 1.5 border reads cleanly) |
| Label strip padding | `10,0,10,0`, contents `Spacing="8"` horizontal, vertically centered |
| Radio glyph | `&#xE73A;` unselected / `&#xE73E;` selected, `FontSize="14"` |
| Radio colour | `SettingsMutedBrush` unselected / `SettingsAccentBrush` selected |
| Card label | `FontSize="11"`, `SemiBold` (card 1 uses an embedded `&#x0a;` + `LineHeight="13"` for two lines) |
| Selection affordance | `Tapped` on the `Border` — **not** a `RadioButton`, no keyboard focus, no `AutomationPeer` |

Preview graphics are pure XAML (nested `Border`s, no images):

- **Follow OS** — split 50/50, left `#BACEE4` bg with `#FFFFFF` card, right `#243447` bg with `#1A1A1A` card; accent bars `#7B61FF` (left) and `#00C4B4` (right), grey bars `#CCCCCC`/`#555555`. Inner borders use asymmetric margins (`14,14,-6,0` / `-6,14,14,0`) so the two halves visually join.
- **Light** — `#CADDF3` bg, `#FFFFFF` card inset `18,14,18,0`, bars `#7B61FF` + `#DDDDDD`.
- **Dark** — `#1D2A3A` bg, `#141414` card inset `18,14,18,0`, bars `#00E5C0` + `#3E3E3E`.

Bar geometry throughout: `Height="3"`, widths 20–38, `CornerRadius="1.5"`, `HorizontalAlignment="Left"`.

### About cards

| Element | Value |
| :-- | :-- |
| Hero card | `CornerRadius="12"`, `Padding="24"`, `BorderThickness="1"`, `Margin="0,0,0,24"` |
| Hero columns | `90 / *`; logo `Image` `72×72`, left-aligned, vertically centered |
| Hero text | name `18` Bold · version `13` SemiBold in `SettingsAccentBrush` · blurb `12` wrapped |
| Spec card | `CornerRadius="12"`, `Padding="20"`, inner `Spacing="12"` |
| Spec grid | columns `160 / *`, four rows of `Height="26"` |
| Spec label | `12`, `SettingsMutedBrush`; value `12` `SemiBold`, `SettingsTitleBrush` |
| Legal row | horizontal `Spacing="16"`, `HyperlinkButton`s separated by `&#8226;` bullets in `SettingsMutedBrush` |

## A.3 Typography ramp (as actually used)

| Role | Size | Weight | Extras | Brush |
| :-- | --: | :-- | :-- | :-- |
| Page title (sidebar, 2 lines) | 30 | Bold | `LineHeight=34`, serif-fallback stack | `SettingsHeaderBrush` |
| Section header | 22 | Bold | `Margin 0,0,0,28` | `SettingsHeaderBrush` |
| Hero app name | 18 | Bold | — | `SettingsTitleBrush` |
| Setting title | 14 | SemiBold | — | `SettingsTitleBrush` |
| Hero version line | 13 | SemiBold | — | `SettingsAccentBrush` |
| Body / description | 12 | Regular | `LineHeight=18`, `TextWrapping=Wrap` | `SettingsSubtitleBrush` |
| Inline link (in body) | 12 | Regular | `<Underline>` run | `SettingsAccentBrush` |
| Nav label | 11 | Bold | `CharacterSpacing=100`, ALL CAPS | Nav active/inactive |
| CTA link | 11 | Bold | `CharacterSpacing=80`, ALL CAPS | `SettingsAccentBrush` |
| Card label | 11 | SemiBold | — | `SettingsTitleBrush` |
| Legal / copyright | 11 | Regular | `LineHeight=16`, `MaxWidth=560` | `SettingsMutedBrush` |

Two letter-spacing values only: **100** for nav, **80** for CTAs. Both are all-caps.

## A.4 Colour tokens — complete, both themes

Logi declares these in `Themes/AppThemeResources.xaml` under `ResourceDictionary.ThemeDictionaries`
(keys `Dark` / `Light`; there is **no** `Default` dictionary — see §F.3).

| Token | Dark | Light | Used by |
| :-- | :-- | :-- | :-- |
| `AppBackgroundBrush` | `#2B2B2B` | `#F2F2F2` | Page + sidebar background |
| `SettingsHeaderBrush` | `#FFFFFF` | `#1A1A1A` | Page title, section headers, back glyph |
| `SettingsTitleBrush` | `#FFFFFF` | `#1A1A1A` | Setting titles, card labels, spec values |
| `SettingsSubtitleBrush` | `#A0A0A0` | `#666666` | Descriptions, body copy |
| `SettingsBodyBrush` | `#D0D0D0` | `#333333` | *(declared, unused on this page)* |
| `SettingsMutedBrush` | `#808080` | `#777777` | Spec labels, bullets, copyright, unselected radio |
| `SettingsAccentBrush` | `#8A67FF` | `#6C47FF` | CTAs, hyperlinks, version line, selected radio |
| `SettingsNavInactiveBrush` | `#A0A0A0` | `#444444` | Unselected nav label |
| `SettingsNavActiveBrush` | `#8A67FF` | `#6C47FF` | Selected nav label |
| `SettingsNavIndicatorBrush` | `#8A67FF` | `#6C47FF` | 4px selection bar |
| `SettingsCardBackgroundBrush` | `#333333` | `#FFFFFF` | Theme cards, About cards |
| `SettingsCardBorderBrush` | `#444444` | `#E0E0E0` | Card border, unselected |
| `SettingsCardSelectedBorderBrush` | `#8A67FF` | `#6C47FF` | Card border, selected |
| `SettingsIconBrush` | `#AAAAAA` | `#666666` | The gear on `MainPage` (entry point) |

**The accent is violet, not teal.** `#8A67FF` (Dark) / `#6C47FF` (Light). This directly contradicts
`docs/logi_options_design_features.md`'s "Signature Teal", which describes the shipping Logitech
product, not this codebase. Teal *does* appear in this codebase — but on the Action Ring surface
(`ActionRingAccentBrush` = `#00DDB3` Dark / `#7B61FF` Light), not on App Settings. See §D.1.

## A.5 Section-by-section content inventory

### GENERAL

| # | Control | Detail |
| --: | :-- | :-- |
| 1 | Version line | `TextBlock` `14` SemiBold — `"Logi Options+ version 2.7.961922"` |
| 2 | Update status | `12` subtitle `"You have the latest version!"` + inline `HyperlinkButton` `"See release notes"` (`12` SemiBold, accent) |
| 3 | Check-for-update CTA | Transparent `Button` wrapping `CheckForUpdateText` — `"CHECK FOR UPDATE"` |
| 4 | Auto-update toggle | Row `* / Auto`; `"Automatically install software updates"` + `ToggleSwitch IsOn="True"`, `OnContent`/`OffContent` blank |
| 5 | Language | Label + `ComboBox` `Width="260" Height="38" CornerRadius="6"`, `SelectedIndex="0"`. Items: *Use system language*, *English (United States)*, *Español*, *Français*, *Deutsch*, *日本語* |
| 6 | Color theme | Label + the 3-card grid from §A.2 |

### FEEDBACK & SUPPORT

| # | Block | Detail |
| --: | :-- | :-- |
| 1 | General Feedback | Title + wrapped 12/18 body + CTA `"SHARE GENERAL FEEDBACK"` |
| 2 | Rate your experience | Title + one-line body + CTA `"RATE NOW"` |
| 3 | Troubleshooting and Support | Title + `"Report issues or request support."` + CTA `"REPORT ISSUES/REQUEST SUPPORT"` |
| 4 | Bluetooth connectivity issues | Title + multi-run body with two inline underlined links (*click here*, *Visit FAQ*) and a `LineBreak`; three `CheckBox`es (`IssuePairing`, `IssueDisconnect`, `IssueLag`, all `FontSize="12"`); CTA `"SEND BLUETOOTH REPORT"` |

### PRIVACY & DATA

| # | Block | Detail |
| --: | :-- | :-- |
| 1 | Share usage data for Analytics | `* / Auto` row, wrapped body with inline underlined *privacy policy*, `ToggleSwitch IsOn="False"` `VerticalAlignment="Top"` |
| 2 | Recommendations | Same shape, one-line body, `ToggleSwitch IsOn="False"` |
| 3 | EULA + copyright | `HyperlinkButton "End User License Agreement"` (`12` SemiBold accent) then `11`/`16` muted copyright at `MaxWidth="560"` |

### ABOUT

| # | Block | Detail |
| --: | :-- | :-- |
| 1 | Hero card | `Assets/AppLogo.png` 72px + name `"Logi Options+ (Windows MX)"` + `"Version 2.7.961922 (Release x64)"` + one-paragraph blurb |
| 2 | Spec card | `"System & Runtime Specifications"` + 4 label/value rows: *Target OS*, *UI Framework*, *Managed Runtime*, *Hardware Ecosystem* |
| 3 | Legal & Notices | Heading + 3 bullet-separated links: *Privacy Statement*, *Terms of Service*, *Third-Party Software* |

## A.6 Behaviour — what the code-behind actually does

**`SelectSection(string)`** (`SettingsPage.xaml.cs:48`) is the whole nav engine. Per call it writes
**twelve** properties: 4 section `Visibility`, 4 indicator `Visibility`, 4 label `Foreground`.
Brushes are pulled with `(Brush)Application.Current.Resources["SettingsNavActiveBrush"]`.

**Theme selection** — each card's `Tapped` handler does three things:
`App.SetTheme(theme)` → `UpdateThemeCardSelection(theme)` → `NotifyThemeChanged(theme)`.

- `App.SetTheme` sets `RequestedTheme` on `MainWindow.Content` as a `FrameworkElement`.
- `UpdateThemeCardSelection` resets all three cards (border → default, glyph → `E73A`, glyph fg →
  muted), then applies selected styling to one.
- `NotifyThemeChanged` calls `MainWindow.UpdateTitleBarForTheme(theme)` (repaints caption-button
  colours) and then **re-runs `SelectSection(_currentSection)`** — because the `Foreground` values it
  cached are stale brushes from the previous theme dictionary.

**Transient button copy** — two `async void` handlers drive a text state machine with `Task.Delay`:

- `CHECK FOR UPDATE` → `CHECKING FOR UPDATES...` (1000ms) → `YOU HAVE THE LATEST VERSION!` (2000ms) → reset.
- `SEND BLUETOOTH REPORT` → validates ≥1 checkbox (`PLEASE SELECT AN ISSUE ABOVE`, 2000ms) → `SENDING REPORT...` (1200ms) → `REPORT SENT! THANK YOU` (2500ms) → clears checkboxes → reset.

**Eight handlers are empty stubs**: `ReleaseNotes`, `ShareFeedback`, `RateNow`, `Troubleshoot`,
`Eula`, `PrivacyStatement`, `TermsOfService`, `ThirdParty`.

## A.7 The second settings surface (for completeness)

`ActionRingPage.xaml:687–890` holds an *in-layout* settings view with a different visual language —
worth knowing about so it isn't mistaken for the screen above:

- Header row `68`px: back chevron `E72B` + `"Actions Ring"` at `20` Bold.
- Left nav column `280`, buttons `Width="150"`, `Height="40"`, `CornerRadius="6"`, **filled**
  background when active (`ActionRingSettingsActiveBackgroundBrush`), with icons (`EA3B` LAYOUT,
  `E700` SETTINGS) and `CharacterSpacing="50"` labels.
- Content `ScrollViewer` `Padding="40,20,60,40"`, `MaxWidth="520"`.
- Group headers are `11`/Bold/`CharacterSpacing=80` caps in *secondary* colour (`GENERAL`,
  `APPEARANCE`) — item titles are `15` Bold, descriptions `12`.
- Real `RadioButton`s with a `GroupName`, unlike the tap-a-`Border` theme cards.

Two different selection idioms (4px rail vs. filled pill) and two different accents live in the same
app. **The App Settings screen (§A.1–A.6) is the one this plan ports.**

---

# Part B — SwiftX Settings as it stands

`SettingsWindow` is a **top-level `Window`**, not a page. Key facts:

- `MicaBackdrop { Kind = BaseAlt }`, `ExtendsContentIntoTitleBar = true`, custom `AppTitleBar.WithAppMark("SwiftX Settings")` inserted at row 0 from code-behind.
- Client size fixed at **1040 × 700 dip**, `IsMaximizable = false`, rounded corners, centred on cursor.
- Navigation is a **`muxc:NavigationView`**, `PaneDisplayMode="Left"`, `OpenPaneLength="200"`, settings/back chrome suppressed, every item carrying a `FontIcon` and an `AutomationProperties.AutomationId`.
- **Seven** panels, switched by `Nav_SelectionChanged` on `Tag`, with side effects (`RenderPreview`, `RebuildItemsList`, `RebuildToolsList`).

| Pane group | Items |
| :-- | :-- |
| Menu | Home (`E80F`), General (`E713`), Elements (`E71D`), Dev Tools (`E90F`) |
| Footer | Feedback (`E939`), About (`E946`), Privacy (`EA18`) |

- Content plate `Padding="32,24,32,24"` over `SwiftXCanvasBrush`; panels cap at `MaxWidth` 920 (Home) or 720 (Privacy/Feedback/About).
- Local styles: `Card` (`CornerRadius=8`, `BorderThickness=1`, `Padding=16,14`), `SecondaryText`, `SecondaryCaption`, `AccentLink` (11–12px semibold caps hyperlink).
- Every setting is a **card row**: `Auto / * / Auto` → `FontIcon` · title+description · control.
- Theme is a **4-item `ComboBox`** (Light / Dark / **Midnight** / System). `Midnight` persists as `AppTheme.Black`.
- A **user-settable accent colour** already exists (`ColorPicker` in a `Flyout`, `AppConfig.AccentColor`, applied by `ApplyAccentColorOverride` into `Root.Resources`). Defaults: `#3DDCD3` Dark / `#0B7C7C` Light.
- Three theme dictionaries — `Dark`, `Light`, **`Default`** — plus Midnight handled by `ApplyBlackOverrides` swapping a fixed key list at runtime.
- Existing Settings-only tokens: `SettingsSurfaceBrush`, `SettingsSecondaryTextBrush`, `SettingsAccentBrush`, `SettingsAccentHoverBrush`, `SwiftXCardBackgroundBrush`, `SwiftXCardStrokeBrush`, `SwiftXCanvasBrush`, `SwiftXNavPaneBrush`, `SwiftXRingCanvasBrush`.
- **No UI tests.** `tests/SwiftX.Tests` covers models, services, formats, security and performance only — nothing references `SettingsWindow` or any settings `AutomationId`. The redesign carries no test-breakage risk; it also gets no test safety net.

---

# Part C — Gap analysis

## C.1 Navigation shell

| Aspect | Logi | SwiftX | Delta |
| :-- | :-- | :-- | :-- |
| Container | `Page` in a `Frame` | top-level `Window` | Logi's Back button has **no SwiftX equivalent** — nothing to go back to |
| Rail | hand-rolled buttons, 270px | `NavigationView`, 200px | Replace or restyle (§D.2) |
| Selection cue | 4px bar + label colour | filled pill (Fluent default) | Restyle |
| Icons | none | one per item | Drop or keep (§D.2) |
| Header | 2-line 30px editorial title in the rail | window title bar only | New: add the title block |
| Items | 4 | 7 | Map (§C.2) |

## C.2 Section mapping

| Logi section | SwiftX destination |
| :-- | :-- |
| — | **Home** — ring preview. No Logi analogue; SwiftX-only, must survive |
| `GENERAL` | **General** — largely overlaps; Logi adds update/language, SwiftX adds hotkey/hot-corner/startup/backup/reset |
| — | **Elements** — SwiftX-only |
| — | **Dev Tools** — SwiftX-only |
| `FEEDBACK & SUPPORT` | **Feedback** — already built in Logi's order; needs restyle only |
| `PRIVACY & DATA` | **Privacy** — SwiftX's is far richer and factually different (no telemetry to toggle) |
| `ABOUT` | **About** — SwiftX's is one card; Logi's is three blocks |

**Conclusion:** this is a *visual language* port, not a content port. Six of SwiftX's seven pages have
no Logi counterpart, and three Logi controls (auto-update toggle, language picker, Bluetooth report)
have no SwiftX counterpart. What ports cleanly: the shell, the rail, the type ramp, the colour
tokens, the theme-card picker, and the About page's card structure.

## C.3 Controls that cannot be ported as-is

| Logi control | Why not | Resolution |
| :-- | :-- | :-- |
| `Automatically install software updates` | SwiftX has no updater. `PackagedRuntime` documents that update checks are hidden in the Store build, and the `RingManager.UpdateChecksSupported` its doc-comment cites **does not exist** | Omit. Optionally file the missing member as separate work |
| `CHECK FOR UPDATE` + release notes | same | Omit (or stub against a real GitHub-releases check for the unpackaged build only) |
| Language `ComboBox` | `Services/Loc.cs` is an English-only Phase-1 string table with no resource loader | Omit |
| Bluetooth report | No devices, no Bluetooth, nothing to send | Omit |
| Analytics / Recommendations toggles | SwiftX's Privacy page states there is *no* telemetry. A toggle would contradict shipped copy | Omit — deliberately |
| Back button | No parent frame | Replace with nothing; the window closes |
| Logo `Image` 72px | SwiftX's mark is vector geometry from `AppLogo.Create(48)` | Keep `AppLogo`, size it to 72 in the hero card |

## C.4 Doc conflict to resolve

`docs/logi_options_design_features.md` says the accent is **teal** and cards are `#F0F4F8` /
`#2C2C2C`. `Colors.xaml` already implements exactly that, citing the doc. But the actual `LogiApp`
source uses **violet** `#8A67FF`/`#6C47FF` with cards `#333333`/`#FFFFFF`. Both can't be "the Logi
palette". §D.1 decides; whichever wins, the losing doc gets a note so the next reader isn't misled.

---

# Part D — Decisions required

## D.1 Accent colour — **recommend: keep SwiftX teal as the default**

Adopting violet would change the default accent of a shipping app for fidelity to a reference that
SwiftX's own docs already describe differently. SwiftX users can already pick any accent, so the
violet is one click away for anyone who wants it. **Keep `#3DDCD3` / `#0B7C7C` as shipped defaults;
port the Logi *structure* (`NavActive`, `NavIndicator`, `CardSelectedBorder` as distinct tokens) and
point them at the accent so the user's override reaches them too.** Add a note to
`logi_options_design_features.md` recording the violet found in source.

*Alternative if fidelity is the priority:* swap `DefaultAccentHexDark`/`Light` in
`SettingsWindow.xaml.cs:380-381` to `#8A67FF`/`#6C47FF` and re-tune `SettingsAccentHoverBrush`. One
two-line change; everything else in this plan is unaffected either way.

## D.2 Rail: restyle `NavigationView`, or hand-roll it? — **recommend: restyle**

Hand-rolling matches Logi exactly (that is literally what Logi did) but throws away arrow-key
navigation, focus visuals, `ListItem` automation peers, and pane adaptivity — all of which
`docs/windows-app-design-standards.md` §7.2/§7.3 require. `NavigationViewItem` can be driven to the
same look via its own template keys:

- `NavigationViewItemBackgroundSelected*` → `Transparent` (kill the pill)
- `NavigationViewSelectionIndicatorForeground` → the accent (the 4px bar already exists in the template)
- `NavigationViewItemForeground` / `…Selected` → the nav inactive/active tokens
- `OpenPaneLength` `200` → `270`; item height to `44` via `NavigationViewItemOnLeftMinHeight`

This lands the Logi look with none of the accessibility loss. Escalate to a full hand-rolled rail
only if a specific detail proves untemplatable.

## D.3 Nav labels — **recommend: adopt Logi's caps**

`GENERAL` / `ELEMENTS` / `DEV TOOLS` / `FEEDBACK & SUPPORT` / `PRIVACY & DATA` / `ABOUT` at 11px
Bold `CharacterSpacing=100`. Keep `HOME` first. Keep the icons — Logi drops them, but SwiftX has
seven items where Logi has four, and the icons are what makes seven scannable.

## D.4 Setting rows: keep SwiftX's cards, or go flat like Logi? — **recommend: keep cards**

Logi's General page is flat rows with generous margins; SwiftX's is bordered cards. Cards carry more
weight but SwiftX has many more per-setting controls (hotkey capture, sliders, dual buttons) that
need the visual grouping. **Adopt Logi's *typography and spacing* inside the existing card
shell** rather than deleting the cards. Revisit only if the result reads busy.

## D.5 Where does the two-line title go?

Logi puts `Application` / `Settings` in the rail above the nav. SwiftX has a real title bar already
saying "SwiftX Settings". Recommend: put a two-line `SwiftX` / `Settings` block in the rail at
30/Bold/34, and let the title bar keep its own short caption. It reads as branding, not duplication.

---

# Part E — Implementation plan

Six phases, each independently reviewable and each leaving the app in a working state.

### Phase 1 — Tokens (`src/SwiftX/Themes/Colors.xaml`)

Add to **all three** dictionaries (`Dark`, `Light`, `Default`), and to `BlackOverrideKeys` in
`SettingsWindow.xaml.cs:285` where Midnight needs its own value:

| New token | Dark | Light | Default (=Dark) | Midnight override |
| :-- | :-- | :-- | :-- | :-- |
| `SettingsHeaderBrush` | `#FFFFFF` | `#1A1A1A` | `#FFFFFF` | `#FFFFFF` |
| `SettingsTitleBrush` | `#FFFFFF` | `#1A1A1A` | `#FFFFFF` | `#FFFFFF` |
| `SettingsMutedBrush` | `#808080` | `#777777` | `#808080` | `#808080` |
| `SettingsNavInactiveBrush` | `#A0A0A0` | `#444444` | `#A0A0A0` | `#A0A0A0` |
| `SettingsNavActiveBrush` | → accent | → accent | → accent | → accent |
| `SettingsNavIndicatorBrush` | → accent | → accent | → accent | → accent |
| `SettingsCardSelectedBorderBrush` | → accent | → accent | → accent | → accent |

`SettingsSecondaryTextBrush` already exists and matches Logi's subtitle role (`#AAAAAA` / `#555555`
vs Logi's `#A0A0A0` / `#666666`) — **keep SwiftX's values**, they are within a hair and already
shipped. Do **not** rename `SwiftXCardBackgroundBrush`/`SwiftXCardStrokeBrush`; they are shared with
`AddItemWindow` and the tool windows (see the existing comments in `SettingsWindow.xaml:10-14`).

The three accent-derived tokens must be added to `ApplyAccentColorOverride`
(`SettingsWindow.xaml.cs:334`) so a user's custom accent reaches the rail and the theme cards, not
just links and toggles.

### Phase 2 — Rail (`SettingsWindow.xaml`, `Colors.xaml`)

1. `OpenPaneLength` `200` → `270`.
2. Override the `NavigationViewItem*` template keys listed in §D.2, per theme dictionary.
3. Add a `NavigationView.PaneHeader` carrying the two-line `SwiftX` / `Settings` title
   (`Segoe UI, Cambria, Georgia`, 30, Bold, `LineHeight=34`, `SettingsHeaderBrush`), padded
   `32,24,0,0`.
4. Re-label all seven items to caps per §D.3; set `FontSize=11`, `FontWeight=Bold`,
   `CharacterSpacing=100` via an item style.
5. Keep every existing `AutomationProperties.AutomationId` **unchanged** — they cost nothing and are
   the hook any future UI test will need.

### Phase 3 — Type ramp inside panels (`SettingsWindow.xaml`)

Add to `Grid.Resources`, alongside `Card` / `SecondaryText` / `AccentLink`:

```
SectionHeader   → 22 / Bold  / SettingsHeaderBrush   / Margin 0,0,0,28
SettingTitle    → 14 / SemiBold / SettingsTitleBrush
SettingBody     → 12 / Regular / SettingsSecondaryTextBrush / LineHeight 18 / Wrap
MutedFine       → 11 / Regular / SettingsMutedBrush / LineHeight 16
```

Then sweep all seven panels: `TitleTextBlockStyle` → `SectionHeader`,
`BodyStrongTextBlockStyle` → `SettingTitle`, `SecondaryCaption` → `SettingBody`.
`AccentLink` already matches Logi's CTA (11/Bold/caps) — add `CharacterSpacing="80"` to it.
Content-plate padding `32,24,32,24` → `48,28,64,48`; panel `MaxWidth` 920/720 → **780** everywhere
except Home, which needs its 920 for the ring preview + sidebar row.

### Phase 4 — Theme picker (`SettingsWindow.xaml`, `.xaml.cs`)

Replace the `ThemeBox` `ComboBox` with a **four**-card grid — SwiftX has one more theme than Logi:

`System` (split preview) · `Light` · `Dark` · `Midnight`

Column definitions `150 / 16 / 150 / 16 / 150 / 16 / 150` = 678px, inside the 780 cap. Card
internals exactly per §A.2. Midnight's preview: `#0A0A0A` outer, `#000000` inner card, accent bar in
the current accent, grey bar `#2A2A2A`.

**Do not copy Logi's `Tapped`-on-a-`Border` idiom** — it is unfocusable and invisible to Narrator.
Use `RadioButton`s with `GroupName="AppTheme"` and a `ControlTemplate` that draws the card, so
keyboard, focus visuals and automation come free. Bind selection to the existing
`SelectTheme(AppTheme)` / `ThemeBox_SelectionChanged` logic — rename the handler, keep the body.
Retain `Tag` values `Light` / `Dark` / `Black` / `System` verbatim: `Black` is persisted by name in
`ring.json` and renaming it strands existing configs (`AppConfig.cs:9-18`).

Card selection state must be repainted from `ApplyAccentColorOverride` and from the
`Root.ActualThemeChanged` handler, both already in place.

### Phase 5 — About page (`SettingsWindow.xaml`, `.xaml.cs`)

Rebuild to Logi's three blocks:

1. **Hero card** — `CornerRadius=12`, `Padding=24`, columns `90 / *`. `AboutMark.Content = AppLogo.Create(72)` (was 48). Name `SwiftX` at 18/Bold; `VersionText` at 13/SemiBold in `SettingsAccentBrush`; existing blurb at 12.
2. **Spec card** — `CornerRadius=12`, `Padding=20`, grid `160 / *`, rows of 26. SwiftX's honest four: *Target OS*, *UI Framework*, *Managed Runtime*, *Distribution* (read `PackagedRuntime.IsPackaged` → `"MSIX (Microsoft Store)"` / `"Portable (unpackaged)"`). Source the first three from the csproj rather than hard-coding.
3. **Legal & Notices** — bullet-separated links. SwiftX has GitHub + LICENSE; use *Privacy* (navigates to the Privacy page, as `ReadPrivacyLink` already does), *Licence*, *Third-Party Notices*. Do **not** ship dead links — Logi's eight empty handlers are a pattern to avoid (§F.1).

### Phase 6 — Docs + verification

- Add the violet-vs-teal note to `docs/logi_options_design_features.md` (§C.4).
- Update `DESIGN.md` and `docs/windows-app-design-standards.md` §9 matrix with the new rail and type ramp.
- Run the checklist in Part G.

### Effort

| Phase | Files | Size |
| :-- | :-- | :-- |
| 1 Tokens | `Colors.xaml`, `SettingsWindow.xaml.cs` | S |
| 2 Rail | `SettingsWindow.xaml`, `Colors.xaml` | M |
| 3 Type ramp | `SettingsWindow.xaml` (all 7 panels) | M — wide but mechanical |
| 4 Theme picker | `SettingsWindow.xaml`, `.xaml.cs` | L — new templated control |
| 5 About | `SettingsWindow.xaml`, `.xaml.cs` | M |
| 6 Docs | `docs/*`, `DESIGN.md` | S |

---

# Part F — What *not* to copy

## F.1 Dead handlers
Logi ships eight `{ }` click handlers. Every link SwiftX adds must go somewhere real or not exist.

## F.2 App-level brush lookups
`(Brush)Application.Current.Resources["…"]` resolves against the *application's* theme, not the
element's. SwiftX already documents this exact trap twice (`SettingsWindow.xaml:24-26`, and the
`ReadPrivacyLink` comment at `:842`). Use element-scoped lookups and `Root.Resources`, the way
`ApplyAccentColorOverride` already does.

## F.3 A missing `Default` dictionary
Logi declares only `Dark` and `Light`. SwiftX declares `Default` too, and needs it — `ThemeHelper`
and `AppTheme.System` depend on it. Every token added in Phase 1 goes into **all three**.

## F.4 Unfocusable selection
`Tapped` on a `Border` (theme cards) is mouse-only. §D.4 of the design-standards doc and §7.3
(keyboard navigation) both rule it out. Template a `RadioButton` instead.

## F.5 Fixed pixel widths on text rows
Logi's spec grid uses `Width="160"` label columns and `Height="26"` rows. At 200% scaling with a
longer localised label these clip. Use `Auto` + `MinWidth`, and let rows size to content.

## F.6 Hard-coded version strings
Logi hard-codes `2.7.961922` in two places. SwiftX already reads
`typeof(App).Assembly.GetName().Version` — keep that.

---

# Part G — Verification checklist

**Visual**

- [ ] Rail is 270px; the 4px indicator sits flush against the pane's left edge at x=0
- [ ] Selected nav label is accent-coloured; unselected is `SettingsNavInactiveBrush`; no pill fill in any state
- [ ] Two-line title renders at 30/34 without clipping at 100%, 125%, 150%, 200% scaling
- [ ] All four theme cards render at 150×130 and fit inside the 780 content cap
- [ ] Selected card shows a 1.5px accent border **and** the `E73E` glyph; unselected shows `E73A` in muted
- [ ] Type ramp matches §A.3 on all seven panels

**Theme**

- [ ] Light / Dark / **Midnight** / System each render correctly, including the new tokens
- [ ] Switching theme repaints the rail, the cards' selection state, and the caption buttons in one pass
- [ ] An OS theme flip under `System` does the same (`Root.ActualThemeChanged`)
- [ ] A custom accent from the `ColorPicker` reaches the nav label, the indicator, and the selected card border
- [ ] `Reset to default` returns all of the above to the shipped teal

**Behaviour & state**

- [ ] Every existing setting still round-trips through `ring.json`
- [ ] `AppTheme.Black` still persists as `"Black"`; an existing config with `"Black"` still loads as Midnight
- [ ] Home panel's ring preview, drag-reorder, and group expansion are untouched
- [ ] Panel side effects still fire on nav (`RenderPreview` / `RebuildItemsList` / `RebuildToolsList`)

**Accessibility** (`docs/windows-app-design-standards.md` §7)

- [ ] Tab + arrow keys traverse the rail and the theme cards; focus visuals are visible in all four themes
- [ ] Narrator announces each nav item and each theme card with its name and selected state
- [ ] Windows High Contrast still overrides everything (`Services/HighContrast.cs`)
- [ ] All `AutomationId`s from before the change still resolve

**Build**

- [ ] `dotnet build` clean, no new warnings
- [ ] `dotnet test` — the suite still passes clean (last recorded run: 289 passing, commit `b2df09f`). No UI coverage exists, so this is a regression check on the untouched layers only
