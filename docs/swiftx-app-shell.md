# The SwiftX app shell — as built

**Status:** shipped. This is the record of what the redesign actually did.
**Supersedes the execution of:** [`swiftx-app-shell-plan.md`](swiftx-app-shell-plan.md),
[`full-app-mockups-logi-base.md`](full-app-mockups-logi-base.md),
[`settings-and-tools-mockups.md`](settings-and-tools-mockups.md) and
[`settings-screen-logi-alignment-plan.md`](settings-screen-logi-alignment-plan.md). Those four are
kept as written — they hold the measurements and the mockups this was built to, and the reasoning
behind decisions this document only records the outcome of.
**Design base:** [`framedparadox/windows-app-mx`](https://github.com/framedparadox/windows-app-mx)
@ `logiApp/dotnet` (`be474f5`), project `LogiApp/`.
**Mockups:** [`docs/images/`](images) — `16-app-home`, `17-app-ring`, `18-app-tools`,
`21-app-settings` are the four this was built against; `4`–`8` are the earlier round whose
three-nav-box Home survived into it.

---

## What changed

`SwiftX` had no main window. `SettingsWindow` was the only real one, and the app's whole surface was
a seven-item `NavigationView` inside it. That window is now **`ShellWindow`** — titled *SwiftX app*
— with five screens, and Settings is one of them rather than the window itself.

**`SettingsWindow` was promoted, not replaced.** Every handler, the orbit preview's slot geometry
and drag-reorder, the group-expansion model, the hot-corner quadrant, the theme/accent engine and
the window chrome are the same code, re-homed. That is what kept a change this size from being a
rewrite of behaviour: the redesign moves panels into Logi's frame and restyles them, and the only
logic that is genuinely new is the screen router, the theme-card picker, and the first-run guide.

| Was | Now |
| :-- | :-- |
| `SettingsWindow` — a `NavigationView` with 7 panels | `ShellWindow` — 5 Visibility-switched screens |
| Home panel (orbit preview + size/radius/hot corner) | **Orbit Settings** screen, plus Hover style moved in from General |
| General panel | **Settings › General**, minus Hover style, plus *Set-up guide* |
| Elements panel | **Elements** screen |
| Dev Tools panel | **Tools** screen |
| Feedback / About / Privacy panels | **Settings** › the other three rail sections |
| — | **Home** screen: greeting header + three portrait nav boxes |
| — | **`OnboardingWindow`**: the first-run set-up guide |

## The five screens

**Home** is Logi's `MainPage` header verbatim — `Padding="40,20,20,20"`, greeting at 30px Bold in
the `Segoe UI, Cambria, Georgia, 'Times New Roman'` stack, right-aligned 36×36 `CornerRadius=18`
toolbar buttons. Logi has five of those; two have no SwiftX counterpart (ADD DEVICE — no device
pairing; the profile avatar — no account) and its Action Ring button is a nav box of its own below,
so what remains is Logi's theme toggle and its gear, in Logi's own order.

Where Logi's content row is empty by design (its device grid is product art, not source), three
`268 × 340` portrait boxes sit at a 34px gutter: **Orbit Settings**, **Tools**, **Elements**. Each is
an ⌀88 icon medallion, a 22px Bold title, a wrapped description, a 22px meta pill, then a 1px
divider and a centred `OPEN` at 11px Bold `CharacterSpacing=80` in the accent — Logi's own "ADD" bar.

**Orbit Settings**, **Tools** and **Elements** each take Logi's `ActionRingPage` frame: a 68px header
with a back chevron (`E72B`) and a 20px Bold title.

**Settings** is `LogiApp/SettingsPage.xaml` measured value for value — a 270px sidebar at
`32,24,0,32`, a 36×36 back button, the two-line 30/34 editorial title, a nav stack pulled back by
`-32` so the 4px indicator sits flush at x=0, 44px rows over a `4 / 28 / *` grid, and content at
`48,28,64,48` capped to 780. Four sections, in Logi's own order and casing: `GENERAL`,
`FEEDBACK & SUPPORT`, `PRIVACY & DATA`, `ABOUT`.

## Where the design was followed, and where it was not

Three places depart from the reference deliberately.

**The theme picker is not tap-a-`Border`.** Logi's is, and so were its cards going to be; a tapped
`Border` takes no focus and raises no automation peer, so the picker would have been unreachable by
keyboard and silent to Narrator. Every selectable tile here — the three Home nav boxes and the four
theme cards — is a `Button` wrapping its card instead. Same geometry, same look, with focus visuals
and an automation name for free (`docs/windows-app-design-standards.md` §7.3).

**Tools stayed a `ListView`.** The mockups draw it as a card grid. The card grid was the one part of
the design that would have cost a behaviour — dragging a tool to reorder it inside the orbit's Tools
group — and a working reorder is worth more than the extra card chrome.

**Logi's colours are carried at SwiftX's opacity.** `ShellBackgroundBrush` / `ShellCardBackgroundBrush`
/ `ShellCardBorderBrush` are `#2B2B2B` / `#333333` / `#444444` — Logi's `AppThemeResources.xaml`
Dark dictionary exactly — but the two fills carry the `0xD9` alpha every other Dark plate in
`Themes/Colors.xaml` already carries, so the app window's Mica still reads through. Painting them
flat would have made the shell the one patch of the window Mica could not reach. Light is Logi's
Light dictionary verbatim and fully opaque, because Light carries no alpha anywhere in this app.

Everything else is copied: the metrics, the type ramp, the two letter-spacing values (100 for nav,
80 for CTAs), the theme-card previews' own nested-`Border` graphics, the About hero card's
`90 / *` columns at `CornerRadius=12` `Padding=24`.

## The accent changed

The built-in default moved from SwiftX's teal (`#3DDCD3` Dark / `#0B7C7C` Light) to Logi's violet
(`#8A67FF` / `#6C47FF`). `settings-screen-logi-alignment-plan.md` §D.1 recommended keeping the teal;
`full-app-mockups-logi-base.md` overrode that, and the mockups this was built to are violet.

It is only the *fallback*: a colour already in `AppConfig.AccentColorHex` is untouched, and the teal
is one click away in the picker. Both values live in exactly two places —
`AccentPalette.DefaultHexDark`/`Light` and `SettingsAccentBrush`/`SettingsAccentHoverBrush` in
`Themes/Colors.xaml` — and have to change together.

## First run

`OrbitManager.Start` opens `OnboardingWindow` instead of the app window on a first run. Three steps:
a welcome, a picker for what goes on the orbit, and the keys and corner that summon it (with
*Start with Windows* and *Desktop shortcut* alongside).

Two properties matter more than the UI:

1. **Nothing is written until Finish.** The guide holds its own state and commits once.
2. **The orbit is seeded before the guide opens.** Dismissing it therefore lands exactly where
   SwiftX landed before the guide existed — a working default orbit — rather than an empty one. The
   guide's own tick-boxes start on that same default six (`OnboardingCatalog`'s `DefaultOn` set is
   `SeedDefaults`' six, in the same order), so finishing without touching anything is a no-op by
   design.

Every suggestion is existence-checked against the machine before it is offered, so no tile ever
proposes an app that is not installed. The orbit's six user slots are enforced in the picker (further
tiles disable once the count is reached) and again at commit.

