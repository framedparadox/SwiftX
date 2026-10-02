#Requires -Version 7.0
<#
.SYNOPSIS
  Drives the REAL app (a Release build) through startup, idle, summon/dismiss load and
  Settings open/close load, and measures what unit tests cannot: cold-start time, idle CPU,
  UI-thread stalls, and per-cycle resource drift (private bytes, handles, GDI, USER, threads).

.DESCRIPTION
  Isolation (non-negotiable): every instance gets its own SWIFTX_DATA_DIR (so it is a separate
  single-instance key, never touches %AppData%\SwiftX, and cannot collide with the user's own
  running SwiftX), its own TEMP (so it cannot truncate the user's %TEMP%\swiftx.log), and its own
  summon hotkey (so it cannot register or trigger the user's). The script only ever kills
  processes it started itself.

  Driving is by window messages posted directly to the instance's own windows (WM_HOTKEY to the
  hidden message window to summon, the registered show-Settings message to open Settings,
  WM_CLOSE to close it). No keyboard or mouse input is injected, so nothing is typed into or
  clicked on the user's desktop. Windows DO appear and disappear on screen and the orbit takes
  foreground: announce the run, and do not run it while the user is mid-task.

  Responsiveness is measured with a background thread that sends WM_NULL to the instance's UI
  thread every 25 ms and records how long it takes to answer. A free UI thread answers in
  microseconds; a stall is any answer slower than -StallBudgetMs.

  Drift is the least-squares slope of each counter against the cycle number after discarding the
  first 20% as warm-up, expressed per 100 cycles. A real leak is monotonic; the GC's sawtooth is not.

  Phases: startup | idle | summon | spike | settings | activation | unreachable | attach | all
    spike:       a burst of summon/dismiss toggles, then recovery (does the orbit still answer?)
    activation:  summon/dismiss the orbit over an open Settings window (Activated/Deactivated churn)
    unreachable: orbit items on unroutable network shares; the UI thread must not stall on them
                 (-NoUnreachableItems runs the same phase without them: the cold-summon control)
    attach: read-only sampling of an already-running instance (-AttachPid). Never drives it.

.EXAMPLE
  measure-app.ps1 -Phase startup,idle,summon,settings -SummonCycles 150 -SettingsCycles 40
  measure-app.ps1 -Phase attach -AttachPid 12592 -AttachMinutes 30
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$WorkRoot,
    [string]$OutDir,
    [ValidateSet('startup', 'idle', 'summon', 'spike', 'settings', 'activation', 'unreachable', 'attach', 'all')]
    [string[]]$Phase = @('startup', 'idle', 'summon', 'settings'),
    [int]$SpikeToggles = 300,
    [int]$ActivationPairs = 100,
    [switch]$KeepRunning,
    [switch]$OmitWebLink,
    [switch]$NoUnreachableItems,
    [int]$StartupRuns = 5,
    [int]$IdleSeconds = 30,
    [ValidateSet('default', 'hotcorner-off', 'settings-open')]
    [string[]]$IdleVariants = @('default', 'hotcorner-off', 'settings-open'),
    [int]$SummonCycles = 150,
    [int]$SettingsCycles = 40,
    [int]$AttachPid = 0,
    [int]$AttachMinutes = 10,
    [double]$MaxPrivateMbPer100 = 1.5,
    [double]$MaxHandlesPer100 = 4,
    [double]$MaxGdiPer100 = 2,
    [double]$MaxUserPer100 = 2,
    [double]$MaxIdleCpuPercent = 1.0,
    [int]$StallBudgetMs = 250
)

. "$PSScriptRoot\_common.ps1"
if (-not ('Win32Probe' -as [type])) {
    Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Win32Probe.cs'))
}

$repo = Resolve-RepoRoot
$work = if ($WorkRoot) { $WorkRoot } else { Get-DefaultWorkRoot }
if ($Phase -contains 'all') { $Phase = @('startup', 'idle', 'summon', 'spike', 'settings', 'activation', 'unreachable') }

if (-not $ExePath -and $Phase -ne 'attach') {
    $ExePath = Join-Path $work 'workspace\src\SwiftX\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\SwiftX.exe'
}
if ($Phase -ne 'attach' -and -not (Test-Path -LiteralPath $ExePath)) {
    Write-Error "App not found at $ExePath. Run build-app.ps1 -Configuration Release first."
    exit 2
}

if (-not $OutDir) { $OutDir = Join-Path $work 'live' }
$run = New-RunFolder -Root $OutDir -Label 'live'
$cores = [Environment]::ProcessorCount
$script:launched = [System.Collections.Generic.List[int]]::new()
$script:kept = [System.Collections.Generic.List[int]]::new()
$SummonHotkey = 'Ctrl+Alt+Shift+F24'   # obscure on purpose: never the user's own Ctrl+Shift+X
$WM_HOTKEY = 0x0312
$WM_CLOSE = 0x0010

# ---- Small statistics helpers -----------------------------------------------------------------
function Get-Median([double[]]$v) {
    if ($null -eq $v -or @($v).Count -eq 0) { return $null }
    $s = @($v | Sort-Object)
    $m = [int][math]::Floor($s.Count / 2)
    if ($s.Count % 2) { return [double]$s[$m] } else { return ([double]$s[$m - 1] + [double]$s[$m]) / 2 }
}
function Get-Percentile([double[]]$v, [double]$p) {
    if ($null -eq $v -or @($v).Count -eq 0) { return $null }
    $s = @($v | Sort-Object)
    $i = [math]::Min($s.Count - 1, [math]::Max(0, [int][math]::Ceiling($p * $s.Count) - 1))
    return [double]$s[$i]
}
function Round1($x) { if ($null -eq $x) { $null } else { [math]::Round([double]$x, 1) } }

