# SwiftX — Reliability, Performance and Security Review

**Date:** 2026-09-07
**Branch:** `feat/acryllic-theme`
**Scope:** crash analysis, hang analysis, performance/load/security testing, ring circle rendering (attempted, reverted)
**Baseline:** 190 tests passing → **275 tests passing**, Debug and Release both clean

---

## 1. Summary

| # | Finding | Severity | Reachable by | Status |
|---|---------|----------|--------------|--------|
| 1 | XML nesting caused unbounded recursion → **StackOverflowException**, an unrecoverable process kill | **Critical** | Pasting deep XML, then "Convert to JSON" | Fixed |
| 2 | XML indentation is quadratic in depth → **OutOfMemoryException** on a 350 KB input | **High** | Pasting deep XML, then "Format" | Fixed |
| 3 | `JksReader` sized a list from an attacker-chosen count → **OutOfMemoryException** from a 35-byte file | **High** | Dropping a crafted `.jks` on the Certificate tool | Fixed |
| 4 | Test suite wrote to the **real** `%AppData%\SwiftX`, destroying the user's secure-value store | **High** | Running `dotnet test` | Fixed (data already lost — see §7) |
| 5 | Editor rebuilt the whole document twice per keystroke | Medium | Typing in a large document | Fixed |
| 6 | Ring bubble circle rendering | — | — | **Attempted, reverted** — the change put a square behind every circle (§9) |

Findings 1–3 are all cases of the same class: **untrusted input reaching an unbounded resource
decision.** Finding 1 is the most serious because a `StackOverflowException` cannot be caught in
.NET — it does not reach `App.UnhandledException`, produces no log line and no dialog. The process
simply disappears.

Items considered and **not** changed are listed in §8, with reasoning.

---

## 2. Method

Analysis was static (reading every service, model and tool page) combined with **executable
probes** — each suspected defect was reproduced against the real code before any fix was written,
and re-run afterwards. Nothing in the table above is inferred; each row has a reproduction.

Pattern sweeps that came back clean, and so are recorded as evidence of absence:

- **Sync-over-async** — no `.Result`, `.Wait()` or `GetAwaiter().GetResult()` anywhere in `src/`,
  so the classic UI-thread deadlock is structurally absent.
- **Unbounded loops** — no `while (true)`, `for (;;)` or equivalent.
- **Lock usage** — two locks total (`Diag`, `ShortcutResolver`), neither nested, so no lock-ordering
  deadlock is possible.
- **`async void`** — 12 instances, all event handlers, which is the sanctioned use.
- **Unguarded indexing** — every `[0]` / `.First()` found is preceded by a count check.

The codebase is defensively written; the defects found were all at the boundary where a *number
supplied by the input* decides how much work or memory to commit.

---

## 3. Crash analysis

### 3.1 Stack overflow on nested XML — *Critical*

`XmlFormat.ConvertElement` recursed once per element with no depth limit. Loading the document was
safe (`XDocument.Load` is iterative), but the JSON conversion walked the resulting tree recursively.

**Reproduction and measurement.** A document of `<a>` repeated *N* times:

| Depth | Input size | Result |
|-------|-----------|--------|
| 500 | 3 KB | passes |
| 2 000 | 12 KB | passes |
| **4 000** | **24 KB** | **process killed** |

At depth 4 000 the test host printed a wall of `ConvertElement` frames and reported
`Test Run Aborted` — not a failed test, an ended process.

**Why it mattered more than an ordinary bug.** `FormatterPage.ConvertTo` wraps the conversion in
`catch (Exception)` and shows the message on the status bar, so *every other* malformed document is
reported cleanly. A stack overflow bypasses that handler, and also bypasses the
`App.UnhandledException` guard that deliberately sets `e.Handled = true` to keep the ring alive
through composition glitches. 24 KB of text pasted into the Formatter closed SwiftX with no trace.

### 3.2 Out of memory on nested XML — *High*

`Format`, `Minify` and `Validate` never recursed, so they did not overflow. They ran out of memory
instead, for a different reason: **indentation is quadratic in depth.** Level *n* contributes *n*
indent characters to every line below it, so a 50 000-deep document — 350 KB of text, comfortably
inside the 50 MB `InputLimits.MaxTextBytes` ceiling — asked `XmlWriter` for a multi-gigabyte string:

