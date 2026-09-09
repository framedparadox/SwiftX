# SwiftX — Security, Performance and Windows Design Review

**Date:** 2026-09-08
**Branch:** `claude/windows-app-security-perf-design-fcusa5`
**Scope:** a second full pass over `src/` — security, performance, and conformance to
[`windows-app-design-standards.md`](windows-app-design-standards.md) — plus the tests for
everything changed.
**Predecessor:** [`reliability-performance-and-security.md`](reliability-performance-and-security.md),
whose findings are all still fixed and are not revisited here.

---

## 1. Summary

| # | Finding | Class | Severity | Reachable by | Status |
|---|---------|-------|----------|--------------|--------|
| 1 | Copied secrets were placed in **clipboard history and cloud clipboard** | Security | **High** | Pressing Copy in Secure values | Fixed |
| 2 | `MaxConfigBytes` was declared and **never applied** — config import read any file whole | Security | **High** | Importing a mis-picked file | Fixed |
| 3 | `MaxIconBytes` was declared and **never applied** — a custom icon read any file whole, on every render | Security | **High** | Setting a custom icon | Fixed |
| 4 | Favicon responses were **buffered in full, then size-checked** | Security | Medium | A web-link item on a hostile/broken host | Fixed |
| 5 | Certificates and private keys were **never released** — native contexts and key material outlived their window | Security | Medium | Opening any certificate or PFX | Fixed |
| 6 | The diagnostic log had **no size ceiling** | Security | Low | A repeating fault in a long-running session | Fixed |
| 7 | A failed `SetWindowRgn` **leaked its GDI region** | Security | Low | Region set failing under GDI pressure | Fixed |
| 8 | The ring **re-extracted every shell icon on every summon** | Performance | **High** | Every summon | Fixed |
| 9 | Syntax colouring allocated **four strings and a brush per token** | Performance | **High** | Typing in any document | Fixed |
| 10 | The hot corner made a **WinRT activation every 50 ms, forever** | Performance | Medium | Always (hot corner is on by default) | Fixed |
| 11 | The line-number gutter was rebuilt with LINQ, **one boxed int and one string per line** | Performance | Low | Any repaint | Fixed |
| 12 | **12 Settings/Add-item controls had no accessible name** | Design (§7.2) | **High** | Any Narrator user | Fixed |
| 13 | The **ring carried no accessibility metadata at all** | Design (§7.2) | **High** | Any Narrator user | Partly fixed — §6.2 |
| 14 | An icon-and-text button had no name and read out its **PUA glyph** | Design (§7.2) | Medium | Narrator on the Formatter sidebar | Fixed |
| 15 | The ring **ignores High Contrast** — acrylic and hardcoded colours regardless | Design (§7.1) | **High** | Any contrast-theme user | **Open — §6.3** |
| 16 | 36 spacing values off the 4 px grid | Design (§6.1) | Low | — | **Open — §6.4** |

Findings 2, 3 and 15 share a shape worth naming: **the codebase documented a protection it did not
have.** `InputLimits` declared four ceilings and applied two. `windows-app-design-standards.md` §9
certifies the app at "100% Compliance" including a row for accessibility that reads
*"automated `AutomationProperties.SetName` across all tools and custom buttons"* — while the
Settings window had none at all and the ring had none at all. A constant that nothing calls and a
compliance matrix that nothing checks fail identically: silently, and with a paper trail saying
otherwise.

---

## 2. Method

Static analysis of every service, model, control and tool page, plus the two XAML windows and the
theme dictionary. Where a claim could be checked mechanically it was: the accessible-name audit is
a script that parses both XAML files and reports every `Button`/`ToggleSwitch`/`ComboBox`/`TextBox`/
`Slider` with no `Name`, no `LabeledBy`, no `Header` and no string `Content`, and it is what turned
"probably fine" into the twelve rows of finding 12. The same script re-run after the fix reports
clean.

### What was executed, and what was not