function Get-Slope([double[]]$x, [double[]]$y) {
    $n = $x.Count
    if ($n -lt 3) { return $null }
    $sx = ($x | Measure-Object -Sum).Sum; $sy = ($y | Measure-Object -Sum).Sum
    $sxy = 0.0; $sxx = 0.0
    for ($i = 0; $i -lt $n; $i++) { $sxy += $x[$i] * $y[$i]; $sxx += $x[$i] * $x[$i] }
    $den = $n * $sxx - $sx * $sx
    if ($den -eq 0) { return $null }
    return ($n * $sxy - $sx * $sy) / $den
}

function Get-Drift {
    <# Slope per 100 x-units after discarding the first 20% of samples, plus the change between
       the early and late medians (robust to the GC's sawtooth). #>
    param($Rows, [string]$Prop, [string]$XProp = 'cycle', [double]$Per = 100)
    $rows = @($Rows)
    if ($rows.Count -lt 6) { return [ordered]@{ slopePer100 = $null; early = $null; late = $null; delta = $null; samples = $rows.Count } }
    $skip = [math]::Max(1, [int][math]::Floor($rows.Count * 0.2))
    $use = @($rows[$skip..($rows.Count - 1)])
    $x = [double[]]($use | ForEach-Object { [double]$_.$XProp })
    $y = [double[]]($use | ForEach-Object { [double]$_.$Prop })
    $k = [math]::Max(2, [int][math]::Floor($use.Count / 4))
    $early = Get-Median $y[0..($k - 1)]
    $late = Get-Median $y[($y.Count - $k)..($y.Count - 1)]
    $slope = Get-Slope $x $y
    return [ordered]@{
        slopePer100 = if ($null -ne $slope) { [math]::Round($slope * $Per, 2) } else { $null }
        early       = Round1 $early
        late        = Round1 $late
        delta       = Round1 ($late - $early)
        samples     = $use.Count
    }
}

# ---- Sandbox + process management -------------------------------------------------------------
function New-Config {
    param([hashtable]$Override = @{}, [object[]]$ExtraItems = @())
    $items = @(
        [ordered]@{ Kind = 'Application'; DisplayName = 'Notepad'; Target = "$env:WINDIR\System32\notepad.exe" }
        [ordered]@{ Kind = 'Application'; DisplayName = 'Calculator'; Target = "$env:WINDIR\System32\calc.exe" }
        [ordered]@{ Kind = 'Application'; DisplayName = 'File Explorer'; Target = "$env:WINDIR\explorer.exe" }
        [ordered]@{ Kind = 'Folder'; DisplayName = 'Downloads'; Target = (Join-Path $env:USERPROFILE 'Downloads') }
        # Refused instantly (nothing listens on the discard port), so it exercises the favicon
        # path without a real network dependency. -OmitWebLink isolates that path when drift shows up.
        if (-not $OmitWebLink) { [ordered]@{ Kind = 'WebLink'; DisplayName = 'Local'; Target = 'http://127.0.0.1:9/' } }
    ) + $ExtraItems
    $cfg = [ordered]@{
        Seeded           = $true
        Hotkey           = $SummonHotkey
        HotkeyEnabled    = $true
        HotCornerEnabled = $true
        Orbits           = @([ordered]@{ Name = ''; Items = $items })
    }
    foreach ($k in $Override.Keys) { $cfg[$k] = $Override[$k] }
    return $cfg
}

function New-Sandbox {
    param([string]$Name, $Config)
    $root = Join-Path $run $Name
    $data = Join-Path $root 'data'
    $tmp = Join-Path $root 'tmp'
    New-Item -ItemType Directory -Force -Path $data, $tmp | Out-Null
    ($Config | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath (Join-Path $data 'orbit.json') -Encoding utf8
    return [pscustomobject]@{ Name = $Name; Root = $root; Data = $data; Tmp = $tmp; Log = (Join-Path $tmp 'swiftx.log') }
}

function Start-Instance {
    param($Sandbox, [string[]]$AppArgs = @())
    $psi = [Diagnostics.ProcessStartInfo]::new($ExePath)
    $psi.WorkingDirectory = Split-Path $ExePath
    $psi.UseShellExecute = $false
    foreach ($a in $AppArgs) { $psi.ArgumentList.Add($a) }
    $psi.Environment['SWIFTX_DATA_DIR'] = $Sandbox.Data
    $psi.Environment['TEMP'] = $Sandbox.Tmp
    $psi.Environment['TMP'] = $Sandbox.Tmp
    $p = [Diagnostics.Process]::Start($psi)
    $script:launched.Add($p.Id)
    return $p
}

function Stop-Instance {
    param($Process)
    # Guard: only a process this script launched is ever stopped.
    if ($Process -and -not $Process.HasExited -and $script:launched.Contains($Process.Id)) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        [void]$Process.WaitForExit(8000)
    }
}

function Get-Counters {
    param($Process, [Diagnostics.Stopwatch]$Clock)
    $Process.Refresh()
    [pscustomobject]@{
        t       = [math]::Round($Clock.Elapsed.TotalSeconds, 2)
        wsMb    = [math]::Round($Process.WorkingSet64 / 1MB, 1)
        privMb  = [math]::Round($Process.PrivateMemorySize64 / 1MB, 1)
        handles = $Process.HandleCount
        threads = $Process.Threads.Count
        gdi     = [Win32Probe]::Gdi($Process.Handle)
        user    = [Win32Probe]::UserObjects($Process.Handle)
        cpuSec  = [math]::Round($Process.TotalProcessorTime.TotalSeconds, 3)
    }
}

function Get-InstanceKey([string]$DataDir) {
    $bytes = [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($DataDir).ToLowerInvariant())
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes), 0, 8)
}

function Get-AppLogFaults($Sandbox) {
    if (-not (Test-Path -LiteralPath $Sandbox.Log)) { return @() }
    return @(Select-String -LiteralPath $Sandbox.Log -Pattern 'FATAL|UNHANDLED|UNOBSERVED|FIRST-CHANCE| failed' |
            Select-Object -First 12 | ForEach-Object { $_.Line.Substring(0, [math]::Min(200, $_.Line.Length)) })
}

