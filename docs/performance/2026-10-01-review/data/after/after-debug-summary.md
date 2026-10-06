# Test run: after-debug

- Outcome: **PASSED** - 862 passed, 0 failed, 0 skipped of 862
- Configuration: Debug / x64, mode Scratch, filter: `(none)`
- Wall time (build + test): 56.9s
- Quiet gate: CPU 16% (limit 25%), quiet = True
- Commit: 852cf87 on develop (55 uncommitted paths)
- Power: plan 'Balanced', on battery = False

## Slowest tests
- 7.158s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.AtomicWrite_UnderConcurrentPublishes_LandsWholeAndLeavesNothingBehind
- 4.029s  SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
- 3.447s  SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
- 3.292s  SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
- 3.291s  SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
- 2.626s  SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
- 2.271s  SwiftX.Tests.Security.SecurityHardeningTests.CertificateReader_OnStructuredGarbage_AlwaysReturnsAResult
- 2.078s  SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
- 1.561s  SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.BothStores_SavedFromManyThreadsAtOnce_NeverPublishATornFile
- 1.376s  SwiftX.Tests.Formats.ToolLoadAndStressTests.HeavyXml_LoadFormatMinifyConvertCycle_RepeatsSuccessfully

## Measurements reported by the tests
### SwiftX.Tests.Performance.PerformanceBudgetTests.SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare
```
json tokenize: 8.0ms at 10000 -> 68.0ms at 80000 (time x8.5 for size x8.0, ceiling x24.0)
xml tokenize: 7.8ms at 10000 -> 52.2ms at 80000 (time x6.7 for size x8.0, ceiling x24.0)
json folds: 10.7ms at 10000 -> 98.1ms at 80000 (time x9.1 for size x8.0, ceiling x24.0)
xml folds: 10.5ms at 10000 -> 89.4ms at 80000 (time x8.6 for size x8.0, ceiling x24.0)
line index: 3.0ms at 10000 -> 22.9ms at 80000 (time x7.6 for size x8.0, ceiling x24.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare
```
json validate: 2.1ms at 10000 -> 20.0ms at 80000 (time x9.4 for size x8.0, ceiling x32.0)
json format: 9.6ms at 10000 -> 85.9ms at 80000 (time x8.9 for size x8.0, ceiling x32.0)
json minify: 8.7ms at 10000 -> 77.8ms at 80000 (time x8.9 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.RepeatedFormatCycles_DoNotRetainEachDocument
```
managed heap: 6MB -> 11MB (growth 4MB)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlFormatting_AcrossThePermittedDepthBand_StaysAffordable
```
xml format by depth: 0.0ms at 64 -> 0.4ms at 256 (time x0.4 for size x4.0, ceiling x8.0)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.XmlPipeline_CostGrowsWithTheInput_NotItsSquare
```
xml validate: 1.5ms at 10000 -> 12.8ms at 80000 (time x8.5 for size x8.0, ceiling x32.0)
xml format: 8.0ms at 10000 -> 98.1ms at 80000 (time x12.3 for size x8.0, ceiling x32.0)
xml to canonical: 13.5ms at 10000 -> 197.1ms at 80000 (time x14.6 for size x8.0, ceiling x32.0)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.CodecsAndSanitizer_RepeatedThousandsOfTimes_DoNotAccumulate
```
3000 codec cycles; heap growth 0MB
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.InputSanitizer_OnAHeavyEscapedPayload_StaysFast
```
escaped payload: 699,556 chars
sanitize: 5.7ms (budget 5000ms)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Json_HeavyPayload_EveryOperationStaysInteractive
```
payload: 6,145,385 chars
json validate: 13.3ms (budget 10000ms)
json format: 56.9ms (budget 10000ms)
json minify: 45.8ms (budget 10000ms)
json sort keys: 71.6ms (budget 10000ms)
json canonical round trip: 166.8ms (budget 15000ms)
```
### SwiftX.Tests.Performance.AllocationBudgetTests.XmlValidate_DoesNotBuildADocumentTree
```
3,670,391 chars: validate allocated 11,496 bytes; building the tree allocates 18,207,504 bytes (x1,584)
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Tokenizers_WellPastTheEditorHighlightCap_StayFast
```
json 1,205,385 chars, xml 710,391 chars
json tokenize: 7.8ms (budget 5000ms)
xml tokenize: 6.4ms (budget 5000ms)
json folds: 10.8ms (budget 5000ms)
xml folds: 12.3ms (budget 5000ms)
line index: 2.6ms (budget 2000ms)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.TheFullFormatterCycle_RepeatedHundredsOfTimes_DoesNotAccumulate
```
300 full cycles in 802ms; heap 5MB -> 6MB (growth 0MB)
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.EveryToolUnderSimultaneousLoad_CompletesWithoutFaulting
```
mixed load across 24 threads: 73ms
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Base64_MultiMegabytePayload_RoundTripsWithinBudget
```
base64 encode: 4.6ms (budget 5000ms)
base64 decode: 39.2ms (budget 5000ms)
```
### SwiftX.Tests.Formats.QuickFormatterTests.Analyze_OnAHeavyPayload_StaysWellInsideAnInteractiveBudget
```
1,257,790 chars analyzed in 272ms
```
### SwiftX.Tests.Load.ConcurrencyAndEnduranceTests.OrbitStore_WithAVeryLargeConfiguration_RoundTripsIntact
```
5000-item config save+load: 57ms
```
### SwiftX.Tests.Performance.PerformanceBudgetTests.Xml_HeavyPayload_EveryOperationStaysInteractive
```
payload: 3,670,391 chars
xml validate: 8.1ms (budget 10000ms)
xml format: 83.4ms (budget 15000ms)
xml minify: 66.9ms (budget 15000ms)
xml to canonical: 125.9ms (budget 15000ms)
```