```
System.OutOfMemoryException : Insufficient memory to continue the execution of the program.
   at System.Text.StringBuilder.AppendWithExpansion(Char& value, Int32 valueCount)
   at System.Xml.XmlEncodedRawTextWriter.FlushBuffer()
   ...
   at SwiftX.Services.Formats.XmlFormat.Format(String text, FormatOptions options)
```

`Format` catches only `XmlException`, so this propagated out of a UI command handler. This directly
contradicts the design document's §22 contract: *"refused with a clear, specific message — never an
OOM, never a hang."*

### 3.3 Out-of-memory from a 35-byte keystore — *High*

`JksReader`'s cursor bounds-checked every *read*, so a lie about a count surfaced harmlessly as a
`FormatException` on the first read past the end. But the chain list was sized from the count
**before** any of it was read:

```csharp
int chainLen = cursor.ReadPositiveInt32();
var chain = new List<byte[]>(chainLen);   // chainLen can be int.MaxValue
```

A 35-byte file declaring `int.MaxValue` certificates asked for a 17 GB backing array:

```
System.OutOfMemoryException : Array dimensions exceeded supported range.
```

`OutOfMemoryException` is neither a `FormatException` nor anything else in the call chain catches,
which broke the "never throws" contract `CertificateReader` documents for the whole layer — and
that layer is reached from an `async void` drop handler.

### 3.4 The fixes