function Get-NewDumps([datetime]$Since) {
    $dir = Join-Path $env:LOCALAPPDATA 'CrashDumps'
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Filter 'SwiftX.exe.*.dmp' | Where-Object LastWriteTime -gt $Since | ForEach-Object Name)
}

function Test-Crashed($Process) {
    if ($Process.HasExited) { return [ordered]@{ crashed = $true; exitCode = ('0x{0:X8}' -f ([uint32]$Process.ExitCode)) } }
    return [ordered]@{ crashed = $false; exitCode = $null }
}

function Add-DriftVerdict {
    <# Attaches drift numbers and a GROWING/stable verdict per counter to a phase result. #>
    param($Phase, $Rows)
    $drift = [ordered]@{
        privMb  = Get-Drift $Rows 'privMb'
        wsMb    = Get-Drift $Rows 'wsMb'
        handles = Get-Drift $Rows 'handles'
        gdi     = Get-Drift $Rows 'gdi'
        user    = Get-Drift $Rows 'user'
        threads = Get-Drift $Rows 'threads'
    }
    $limits = @{ privMb = $MaxPrivateMbPer100; handles = $MaxHandlesPer100; gdi = $MaxGdiPer100; user = $MaxUserPer100; threads = 2; wsMb = $MaxPrivateMbPer100 * 2 }
    $verdicts = [ordered]@{}
    foreach ($k in $drift.Keys) {
        $s = $drift[$k].slopePer100
        $verdicts[$k] = if ($null -eq $s) { 'insufficient data' } elseif ($s -gt $limits[$k]) { 'GROWING' } else { 'stable' }
    }
    $Phase.drift = $drift
    $Phase.driftVerdict = $verdicts
    $Phase.samples = @($Rows)
}

function Get-PingSummary($Pinger) {
    $s = $Pinger.Stats()
    return [ordered]@{
        pings = [int]$s[0]; p50Ms = Round1 $s[1]; p95Ms = Round1 $s[2]; p99Ms = Round1 $s[3]; maxMs = Round1 $s[4]
        timeouts = [int]$s[5]; stalls = [int]$s[6]; longestStallMs = Round1 $s[7]; stallBudgetMs = $StallBudgetMs
        stallList = @($Pinger.Stalls() | Select-Object -First 15 | ForEach-Object { [ordered]@{ atSec = Round1 $_[0]; ms = Round1 $_[1] } })
    }
}

# ---- Phases -----------------------------------------------------------------------------------
$result = [ordered]@{
    generated = (Get-Date).ToString('o')
    exe       = $ExePath
    runFolder = $run
    facts     = Get-MachineFacts -RepoRoot $repo
    cores     = $cores
    phases    = [ordered]@{}
}

function Invoke-StartupPhase {
    $runs = @()
    for ($i = 1; $i -le $StartupRuns; $i++) {
        $sb = New-Sandbox "startup-$i" (New-Config)
        $since = Get-Date
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Instance $sb @()
        $tTray = $null; $tSettings = $null
        while ($clock.ElapsedMilliseconds -lt 30000 -and (-not $tTray -or -not $tSettings) -and -not $p.HasExited) {
            if (-not $tTray -and [Win32Probe]::FindMessageWindow($p.Id) -ne 0) { $tTray = $clock.Elapsed.TotalMilliseconds }
            if (-not $tSettings -and [Win32Probe]::FindVisibleByTitle($p.Id, 'SwiftX app') -ne 0) { $tSettings = $clock.Elapsed.TotalMilliseconds }
            [Threading.Thread]::Sleep(2)
        }
        # Settled = the first second in which the process used under 50 ms of CPU.
        $tSettled = $null
        $prev = $null
        $limit = $clock.Elapsed.TotalSeconds + 25
        while (-not $p.HasExited -and $clock.Elapsed.TotalSeconds -lt $limit) {
            Start-Sleep -Milliseconds 1000
            $p.Refresh()
            $cur = $p.TotalProcessorTime.TotalSeconds
            if ($null -ne $prev -and ($cur - $prev) -lt 0.05) { $tSettled = $clock.Elapsed.TotalSeconds; break }
            $prev = $cur
        }
        Start-Sleep -Milliseconds 1500
        $state = Test-Crashed $p
        $c = if (-not $p.HasExited) { Get-Counters $p $clock } else { $null }
        $peak = if (-not $p.HasExited) { [math]::Round($p.PeakWorkingSet64 / 1MB, 1) } else { $null }
        $cpuTotal = if (-not $p.HasExited) { [math]::Round($p.TotalProcessorTime.TotalSeconds, 2) } else { $null }
        Stop-Instance $p
        $runs += [pscustomobject][ordered]@{
            run = $i; trayReadyMs = Round1 $tTray; settingsVisibleMs = Round1 $tSettings
            cpuQuietAfterSec = Round1 $tSettled; cpuSecondsToSettle = $cpuTotal; peakWsMb = $peak
            wsMb = ${c}?.wsMb; privMb = ${c}?.privMb; handles = ${c}?.handles; gdi = ${c}?.gdi; user = ${c}?.user; threads = ${c}?.threads
            crashed = $state.crashed; exitCode = $state.exitCode
            newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb
        }
        Write-Host ("  startup {0}: tray {1} ms, Settings visible {2} ms, quiet after {3}s, ws {4} MB, priv {5} MB" -f $i, (Round1 $tTray), (Round1 $tSettings), (Round1 $tSettled), ${c}?.wsMb, ${c}?.privMb)
    }
    $good = @($runs | Where-Object { -not $_.crashed -and $null -ne $_.settingsVisibleMs })
    $warm = @($good | Select-Object -Skip 1)   # run 1 is cold: file cache, JIT, antivirus first-scan
    return [ordered]@{
        runs = $runs
        coldSettingsVisibleMs = if ($good.Count) { $good[0].settingsVisibleMs } else { $null }
        warmSettingsVisibleMedianMs = if ($warm.Count) { Round1 (Get-Median ([double[]]($warm | ForEach-Object settingsVisibleMs))) } else { $null }
        warmSettingsVisibleMaxMs = if ($warm.Count) { Round1 (($warm | Measure-Object settingsVisibleMs -Maximum).Maximum) } else { $null }
        warmTrayReadyMedianMs = if ($warm.Count) { Round1 (Get-Median ([double[]]($warm | ForEach-Object trayReadyMs))) } else { $null }
        medianPrivMb = if ($good.Count) { Round1 (Get-Median ([double[]]($good | ForEach-Object privMb))) } else { $null }
        medianHandles = if ($good.Count) { Get-Median ([double[]]($good | ForEach-Object handles)) } else { $null }
    }
}

