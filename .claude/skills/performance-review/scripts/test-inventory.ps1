#Requires -Version 7.0
<#
.SYNOPSIS
  Writes the "what is tested, and did it pass" document: every test class, what it is for (the first
  sentence of its doc comment), how many tests ran, how many passed, and how long they took.

.DESCRIPTION
  Joins two sources: the per-test results a run-suite.ps1 run wrote (tests.csv) and the test
  sources (class summaries). Nothing is hand-maintained, so the document cannot drift from the
  suite; regenerate it after every review.

.EXAMPLE
  test-inventory.ps1 -ResultsCsv <run>\tests.csv -Out docs\performance\2026-10-01-review\test-inventory.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsCsv,
    [Parameter(Mandatory)][string]$Out,
    [string]$RepoRoot,
    [string]$TestsDir = 'tests'
)

. "$PSScriptRoot\_common.ps1"
$repo = Resolve-RepoRoot -Hint $RepoRoot
$results = Import-Csv -LiteralPath $ResultsCsv

# xunit names a test <namespace>.<class>.<method>[(args)]. Split off the method to get the class.
function Get-ClassName([string]$name) {
    $n = $name -replace '\(.*$', ''
    $i = $n.LastIndexOf('.')
    if ($i -lt 0) { return $n }
    return $n.Substring(0, $i)
}

$byClass = $results | Group-Object { Get-ClassName $_.Name }

# Class -> summary sentence, from the sources.
$summaries = @{}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repo $TestsDir) -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $ns = if ($text -match 'namespace\s+([\w.]+)\s*[;{]') { $Matches[1] } else { '' }
    foreach ($m in [regex]::Matches($text, '(?s)((?:[ \t]*///[^\r\n]*\r?\n)+)\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+(?:sealed\s+|static\s+)?class\s+(\w+)')) {
        $doc = $m.Groups[1].Value -replace '(?m)^\s*///\s?', ''
        $doc = $doc -replace '(?s)<para>.*', ''            # first paragraph only
        # A reference carries the words: keep the type or member name rather than dropping it.
        $doc = [regex]::Replace($doc, '<see(?:also)?\s+cref="([^"]+)"\s*/>', { param($m) ($m.Groups[1].Value -replace '^[A-Z]:', '' -split '\.')[-1] })
        $doc = [regex]::Replace($doc, '<see\s+langword="([^"]+)"\s*/>', '$1')
        $doc = $doc -replace '<[^>]+>', ''                 # strip tags
        $doc = ($doc -replace '\s+', ' ').Trim()
        $first = ($doc -split '(?<=[.!?])\s', 2)[0]
        $summaries["$ns.$($m.Groups[2].Value)"] = $first
    }
}

$md = [Text.StringBuilder]::new()
[void]$md.AppendLine('# Test inventory')
[void]$md.AppendLine()
$total = @($results).Count
$passed = @($results | Where-Object Outcome -eq 'Passed').Count
$failed = @($results | Where-Object Outcome -eq 'Failed').Count
$seconds = [math]::Round((($results | Measure-Object { [double]$_.Seconds } -Sum).Sum), 1)
[void]$md.AppendLine("Generated from a run of **$total** tests: **$passed** passed, **$failed** failed, $seconds s of test time (cumulative, tests run in parallel). Class descriptions are the first sentence of each class's doc comment in the test sources.")
[void]$md.AppendLine()

$folders = $byClass | Group-Object { ($_.Name -split '\.')[2] } | Sort-Object Name   # SwiftX.Tests.<Folder>.<Class>
foreach ($folder in $folders) {
    $count = ($folder.Group | ForEach-Object Count | Measure-Object -Sum).Sum
    [void]$md.AppendLine("## $($folder.Name) ($count tests)")
    [void]$md.AppendLine()
    [void]$md.AppendLine('| Class | Tests | Passed | Seconds | What it covers |')
    [void]$md.AppendLine('|---|---:|---:|---:|---|')
    foreach ($c in ($folder.Group | Sort-Object Name)) {
        $short = ($c.Name -split '\.')[-1]
        $p = @($c.Group | Where-Object Outcome -eq 'Passed').Count
        $s = [math]::Round((($c.Group | Measure-Object { [double]$_.Seconds } -Sum).Sum), 1)
        $what = if ($summaries.ContainsKey($c.Name)) { $summaries[$c.Name] -replace '\|', '/' } else { '' }
        [void]$md.AppendLine("| $short | $($c.Count) | $p | $s | $what |")
    }
    [void]$md.AppendLine()
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$md.ToString() | Set-Content -LiteralPath $Out -Encoding utf8
Write-Host "wrote $Out ($total tests in $($byClass.Count) classes)"
