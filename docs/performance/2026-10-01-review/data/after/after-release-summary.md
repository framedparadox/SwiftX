# Test run: after-release-perf-load

- Outcome: **PASSED** - 30 passed, 0 failed, 0 skipped of 30
- Configuration: Release / x64, mode Scratch, filter: `FullyQualifiedName~SwiftX.Tests.Performance|FullyQualifiedName~SwiftX.Tests.Load`
- Wall time (build + test): 54.3s
- Quiet gate: CPU 16.7% (limit 25%), quiet = True
- Commit: 852cf87 on develop (55 uncommitted paths)
- Power: plan 'Balanced', on battery = False

## Slowest tests
- 7.501s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.AtomicWrite_UnderConcurrentPublishes_LandsWholeAndLeavesNothingBehind
- 5.478s  SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
- 3.545s  SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
- 3.101s  SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
- 2.726s  SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
- 1.851s  SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
- 1.3s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.BothStores_SavedFromManyThreadsAtOnce_NeverPublishATornFile
- 1.191s  SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
- 0.937s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.OrbitStore_ExportImportCycle_IsStableAcrossManyRepetitions
- 0.92s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.TheFullFormatterCycle_RepeatedHundredsOfTimes_DoesNotAccumulate

## Measurements reported by the tests
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.TheFullFormatterCycle_RepeatedHundredsOfTimes_DoesNotAccumulate
```
300 full cycles in 885ms; heap 4MB -> 4MB (growth 0MB)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.OrbitStore_WithAVeryLargeConfiguration_RoundTripsIntact
```
5000-item config save+load: 248ms
```
### SwiftX.Tests.Performance.AllocationBudgetTests.XmlValidate_DoesNotBuildADocumentTree
```
3,670,391 chars: validate allocated 11,496 bytes; building the tree allocates 18,207,512 bytes (x1,584)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
```
managed heap: 5MB -> 8MB (growth 3MB)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
```
json validate: 2.2ms at 10000 -> 19.2ms at 80000 (time x8.8 for size x8.0, ceiling x32.0)
json format: 8.4ms at 10000 -> 76.1ms at 80000 (time x9.0 for size x8.0, ceiling x32.0)
json minify: 7.8ms at 10000 -> 64.3ms at 80000 (time x8.2 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Base64_MultiMegabytePayload_RoundTripsWithinBudget
```
base64 encode: 4.4ms (budget 5000ms)
base64 decode: 14.5ms (budget 5000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.InputSanitizer_OnAHeavyEscapedPayload_StaysFast
```
escaped payload: 699,556 chars
sanitize: 1.4ms (budget 5000ms)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.CodecsAndSanitizer_RepeatedThousandsOfTimes_DoNotAccumulate
```
3000 codec cycles; heap growth 0MB
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Tokenizers_WellPastTheEditorHighlightCap_StayFast
```
json 1,205,385 chars, xml 710,391 chars
json tokenize: 2.4ms (budget 5000ms)
xml tokenize: 2.0ms (budget 5000ms)
json folds: 3.3ms (budget 5000ms)
xml folds: 2.8ms (budget 5000ms)
line index: 0.5ms (budget 2000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlFormatting_AcrossThePermittedDepthBand_StaysAffordable
```
xml format by depth: 0.1ms at 64 -> 0.4ms at 256 (time x0.4 for size x4.0, ceiling x8.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
```
payload: 6,145,385 chars
json validate: 12.9ms (budget 10000ms)
json format: 48.0ms (budget 10000ms)
json minify: 41.5ms (budget 10000ms)
json sort keys: 67.1ms (budget 10000ms)
json canonical round trip: 169.1ms (budget 15000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
```
xml validate: 1.6ms at 10000 -> 11.1ms at 80000 (time x6.9 for size x8.0, ceiling x32.0)
xml format: 7.8ms at 10000 -> 105.1ms at 80000 (time x13.5 for size x8.0, ceiling x32.0)
xml to canonical: 11.8ms at 10000 -> 196.0ms at 80000 (time x16.6 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
```
payload: 3,670,391 chars
xml validate: 7.3ms (budget 10000ms)
xml format: 69.4ms (budget 15000ms)
xml minify: 59.6ms (budget 15000ms)
xml to canonical: 125.9ms (budget 15000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
```
json tokenize: 2.3ms at 10000 -> 20.9ms at 80000 (time x9.2 for size x8.0, ceiling x24.0)
xml tokenize: 1.8ms at 10000 -> 16.4ms at 80000 (time x9.3 for size x8.0, ceiling x24.0)
json folds: 2.9ms at 10000 -> 29.0ms at 80000 (time x10.0 for size x8.0, ceiling x24.0)
xml folds: 4.2ms at 10000 -> 24.6ms at 80000 (time x5.9 for size x8.0, ceiling x24.0)
line index: 0.4ms at 10000 -> 3.8ms at 80000 (time x3.8 for size x8.0, ceiling x24.0)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.EveryToolUnderSimultaneousLoad_CompletesWithoutFaulting
```
mixed load across 24 threads: 109ms
```