function Invoke-IdleVariant {
    param([string]$Name, $Config, [string[]]$AppArgs = @('--startup'), [string]$WaitForTitle = '')
    $sb = New-Sandbox "idle-$Name" $Config
    $p = Start-Instance $sb $AppArgs
    $up = if ($WaitForTitle) { [Win32Probe]::WaitTitle($p.Id, $WaitForTitle, $true, 30000) } else { [Win32Probe]::WaitMessageWindow($p.Id, 30000) }
    if ($up -lt 0) {
        Stop-Instance $p
        return [ordered]@{ variant = $Name; error = 'window never appeared'; appLogFaults = Get-AppLogFaults $sb }
    }
    # Let start-up work finish; idle means idle. The Settings window's Home marks play for 20 s after
    # arrival (HomeIdlePolicy.RestAfter) before resting, so that variant waits past it: it measures the
    # resting window, which is what a window left open behind other windows is.
    Start-Sleep -Seconds $(if ($WaitForTitle) { 26 } else { 12 })
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $rows = @()
    for ($s = 0; $s -lt $IdleSeconds; $s++) {
        if ($p.HasExited) { break }
        $r = Get-Counters $p $clock
        $r | Add-Member -NotePropertyName cycle -NotePropertyValue $s
        $rows += $r
        Start-Sleep -Milliseconds 1000
    }
    $state = Test-Crashed $p
    Stop-Instance $p
    $cpuPct = $null
    if ($rows.Count -ge 2) {
        $dt = $rows[-1].t - $rows[0].t
        if ($dt -gt 0) { $cpuPct = [math]::Round((($rows[-1].cpuSec - $rows[0].cpuSec) / $dt) * 100, 3) }
    }
    return [ordered]@{
        variant = $Name; seconds = $IdleSeconds
        cpuPercentOfOneCore = $cpuPct
        cpuPercentOfMachine = if ($null -ne $cpuPct) { [math]::Round($cpuPct / $cores, 3) } else { $null }
        wsMbStart = $rows[0].wsMb; wsMbEnd = $rows[-1].wsMb
        privMbStart = $rows[0].privMb; privMbEnd = $rows[-1].privMb
        handles = $rows[-1].handles; threads = $rows[-1].threads; gdi = $rows[-1].gdi; user = $rows[-1].user
        crashed = $state.crashed; exitCode = $state.exitCode
        appLogFaults = Get-AppLogFaults $sb
    }
}

function Invoke-IdlePhase {
    $none = [ordered]@{ skipped = $true }
    $a = if ($IdleVariants -contains 'default') { Invoke-IdleVariant 'default' (New-Config) } else { $none }
    $b = if ($IdleVariants -contains 'hotcorner-off') { Invoke-IdleVariant 'hotcorner-off' (New-Config @{ HotCornerEnabled = $false }) } else { $none }
    $delta = $null
    if ($a.Contains('cpuPercentOfOneCore') -and $b.Contains('cpuPercentOfOneCore') -and $null -ne $a.cpuPercentOfOneCore -and $null -ne $b.cpuPercentOfOneCore) {
        $delta = [math]::Round($a.cpuPercentOfOneCore - $b.cpuPercentOfOneCore, 3)
    }
    # The Settings window sitting open and untouched: the state most people leave the app in
    # while they read or think. Animated surfaces show up here and nowhere else.
    $c = if ($IdleVariants -contains 'settings-open') { Invoke-IdleVariant 'settings-open' (New-Config) @() 'SwiftX app' } else { $none }
    Write-Host ("  idle (tray only): {0}% of one core; hot corner off: {1}%; Settings window open: {2}%" -f ${a}['cpuPercentOfOneCore'], ${b}['cpuPercentOfOneCore'], ${c}['cpuPercentOfOneCore'])
    return [ordered]@{ default = $a; hotCornerOff = $b; settingsOpen = $c; hotCornerCostPercentOfOneCore = $delta; budgetPercentOfOneCore = $MaxIdleCpuPercent }
}

