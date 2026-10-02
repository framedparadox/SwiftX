#Requires -Version 7.0
<#
.SYNOPSIS
  Builds and runs the SwiftX test suite (or a slice of it) in isolation, then summarises it.

.DESCRIPTION
  Scratch (default): mirrors the working tree - uncommitted edits included - into a persistent
  scratch workspace and builds/tests there. The repo's bin/obj are never touched, and the user's
  running SwiftX.exe or debugger cannot lock anything this run needs. The workspace is kept
  between runs so every build after the first is incremental.

  InPlace: builds and tests inside the repo. Refused while SwiftX.exe is running, because the
  apphost copy fails on the locked bin folder and reads as "Build FAILED" with zero compile errors.

  Safety: refuses to run unless tests/SwiftX.Tests/TestEnvironment.cs still redirects
  SWIFTX_DATA_DIR from a [ModuleInitializer]. Without it a test that calls SecureValueStore.Save
  silently replaces the real %AppData%\SwiftX\secure-values.json.

  Outputs, in <WorkRoot>\runs\<stamp>-<Label>\ : dotnet-test.log, results.trx, summary.json,
  summary.md. Exit code: 0 all passed, 1 test failures, 2 build/run failure.

.EXAMPLE
  run-suite.ps1 -Label perf -Filter "FullyQualifiedName~PerformanceBudgetTests"
  run-suite.ps1 -Label full -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Filter = '',
    [string]$Label = 'suite',
    [ValidateSet('Scratch', 'InPlace')][string]$Mode = 'Scratch',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$Platform = 'x64',
    [string]$RepoRoot,
    [string]$WorkRoot,
    [double]$QuietCpuPercent = 25,
    [int]$QuietTimeoutSeconds = 120,
    [switch]$SkipQuietCheck,
    [switch]$NoBuild
)

. "$PSScriptRoot\_common.ps1"

$repo = Resolve-RepoRoot -Hint $RepoRoot
$work = if ($WorkRoot) { $WorkRoot } else { Get-DefaultWorkRoot }
New-Item -ItemType Directory -Force -Path $work | Out-Null

# ---- Guard: the data-directory redirect must exist -------------------------------------------
$envFile = Join-Path $repo 'tests\SwiftX.Tests\TestEnvironment.cs'
if (-not (Test-Path -LiteralPath $envFile) -or
    -not (Select-String -LiteralPath $envFile -Pattern 'ModuleInitializer' -Quiet) -or
    -not (Select-String -LiteralPath $envFile -Pattern 'SWIFTX_DATA_DIR' -Quiet)) {
    Write-Error ("REFUSING TO RUN: $envFile no longer redirects SWIFTX_DATA_DIR from a [ModuleInitializer]. " +
        "Running the suite would overwrite the real %AppData%\SwiftX\secure-values.json. Restore the redirect first.")
    exit 2
}

# ---- Where to build ---------------------------------------------------------------------------
$running = @(Get-Process -Name SwiftX -ErrorAction SilentlyContinue)
if ($Mode -eq 'InPlace' -and $running.Count -gt 0) {
    Write-Error ("SwiftX.exe is running (PID $($running.Id -join ', ')); it locks src\SwiftX\bin. " +
        "Use -Mode Scratch (the default). Never kill the user's session.")
    exit 2
}

