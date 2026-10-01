# Test run: baseline-debug

- Outcome: **PASSED** - 796 passed, 0 failed, 0 skipped of 796
- Configuration: Debug / x64, mode Scratch, filter: `(none)`
- Wall time (build + test): 3m 00s
- Quiet gate: CPU 24% (limit 25%), quiet = True
- Commit: 852cf87 on develop (3 uncommitted paths)
- Power: plan 'Balanced', on battery = False

## Slowest tests
- 16.424s  SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
- 12.641s  SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
- 12.036s  SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
- 9.821s  SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
- 9.155s  SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
- 7.218s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.AtomicWrite_UnderConcurrentPublishes_LandsWholeAndLeavesNothingBehind
- 6.844s  SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
- 5.347s  SwiftX.Tests.Security.SecurityHardeningTests.CertificateReader_OnStructuredGarbage_AlwaysReturnsAResult
- 1.682s  SwiftX.Tests.Performance.PerformanceBudgetTests.Base64_MultiMegabytePayload_RoundTripsWithinBudget
- 1.675s  SwiftX.Tests.Formats.ToolLoadAndStressTests.HeavyXml_LoadFormatMinifyConvertCycle_RepeatsSuccessfully

## Measurements reported by the tests
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.EveryToolUnderSimultaneousLoad_CompletesWithoutFaulting
```
mixed load across 24 threads: 274ms
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.OrbitStore_WithAVeryLargeConfiguration_RoundTripsIntact
```
5000-item config save+load: 163ms
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
```
payload: 3,670,391 chars
xml validate: 69.1ms (budget 10000ms)
xml format: 195.7ms (budget 15000ms)
xml minify: 246.4ms (budget 15000ms)
xml to canonical: 475.6ms (budget 15000ms)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.TheFullFormatterCycle_RepeatedHundredsOfTimes_DoesNotAccumulate
```
300 full cycles in 972ms; heap 5MB -> 5MB (growth 0MB)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
```
payload: 6,145,385 chars
json validate: 30.5ms (budget 10000ms)
json format: 207.1ms (budget 10000ms)
json minify: 140.1ms (budget 10000ms)
json sort keys: 255.4ms (budget 10000ms)
json canonical round trip: 641.4ms (budget 15000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.InputSanitizer_OnAHeavyEscapedPayload_StaysFast
```
escaped payload: 699,556 chars
sanitize: 23.1ms (budget 5000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlFormatting_AcrossThePermittedDepthBand_StaysAffordable
```
xml format by depth: 0.1ms at 64 -> 0.6ms at 256 (time x0.6 for size x4.0, ceiling x8.0)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.CodecsAndSanitizer_RepeatedThousandsOfTimes_DoNotAccumulate
```
3000 codec cycles; heap growth 0MB
```
### SwiftX.Tests.Formats.QuickFormatterTests.Analyze_OnAHeavyPayload_StaysWellInsideAnInteractiveBudget
```
1,257,790 chars analyzed in 328ms
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
```
json tokenize: 14.0ms at 10000 -> 213.0ms at 80000 (time x15.3 for size x8.0, ceiling x24.0)
xml tokenize: 35.3ms at 10000 -> 94.9ms at 80000 (time x2.7 for size x8.0, ceiling x24.0)
json folds: 55.8ms at 10000 -> 183.6ms at 80000 (time x3.3 for size x8.0, ceiling x24.0)
xml folds: 23.6ms at 10000 -> 237.1ms at 80000 (time x10.1 for size x8.0, ceiling x24.0)
line index: 4.3ms at 10000 -> 65.1ms at 80000 (time x15.2 for size x8.0, ceiling x24.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
```
managed heap: 6MB -> 10MB (growth 4MB)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
```
json validate: 13.0ms at 10000 -> 55.6ms at 80000 (time x4.3 for size x8.0, ceiling x32.0)
json format: 20.2ms at 10000 -> 202.0ms at 80000 (time x10.0 for size x8.0, ceiling x32.0)
json minify: 18.1ms at 10000 -> 245.9ms at 80000 (time x13.6 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Tokenizers_WellPastTheEditorHighlightCap_StayFast
```
json 1,205,385 chars, xml 710,391 chars
json tokenize: 14.3ms (budget 5000ms)
xml tokenize: 13.1ms (budget 5000ms)
json folds: 52.8ms (budget 5000ms)
xml folds: 32.9ms (budget 5000ms)
line index: 4.3ms (budget 2000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
```
xml validate: 9.2ms at 10000 -> 254.1ms at 80000 (time x27.7 for size x8.0, ceiling x32.0)
xml format: 13.9ms at 10000 -> 419.3ms at 80000 (time x30.1 for size x8.0, ceiling x32.0)
xml to canonical: 85.6ms at 10000 -> 865.6ms at 80000 (time x10.1 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Base64_MultiMegabytePayload_RoundTripsWithinBudget
```
base64 encode: 17.2ms (budget 5000ms)
base64 decode: 138.2ms (budget 5000ms)
```