function Invoke-SummonPhase {
    $sb = New-Sandbox 'summon' (New-Config)
    $since = Get-Date
    $p = Start-Instance $sb @('--startup')
    $ready = [Win32Probe]::WaitMessageWindow($p.Id, 30000)
    if ($ready -lt 0) { Stop-Instance $p; return [ordered]@{ error = 'message window never appeared'; appLogFaults = Get-AppLogFaults $sb } }
    $msg = [Win32Probe]::FindMessageWindow($p.Id)
    Start-Sleep -Seconds 6

    $pinger = [UiPinger]::new()
    $pinger.Start($msg, 25, 120000, $StallBudgetMs)
    $baseline = [Win32Probe]::CountVisible($p.Id)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $rows = @(); $summon = [System.Collections.Generic.List[double]]::new(); $dismiss = [System.Collections.Generic.List[double]]::new()
    $failures = 0; $completed = 0

    for ($c = 1; $c -le $SummonCycles; $c++) {
        if ($p.HasExited) { break }
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
        $ms = [Win32Probe]::WaitVisibleCount($p.Id, $baseline, $true, 8000)
        if ($ms -lt 0) { $failures++; Start-Sleep -Milliseconds 500; continue }
        $summon.Add($ms)
        Start-Sleep -Milliseconds 250
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
        $ms2 = [Win32Probe]::WaitVisibleCount($p.Id, $baseline, $false, 8000)
        if ($ms2 -lt 0) { $failures++ } else { $dismiss.Add($ms2) }
        $completed++
        Start-Sleep -Milliseconds 100
        if ($c -eq 1 -or $c % 5 -eq 0) {
            $r = Get-Counters $p $clock
            $r | Add-Member -NotePropertyName cycle -NotePropertyValue $c
            $rows += $r
        }
    }
    Start-Sleep -Seconds 3
    $state = Test-Crashed $p
    if (-not $p.HasExited) {
        $r = Get-Counters $p $clock
        $r | Add-Member -NotePropertyName cycle -NotePropertyValue ($SummonCycles + 1)
        $rows += $r
    }
    $ping = Get-PingSummary $pinger
    Stop-Instance $p
    $pinger.Stop()

    $out = [ordered]@{
        cyclesRequested = $SummonCycles; cyclesCompleted = $completed; failures = $failures
        summonMs = [ordered]@{ p50 = Round1 (Get-Percentile $summon 0.5); p95 = Round1 (Get-Percentile $summon 0.95); max = Round1 (($summon | Measure-Object -Maximum).Maximum); first = Round1 ($summon | Select-Object -First 1) }
        dismissMs = [ordered]@{ p50 = Round1 (Get-Percentile $dismiss 0.5); p95 = Round1 (Get-Percentile $dismiss 0.95); max = Round1 (($dismiss | Measure-Object -Maximum).Maximum) }
        uiThread = $ping
        crashed = $state.crashed; exitCode = $state.exitCode
        newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb
    }
    if ($rows.Count) { Add-DriftVerdict $out $rows }
    Write-Host ("  summon: {0}/{1} cycles, summon p50 {2} ms p95 {3} ms, UI stalls {4} (longest {5} ms), crashed {6}" -f $completed, $SummonCycles, $out.summonMs.p50, $out.summonMs.p95, $ping.stalls, $ping.longestStallMs, $state.crashed)
    return $out
}

function Invoke-SettingsPhase {
    $sb = New-Sandbox 'settings' (New-Config)
    $since = Get-Date
    $p = Start-Instance $sb @('--startup')
    if ([Win32Probe]::WaitMessageWindow($p.Id, 30000) -lt 0) { Stop-Instance $p; return [ordered]@{ error = 'message window never appeared'; appLogFaults = Get-AppLogFaults $sb } }
    $msg = [Win32Probe]::FindMessageWindow($p.Id)
    $showSettings = [Win32Probe]::Register('SwiftX.ShowSettings.v1.' + (Get-InstanceKey $sb.Data))
    Start-Sleep -Seconds 6

    $pinger = [UiPinger]::new()
    $pinger.Start($msg, 25, 120000, $StallBudgetMs)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $rows = @(); $open = [System.Collections.Generic.List[double]]::new(); $ready = [System.Collections.Generic.List[double]]::new()
    $close = [System.Collections.Generic.List[double]]::new(); $failures = 0; $completed = 0

    for ($c = 1; $c -le $SettingsCycles; $c++) {
        if ($p.HasExited) { break }
        $t0 = [Diagnostics.Stopwatch]::StartNew()
        [void][Win32Probe]::Post($msg, $showSettings, 0, 0)
        $ms = [Win32Probe]::WaitTitle($p.Id, 'SwiftX app', $true, 20000)
        if ($ms -lt 0) { $failures++; continue }
        $open.Add($ms)
        # Interactive = five consecutive pings answered within 30 ms (the Loaded handler is done).
        $fast = 0
        while ($t0.ElapsedMilliseconds -lt 25000 -and $fast -lt 5) {
            $lat = [Win32Probe]::Ping($msg, 30000)
            if ($lat -ge 0 -and $lat -lt 30) { $fast++ } else { $fast = 0 }
            [Threading.Thread]::Sleep(10)
        }
        $ready.Add($t0.Elapsed.TotalMilliseconds)
        Start-Sleep -Milliseconds 300
        $h = [Win32Probe]::FindVisibleByTitle($p.Id, 'SwiftX app')
        if ($h -ne 0) { [void][Win32Probe]::Post($h, $WM_CLOSE, 0, 0) }
        $ms2 = [Win32Probe]::WaitTitle($p.Id, 'SwiftX app', $false, 15000)
        if ($ms2 -lt 0) { $failures++ } else { $close.Add($ms2) }
        $completed++
        Start-Sleep -Milliseconds 400
        if ($c -eq 1 -or $c % 2 -eq 0) {
            $r = Get-Counters $p $clock
            $r | Add-Member -NotePropertyName cycle -NotePropertyValue $c
            $rows += $r
        }
    }
    Start-Sleep -Seconds 3
    $state = Test-Crashed $p
    if (-not $p.HasExited) {
        $r = Get-Counters $p $clock
        $r | Add-Member -NotePropertyName cycle -NotePropertyValue ($SettingsCycles + 1)
        $rows += $r
    }
    $ping = Get-PingSummary $pinger
    $keptPid = $null
    if ($KeepRunning -and -not $p.HasExited) { $keptPid = $p.Id; $script:kept.Add($p.Id) } else { Stop-Instance $p }
    $pinger.Stop()

    $out = [ordered]@{
        keptRunningPid = $keptPid
        cyclesRequested = $SettingsCycles; cyclesCompleted = $completed; failures = $failures
        windowVisibleMs = [ordered]@{ p50 = Round1 (Get-Percentile $open 0.5); p95 = Round1 (Get-Percentile $open 0.95); max = Round1 (($open | Measure-Object -Maximum).Maximum); first = Round1 ($open | Select-Object -First 1) }
        interactiveMs = [ordered]@{ p50 = Round1 (Get-Percentile $ready 0.5); p95 = Round1 (Get-Percentile $ready 0.95); max = Round1 (($ready | Measure-Object -Maximum).Maximum) }
        closeMs = [ordered]@{ p50 = Round1 (Get-Percentile $close 0.5); p95 = Round1 (Get-Percentile $close 0.95) }
        uiThread = $ping
        crashed = $state.crashed; exitCode = $state.exitCode
        newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb
    }
    if ($rows.Count) { Add-DriftVerdict $out $rows }
    Write-Host ("  settings: {0}/{1} cycles, visible p50 {2} ms, interactive p50 {3} ms, UI stalls {4} (longest {5} ms), crashed {6}" -f $completed, $SettingsCycles, $out.windowVisibleMs.p50, $out.interactiveMs.p50, $ping.stalls, $ping.longestStallMs, $state.crashed)
    return $out
}