**XML depth ceiling** (`XmlFormat.MaxDepth = 256`, matching `JsonFormat`'s existing parser limit):

- Enforced once in `LoadSecure`, the single shared load path, so all four public entry points
  inherit it.
- The check itself walks the tree **iteratively on an explicit stack** — recursing to measure
  nesting would be the very thing being guarded against.
- Depth is measured per branch, so a wide-but-shallow document (20 000 siblings, 3 levels) is
  unaffected. There is a test for exactly that.
- A companion guard on `BuildElement` bounds the write side.

One ceiling fixes both 3.1 and 3.2, because both are consequences of unbounded depth.

**Keystore counts** (`JksReader.Cursor.ReadCount`): a declared repeat count is rejected when the
bytes remaining could not possibly hold that many items, checked *before* any collection is sized.
Applied to both the entry count and the chain length.

---

## 4. Hang analysis

No true deadlock or infinite loop exists (see §2). The hang risk in this app is **synchronous work
on the UI thread**, since every format/validate command runs inline in its handler.

### 4.1 Fixed: whole document materialised twice per keystroke

`CodeEditor.RefreshGutter()` ran on every `TextChanged` and reached the document through the `Text`
property, which costs **two full copies**: one from `Document.GetText`, and a second from its
`Replace('\r', '\n')`. On a multi-megabyte document that is megabytes of garbage per keypress on
the UI thread.

Critically, `MaxHighlightLength` does **not** bound this — it gates colouring only, while the gutter
is rebuilt for documents all the way up to the 50 MB file ceiling.

Replaced with a `LineCount()` helper that counts line breaks off the raw document text over a
`ReadOnlySpan<char>`, allocating nothing beyond the unavoidable `GetText` string. Counting both
`\r` and `\n` makes it exactly equivalent to counting `\n` in `Text`.

### 4.2 Verified: the algorithms themselves are linear

Every hot path was measured at two input sizes and checked for superlinear growth — see §5.2. All
are proportional to their input.

---

## 5. Performance and load testing

### 5.1 Measured throughput

Absolute figures from this machine, taken as the minimum of five runs:

| Operation | Payload | Time |
|-----------|---------|------|
| JSON validate | 6.1 MB | 29 ms |
| JSON format | 6.1 MB | 133 ms |
| JSON minify | 6.1 MB | 191 ms |
| JSON sort keys | 6.1 MB | 250 ms |
| JSON canonical round trip | 6.1 MB | 663 ms |
| XML validate | 3.7 MB | 128 ms |
| XML format | 3.7 MB | 242 ms |
| XML to canonical | 3.7 MB | 233 ms |
| JSON tokenize | 1.2 MB | 11 ms |
| XML tokenize | 0.7 MB | 12 ms |
| Fold finder (JSON) | 1.2 MB | 14 ms |
| Base64 encode / decode | 4 MB | 13 ms / 100 ms |
| Config save + load | 5 000 items | 159 ms |
| Mixed load, 24 threads | — | 629 ms |

The editor never hands the tokenizers more than `MaxHighlightLength` (100 000 characters), an order
of magnitude below the sizes measured above, so live colouring has a wide margin.

### 5.2 Scaling — the assertions that actually catch regressions

An absolute budget is really a statement about the machine running it. The **scaling** tests are the
regression cover: they time the same operation at two input sizes and assert cost grows with the
input rather than with its square. Over an ×8 size span, linear is ×8 and quadratic is ×64.

| Operation | Growth for ×8 input | Verdict |
|-----------|--------------------|---------|
| JSON validate | ×8.5 | linear |
| JSON format | ×5.3 | linear |
| JSON minify | ×8.0 | linear |
| JSON tokenize | ×5.9 | linear |
| XML tokenize | ×4.2 | linear |
| Fold finder (JSON / XML) | ×7.0 / ×7.0 | linear |
| Line index | ×6.5 | linear |
| XML validate / format / to canonical | ×10.2 / ×12.5 / ×9.2 | linear (see note) |

**Two measurement corrections were needed before these numbers meant anything**, and both are
documented in the test file because they are easy to reintroduce:

1. **Isolate the sizes.** The first version built both payloads up front and held them alive in
   closures across both phases, so the large measurement paid for collecting the small one. XML
   validate reported ×20 growth and looked quadratic; measured on its own it grows ×8.7. Each size
   is now measured with the previous payload dropped and collected first.
2. **Start above the cache-resident range, and span wide.** A 5 000-row XML load costs ~3 ms with
   its working set in cache, so comparing it to 40 000 rows measures the memory hierarchy as much
   as the algorithm. XML now starts at 10 000 rows and spans ×8 rather than ×4 — over a ×4 span
   linear (×4) and quadratic (×16) are too close, and observed values straddled the ceiling.

XML additionally carries an explicit ×3 noise allowance rather than the default ×2, because it
builds a full `XDocument` tree per run: an identical 80 000-row `Format` was measured between 180 ms
and 298 ms across four runs — a 1.7× spread from garbage collection alone. Even ×3 leaves a wide
margin below quadratic. The suite was then run five consecutive times to confirm stability.

### 5.3 Endurance and concurrency

- **300 full formatter cycles** (detect → validate → format → minify → convert → fold → tokenize):
  942 ms, managed heap growth **0 MB**.
- **3 000 codec cycles**: heap growth **0 MB**.
- **Concurrency**: every format, tokenizer and codec is a process-wide singleton
  (`JsonFormat.Instance` and friends) and tool windows are genuinely independent, so these really
  are reached from multiple threads. 24 threads released simultaneously from a `Barrier`, each
  formatting a payload **only it knows the contents of** — distinct payloads are what make this a
  real test, since with identical input a formatter leaking state between calls would still return
  the right answer by accident. No cross-contamination.

---

## 6. Security testing

The design document's §21 (XXE and entity expansion) and §22 (parse-bomb and allocation guards) are
the contracts. `XmlFormat`'s own class comment says they are *"enforced by tests, not by
convention"* — this suite is what that sentence now refers to.

The recurring theme: **a hardening measure is only real if the failure is reported.** A parser that
refuses hostile input by throwing something its caller does not catch has not protected anything —
it has moved the crash. Assertions therefore check for a clean result or a caught exception type,
not merely for the absence of the attack's effect.

Covered:

- **XXE, six variants** — `SYSTEM` file read, HTTP exfiltration, `PUBLIC` identifier, parameter
  entity, external DTD subset, UNC path. All refused by all four entry points. Testing the variants
  separately matters: a reader hardened only against the classic `file://` case would still pass a
  single-case test.
- **Refusal messages do not echo the target** — a diagnostic quoting a resolved entity's contents
  would defeat the guard by another route.
- **Billion laughs at full nine-level strength** — refused before the first expansion.
- **Nesting bombs** — refused with a message that *names the limit*, per §22's wording.
- **Secrets at rest** — asserted against the actual bytes on disk, with a real subsequence search,
  that neither the value nor the *key name* appears in UTF-8 or UTF-16. Key names alone
  (`AWS_SECRET_KEY`, `Prod DB password`) are sensitive metadata.
- **Tampered and malformed stores** — a flipped ciphertext byte, invalid base64, missing fields and
  non-JSON all degrade to "no entries" rather than throwing out of `Load`, which is called during
  window construction where an exception has no caller.
- **DPAPI binding** — the blob is bound to the user, not merely obfuscated.
- **Certificate and keystore fuzzing** — structured garbage behind plausible magic bytes (JKS,
  JCEKS, DER, PEM, PKCS#7), plus truncation at *every* prefix length of a valid keystore header.
  Structured input is used deliberately: uniform random noise mostly fails format detection and
  never reaches the interesting paths.
- **Configuration import** — malformed, empty, wrong-shaped and depth-bombed config files all
  return null ("keep what you had") rather than throwing or half-applying.
- **Codecs** — non-UTF-8 bytes reported rather than silently replaced with U+FFFD, which would
  quietly corrupt a decoded secret; truncated `\uXXXX` escapes at the end of the buffer.
- **Input ceilings** — an oversized file is refused from its metadata, before a byte is read.

---

## 7. Test isolation — a data-loss incident

**`SecureValuesAndCertStressTests` wrote to the real `%AppData%\SwiftX`.** `SecureValueStore.Save`
serializes the list it is handed and moves it over the existing file — there is no scratch mode and
no merge. Running the suite therefore replaced the user's real `secure-values.json` with 1 000
generated test entries. This happened during the baseline run at the start of this review; the file
is now 615 KB (consistent with the generated payload) and stamped with that run's time.
`ring.json` was untouched. There is no backup and no undo.

**Fix:** [`tests/SwiftX.Tests/TestEnvironment.cs`](../tests/SwiftX.Tests/TestEnvironment.cs) sets
`SWIFTX_DATA_DIR` to a per-process temp directory from a `[ModuleInitializer]`.

A module initializer is required rather than a fixture or a base class: `RingStore.DataDirectory` is
a `static readonly` resolved **once per process**, so the redirect has to happen before any test
touches it, and a module initializer runs at assembly load — ahead of every test. Verified by
comparing the real file's timestamp and size across a full run.

**Any test that persists must go through this.** A test calling `SecureValueStore` or `RingStore`
without the redirect in place destroys real user data.

---

## 8. Considered and not changed

Deliberate omissions, so they are decisions rather than oversights:

1. **`CodeView.Render` builds one `Run` per token** into a single `TextBlock`, and its gutter
   builds a full line-number string. At the 300 000-character `MaxHighlightLength` cap that is a
   lot of inline objects. Bounded by the cap and not currently a measured problem, but it is the
   next thing to hit if that cap is raised.
2. **`TokenizeRangeViaWholeDocument` tokenizes the whole document and filters**, so the documented
   "visible lines only" tier is not actually incremental. The interface already anticipates this
   (`StateAfter` exists for a resumable implementation); at current sizes the whole-document pass
   is cheap, as §5.1 shows.
3. **`SetWindowRgn` clips painted pixels as a side effect of hit-testing.** The ring uses a Win32
   region purely for click-through, but a window region also clips what DWM composites — the code's
   own comments describe fighting this. `ContentRadius` currently reserves ~14 DIP of slack so the
   boundary never crosses a bubble, so it is not causing visible damage. Decoupling the two
   (handling `WM_NCHITTEST` instead) would be cleaner but risks input regressions in a WinUI island
   window that cannot be verified without extensive manual testing.
4. **`App.UnhandledException` sets `e.Handled = true` unconditionally.** This is deliberate and
   documented — crashing the ring over a backdrop repaint glitch is worse than logging it — but it
   also means a genuine bug is swallowed. Worth revisiting with a severity distinction; out of scope
   here.

---

## 9. Ring circle rendering — attempted, reverted

**Status: no change is in the tree. The bubbles render exactly as they did before this review.**

### 9.1 What was attempted

Each ring bubble in `CreateBubble` is composed of three layers, every one of them rounded by
`CornerRadius = diameter / 2`: a `SystemBackdropElement` carrying the acrylic, a `Border` carrying
the theme tint, and the host `Border` that owns the child content and that every animation scales.

The theory was that a `CornerRadius` on an element clipping *composed* content is realised as a
`CompositionRoundedRectangleClip`, which is a hard per-pixel in-or-out test with no partial coverage
at the boundary — a staircase on a curve. The attempted replacement was a `CompositionGeometricClip`
over a `CompositionEllipseGeometry`, applied through
`ElementCompositionPreview.GetElementVisual(element).Clip`, plus an `Ellipse` for the tint and
removal of the host's now-supposedly-redundant clip.

### 9.2 Why it was reverted

**It put a square acrylic panel behind every circle.** The composition clip never took effect —
XAML manages the element visual's `Clip` property and overwrites an assignment made from outside it,
so the `SystemBackdropElement` was left completely unclipped. The acrylic backdrop is square by
nature; its `CornerRadius` was the only thing making it circular.

The regression was worse than a single wrong call because the change removed **two** clips at once.
The host `Border`'s `CornerRadius` had been a second net that would have clipped the square backdrop
back into a circle on its own. Reasoning that it was "redundant" was only valid *if* the replacement
clip worked — which made a recoverable mistake into a visible one.

Reverted in full: `git checkout` on `RingWindow.xaml.cs` and deletion of the `CircleClip` helper.
Nothing else in this review touches that file, so the revert is exact.

### 9.3 What is actually known now

- The premise that `SystemBackdropElement`'s `CornerRadius` clip is aliased was **never verified**,
  and it remains unverified. It was an inference from general composition-clip behaviour.
- `ElementCompositionPreview.GetElementVisual(...).Clip` is **not** a usable place to clip a XAML
  element: XAML owns that property. This is now measured, not inferred.
- The host `Border`'s `CornerRadius` is **load-bearing**, not redundant. It is what guarantees a
  circular silhouette regardless of what the inner layers do.

### 9.4 If the circles genuinely need work

Any future attempt must start with a visual check of the current build, so there is a before/after
rather than a theory — that is the step whose absence let this ship. If aliasing is confirmed, the
route that does not fight XAML for the `Clip` property is a `CompositionMaskBrush` with a generated
antialiased alpha mask, drawn on a `SpriteVisual` attached via
`ElementCompositionPreview.SetElementChildVisual`. That stays dependency-free —
`System.Drawing.Common` is already referenced and can produce the mask bitmap. It is a substantially
larger change and should be made one layer at a time, keeping the existing `CornerRadius` in place
until the replacement is confirmed on screen.

The **Settings** preview bubbles were examined and are not affected either way: they paint their own
`Background` and `BorderBrush`, which the XAML renderer antialiases, and their centred glyph never
reaches the clip boundary.

## 10. Test suites

| File | Tests | Covers |
|------|-------|--------|
| [`Resilience/CrashAndHangRegressionTests.cs`](../tests/SwiftX.Tests/Resilience/CrashAndHangRegressionTests.cs) | 15 | Depth bombs, hostile keystores, tokenizer truncation, interactive-budget hang guards |
| [`Performance/PerformanceBudgetTests.cs`](../tests/SwiftX.Tests/Performance/PerformanceBudgetTests.cs) | 10 | Heavy-payload budgets, scaling/complexity guards, retention |
| [`Security/SecurityHardeningTests.cs`](../tests/SwiftX.Tests/Security/SecurityHardeningTests.cs) | 49 | XXE, entity expansion, secrets at rest, fuzzing, import, codecs, ceilings |
| [`Load/ConcurrencyAndEnduranceTests.cs`](../tests/SwiftX.Tests/Load/ConcurrencyAndEnduranceTests.cs) | 11 | Singleton thread-safety, store round trips, endurance, mixed load |
| [`TestEnvironment.cs`](../tests/SwiftX.Tests/TestEnvironment.cs) | — | Data-directory isolation (§7) |

```bash
dotnet test SwiftX.slnx -c Debug                                          # all 275
dotnet test SwiftX.slnx --filter "FullyQualifiedName~CrashAndHang"        # crash regressions
dotnet test SwiftX.slnx --filter "FullyQualifiedName~PerformanceBudget"   # perf, with timings
dotnet test SwiftX.slnx --filter "FullyQualifiedName~SecurityHardening"   # security
dotnet test SwiftX.slnx --filter "FullyQualifiedName~ConcurrencyAndEndurance"
```

Add `--logger "console;verbosity=detailed"` to see the performance suite's measured timings and
scaling ratios, which are written to test output rather than only asserted on.

**Runtime:** the full suite takes ~50 s. The performance and security suites dominate; the rest run
in seconds.
