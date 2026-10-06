#Requires -Version 7.0
<#
.SYNOPSIS
  Builds the app (not the tests) from a scratch copy of the working tree and prints the exe path.

.DESCRIPTION
  Release is what ships and what startup/memory/idle numbers must be taken from: Debug code is
  unoptimised and the numbers it produces are not the user's numbers. Building in the scratch
  workspace means the user's running SwiftX.exe (and any debugger on it) cannot lock the output.

.EXAMPLE
  build-app.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$RepoRoot,
    [string]$WorkRoot
)

. "$PSScriptRoot\_common.ps1"
$repo = Resolve-RepoRoot -Hint $RepoRoot
$work = if ($WorkRoot) { $WorkRoot } else { Get-DefaultWorkRoot }
New-Item -ItemType Directory -Force -Path $work | Out-Null

$ws = Sync-ScratchWorkspace -RepoRoot $repo -WorkRoot $work
$project = Join-Path $ws 'src\SwiftX\SwiftX.csproj'
$log = Join-Path $work "build-app-$Configuration.log"

$clock = [Diagnostics.Stopwatch]::StartNew()
& dotnet build $project -nologo "-p:Platform=$Platform" -c $Configuration 2>&1 | Tee-Object -FilePath $log | Out-Null
$exit = $LASTEXITCODE
$clock.Stop()

$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$exe = Join-Path $ws "src\SwiftX\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid\SwiftX.exe"

if ($exit -ne 0 -or -not (Test-Path -LiteralPath $exe)) {
    Write-Host "BUILD FAILED (exit $exit). Log: $log"
    Select-String -LiteralPath $log -Pattern ': error [A-Z]+\d+' | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
    exit 2
}

$size = [math]::Round((Get-ChildItem -LiteralPath (Split-Path $exe) -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ("built {0} {1} in {2}; output folder {3} MB" -f $Configuration, $Platform, (Format-Duration $clock.Elapsed), $size)
Write-Host "EXE=$exe"