function Invoke-ActivationPhase {
    # Settings is opened once, then repeatedly deactivated and re-activated by summoning and dismissing
    # the orbit over it: the orbit takes foreground, Settings loses it, and gets it back when the
    # orbit goes away - the same Activated / Deactivated pair Alt-Tabbing away and back produces, with
    # no input injected. (Posting WM_ACTIVATE straight at the window does NOT raise WinUI's
    # Window.Activated, so that is not a usable shortcut.) Several window classes re-apply their
    # chrome on every Activated event, so anything that leaks per call shows up here as drift per
    # activation and nowhere else.
    $WM_HOTKEY = 0x0312
    $sb = New-Sandbox 'activation' (New-Config)
    $since = Get-Date
    $p = Start-Instance $sb @('--startup')
    if ([Win32Probe]::WaitMessageWindow($p.Id, 30000) -lt 0) { Stop-Instance $p; return [ordered]@{ error = 'message window never appeared'; appLogFaults = Get-AppLogFaults $sb } }
    $msg = [Win32Probe]::FindMessageWindow($p.Id)
    $showSettings = [Win32Probe]::Register('SwiftX.ShowSettings.v1.' + (Get-InstanceKey $sb.Data))
    Start-Sleep -Seconds 5
    [void][Win32Probe]::Post($msg, $showSettings, 0, 0)
    if ([Win32Probe]::WaitTitle($p.Id, 'SwiftX app', $true, 20000) -lt 0) { Stop-Instance $p; return [ordered]@{ error = 'Settings window never appeared' } }
    $h = [Win32Probe]::FindVisibleByTitle($p.Id, 'SwiftX app')
    Start-Sleep -Seconds 3   # let the window finish loading before it is toggled

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $rows = @()
    for ($i = 1; $i -le $ActivationPairs; $i++) {
        if ($p.HasExited) { break }
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)   # summon the orbit: Settings is deactivated
        Start-Sleep -Milliseconds 400
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)   # dismiss it: focus returns
        Start-Sleep -Milliseconds 700
        if ($i -eq 1 -or $i % 10 -eq 0) {
            $r = Get-Counters $p $clock
            $r | Add-Member -NotePropertyName cycle -NotePropertyValue $i
            $rows += $r
        }
    }
    Start-Sleep -Seconds 2
    $state = Test-Crashed $p
    Stop-Instance $p
    $out = [ordered]@{ pairs = $ActivationPairs; crashed = $state.crashed; exitCode = $state.exitCode; newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb }
    if ($rows.Count) { Add-DriftVerdict $out $rows }
    $first = $rows | Select-Object -First 1; $last = $rows | Select-Object -Last 1
    if ($first -and $last -and $ActivationPairs -gt 1) {
        $out.gdiPerActivationPair = [math]::Round(($last.gdi - $first.gdi) / ($last.cycle - $first.cycle), 2)
        $out.handlesPerActivationPair = [math]::Round(($last.handles - $first.handles) / ($last.cycle - $first.cycle), 2)
    }
    Write-Host ("  activation: {0} pairs, GDI +{1} and handles +{2} per pair, crashed {3}" -f $ActivationPairs, $out['gdiPerActivationPair'], $out['handlesPerActivationPair'], $state.crashed)
    return $out
}

