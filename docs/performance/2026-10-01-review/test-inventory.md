# Test inventory

Generated from a run of **862** tests: **862** passed, **0** failed, 46.1 s of test time (cumulative, tests run in parallel). Class descriptions are the first sentence of each class's doc comment in the test sources.

## Formats (99 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| CsvFormatTests | 21 | 21 | 0 |  |
| FormatRegistryDetectionTests | 6 | 6 | 0 | FormatRegistry.DetectBest is what QuickFormatPage and FormatterPage both lean on for auto-detect — these exercise it across all four built-in formats together, since each format's own DetectConfidence is only meaningful relative to the others (see FormatRegistry.All's ordering note on JSON/XML taking priority on genuinely ambiguous input). |
| InputSanitizerTests | 4 | 4 | 0 |  |
| JsonFormatTests | 6 | 6 | 0 |  |
| QuickFormatterTests | 25 | 25 | 1.1 | Cover for QuickFormatter — the whole of Quick Format's "what do I do with this text" decision, extracted from the page so it can be asserted without a UI thread. |
| ToolLoadAndStressTests | 10 | 10 | 2.3 | Comprehensive load, stress, and endurance tests for JSON, XML, Base64, and tool data pipelines. |
| ValidateCleanPipelineTests | 2 | 2 | 0 | Mirrors the Validate button: sanitize messy paste, then pretty-print at 2 spaces. |
| XmlFormatSecurityTests | 5 | 5 | 0 |  |
| YamlFormatTests | 20 | 20 | 0 |  |

## Load (13 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| ConcurrencyAndEnduranceTests | 13 | 13 | 11.9 | Load cover along the two axes the per-operation performance tests do not reach: many at once and many in a row. |

## Models (170 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| ElementLibraryTests | 22 | 22 | 0 | Covers ElementLibrary: which items each category of the "Add to orbit" sidebar lists: the catalog's suggestions plus whatever the orbit already carries. |
| ElementSectionsTests | 27 | 27 | 0.1 | Covers how the Elements screen sorts the orbit into category sections, what it leaves out, and the small facts a card states about an item. |
| GroupDropRulesTests | 13 | 13 | 0 | Covers GroupDropRules: what may be dropped onto a group in the "Add to orbit" sidebar. |
| HotCornerItemsTests | 10 | 10 | 0 | Covers what the hot corner's quadrant shows: the orbit's own picks until the corner is arranged by hand, the stored arrangement after that, and the mapping from a stored entry back to the live thing it names. |
| HotCornerZoneTests | 13 | 13 | 0 |  |
| HotkeyGestureTests | 15 | 15 | 0 |  |
| OrbitSlotsTests | 15 | 15 | 0 | Covers OrbitSlots: removing an item leaves its slot empty instead of closing up the ring, and adding fills empty slots before extending it. |
| QuadrantLayoutTests | 22 | 22 | 0 | Covers the quarter circle a hot corner set to Quadrant summons: the two things its geometry has to guarantee — items that clear the screen edges and each other — and which items land on it. |
| SystemOrbitItemsTests | 27 | 27 | 0 | Covers the two built-in orbit entries. |
| ToolsGroupTests | 6 | 6 | 0 | Covers EnsureToolsGroup, the migration OrbitManager.Start runs on every load: every built-in tool belongs inside the one Tools group, wherever an older config happened to leave them. |

## Performance (17 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| AllocationBudgetTests | 7 | 7 | 1 | Allocation budgets: how much garbage an operation makes, which — unlike how long it takes — does not depend on what else the machine is doing. |
| PerformanceBudgetTests | 10 | 10 | 19.6 | Heavy-load performance cover for the pure computation behind the tools — the work that happens on the UI thread while the window is unable to repaint, so its cost is felt as a freeze. |

## Resilience (60 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| ConfigShapeResilienceTests | 16 | 16 | 0.2 | A configuration file the app did not write must not be able to stop it starting. |
| CrashAndHangRegressionTests | 18 | 18 | 1 | Regression cover for the ways SwiftX could be made to die or stop responding on input a user can actually hand it — a pasted document, a dropped file. |
| EventSubscriptionLifetimeTests | 5 | 5 | 0.4 | Closed windows must be collectable. |
| NativeCapabilityGuardTests | 2 | 2 | 0 | Guards around native APIs whose failure is not an exception. |
| NetworkPathHangTests | 19 | 19 | 0.3 | An orbit item on a network share must never be able to freeze the app. |