This review was written on Linux against a `net10.0-windows10.0.26100.0` WinUI 3 target, so the
normal `dotnet build` / `dotnet test` route is unavailable: the build reaches the Windows-only
`XamlCompiler.exe` and stops with *"Exec format error"*. That blocks the XAML half of the build, not
the C# half — so the C# was compiled and the tests were run, by driving Roslyn directly.

**Compiled.** `dotnet restore` succeeds under `-p:EnableWindowsTargeting=true`, which puts all 209
reference assemblies on disk — the real `Microsoft.WinUI`, `Microsoft.Windows.SDK.NET` and
WindowsAppSDK projections, not stand-ins. `csc` was then invoked against that reference set with the
project's own settings (`nullable:enable`, `langversion:latest`, `unsafe+`), the SDK-generated
`GlobalUsings` and `AssemblyInfo`, and the same source generators the real build uses
(`LibraryImportGenerator`, `WinRT.SourceGenerator`). Both assemblies compile with **zero errors**:
the app (every file except three XAML code-behinds, see below) and the **entire** test project.

That is a real semantic check, not a syntax check. It is what confirms, among other things, that
`DataPackagePropertySet` really does convert to `IDictionary<string, object>` — the one API in this
change set that was assumed rather than known.

**Executed.** VSTest's runner is platform-gated for this TFM, but `[Fact]`/`[Theory]` are attributes
and `Assert` is an ordinary library, so a small reflection driver discovers and invokes them. Every
test in the project was run this way, before and after the change, from the same harness:

| | Passed | Failed |
|---|---|---|
| Baseline (`a10b89d`, pre-change) | 247 | 7 |
| This branch | **289** | 7 |

The two failure sets are **identical, line for line** — verified by diffing them. All seven are
pre-existing environment limits, not defects: four need DPAPI (`ProtectedData` throws
`PlatformNotSupportedException` off Windows) and three need real Windows paths such as `C:\Windows`
to exist. Twenty-one `[Theory]` cases use `MemberData`/`ClassData`, which the small driver does not
enumerate; that count is identical in both runs too.

So: **+42 passing cases, zero regressions.**

**Not executed, and therefore not claimed:**

- **The XAML markup compile.** `XamlCompiler.exe` cannot run here, so the four `.xaml` files were
  checked only for XML well-formedness. The attached properties they now set are confirmed to exist
  by the C# compile, but `AutomationProperties.LabeledBy="{Binding ElementName=TargetLabel}"` in
  `AddItemWindow.xaml` is markup that no compiler has seen. It is a standard WinUI idiom; it is
  still the one edit in this change set with no mechanical check behind it.
- **Three XAML code-behinds** — `App.xaml.cs`, `SettingsWindow.xaml.cs`, `AddItemWindow.xaml.cs` —
  were replaced with stubs, since compiling them needs generated halves that only the XAML compiler
  produces. **No C# changed by this review lives in them**; their `.xaml` files changed, their
  `.cs` files did not. `RingWindow.xaml.cs`, which *was* changed, is compiled for real against a
  hand-written stand-in for its generated half.
- **Anything visual.** The app was never launched, no window was rendered, and no screenshot was
  taken. This is why finding 15 is left open (§6.3) rather than fixed blind.

Sweeps that came back clean, recorded as evidence of absence:

- **Interop error handling** — every `DllImport` whose result is passed to
  `Marshal.GetLastWin32Error()` carries `SetLastError = true`. No mismatches.
- **GDI object lifetime** — `SetEllipticHitRegions` deletes each intermediate elliptic region and
  hands the union to `SetWindowRgn`, which takes ownership. The one gap was the failure path
  (finding 7).
