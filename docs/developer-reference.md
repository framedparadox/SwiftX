# SwiftX — Developer Reference

Technical details moved out of the README so the user-facing document stays focused on behaviour.
This document covers source structure, design invariants, testing, theming internals, localisation
implementation, brand assets, the full configuration schema, and Store packaging.

---

## Contents

- [Source structure](#source-structure)
- [Key design invariants](#key-design-invariants)
- [Test suite](#test-suite)
- [Theme internals](#theme-internals)
- [Language internals](#language-internals)
- [Brand and assets](#brand-and-assets)
- [Configuration reference](#configuration-reference)
- [Packaging for the Microsoft Store](#packaging-for-the-microsoft-store)

---

## Source structure

```
src/SwiftX/
  App.xaml.cs              Startup, single-instance lock, unhandled-exception guard
  OrbitManager.cs          Owns config, tray, hotkey, hot corner, windows, activation
  OrbitWindow.xaml(.cs)    The orbit itself: layout, animation, hover, flyout, input
  ShellWindow.xaml(.cs)    The SwiftX app window: Home, Orbit, Tools, Elements, Settings
  OnboardingWindow.xaml    The first-run set-up guide
  AddItemWindow.xaml       "Add to orbit" / "Add item", including drag-and-drop from Explorer

  Models/
    AppConfig.cs           Persisted settings
    OrbitItem.cs           One orbit entry
    OrbitProfile.cs        One orbit's items
    OrbitMetrics.cs        All orbit geometry, as one set of derived numbers
    SystemOrbitItems.cs    Settings / Close / Add-new, synthesized not stored
    ToolCatalog.cs         The built-in tools and the Tools-group migration
    HotCorner.cs           Corner enum, layout enum, and the trigger-patch test
    QuadrantLayout.cs      The quarter circle: radius, angles, corner mapping, item pick
    HotCornerItems.cs      What the corner shows: default pick vs stored arrangement
    HotkeyGesture.cs       Parse/format a system-wide shortcut
    ElementCategory.cs     Category buckets for the Add-to-orbit sidebar and Elements screen
    MouseTriggerAction.cs  Gesture enum for the mouse trigger (LongPress/DoubleClick/None)
    MouseTriggerButton.cs  Button enum for the mouse trigger

  Services/
    MessageWindow.cs       One hidden HWND: tray callbacks, WM_HOTKEY, WM_TIMER
    HotkeyService.cs       RegisterHotKey, keyed by id
    HotCornerWatcher.cs    Pointer polling, dwell, re-arm, drag guard
    MouseTriggerWatcher.cs Mouse-button trigger: long press, double-click, or plain press
    MouseDoubleClickDetector.cs  Double-click timing logic
    TrayIconService.cs     Notification-area icon and its native menu
    WindowChrome.cs        Borderless/transparent/hit-region window plumbing; cloaking
    IconService.cs         Shell icons (jumbo), favicons, disk cache
    OrbitStore.cs          Load/save/import/export
    Launcher.cs            Starting whatever an item points at
    SecureValueStore.cs    DPAPI-protected key/secret storage
    AppLogo.cs             The SwiftX mark, as vector geometry (see Brand and assets)
    Greeting.cs            The Home screen's time-of-day line
    OnboardingCatalog.cs   What the set-up guide offers, existence-checked per machine
    LanguageCatalog.cs     Culture tag, flow direction, font-size compensation per language
    StoreUpdateChecker.cs  Background Microsoft Store update check
    Formats/, Syntax/      JSON/XML parsing, validation, tokenizing
  ToolPages/               The four tool UIs
  Controls/AppTitleBar.cs  Title bar for the app's own and the tool windows
  Controls/HandCursor.cs   Pointer-cursor helper for clickable orbit sidebar controls
  Interop/NativeMethods.cs The Win32 surface, grouped by what needs it

  Assets/SwiftX.ico        Executable icon; also the shell and tray icon
  Images/                  MSIX tile set

img/SwiftX.png             Master art every raster asset is generated from
TERMS.md                   Terms and conditions, linked from Settings > About
docs/                      Design standards and the mockups
notes/                     Internal design/review notes (not shipped, not linked from the app)
tools/                     One-off maintenance scripts (brand asset generation)
```

---

## Key design invariants

**Geometry lives in `OrbitMetrics`.** All orbit geometry — radii, band positions, item angles,
flyout geometry, quadrant layout — is derived from one radius through `FlyoutRadiusFor`,
`FlyoutBandStartFor`, `ContentRadiusFor` and friends. The same drawing code serves both the full
orbit and the quarter circle; the quadrant just feeds in a different radius. `OrbitMetrics` is
computed once per config change, not per frame.

**The overlay's hit region is real clipping.** `WindowChrome.SetEllipticHitRegions` clips DWM's
composited pixels, not just input, so the region must bound everything painted — hover growth and
an open flyout included, plus a second circle around the hover pill while one is showing.

**System orbit items are never persisted.** `SystemOrbitItems` synthesizes Settings, Close, and
Add-new at render time. They cannot be reordered, hidden, or deleted, and never appear in the
Elements list or the Add-to-orbit sidebar.

**Cloaking on window activation.** `WindowChrome.SetCloaked` hides the window from the compositor
between activation and the first painted frame, preventing the blank-white-flash that WinUI
produces before content is ready. The same guard covers `ShellWindow` on first open.

---

## Test suite

```bash
dotnet test tests/SwiftX.Tests/SwiftX.Tests.csproj -p:Platform=x64
```

The suite covers the parts that can be reasoned about without a window on screen:

- `Models/HotCornerZoneTests` — trigger patch: corner pixel of each corner, only ever one corner
  matching, mid-edge and off-display rejection, displays at a negative origin, and the clamp that
  stops an oversized patch swallowing half the screen.
- `Models/QuadrantLayoutTests` — every item wholly on screen from all four corners, no two
  touching, round trip between corner-local and canvas angles, a radius left alone when it already
  fits, and which items the corner picks by default.
- `Models/HotCornerItemsTests` — following the orbit until edited, seeding on first edit, an
  arrangement surviving orbit changes, QuickAccess and Tools staying live once locked in, the
  fallback copy for an item that left the orbit, reset, and the four-place cap.
- `Models/HotCornerGroupLinkTests` — linking hot-corner groups back to their orbit counterparts on
  startup and import.
- `Models/ToolsGroupTests` — Tools-group migration: adoption by name, consolidation, adding a
  missing tool, slot placement, leaving an intentionally tool-free orbit alone.
- `Models/MouseTriggerActionTests`, `Models/HotkeyGestureTests`, `Models/SystemOrbitItemsTests`.
- `Formats/…` — JSON/XML formatting, validation, sanitizing, and XML security.
- `Services/…` — round trip through `OrbitStore`, item construction, icon identity, Base64,
  certificate and JKS readers, value masking, display-bounds cache, code folding,
  `MouseTriggerWatcher` dispatch, `MouseDoubleClickDetector` timing, `LanguageCatalog` coverage.
- `Security/…` — general hardening, bounds on every input whose length a remote party or an opened
  file gets to choose, and the lifetime of a secret in memory and on the clipboard.
- `Performance/PerformanceBudgetTests` — tool pipelines staying interactive on heavy payloads and
  growing linearly with input size.
- `Resilience/CrashAndHangRegressionTests`, `Resilience/WindowActivationFrameTests`,
  `Resilience/DockSyncSourceTests`, `Load/ConcurrencyAndEnduranceTests` — malformed and hostile
  input, activation-frame sequencing, dock synchronisation, and sustained concurrent use.

**Test isolation:** `TestEnvironment` points `SWIFTX_DATA_DIR` at a unique temp path from a
`[ModuleInitializer]` — before anything reads `OrbitStore.DataDirectory`, which is a `static
readonly` resolved once per process. This is data-loss prevention: `SecureValueStore` and
`OrbitStore` both save whole-file, so a test that writes through them without the redirect would
replace the running user's real store.

---

## Theme internals

### WinUI resource system

WinUI only ever resolves `{ThemeResource}` against `Light`, `Dark`, and `Default` element themes.
**Midnight** and **Frosted glass** therefore cannot be independent theme dictionaries — both ride
Dark's `ElementTheme` (native chrome, title bar and stock controls stay dark-themed) and are told
apart at the brush level instead.

`ShellWindow.ApplySurfaceOverrides` shadows the shell's surface keys with element-local entries in
the window root's own resources for Midnight and Frosted glass. `ThemeHelper.ColorFor`'s `AppTheme`
overload answers for everything painted straight onto an element (which is the same set of values
and must agree with `ApplySurfaceOverrides` by hand).

The `Black` enum member keeps its name because `JsonStringEnumConverter` persists enum values by
name; renaming it would strand every config that already has `"Black"` in `orbit.json`. The UI
labels it **Midnight**.

Switching between Dark and Midnight needs a nudge: `RequestedTheme` never changes value between
the two, and WinUI only re-walks `{ThemeResource}` lookups when it does. `ForceThemeResourceRefresh`
flips the window root to the other theme and straight back.

A `{ThemeResource}` re-resolution reaches only what is *bound*. Brushes written straight onto
elements — the Elements and Tools screens build every card that way — are local values no dictionary
can reach, so `ShellWindow.RepaintThemedContent` re-runs all of those builders on every theme
change.

### Mica

`ShellWindow`, `OnboardingWindow`, and every tool window (`ToolWindowBase.ApplyBackdrop`) attach
`MicaBackdrop` (`MicaKind.BaseAlt`) for every theme but Frosted glass, which gets a thin
`DesktopAcrylicController` (`FrostedGlassBackdrop`) instead. `ApplyBackdrop` swaps between the
two and is guarded on the type already attached, since rebuilding a backdrop tears down its
composition target and flickers the window.

### Frosted glass

`FrostedGlassPalette` carries the source theme's affine alpha curves (`opacity * slope + offset`,
clamped) and both the resource override and `ThemeHelper` resolve from it, so a card painted in
code and the one beside it painted from a `{ThemeResource}` cannot disagree.

The frost level (`FrostedGlassLevel.Light/Medium/Dark`) lives in `FrostedGlassBackdrop.Level` —
static, app-wide state set by `OrbitManager`. Changing it writes one float onto the live acrylic
controller rather than rebuilding the backdrop.

| Level | Backdrop opacity | Card fill |
|-------|-----------------|-----------|
| **Light** (default) | 0.65 | `#59181820` |
| **Medium** | 0.80 | `#6B181820` |
| **Dark** | 0.95 | `#7C181820` |

### Orbit acrylic bubbles

Each item bubble is three stacked layers inside the one `Border` that every entrance, hover and
flyout animation already targets:

1. A `SystemBackdropElement` carrying its own `DesktopAcrylicBackdrop`, bottom-most, sampling the
   real desktop behind the bubble.

2. A theme tint over the blur — `#66000000` Dark, `#66FFFFFF` Light, `#CC000000` Midnight.

3. The icon or glyph, and a group's outward arc.

The acrylic disc is drawn half a pixel smaller than the tint (`BackdropInset`).
`SystemBackdropElement` can only round itself with a composition rounded-rect clip whose edge is a
hard stair-step, while the tint's rounded background is antialiased by XAML — at equal sizes the
aliased disc would peek out from behind the feathered one. The inset closes that gap.

Hover under **Invert** swaps the tint to a fully opaque inverted fill. **Zoom** scales only.
**Tint** applies the user's colour; the glyph colour adapts for contrast. An opaque tint fades the
acrylic disc out. Close always goes `#EF6F6F` regardless of hover style.

### Light palette

Light is a pure white canvas with cards a cool `#F0F4F8` — panels recessed into the page. The nav
pane's selected-item fills (`#DCE5EE` / `#D2DDE8` / `#C8D5E2`) are spelled out explicitly instead
of using WinUI's `SubtleFill*` tokens, which Windows accent personalization can tint.

### Accent colour

`ApplyAccentColorOverride` always paints the accent, even the default, because falling through to
the framework resolves to the *Windows* system accent rather than SwiftX's own. One stored colour
becomes three states: the colour itself, an 18% lighter hover, and an 18% darker pressed.

### High contrast

`HighContrast.IsActive()` is checked by the three places that paint a colour the OS palette cannot
vouch for: the code editor and the read-only code view drop their syntax colours, and the tool
status bar's error chip clears its custom red.

---

## Language internals

Every string lives in `Services/Loc.cs` and the per-language tables under
`Services/Localization/` (one file per language), keyed by short dotted names (`"Tray.Show"`)
rather than the English text itself. English (US) is the base table; any key a language hasn't
translated falls back to it row by row.

`LanguageCatalog` carries:
- The culture tag used to resolve **System** against Windows' own UI language.
- The `FlowDirection` flag (RTL for Arabic and Hebrew).
- Per-language font-size compensation where the script needs a larger size for readability.

The tool windows (Data Formatter, Secure values, Base64, Certificate) and the tray menu re-read
the string table fresh each time they build their UI, so language switches take effect immediately
for them. Windows built once from literal XAML text (Settings itself, Add item, the orbit's own
tooltips) still need a restart.

---

## Brand and assets

The SwiftX mark is four gradient petals pinwheeled around a cross-shaped gap. Each petal is a
square with two opposite corners sharp (straddling the centre) and two rounded hard.

`img/SwiftX.png` (1000×1000 RGBA) is the master. Every raster asset is derived from it:

| Asset | Where it shows up |
|-------|-------------------|
| `src/SwiftX/Assets/SwiftX.ico` | Executable, Explorer, Alt-Tab, notification area. Nine frames, 16–256 px. |
| `src/SwiftX/Images/Square*.png`, `Wide310x150Logo.png`, `StoreLogo.png`, `SplashScreen.png` | MSIX tile set. |

Regenerate all raster assets after changing the master:

```bash
python tools/generate-brand-assets.py     # needs Pillow
```

### The in-app mark

The mark drawn *inside* the app is not a bitmap. `Services/AppLogo.cs` composes it as live vector
geometry over a 48×48 design box, wrapped in a `Viewbox`, so it stays crisp at any hub size and
any display scale.

It comes in two forms:
- `AppLogo.Create` — the flat mark, for the About card and title bars (sits on a solid surface).
- `AppLogo.CreateFloating` — adds a composition drop shadow per petal, masked by the petal's own
  painted shape. Used by the orbit's hub and the orbit preview on the Home page, where the mark
  floats straight on the desktop.

Either form can play itself in/out (`AppLogo.Mark.PlayEntrance` / `PlayExit`): the whole mark
swings a quarter turn while each petal, going round clockwise from top-left a beat apart, untwists
and grows into its quadrant. Only the petal layer turns — the shadow stays put.

**Petal gradients** (outer corner → centre-facing corner):

| Petal | Outer | Centre |
|-------|-------|--------|
| Top-left | `#FF0A76` | `#FFD510` |
| Top-right | `#2CD098` | `#1D4ADB` |
| Bottom-left | `#2CD098` | `#1D4ADB` |
| Bottom-right | `#F1067E` | `#1A65B5` |

These are hard-coded (not theme resources) because the mark must read as *coloured* against every
SwiftX surface.

---

## Configuration reference

### Top-level keys

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Orbits` | array | one profile | Orbit profiles. Only the first is used today. |
| `LaunchAtStartup` | bool | `false` | Mirrors the "Start with Windows" switch. |
| `Theme` | `Light`/`Dark`/`Black`/`FrostedGlass`/`System` | `Dark` | Window and orbit theme. `Black` is **Midnight** in the UI. |
| `FrostedGlass` | `Light`/`Medium`/`Dark` | `Light` | Frost level; read only while `Theme` is `FrostedGlass`. |
| `AccentColorHex` | `"#RRGGBB"` or null | `null` | App accent colour. `null` uses the built-in violet. |
| `Language` | `System`/`EnglishUS`/`EnglishUK`/`French`/`German`/`Spanish`/`Italian`/`PortugueseBR`/`Dutch`/`Polish`/`Turkish`/`Russian`/`Arabic`/`Hebrew`/`Hindi`/`Japanese`/`ChineseSimplified`/`Korean`/`Ukrainian`/`Vietnamese`/`Indonesian`/`Thai`/`Swedish`/`Danish`/`Norwegian`/`Czech`/`Greek`/`Romanian` | `System` | UI language. |
| `HoverStyle` | `Invert`/`Zoom`/`Tint`/`Animate` | `Invert` | Item hover effect. |
| `HoverZoomDirection` | `In`/`Out` | `In` | Zoom direction; read only while `HoverStyle` is `Zoom`. |
| `HoverTintColorHex` | `"#RRGGBB"` or null | `null` (black) | Tint colour; read only while `HoverStyle` is `Tint`. |
| `HoverAnimation` | `Pulse`/`Bounce`/`Wobble`/`Shake`/`Spin`/`Float` | `Pulse` | Motion; read only while `HoverStyle` is `Animate`. |
| `Hotkey` | string | `"Ctrl+Shift+X"` | Summon shortcut, in readable form. |
| `HotkeyEnabled` | bool | `true` | Whether the shortcut is registered. |
| `MouseTriggerEnabled` | bool | `true` | Whether a mouse-button gesture summons the orbit. |
| `MouseTriggerButton` | `Left`/`Right`/`Middle`/`X1`/`X2` | `Middle` | Which mouse button. |
| `MouseTriggerAction` | `LongPress`/`DoubleClick`/`None` | `LongPress` | The gesture. |
| `HotCornerEnabled` | bool | `true` | Whether a screen corner summons the orbit. |
| `HotCorner` | `TopLeft`/`TopRight`/`BottomLeft`/`BottomRight` | `TopLeft` | Which corner. |
| `HotCornerLayout` | `Orbit`/`Quadrant` | `Orbit` | Whether the corner opens the whole orbit or the quadrant. |
| `HotCornerOrbit` | array | empty | Your corner arrangement. Empty means "follow the orbit". |
| `ItemShape` | `Circle`/`Squircle`/`Square` | `Circle` | Orbit item outline. |
| `ItemSize` | `Small`/`Medium`/`Large` | `Medium` | Orbit item draw size. |
| `OrbitRadius` | number or null | `null` (100) | Hub-to-item distance, 80–220 px. |
| `ShowDataTool` | bool | `true` | Data Formatter in the orbit's Tools item. |
| `ShowSecureValuesTool` | bool | `true` | Secure values in the Tools item. |
| `ShowBase64Tool` | bool | `true` | Base64 in the Tools item. |
| `ShowCertificateTool` | bool | `true` | Certificate in the Tools item. |
| `ReuseToolWindows` | bool | `true` | Reopening a tool reuses its window. No UI switch; config only. |
| `FeedbackRating` | number | `0` | Stars set on the Feedback page. Never sent anywhere. |
| `Seeded` | bool | — | Set once after the first run plants the default items. |

### Orbit items

Each entry in `Orbits[0].Items`, a group's `Children`, and `HotCornerOrbit`:

| Field | Meaning |
|-------|---------|
| `Id` | Stable identity. `HotCornerOrbit` entries match back to orbit items by this. |
| `Kind` | `Application`, `File`, `Folder`, `WebLink`, `Shortcut`, `Group`, or `Tool`. |
| `DisplayName` | The label in the hover pill. |
| `Target` | Path, URL, or tool name (`Data`, `SecureValues`, `Base64`, `Certificate`). The Tools group uses `swiftx:toolsgroup`. |
| `Arguments` | Command-line arguments, for the kinds that launch something. |
| `CustomIconPath` | An icon file to use instead of the resolved one. |
| `CustomGlyph` | A Segoe Fluent codepoint or short text (`B64`) to draw instead of an icon. |
| `CustomIconColor` | `"#RRGGBB"` glyph tint, or null for the theme's own foreground. |
| `Hidden` | Kept in the config but not drawn. The Tools page's switches work this way. |
| `Children` | A group's items. |

Settings, Close, and Add-new are **never** stored — the orbit synthesizes them at render time.

### HotCornerOrbit resolution

An empty `HotCornerOrbit` means "follow the orbit": QuickAccess, Notepad, Tools, and Settings,
recomputed each time. Once you arrange the corner by hand, each entry is mapped back to the live
thing it names before drawing:

- `Settings` → the synthesized Settings entry.
- `swiftx:toolsgroup` → the Tools item on the orbit (Tools page's switches still govern its children).
- `swiftx:quickaccess` → a freshly gathered QuickAccess group (picks up new links and shortcuts).
- Anything else → the orbit item with the same `Id`.

An entry naming something that has since left the orbit keeps working from its own stored copy.

### Startup migrations

Run at every launch; the config is re-saved only when one actually changed something:

1. Stored Settings/Close entries from older builds are stripped.
2. Orbit items pointing at the retired `Json`/`Xml` tool targets are rewritten to `Data`; duplicates
   are collapsed.
3. **Tools are gathered into one group.** Found by its marker target first, then by name (which
   adopts the "Dev tools" group earlier builds seeded). Tools loose at the top level are moved in;
   duplicates are dropped; any tool the orbit had no item for is added.
4. Every tool item's `Hidden` flag is re-pointed at the Tools page's switches.

### Legacy config format (ring.json)

Builds from before the "orbit" rename wrote `%AppData%\SwiftX\ring.json` with `Rings`,
`RingRadius`, and `HotCornerRing` as key names. `ring.json` is read **only** when `orbit.json` does
not exist. `Services/LegacyConfigFormat.cs` maps the old file forward as a pass over the JSON
itself before deserializing:

| Old | New |
|-----|-----|
| `Rings` | `Orbits` |
| `RingRadius` | `OrbitRadius` |
| `HotCornerRing` | `HotCornerOrbit` |
| `HotCornerLayout: "Ring"` | `HotCornerLayout: "Orbit"` |

`ring.json` is never written to; the first save after upgrade lands on `orbit.json` and leaves the
old file in place so downgrading still finds the config it had.

---

## Packaging for the Microsoft Store

SwiftX ships **unpackaged** for everyday development (`WindowsPackageType=None`). The
[`src/SwiftX/Package.appxmanifest`](../src/SwiftX/Package.appxmanifest) adds single-project MSIX
packaging as an opt-in: add `-p:WindowsPackageType=MSIX` to the build command.

**Partner Center identity** — must match exactly or Store ingestion rejects the upload:

| | |
|---|---|
| Package identity name | `44492ajaykontham.SwiftX` |
| Publisher | `CN=93C75305-77D7-448E-B1C4-591A0E9665E1` |
| Publisher display name | `ajaykontham` |

The app declares `runFullTrust` — required because SwiftX P/Invokes Win32 directly for hit-test
regions, global hotkey, hot-corner polling, and the tray icon.

### Building the package

```bash
dotnet build src/SwiftX/SwiftX.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never
```

Produces `src/SwiftX/AppPackages/SwiftX_<version>_x64_Test/SwiftX_<version>_x64.msix`. Repeat
with `-p:Platform=ARM64` for the other architecture. `SelfContained`/`WindowsAppSDKSelfContained`
are already `true`, so the package carries its own .NET and Windows App SDK runtimes.

### Local sideload testing

The build above is unsigned. To install on this machine before submitting:

- Run the generated `Add-AppDevPackage.ps1` — it creates a throwaway self-signed certificate,
  trusts it, and installs the package, **or**
- Sign it yourself: `New-SelfSignedCertificate -Subject "CN=93C75305-77D7-448E-B1C4-591A0E9665E1"
  -CertStoreLocation Cert:\CurrentUser\My -Type CodeSigningCert`, then `signtool sign /fd SHA256
  /a /f <exported .pfx> …`

This is throwaway dev-machine-only trust — irrelevant to the certificate Partner Center uses.

### Submitting to Partner Center

Partner Center re-signs the package on ingestion, so the sideload certificate is irrelevant to
submission. Before uploading:

- **Bump** `<Version>` / `<FileVersion>` / `<AssemblyVersion>` in `SwiftX.csproj` **and**
  `Identity/Version` in `Package.appxmanifest` together. The fourth segment must stay `0`
  (Store-assigned). Store submissions must strictly increase.
- **Run the Windows App Certification Kit** against the built package.
- Partner Center's listing (screenshots, description, age rating, privacy policy URL) is filled in
  on their site; [`TERMS.md`](../TERMS.md) is the source material.