function Invoke-SpikePhase {
    # A burst of irregular toggles far faster than a human can produce: the summon/dismiss
    # animations overlap, IsClosing and IsOpen race, and anything not re-entrancy-safe shows up as
    # a crash, a wedged orbit or a UI stall. Afterwards the app must still work normally.
    $sb = New-Sandbox 'spike' (New-Config)
    $since = Get-Date
    $p = Start-Instance $sb @('--startup')
    if ([Win32Probe]::WaitMessageWindow($p.Id, 30000) -lt 0) { Stop-Instance $p; return [ordered]@{ error = 'message window never appeared'; appLogFaults = Get-AppLogFaults $sb } }
    $msg = [Win32Probe]::FindMessageWindow($p.Id)
    Start-Sleep -Seconds 6
    $pinger = [UiPinger]::new()
    $pinger.Start($msg, 25, 120000, $StallBudgetMs)
    $baseline = [Win32Probe]::CountVisible($p.Id)
    $rnd = [Random]::new(20261001)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $sent = 0
    for ($i = 0; $i -lt $SpikeToggles; $i++) {
        if ($p.HasExited) { break }
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
        $sent++
        [Threading.Thread]::Sleep($rnd.Next(5, 60))
    }
    $burstSeconds = $clock.Elapsed.TotalSeconds
    Start-Sleep -Seconds 4   # let the last animation finish and the queue drain

    # Recovery: the orbit must still open and close on request.
    $visibleNow = [Win32Probe]::CountVisible($p.Id)
    if ($visibleNow -gt $baseline) {
        [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
        [void][Win32Probe]::WaitVisibleCount($p.Id, $baseline, $false, 8000)
        Start-Sleep -Milliseconds 600
    }
    [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
    $opens = [Win32Probe]::WaitVisibleCount($p.Id, $baseline, $true, 8000)
    Start-Sleep -Milliseconds 400
    [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
    $closes = [Win32Probe]::WaitVisibleCount($p.Id, $baseline, $false, 8000)

    $state = Test-Crashed $p
    $ping = Get-PingSummary $pinger
    $counters = if (-not $p.HasExited) { Get-Counters $p $clock } else { $null }
    Stop-Instance $p
    $pinger.Stop()
    Write-Host ("  spike: {0} toggles in {1:N1}s; recovery open {2} ms / close {3} ms; UI stalls {4} (longest {5} ms); crashed {6}" -f $sent, $burstSeconds, (Round1 $opens), (Round1 $closes), $ping.stalls, $ping.longestStallMs, $state.crashed)
    return [ordered]@{
        togglesSent = $sent; burstSeconds = Round1 $burstSeconds
        recoveredOpen = ($opens -ge 0); recoveredClose = ($closes -ge 0)
        recoveryOpenMs = Round1 $opens; recoveryCloseMs = Round1 $closes
        uiThread = $ping; counters = $counters
        crashed = $state.crashed; exitCode = $state.exitCode
        newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb
    }
}

function Invoke-UnreachablePhase {
    # TEST-NET-1/2 (RFC 5737) are reserved documentation ranges: never routed, so a connect simply
    # times out - exactly what a laptop off the office VPN does to a mapped share.
    # Built as a statement, not `$extra = if (...) { @() }`: an empty array assigned from an `if`
    # comes out as $null, which -ExtraItems turns into one null entry in the seeded orbit.
    $extra = @()
    if (-not $NoUnreachableItems) {
        $extra = @(
            [ordered]@{ Kind = 'Folder'; DisplayName = 'Unreachable folder'; Target = '\\192.0.2.1\share\folder' }
            [ordered]@{ Kind = 'Application'; DisplayName = 'Unreachable app'; Target = '\\198.51.100.7\tools\app.exe' }
        )
    }
    $sb = New-Sandbox 'unreachable' (New-Config -ExtraItems $extra)
    $since = Get-Date
    $p = Start-Instance $sb @('--startup')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $msgFound = $false
    while ($clock.ElapsedMilliseconds -lt 90000 -and -not $p.HasExited) {
        if ([Win32Probe]::FindMessageWindow($p.Id) -ne 0) { $msgFound = $true; break }
        [Threading.Thread]::Sleep(10)
    }
    if (-not $msgFound) { Stop-Instance $p; return [ordered]@{ error = 'message window never appeared within 90 s'; startupMs = Round1 $clock.Elapsed.TotalMilliseconds; appLogFaults = Get-AppLogFaults $sb } }
    $startupMs = $clock.Elapsed.TotalMilliseconds
    $msg = [Win32Probe]::FindMessageWindow($p.Id)

    $pinger = [UiPinger]::new()
    $pinger.Start($msg, 25, 180000, $StallBudgetMs)
    Start-Sleep -Seconds 12
    # A stall that is still running is counted by the pinger (it reports the outstanding ping), and
    # the instance is then given time to finish it, so the length of the freeze is measured rather
    # than cut off by the end of the observation window.
    $ongoingAtStart = $pinger.OngoingMs()
    $startupResponsive = $pinger.WaitUntilResponsive(90000)
    $afterStartup = Get-PingSummary $pinger

    $pinger.Reset()
    $baseline = [Win32Probe]::CountVisible($p.Id)
    $t0 = [Diagnostics.Stopwatch]::StartNew()
    [void][Win32Probe]::Post($msg, $WM_HOTKEY, 1, 0)
    $visible = [Win32Probe]::WaitVisibleCount($p.Id, $baseline, $true, 120000)
    Start-Sleep -Seconds 3
    $ongoingAtSummon = $pinger.OngoingMs()
    $summonResponsive = $pinger.WaitUntilResponsive(90000)
    $afterSummon = Get-PingSummary $pinger
    $state = Test-Crashed $p
    Stop-Instance $p
    $pinger.Stop()
    Write-Host ("  unreachable: message window after {0} ms; start-up: {1} stall(s), longest {2} ms (still stalled after 12 s: {3} ms); first summon visible in {4} ms, {5} stall(s), longest {6} ms (still stalled 3 s after: {7} ms)" -f (Round1 $startupMs), $afterStartup.stalls, $afterStartup.longestStallMs, (Round1 $ongoingAtStart), (Round1 $visible), $afterSummon.stalls, $afterSummon.longestStallMs, (Round1 $ongoingAtSummon))
    return [ordered]@{
        messageWindowReadyMs = Round1 $startupMs
        stalledStillAfter12sMs = Round1 $ongoingAtStart
        startupUiThread = $afterStartup
        startupResponsiveAgain = $startupResponsive
        firstSummonVisibleMs = Round1 $visible
        stalledStill3sAfterSummonMs = Round1 $ongoingAtSummon
        summonResponsiveAgain = $summonResponsive
        summonUiThread = $afterSummon
        crashed = $state.crashed; exitCode = $state.exitCode
        newDumps = Get-NewDumps $since; appLogFaults = Get-AppLogFaults $sb
    }
}

function Invoke-AttachPhase {
    if ($AttachPid -le 0) { throw '-AttachPid is required for the attach phase.' }
    $p = Get-Process -Id $AttachPid -ErrorAction Stop
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $rows = @()
    $end = $AttachMinutes * 60
    Write-Host "  attached read-only to PID $AttachPid ($($p.ProcessName)); sampling every 5 s for $AttachMinutes min"
    $i = 0
    while ($clock.Elapsed.TotalSeconds -lt $end -and -not $p.HasExited) {
        $r = Get-Counters $p $clock
        $r | Add-Member -NotePropertyName cycle -NotePropertyValue $i
        $rows += $r
        $i++
        Start-Sleep -Seconds 5
    }
    # For an attach the x axis is samples (5 s each), so "per 100" is per 500 s: say so.
    $out = [ordered]@{ pid = $AttachPid; minutes = $AttachMinutes; sampleIntervalSec = 5; note = 'slopePer100 here means per 100 samples = per 500 s' }
    if ($rows.Count) { Add-DriftVerdict $out $rows }
    return $out
}

# ---- Run --------------------------------------------------------------------------------------
try {
    foreach ($ph in $Phase) {
        Write-Host "== $ph"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $result.phases[$ph] = switch ($ph) {
            'startup' { Invoke-StartupPhase }
            'idle' { Invoke-IdlePhase }
            'summon' { Invoke-SummonPhase }
            'spike' { Invoke-SpikePhase }
            'settings' { Invoke-SettingsPhase }
            'activation' { Invoke-ActivationPhase }
            'unreachable' { Invoke-UnreachablePhase }
            'attach' { Invoke-AttachPhase }
        }
        $result.phases[$ph]['phaseSeconds'] = [math]::Round($sw.Elapsed.TotalSeconds, 0)
        Write-Json -Object $result -Path (Join-Path $run 'live-app.json') -Depth 12   # keep partial results if a later phase dies
    }
}
finally {
    # Belt and braces: anything this script launched and left running is stopped; nothing else is.
    foreach ($id in $script:launched) {
        if ($script:kept.Contains($id)) { continue }   # -KeepRunning: the caller wants it for heap tooling and will stop it
        $left = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($left) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    }
}

# ---- Verdict + markdown -----------------------------------------------------------------------
$findings = [System.Collections.Generic.List[string]]::new()
foreach ($name in 'summon', 'settings', 'activation') {
    if (-not $result.phases.Contains($name)) { continue }
    $ph = $result.phases[$name]
    if ($ph.Contains('error')) { $findings.Add("$name : $($ph.error)"); continue }
    if ($ph.crashed) { $findings.Add("$name : THE APP CRASHED (exit $($ph.exitCode))") }
    if ($ph.Contains('failures') -and $ph.failures -gt 0) { $findings.Add("$name : $($ph.failures) cycle step(s) timed out") }
    if ($ph.Contains('uiThread') -and $ph.uiThread.longestStallMs -gt $StallBudgetMs) { $findings.Add("$name : UI thread stalled up to $($ph.uiThread.longestStallMs) ms (budget $StallBudgetMs ms, $($ph.uiThread.stalls) stall(s))") }
    if ($ph.Contains('driftVerdict')) {
        foreach ($k in $ph.driftVerdict.Keys) {
            if ($ph.driftVerdict[$k] -eq 'GROWING') { $findings.Add("$name : $k GROWING at $($ph.drift[$k].slopePer100) per 100 cycles (early $($ph.drift[$k].early) -> late $($ph.drift[$k].late))") }
        }
    }
}
if ($result.phases.Contains('spike')) {
    $s = $result.phases['spike']
    if ($s.Contains('error')) { $findings.Add("spike : $($s.error)") }
    else {
        if ($s.crashed) { $findings.Add("spike : THE APP CRASHED (exit $($s.exitCode))") }
        if (-not $s.recoveredOpen -or -not $s.recoveredClose) { $findings.Add('spike : the orbit did not open and close on request after the burst (wedged)') }
        if ($s.uiThread.longestStallMs -gt $StallBudgetMs) { $findings.Add("spike : UI thread stalled up to $($s.uiThread.longestStallMs) ms") }
    }
}
if ($result.phases.Contains('idle')) {
    $i = $result.phases['idle']
    foreach ($v in 'default', 'settingsOpen') {
        if ($i[$v].Contains('cpuPercentOfOneCore') -and $null -ne $i[$v].cpuPercentOfOneCore -and $i[$v].cpuPercentOfOneCore -gt $MaxIdleCpuPercent) {
            $findings.Add("idle ($v) : $($i[$v].cpuPercentOfOneCore)% of one core exceeds the $MaxIdleCpuPercent% budget")
        }
    }
}
if ($result.phases.Contains('unreachable')) {
    $u = $result.phases['unreachable']
    if (-not $u.Contains('error')) {
        foreach ($k in 'startupUiThread', 'summonUiThread') {
            if ($u[$k].longestStallMs -gt $StallBudgetMs) { $findings.Add("unreachable : UI thread stalled $($u[$k].longestStallMs) ms in $k with an unreachable network target on the orbit") }
        }
    }
}
$result.findings = @($findings)
Write-Json -Object $result -Path (Join-Path $run 'live-app.json') -Depth 12

$md = [Text.StringBuilder]::new()
[void]$md.AppendLine('# Live app measurements')
[void]$md.AppendLine()
[void]$md.AppendLine("- Exe: ``$ExePath``")
[void]$md.AppendLine("- Machine: $($result.facts.cpu), $($result.facts.logicalCores) logical cores, $($result.facts.ramGb) GB, power plan '$($result.facts.powerPlan)', on battery = $($result.facts.onBattery)")
[void]$md.AppendLine("- Commit: $($result.facts.git.sha) ($($result.facts.git.dirtyFiles) uncommitted paths); user's own SwiftX PIDs at run time: $($result.facts.runningSwiftXPids -join ', ')")
[void]$md.AppendLine()
if ($findings.Count) { [void]$md.AppendLine('## Findings'); foreach ($f in $findings) { [void]$md.AppendLine("- $f") } } else { [void]$md.AppendLine('## Findings'); [void]$md.AppendLine('- none: every measured phase is inside its budget') }
[void]$md.AppendLine()
[void]$md.AppendLine('```json')
[void]$md.AppendLine((($result.phases | ConvertTo-Json -Depth 8) -replace '(?s)"samples":\s*\[.*?\]\s*,?', '"samples": [...],'))
[void]$md.AppendLine('```')
$md.ToString() | Set-Content -LiteralPath (Join-Path $run 'live-app.md') -Encoding utf8

Write-Host ''
if ($findings.Count) { Write-Host 'FINDINGS:'; $findings | ForEach-Object { Write-Host "  - $_" } } else { Write-Host 'no findings: all phases inside budget' }
Write-Host "run folder: $run"