## Security (68 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| BoundedInputTests | 12 | 12 | 0.1 | The size ceilings, asserted at the boundary each one actually guards. |
| SecretLifetimeTests | 7 | 7 | 0.3 | How long sensitive material stays alive in the process, and where a copied secret is allowed to travel. |
| SecurityHardeningTests | 49 | 49 | 2.6 | The security properties SwiftX's own design document commits to, asserted rather than assumed. |

## Services (435 tests)

| Class | Tests | Passed | Seconds | What it covers |
|---|---:|---:|---:|---|
| AtomicWriteRetryTests | 5 | 5 | 0.9 | Publishing over a file somebody else currently has open. |
| Base64CodecTests | 12 | 12 | 0 |  |
| CertificateReaderTests | 6 | 6 | 0.6 |  |
| DiagLogLocationTests | 4 | 4 | 0 | Where the diagnostic log goes — and, more to the point, where a test run must not put it. |
| DisplayBoundsCacheTests | 9 | 9 | 0 | The containment test behind the hot corner's display cache, and the tokenizer nothing else covers. |
| FaviconMissCacheTests | 6 | 6 | 0 | The back-off that stops a host with no favicon (or a machine with no network) being asked again on every summon. |
| FrostedGlassLevelTests | 15 | 15 | 0.1 | Frosted glass's three frost levels. |
| GreetingTests | 24 | 24 | 0 | The Home header's time-of-day line. |
| HomeIdlePolicyTests | 7 | 7 | 0 | When the Settings Home marks may animate. |
| HoverConfigTests | 41 | 41 | 0.5 | The hover settings as they cross the persistence boundary: defaults, copy, the on-disk form, and what a config that is old, newer than this build, or hand-edited does to them. |
| HoverMotionTests | 45 | 45 | 0.1 | The Animate hover style's motions as data. |
| HowToGuideTests | 23 | 23 | 0 | The how-to guide that slides out of the Home screen. |
| IconIdentityTests | 8 | 8 | 0.1 | The identity of a resolved icon — what makes one resolution reusable and another stale. |
| JksReaderTests | 5 | 5 | 0.4 |  |
| JumboIconTests | 6 | 6 | 0.7 | The shell's jumbo icon lookup, exercised for real: SHGetFileInfo, SHGetImageList and IImageList::GetIcon against the actual shell rather than a fake. |
| LanguageCatalogTests | 23 | 23 | 0 |  |
| LegacyConfigFormatTests | 12 | 12 | 0 | The ring-to-orbit rename crossed the persistence boundary, so an upgrading user's whole dock rides on this mapping. |
| LocTests | 6 | 6 | 0 | Loc holds its active language as static state, so every test here restores it to EnglishUS in a finally — leaving it pointed at whatever a test last selected would leak into whichever test (in this class or another) runs next. |
| OrbitHoverTests | 50 | 50 | 0 | The pure rules behind the orbit's hover styles: which colour Tint paints and what is drawn on it, which style is really in force, and how big an item settles under the pointer. |
| OrbitItemFactoryTests | 6 | 6 | 0 |  |
| OrbitItemGeometryTests | 8 | 8 | 0 | The rest fill of an orbit bubble, per shape. |
| OrbitMetricsNearestSlotIndexTests | 11 | 11 | 0 | Covers the pure angle math behind the orbit's radial hover (OrbitWindow.OrbitCanvas_PointerMoved delegates to OrbitMetrics.NearestSlotIndex, which cannot be exercised directly since OrbitWindow is a live WinUI Window). |
| OrbitStoreTests | 2 | 2 | 0 |  |
| PlainTokenizerTests | 6 | 6 | 0 | The fallback tokenizer, which had no coverage at all. |
| SecureValuesAndCertStressTests | 4 | 4 | 0.9 | Stress and resilience testing for SecureValueStore, CertificateReader, and InputLimits. |
| FoldFinderTests | 11 | 11 | 0 |  |
| FoldRendererTests | 6 | 6 | 0 |  |
| ThemeHelperTests | 49 | 49 | 0 | ThemeHelper's colour table. |
| TolerantEnumConfigTests | 17 | 17 | 0.1 | Every enum a configuration file carries, read back with a name this build does not know. |
| ValueMaskingTests | 8 | 8 | 0 |  |