## Non-goals

Untouched by this change: the orbit overlay's render path and behaviour, the four tool windows and
their pages, `AddItemWindow`, the config schema (no new persisted field — first run is still
`!AppConfig.Seeded`), and the security/privacy copy. `tests/SwiftX.Tests` references no window type
and no automation id, so the restructure has no test coverage and broke no test.

## Verification

`dotnet build` and `dotnet test` need Windows — the Windows App SDK's XAML compiler and `MakePri`
are Windows-only binaries, and the test host boots the App SDK through a P/Invoke that cannot load
elsewhere. What was verified off-Windows, against generated stand-ins for the XAML compiler's own
`.g.i.cs`:

- `src/SwiftX` compiles with **zero errors and zero warnings**.
- `tests/SwiftX.Tests` compiles against it with zero errors.
- Every XAML file parses as well-formed XML; every event handler named in XAML resolves to a method
  in its code-behind; every `x:Name` the code-behind reaches exists in its XAML.

**Still to check on Windows**, none of which a compiler can answer:

- The `ThemeShadow` on the three Home nav boxes. It is a shared resource with `HomeShadowPlate`
  added to its `Receivers` from the Loaded handler — outside a popup a caster with an empty
  `Receivers` renders no shadow at all — but whether it lands at the right depth is a look, not a
  compile.
- Light / Dark / Midnight / System across all five screens, plus the guide.
- That a custom accent reaches the Settings rail, the selected theme card, and the guide's step dots.
- The 270px rail and the three 268px boxes at 100 / 125 / 150 / 200% scaling.
