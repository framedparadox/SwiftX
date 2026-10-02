# Shared helpers for the performance-review scripts.
# Dot-source it:   . "$PSScriptRoot\_common.ps1"
#
# Everything here is read-only with respect to the product and to the user's data. Nothing in
# this file kills a process, touches %AppData%\SwiftX, or writes inside the repository.

Set-StrictMode -Version Latest

function Resolve-RepoRoot {
    <# Finds the repository root without trusting the caller's working directory, which drifts in
       agent sessions. Order: explicit hint, four levels above this script (the project-skill
       layout .claude/skills/<name>/scripts), then git from the current directory. #>
    param([string]$Hint)

    if ($Hint) { return (Resolve-Path -LiteralPath $Hint).Path }

    $fromScript = Join-Path $PSScriptRoot '..\..\..\..'
    if (Test-Path -LiteralPath (Join-Path $fromScript '.git')) {
        return (Resolve-Path -LiteralPath $fromScript).Path
    }

    $top = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top) { return (Resolve-Path -LiteralPath $top).Path }

    throw 'Cannot find the repository root. Pass -RepoRoot <path>.'
}

function Get-DefaultWorkRoot {
    # Outside the repository on purpose: scratch workspaces and raw logs are large, disposable,
    # and must never end up in a commit.
    return (Join-Path ([IO.Path]::GetTempPath()) 'swiftx-perf-review')
}

function New-RunFolder {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Label)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $Root "$stamp-$Label"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Write-Json {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string]$Path, [int]$Depth = 8)
    $Object | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-CpuPercent {
    <# Average total CPU use over a few one-second samples. Uses CIM rather than Get-Counter
       because counter *names* are localised and CIM property names are not. #>
    param([int]$Seconds = 3)
    $samples = for ($i = 0; $i -lt $Seconds; $i++) {
        $row = Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'"
        [double]$row.PercentProcessorTime
        Start-Sleep -Seconds 1
    }
    return [math]::Round((($samples | Measure-Object -Average).Average), 1)
}

function Wait-QuietMachine {
    <# The wall-clock scaling tests divide a small timing by a large one, so noise from anything
       else on the machine is amplified straight into a false "regression". Wait for the machine to
       settle and say honestly whether it did, so the report can state the conditions. #>
    param([double]$MaxCpuPercent = 25, [int]$TimeoutSeconds = 120)

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $cpu = Get-CpuPercent -Seconds 3
    while ($cpu -gt $MaxCpuPercent -and $clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 5
        $cpu = Get-CpuPercent -Seconds 3
    }
    return [pscustomobject]@{
        Quiet         = ($cpu -le $MaxCpuPercent)
        CpuPercent    = $cpu
        ThresholdPct  = $MaxCpuPercent
        WaitedSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 0)
    }
}

function Get-MachineFacts {
    <# Everything a reader needs to judge whether two sets of numbers are comparable. #>
    param([string]$RepoRoot)

    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1

    $plan = 'unknown'
    $scheme = (& powercfg /getactivescheme 2>$null)
    if ($scheme -match '\((.+)\)') { $plan = $Matches[1] }

    $sdk = (& dotnet --version 2>$null)

    $git = [ordered]@{ sha = $null; branch = $null; dirtyFiles = $null }
    if ($RepoRoot) {
        $git.sha = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
        $git.branch = (& git -C $RepoRoot branch --show-current 2>$null)
        $git.dirtyFiles = @(& git -C $RepoRoot status --porcelain 2>$null).Count
    }

    $running = @(Get-Process -Name SwiftX -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

    return [ordered]@{
        timestamp        = (Get-Date).ToString('o')
        machine          = $env:COMPUTERNAME
        os               = "$($os.Caption) $($os.Version)"
        cpu              = $cpu.Name.Trim()
        logicalCores     = $cpu.NumberOfLogicalProcessors
        ramGb            = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
        powerPlan        = $plan
        onBattery        = [bool]($battery -and $battery.BatteryStatus -eq 1)
        dotnetSdk        = $sdk
        git              = $git
        runningSwiftXPids = $running
    }
}

function Invoke-Robocopy {
    <# robocopy exit codes 0-7 mean success (bit flags for "something was copied", "extras", ...);
       8 and above are failures. Wrapped so callers need not remember that. #>
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination, [string[]]$ExcludeDirs = @())

    $roboArgs = @($Source, $Destination, '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1', '/XF', '*.user')
    if ($ExcludeDirs.Count) { $roboArgs += @('/XD') + $ExcludeDirs }
    & robocopy @roboArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

function Sync-ScratchWorkspace {
    <# Mirrors the working tree (uncommitted edits included) into a persistent scratch workspace and
       returns its path. bin/obj are excluded from the mirror AND preserved in the destination, so
       every build after the first is incremental, and the repo's own bin/obj are never touched. #>
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$WorkRoot)

    $ws = Join-Path $WorkRoot 'workspace'
    Invoke-Robocopy -Source $RepoRoot -Destination $ws `
        -ExcludeDirs @('.git', '.vs', 'bin', 'obj', 'TestResults', 'AppPackages', 'node_modules')
    return $ws
}

function Format-Duration {
    param([timespan]$Span)
    if ($Span.TotalMinutes -ge 1) { return '{0}m {1:00}s' -f [int]$Span.TotalMinutes, $Span.Seconds }
    return '{0:0.0}s' -f $Span.TotalSeconds
}