- **Single instance** — the mutex is `Local\`-scoped and keyed by a hash of the data directory, so
  two SwiftX instances pointed at different directories can coexist and two pointed at the same one
  cannot.
- **XML hardening** — `DtdProcessing.Prohibit`, null resolver, `MaxCharactersFromEntities`, and the
  depth ceiling from the previous review are all still in place on the single shared load path.
- **Secret logging** — no `Diag.Log` call site passes a secure value, a certificate, a password or
  clipboard content. `SecureValueStore` logs the exception message, never the payload.

---

## 3. Security findings

### 3.1 Copied secrets went to clipboard history and to the user's other devices — *High*

`SecureValuesPage`'s Copy did this:

```csharp
var package = new DataPackage();
package.SetText(entry.Value);
Clipboard.SetContent(package);
```

That is correct for ordinary text and wrong for a password. Windows 10/11 does two further things
with clipboard content by default:

- **Clipboard history** (Win+V) retains the last 25 entries and shows them to whoever reaches that
  keyboard afterwards.
- **Cloud clipboard** roams the entry to every device signed in to the same Microsoft account.

SwiftX encrypts these values at rest with DPAPI *specifically* so they never leave this account on
this machine — the tool's own on-screen blurb said so — and then Copy handed them to a feature
whose entire purpose is to sync them off it. The encryption was not defeated; it was bypassed, at
the one moment the user actually reaches for the value.

**Fixed** by [`SecretClipboard`](../src/SwiftX/Services/SecretClipboard.cs), which stamps the two
documented opt-out properties (`CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`) onto the
package. It takes the property bag rather than the `DataPackage` so the decision is unit-testable
without a WinRT activation — `DataPackagePropertySet` implements `IDictionary<string, object>`, so
callers pass `package.Properties` unchanged.

Deliberately **not** applied to the other three Copy commands. The formatter's output, a Base64
result and a certificate's PEM are documents the user is moving between tools; dropping those from
Win+V would be a bug, not a protection. Only Secure values copies a secret.

The on-screen blurb now says what actually happens.

### 3.2 and 3.3 Two ceilings that existed only as constants — *High*

`InputLimits` declares four ceilings. Two were wired to callers. Two were not:

| Constant | Intended caller | What the caller actually did |
|----------|-----------------|------------------------------|
| `MaxConfigBytes` (8 MB) | `RingStore.ImportFrom` | `File.ReadAllText(path)` — no ceiling |
| `MaxIconBytes` (16 MB) | `IconService.FromFileAsync` | `File.ReadAllBytesAsync(path)` — no ceiling |

Both take a path the user picked from a picker, so neither is guaranteed to be the kind of file it
expects. Import on a mis-picked disk image is an `OutOfMemoryException` thrown out of a settings
command. The icon path is worse than a one-off, because it runs **per item on every render**: a
large file set as a custom icon was re-read whole every single time the ring came up.

This is the same defect class the previous review found three times — *untrusted input reaching an
unbounded resource decision* — and it survived that review because the guard was present as a
constant. Grepping for `MaxConfigBytes` found a declaration and a doc comment; nothing looked wrong.

**Fixed.** `ImportFrom` now goes through a new synchronous `InputLimits.ReadText`, added rather than
duplicating the metadata check inline so the ceiling stays in one place; `FromFileAsync` goes through
the existing `ReadBytesAsync` with `MaxIconBytes`. Both refuse from `FileInfo.Length` before a byte
is read, which is the whole point — a check made after the read has already lost.

### 3.4 Favicon responses were buffered before being measured — *Medium*

```csharp
var bytes = await resp.Content.ReadAsByteArrayAsync();
return bytes.Length is > 0 and < 2_000_000 ? bytes : null;
```

The limit was applied to what was *used*, never to what was *allocated*. By the time
`bytes.Length` was consulted, the entire response was already in memory. A host serving a
multi-gigabyte body under `/favicon.ico` — a compromised site, a misconfigured one, or the
third-party fallback service — was buffered in full and then discarded.

This is the only place in SwiftX where the length of a payload is chosen by someone other than the
user, which makes it the only place where "we check afterwards" is not merely untidy.

**Fixed:** `ResponseHeadersRead`, a `Content-Length` pre-check for honest servers, and a bounded
copy loop that abandons the read the moment the source proves longer than the ceiling — so the
guard holds whether the sender declares a length, lies about it, or omits it.

### 3.5 Certificates and private keys were never released — *Medium*

Nothing in the app called `Dispose` on an `X509Certificate2`. `CertificateReader` produced them,
`CertificatePage` displayed them, and both `Render` and `Clear` dropped the reference.

Two consequences, the second the one that matters:

1. Each certificate wraps a native `CERT_CONTEXT` — unmanaged memory the GC cannot see and
   therefore has no pressure to reclaim. A keystore of a few hundred certificates, re-read on every
   wrong-password retry, accumulates those until the finalizer queue notices.
2. **A PKCS#12 carries live private-key material.** It is loaded with `EphemeralKeySet`, so nothing
   is written to a key store — but it is resident in this process, and dropping the reference means
   it stays resident until finalization rather than until the user closed the window.

`CertificatePage` also held `_lastBytes` — the raw keystore file, kept so a password could be
retried against it — for the life of the window, past Clear and past close.

**Fixed.** `ReadOutcome` is now `IDisposable` and owns its certificates; `CertificatePage` disposes
the outgoing outcome whenever it replaces one, and releases both the certificates and the file
bytes (zeroed) on Clear and on window close, through the `PageClosing` token the base class already
provides for exactly this.

### 3.6 and 3.7 Two small ones

**The log had no ceiling.** `Diag` truncated at startup and appended forever after. That was
survivable when it only recorded startup and failures, but the app logs every launch, and the two
things that never stop — the hot corner's 20 Hz poll and the ring's outside-click timer — each log
on fault. A repeating fault wrote until the disk said no. Now capped at 4 MB, after which the file
starts over with a header saying so. The class comment also now states what must never be written
there, since the file sits at a fixed, predictable path in `%TEMP%` and is not encrypted.

**A failed `SetWindowRgn` leaked its region.** The API takes ownership of the handle only when it
succeeds; on failure the caller still owns it. `ApplyHitRegions` runs on every render and every
hover pill, and the process-wide GDI limit is 10 000 objects. Now deleted and logged on failure.

---

## 4. Performance findings

### 4.1 The ring re-extracted every icon on every summon — *High*

`RenderCurrent` fired `ResolveIconAsync` per item, unconditionally. The existing comment said so
outright: *"Every render fires one of these per item, unconditionally and with no de-dup."* And a
render happens on every summon, every theme change and every settings save.

What that costs per item is not small. An app, file or folder goes through the shell's system image
list, a jumbo-icon lookup, `GetIconInfo`, `GetDIBits`, a GDI+ PNG encode and a XAML bitmap decode. A
web link re-reads its cached favicon from disk and re-decodes it. Ten items means ten of those, on
every appearance of a window whose entire value proposition is appearing instantly — to arrive back
at the picture already on screen.

**Fixed** with an identity key rather than a timer or a size-capped cache.
`RingItem.IconKey` is everything `LoadIconAsync` reads — kind, target, custom icon path, custom
glyph — joined with U+001F, a separator that cannot occur in a path or a URL. `IconSignature`
records what the current `IconImage` was resolved from. A render skips the item when the two agree.

Three properties make this correct rather than merely fast, and each has a test in §7:

- **A failure is never cached.** `IconImage` stays null and the next render retries, which is what
  lets a favicon that missed on the first summon appear on a later one.
- **The key is the staleness guard too**, and a stronger one than the `Target` comparison it
  replaces: editing the custom icon path or glyph now also invalidates, so a slow resolution
  landing after an edit is discarded instead of painted over the newer one.
- **A null result on a changed key is assigned, not ignored.** That clears a bitmap the item is no
  longer entitled to — which incidentally fixes a latent bug: choosing a glyph for an item that
  already showed a shell icon would previously have left the icon in place, because the old code
  only ever assigned non-null results and `GlyphVisibility` is derived from `IconImage`.

`RingItem.IconImage`'s setter is now idempotent as well, matching `CustomGlyph`'s: reassigning the
same reference raised three change notifications that every bubble's handler then acted on for
nothing.

### 4.2 Syntax colouring allocated four strings and a brush per token — *High*

`SyntaxPalette.ForegroundFor` returned its colours through:

```csharp
private static Color C(string hex)
{
    hex = hex.TrimStart('#');
    byte r = System.Convert.ToByte(hex[..2], 16);   // three range slices,
    byte g = System.Convert.ToByte(hex[2..4], 16);  // three more strings
    byte b = System.Convert.ToByte(hex[4..6], 16);
    return Color.FromArgb(255, r, g, b);
}
```

Four string allocations and six characters of hex parsing **per call**, to re-derive a constant.
Both callers are render loops over documents of up to 100 000 characters: `CodeView.Render` builds
one `Run` per token, and `CodeEditor.Highlight` colours one document range per token on its 180 ms
debounce — on the UI thread, on every keystroke batch. `CodeView` then constructed a
`new SolidColorBrush(...)` per token on top of that, though there are only ever as many distinct
brushes as there are `TokenKind` values.

**Fixed.** The palette's hex literals are now `uint` literals (`C(0x9CDCFE)`) — identical to read in
source, folded at compile time, zero allocations. `CodeView` memoizes brushes per `(kind, theme)`.
Per-token cost goes from four string allocations plus a brush construction plus a parse, to one
dictionary lookup.

The brush cache is **per instance, not static**, and deliberately so: a `SolidColorBrush` is a
`DependencyObject` with thread affinity to the tree that first uses it, and SwiftX opens a window
per tool. A process-wide cache would hand one window's brush to another — the kind of sharing that
works in every test and fails on a user's machine.

### 4.3 The hot corner made a WinRT activation every 50 ms, forever — *Medium*

`HotCornerWatcher.Poll` resolved `DisplayArea.GetFromPoint(...)` on every tick. The hot corner is
enabled by default and the watcher runs for as long as SwiftX is in the tray, so that is a COM
activation and an allocation twenty times a second, indefinitely, to re-derive a rectangle that
only changes when the pointer crosses monitors or the user reconfigures their displays.

**Fixed** by memoizing the rectangle while the pointer is still inside it, with a 2-second
expiry. Crossing to another monitor misses the cache immediately (the pointer is outside the cached
rectangle); a display reconfiguration under a stationary pointer is picked up within the expiry;
`Configure` invalidates explicitly. In the common case — a stationary or same-monitor pointer — the
activation rate drops from 20/s to 0.5/s.

The containment test this rests on is half-open on its far edges, because that is how Windows lays
monitors out: a 1920-wide primary at x=0 owns columns 0–1919 and the secondary starts at 1920.
Treating the far edge as inside would let two monitors claim the same column and pin the pointer to
whichever was cached first — so the hot corner would fire at a coordinate nowhere near a corner.
That is asserted at every edge in §7.

### 4.4 The gutter — *Low*

`string.Join('\n', Enumerable.Range(1, lineCount))` allocated an enumerator, a boxed `int` and a
separate string per line before joining them. Replaced with a `StringBuilder` loop, capacity capped
at 1 MB so a document near the 50 MB file ceiling grows the builder rather than reserving hundreds
of megabytes up front. The previous review listed this under "considered and not changed"; it is
changed now because it is five lines and strictly better, not because the measurement changed.

---

## 5. What was checked and found correct

Not everything examined was wrong, and the negatives are worth recording so the next pass does not
re-derive them:

- **`JksReader`** is genuinely hardened. The count guard added in the previous review is in the
  right place — before any collection is sized, not merely before the items are read — and the
  cursor bounds-checks every read.
- **`CertificateReader`** uses the non-obsolete `X509CertificateLoader` APIs with
  `Pkcs12LoaderLimits.Defaults` and `EphemeralKeySet`. The format routing (PEM sniff → DER → PKCS#7)
  fails closed at each step.
- **`Base64Codec`** is forgiving on input and strict on output: invalid UTF-8 is *reported*, not
  replaced with U+FFFD, which would silently corrupt a decoded secret.
- **`SecureValueStore`** encrypts key names as well as values, which is the right call — a key name
  like `Prod DB password` is sensitive on its own — and its v1→v2 migration rewrites the file
  immediately so plaintext names do not survive the read.
- **`MessageWindow`** roots its `WndProc` delegate for the window's lifetime and catches everything
  in the callback, so a throwing handler cannot unwind through native code.
- **`Launcher`** never builds a command line by concatenation; `OpenWithPicker` uses `ArgumentList`.
- **`ShortcutResolver`** declares only the vtable slots it calls, in interface order, with a comment
  explaining why that ordering is load-bearing. It is correct.

---

## 6. Design review against `windows-app-design-standards.md`

### 6.1 Accessible names — twelve controls had none (§7.2)

The standard is unambiguous: *"Every interactive element (Button, TextBox, Toggle, List item) must
provide an accessible name."* The audit script found twelve that did not.

| Window | Controls with no name |
|--------|----------------------|
| `SettingsWindow.xaml` | `ItemSizeBox`, `RadiusSlider`, `HotCornerEnabledToggle`, `HotCornerBox`, `ThemeBox`, `HoverStyleBox`, `HotkeyEnabledToggle`, `StartupToggle` |
| `AddItemWindow.xaml` | `TargetBox`, `NameBox`, `ToolBox` |
| `FormatterPage.cs` | `SidebarButton` (icon + text content) |

Every one of them *looks* labelled — there is a `TextBlock` beside it in the next grid column. But a
`TextBlock` in a neighbouring cell is a visual association, not a programmatic one, so Narrator
announced "combo box" with no indication of what it selects. The `ToggleSwitch`es are the worst
case: they carry `OnContent=""` `OffContent=""`, so there was nothing at all to fall back on.

Settings had **twelve `AutomationProperties.AutomationId` attributes and zero names** — automation
IDs for the test harness, nothing for the user.

**Fixed:** explicit `AutomationProperties.Name` on the eight Settings controls and the two
Add-item controls whose labels are static; `AutomationProperties.LabeledBy` on `TargetBox`, whose
label is rewritten at runtime per item kind and would otherwise drift out of step with a literal.
`SidebarButton` gets a name from its label and pushes its `FontIcon` to the raw accessibility view —
its glyph is a private-use codepoint, which is not something to read out.

### 6.2 The ring had no accessibility metadata at all (§7.2) — *partly fixed*

`RingWindow.xaml` and `RingWindow.xaml.cs` contained **no occurrence of the string
`AutomationProperties`**. The ring — the app's primary surface, the thing the product is — is drawn
as bare `Border` elements on a `Canvas` with pointer handlers. A `Border` is not a control. A screen
reader saw an unnamed canvas where the whole launcher is.

**Partly fixed.** Each bubble now carries the display name of the item it launches and sits in the
content view; the glass, tint, icon, glyph and group arc are pushed to the raw view as decoration.
The canvas itself is named.

**Deliberately not fixed here:** the ring is still not keyboard-operable and its bubbles still have
no invokable automation peer, so Narrator can now *read* the ring but cannot *activate* it. That
needs real peers, focus management and arrow-key navigation — a feature, not a review fix, and one
that changes input handling on a WinUI island window with a Win32 hit region. It is the largest
remaining accessibility gap in the app and should be scheduled as work in its own right.

### 6.3 The ring ignores High Contrast (§7.1) — *open, and deliberately so*

§7.1 is explicit about precedence: under a Windows contrast theme an app must *"not draw custom
hardcoded colors, syntax token coloring, or translucent acrylic"* and must bind to the platform
contrast tokens instead.

`CodeView`, `CodeEditor` and `ToolStatusBar` all honour this — they call `HighContrast.IsActive()`
and stand their custom palettes down. **The ring does not.** `CreateBubble` attaches a
`DesktopAcrylicBackdrop` per bubble and paints from `ThemeHelper.ColorFor`, a hardcoded black/white
switch, with no contrast check anywhere in the path. Under Aquatic, Desert, Dusk or Night Sky the
ring keeps painting translucent acrylic and hardcoded colours.

**This is left open on purpose.** The fix is a visual change to the ring's paint path, and this
review could not build or run the app. The predecessor document's §9 records exactly what happens
when the ring's rendering is changed on a theory rather than on a screenshot: a plausible,
well-reasoned change put a square acrylic panel behind every circle and had to be reverted in full,
and its own conclusion was that *"any future attempt must start with a visual check of the current
build."* Shipping an unverified second one would repeat that mistake with the same reasoning that
produced the first.

The remediation is small and known, and should be done by someone who can see the result:

1. In `CreateBubble`, skip the `SystemBackdropElement` entirely when `HighContrast.IsActive()` — an
   acrylic backdrop is the one thing §7.1 names that cannot be recoloured into compliance.
2. Paint the tint `Border` from `SystemColorWindowBrush` and the glyph from
   `SystemColorWindowTextColor`, resolved through the window root rather than
   `Application.Current.Resources` (see `ThemeHelper`'s own class comment for why).
3. Re-render on the high-contrast transition. Note that `HighContrast.IsActive()` constructs an
   `AccessibilitySettings` per call and discards it, so nothing currently observes
   `HighContrastChanged` — the object has to be held to subscribe.
4. Check all four contrast themes on screen before merging, and keep the existing `CornerRadius`
   clips in place throughout: §9.2 establishes they are load-bearing, not redundant.

### 6.4 Spacing grid (§6.1) — *open, reported not changed*

§6.1 requires all margins, paddings and control dimensions on a 4 px grid. Across the two XAML
windows, 36 values are off it:

| Value | Occurrences |
|-------|-------------|
| 2 | 12 |
| 6 | 17 |
| 10 | 3 |
| 14 | 3 |
| 18 | 1 |

Reported rather than corrected. These are cosmetic drift, not defects; 6 px in particular is a
common half-step in real Fluent layouts. Thirty-six unverifiable layout edits in a review that
cannot render the result is a poor trade against the one thing they would buy, which is agreement
with a document this same review is already correcting.

### 6.5 Conformance that holds

- **Typography** — 47 uses of the semantic type-ramp styles (`BodyStrong`, `Caption`, `Title`,
  `Subtitle`). The remaining literal `FontSize` values are icon sizes in §5.3's 12–22 px band and
  the editor's 13 px monospace, both legitimate.
- **Iconography** — glyphs are built from codepoints via `char.ConvertFromUtf32`, exactly as §5.3
  prescribes, with comments explaining that a PUA literal in source can silently corrupt.
- **Corner radii** — cards and panels at 8, controls at 6, the 44 px icon tile at 10. Within §3.
- **Materials** — Mica on Settings, per-bubble `DesktopAcrylicBackdrop` on the ring, solid fallbacks
  defined for all three theme dictionaries including `Default`, which is what is live in the window
  before `ActualTheme` resolves.
- **Semantic brushes** — `CardBackgroundFillColorDefaultBrush`, `AccentFillColorDefaultBrush`,
  `CardStrokeColorDefaultBrush` and friends are used throughout the tool pages. The hardcoded
  values that remain are SwiftX's own ring surfaces and the Settings plates, both documented at
  length in `Colors.xaml` with the reasoning for each.
- **Keyboard accelerators** — Ctrl+Enter (Format/Run), Ctrl+F (Find), Escape (dismiss), and the
  per-tool command bindings in `EditorToolPage`. §7.3 satisfied.
- **Focus visuals** — no `UseSystemFocusVisuals` override anywhere, so the platform visuals are
  retained by default.
- **Theme resource keys** — every key referenced from `SettingsWindow.xaml` exists in all three
  theme dictionaries. A key present in only one would be an unconditional `XamlParseException` the
  moment that theme went live; there are none.

### 6.6 A note on the compliance matrix

`windows-app-design-standards.md` §9 certifies SwiftX at **100% compliance**, including a row
reading *"Dynamic detection via `HighContrast.IsActive()`; automated `AutomationProperties.SetName`
across all tools and custom buttons — 100% — Compliant"*.

At the time it was written, the Settings window had zero accessible names, the ring had zero
accessibility metadata of any kind, and the ring made no high-contrast check at all. That row was
not a small overstatement; it was the exact opposite of the state of the code, on the two axes it
names.

The matrix has not been edited by this review — rewriting the document under audit is the reviewer
marking their own homework a second time. It should be corrected in a change of its own, and it is
worth treating the whole matrix as unverified until each row has something mechanical behind it.
The accessible-name row now does: it is the script described in §2, and it can be run again.

---

## 7. Tests added

Four files, and every one of them asserts something that was silently wrong before this review —
which is the bar for adding a test rather than a comment.

| File | Covers |
|------|--------|
| [`Security/BoundedInputTests.cs`](../tests/SwiftX.Tests/Security/BoundedInputTests.cs) | Config-import ceiling (refused *and* still accepting a real config), `InputLimits.ReadText`'s three-way contract, BOM consumption, the icon ceiling, ceiling ordering, and the bounded favicon reader — over, exactly at, empty, and a drip-fed stream |
| [`Security/SecretLifetimeTests.cs`](../tests/SwiftX.Tests/Security/SecretLifetimeTests.cs) | Certificate and private-key release, idempotent and empty-outcome disposal, and the clipboard opt-out including its literal property names |
| [`Services/IconIdentityTests.cs`](../tests/SwiftX.Tests/Services/IconIdentityTests.cs) | `IconKey` reuse and invalidation per input, rename-is-not-invalidation, field-separator collisions, setter idempotence, and that the signature never reaches `ring.json` |
| [`Services/DisplayBoundsCacheTests.cs`](../tests/SwiftX.Tests/Services/DisplayBoundsCacheTests.cs) | Half-open display-rectangle containment at every edge, negative-origin monitors, empty-rectangle invalidation — plus `PlainTokenizer`, which had no coverage at all |

Three of these deserve their reasoning stated, because in each case the obvious test is the weaker
one:

- **The favicon reader is tested with a drip-fed stream**, not just a `MemoryStream`. A
  `MemoryStream` returns everything in one read; a socket does not. A bounded reader that mistook a
  short read for the end of the body would truncate real favicons and pass every `MemoryStream`
  test ever written, failing only over a network.
- **"Exactly at the ceiling" is tested separately from "over it."** A guard that refuses big things
  is easy to write with an off-by-one that silently drops everything of precisely the limiting
  size, and a refuses-big-things test never catches it.
- **Certificate disposal is asserted through post-disposal behaviour**, accepting either
  `CryptographicException` or `ObjectDisposedException`. The native handle is exactly the thing a
  managed test cannot observe, and pinning the assertion to one exception type would make it a
  statement about which internal check runs first rather than about whether the handle was
  released.

`InternalsVisibleTo("SwiftX.Tests")` was added to the app project for two members that are worth
asserting directly but have no business on the public surface: `IconService.ReadBoundedAsync` and
`HotCornerWatcher.ContainsPoint`.

All four files were compiled and run (§2): **42 cases, all passing**, against a baseline where the
same harness reports 247 passed / 7 failed and this branch reports 289 passed / **the same** 7
failed.

**Not covered, and why:** `Diag`'s 4 MB truncation. Exercising it means driving roughly 70 000
append-and-close cycles through the file, which is tens of seconds for one assertion about a
constant. The guard is a single comparison in `Log`; it is reviewed, not tested.

---

## 8. Open items, in priority order

1. **High Contrast on the ring** (§6.3) — the largest remaining standards gap. Needs a screen.
2. **Keyboard and invoke support on the ring** (§6.2) — the largest remaining accessibility gap.
   Naming the bubbles is a floor, not a fix.
3. **Run the suite on Windows.** It has been run here (§2: 289 passed, 7 environment-only failures,
   identical to baseline), but through a reflection driver on Linux. That leaves three things
   unproven: the XAML markup compile, the seven tests that need DPAPI and real Windows paths, and
   the 21 `MemberData` cases the driver does not enumerate. A single `dotnet test` on Windows
   closes all three.
4. **`App.UnhandledException` swallows everything** — carried over from the previous review's §8.4,
   still unaddressed, still worth a severity distinction.
5. **Correct the §9 compliance matrix** (§6.6), and treat its remaining rows as unverified until
   each has a mechanical check behind it.
6. **Spacing grid** (§6.4) — cosmetic, 36 values, do it with the app on screen.