$root = $repo
if ($Mode -eq 'Scratch') {
    $root = Join-Path $work 'workspace'
    $sync = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Robocopy -Source $repo -Destination $root `
        -ExcludeDirs @('.git', '.vs', 'bin', 'obj', 'TestResults', 'AppPackages', 'node_modules')
    Write-Host ("synced working tree -> {0} ({1})" -f $root, (Format-Duration $sync.Elapsed))
}

# ---- Quiet-machine gate -----------------------------------------------------------------------
$quiet = $null
if (-not $SkipQuietCheck) {
    $quiet = Wait-QuietMachine -MaxCpuPercent $QuietCpuPercent -TimeoutSeconds $QuietTimeoutSeconds
    if (-not $quiet.Quiet) {
        Write-Warning ("machine never went quiet (CPU {0}% > {1}%): timing assertions may be noise, not regressions" -f
            $quiet.CpuPercent, $quiet.ThresholdPct)
    }
}

# ---- Run --------------------------------------------------------------------------------------
$runDir = New-RunFolder -Root (Join-Path $work 'runs') -Label $Label
$log = Join-Path $runDir 'dotnet-test.log'
$project = Join-Path $root 'tests\SwiftX.Tests\SwiftX.Tests.csproj'

$testArgs = @(
    'test', $project, '-nologo', "-p:Platform=$Platform", '-c', $Configuration,
    '--logger', 'trx;LogFileName=results.trx',
    '--logger', 'console;verbosity=detailed',
    '--results-directory', $runDir
)
if ($Filter) { $testArgs += @('--filter', $Filter) }
if ($NoBuild) { $testArgs += '--no-build' }

Write-Host ("dotnet {0}" -f ($testArgs -join ' '))
$clock = [Diagnostics.Stopwatch]::StartNew()
$facts = Get-MachineFacts -RepoRoot $repo
& dotnet @testArgs 2>&1 | Tee-Object -FilePath $log | Out-Null
$dotnetExit = $LASTEXITCODE
$clock.Stop()

# ---- Parse ------------------------------------------------------------------------------------
$trxPath = Join-Path $runDir 'results.trx'
$buildErrors = @(Select-String -LiteralPath $log -Pattern ': error [A-Z]+\d+' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Line.Trim() } | Select-Object -Unique -First 15)
$buildWarnings = @(Select-String -LiteralPath $log -Pattern ': warning [A-Z]+\d+' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Line.Trim() } | Select-Object -Unique).Count

$summary = [ordered]@{
    label         = $Label
    mode          = $Mode
    configuration = $Configuration
    platform      = $Platform
    filter        = $Filter
    workspace     = $root
    runDirectory  = $runDir
    wallSeconds   = [math]::Round($clock.Elapsed.TotalSeconds, 1)
    dotnetExit    = $dotnetExit
    quietGate     = $quiet
    facts         = $facts
    buildErrors   = $buildErrors
    buildWarnings = $buildWarnings
}

if (-not (Test-Path -LiteralPath $trxPath)) {
    $summary.outcome = 'NO-RESULTS (build or run failed before any test finished)'
    Write-Json -Object $summary -Path (Join-Path $runDir 'summary.json')
    Write-Host "NO TRX PRODUCED. See $log"
    $buildErrors | ForEach-Object { Write-Host "  $_" }
    exit 2
}

$xml = [xml](Get-Content -LiteralPath $trxPath -Raw)
$nsm = [System.Xml.XmlNamespaceManager]::new($xml.NameTable)
$nsm.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

$rows = foreach ($n in $xml.SelectNodes('//t:UnitTestResult', $nsm)) {
    [pscustomobject]@{
        Name    = $n.GetAttribute('testName')
        Outcome = $n.GetAttribute('outcome')
        Seconds = [math]::Round(([timespan]::Parse($n.GetAttribute('duration'))).TotalSeconds, 3)
        Message = $n.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $nsm)?.InnerText
        Stack   = $n.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $nsm)?.InnerText
        StdOut  = $n.SelectSingleNode('t:Output/t:StdOut', $nsm)?.InnerText
    }
}
$rows = @($rows)

$summary.totals = [ordered]@{
    total   = $rows.Count
    passed  = @($rows | Where-Object Outcome -eq 'Passed').Count
    failed  = @($rows | Where-Object Outcome -eq 'Failed').Count
    skipped = @($rows | Where-Object { $_.Outcome -notin 'Passed', 'Failed' }).Count
}
$summary.failures = @($rows | Where-Object Outcome -eq 'Failed' | ForEach-Object {
        [ordered]@{
            name    = $_.Name
            message = if ($_.Message) { $_.Message.Substring(0, [math]::Min(1200, $_.Message.Length)) }
            stack   = if ($_.Stack) { ($_.Stack -split "`n" | Select-Object -First 8) -join "`n" }
        }
    })
$summary.slowest = @($rows | Sort-Object Seconds -Descending | Select-Object -First 15 |
        ForEach-Object { [ordered]@{ name = $_.Name; seconds = $_.Seconds } })

# The performance and load tests report their own measurements through ITestOutputHelper
# ("json validate: 12.3ms at 10000 -> 98.1ms at 80000 (time x8.0 ...)"). Keep those verbatim: they
# are the data the report is made of.
$summary.testOutput = [ordered]@{}
foreach ($r in ($rows | Where-Object { $_.StdOut -and $_.Name -match 'Performance|Load|Stress|Endurance|Resilience|Concurrency' })) {
    $summary.testOutput[$r.Name] = $r.StdOut.Trim()
}

$summary.outcome = if ($summary.totals.failed -gt 0) { 'FAILED' } else { 'PASSED' }
Write-Json -Object $summary -Path (Join-Path $runDir 'summary.json') -Depth 10

# Every test with its outcome and duration: the raw material for test-inventory.ps1, which turns a run
# into the "what is tested, and did it pass" document.
$rows | Select-Object Name, Outcome, Seconds |
    Export-Csv -LiteralPath (Join-Path $runDir 'tests.csv') -NoTypeInformation -Encoding utf8

# ---- Human summary ----------------------------------------------------------------------------
$md = [Text.StringBuilder]::new()
[void]$md.AppendLine("# Test run: $Label")
[void]$md.AppendLine()
[void]$md.AppendLine("- Outcome: **$($summary.outcome)** - $($summary.totals.passed) passed, $($summary.totals.failed) failed, $($summary.totals.skipped) skipped of $($summary.totals.total)")
[void]$md.AppendLine("- Configuration: $Configuration / $Platform, mode $Mode, filter: ``$(if ($Filter) { $Filter } else { '(none)' })``")
[void]$md.AppendLine("- Wall time (build + test): $(Format-Duration $clock.Elapsed)")
if ($quiet) { [void]$md.AppendLine("- Quiet gate: CPU $($quiet.CpuPercent)% (limit $($quiet.ThresholdPct)%), quiet = $($quiet.Quiet)") }
[void]$md.AppendLine("- Commit: $($facts.git.sha) on $($facts.git.branch) ($($facts.git.dirtyFiles) uncommitted paths)")
[void]$md.AppendLine("- Power: plan '$($facts.powerPlan)', on battery = $($facts.onBattery)")
[void]$md.AppendLine()
if ($summary.failures.Count) {
    [void]$md.AppendLine('## Failures')
    foreach ($f in $summary.failures) {
        [void]$md.AppendLine("### $($f.name)")
        [void]$md.AppendLine('```'); [void]$md.AppendLine("$($f.message)"); [void]$md.AppendLine('```')
    }
}
[void]$md.AppendLine('## Slowest tests')
foreach ($s in ($summary.slowest | Select-Object -First 10)) { [void]$md.AppendLine("- $($s.seconds)s  $($s.name)") }
if ($summary.testOutput.Count) {
    [void]$md.AppendLine()
    [void]$md.AppendLine('## Measurements reported by the tests')
    foreach ($k in $summary.testOutput.Keys) {
        [void]$md.AppendLine("### $k"); [void]$md.AppendLine('```'); [void]$md.AppendLine($summary.testOutput[$k]); [void]$md.AppendLine('```')
    }
}
$md.ToString() | Set-Content -LiteralPath (Join-Path $runDir 'summary.md') -Encoding utf8

Write-Host ''
Write-Host ("{0}: {1} passed, {2} failed, {3} skipped of {4} in {5}" -f
    $summary.outcome, $summary.totals.passed, $summary.totals.failed, $summary.totals.skipped,
    $summary.totals.total, (Format-Duration $clock.Elapsed))
foreach ($f in $summary.failures) { Write-Host "  FAIL $($f.name)"; Write-Host "       $(($f.message -split "`n")[0])" }
Write-Host "run folder: $runDir"

if ($summary.totals.failed -gt 0) { exit 1 }
exit 0
