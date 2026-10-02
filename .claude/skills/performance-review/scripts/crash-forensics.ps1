#Requires -Version 7.0
<#
.SYNOPSIS
  Collects the evidence of past crashes, hangs and fault-prone behaviour of the app on this
  machine: Windows Application event log, WER user-mode dumps, and the app's own log. Read-only.

.DESCRIPTION
  1. Application event log (ids 1000 crash, 1002 hang, 1026 .NET unhandled) for the process,
     parsed and bucketed by (faulting module, exception code, fault offset). Same bucket =
     same bug; the count and date range say how often and whether it is still happening.
  2. WER dumps in %LOCALAPPDATA%\CrashDumps: no debugger needed. The newest dump(s) are
     string-scanned for managed frames and well-known interop markers.
  3. The app's own log (%TEMP%\swiftx.log): FATAL / UNHANDLED / UNOBSERVED lines.

  PRIVACY: a memory dump can contain anything the app held, including decrypted secrets (SwiftX
  has a Secure Values tool). The scan therefore extracts ONLY strings shaped like code
  identifiers (type/method names) and never prints, stores or returns any other string.

  0xc000027b is a stowed-exception fail-fast: it skips every managed handler and writes nothing
  to the app log, so the dump is the only evidence. 0xc0000005 in a system DLL is a native access
  violation: it is not catchable in managed code either.

.EXAMPLE
  crash-forensics.ps1 -OutDir docs\performance\2026-10-01\data
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'SwiftX',
    [int]$Days = 90,
    [int]$DumpsToScan = 3,
    [string]$OutDir,
    [string]$LogPath = (Join-Path ([IO.Path]::GetTempPath()) 'swiftx.log'),
    [string]$DumpDir = (Join-Path $env:LOCALAPPDATA 'CrashDumps')
)

. "$PSScriptRoot\_common.ps1"
if (-not $OutDir) { $OutDir = Join-Path (Get-DefaultWorkRoot) 'forensics' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class DumpIdentifierScan
{
    // Returns identifier-shaped strings (never anything else) with their occurrence counts.
    public static Dictionary<string, int> Scan(string path, string identifierPattern, int minLength)
    {
        var rx = new Regex(identifierPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        byte[] data = File.ReadAllBytes(path);

        // ASCII runs.
        var sb = new StringBuilder();
        for (int i = 0; i <= data.Length; i++)
        {
            byte b = i < data.Length ? data[i] : (byte)0;
            if (b >= 0x20 && b < 0x7F) { if (sb.Length < 400) sb.Append((char)b); }
            else { Flush(sb, rx, counts, minLength); }
        }

        // UTF-16LE runs, both alignments.
        for (int align = 0; align < 2; align++)
        {
            for (int i = align; i + 1 <= data.Length; i += 2)
            {
                byte lo = data[i];
                byte hi = i + 1 < data.Length ? data[i + 1] : (byte)1;
                if (hi == 0 && lo >= 0x20 && lo < 0x7F) { if (sb.Length < 400) sb.Append((char)lo); }
                else { Flush(sb, rx, counts, minLength); }
            }
            Flush(sb, rx, counts, minLength);
        }
        return counts;
    }

    private static void Flush(StringBuilder sb, Regex rx, Dictionary<string, int> counts, int minLength)
    {
        if (sb.Length >= minLength)
        {
            string s = sb.ToString();
            var m = rx.Match(s);
            if (m.Success)
            {
                string key = m.Value;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
        }
        sb.Clear();
    }
}
'@ -ErrorAction Stop

$result = [ordered]@{ generated = (Get-Date).ToString('o'); process = $ProcessName; days = $Days }

# ---- 1. Event log -------------------------------------------------------------------------------
$since = (Get-Date).AddDays(-$Days)
$events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since; Id = 1000, 1002, 1026 } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match "$ProcessName(\.exe)?" })

$crashes = foreach ($e in ($events | Where-Object Id -eq 1000)) {
    $m = $e.Message
    $field = { param($label) if ($m -match "$label\s*([^,\r\n]+)") { $Matches[1].Trim() } }
    [pscustomobject]@{
        Time          = $e.TimeCreated
        AppVersion    = (& $field 'Faulting application name: [^,]+, version:')
        Module        = (& $field 'Faulting module name:')
        ExceptionCode = (& $field 'Exception code:')
        Offset        = (& $field 'Fault offset:')
    }
}
$crashes = @($crashes)
$buckets = @($crashes | Group-Object Module, ExceptionCode, Offset | ForEach-Object {
        $g = $_.Group | Sort-Object Time
        [ordered]@{
            module        = $g[0].Module
            exceptionCode = $g[0].ExceptionCode
            offset        = $g[0].Offset
            count         = $g.Count
            firstSeen     = $g[0].Time.ToString('yyyy-MM-dd HH:mm')
            lastSeen      = $g[-1].Time.ToString('yyyy-MM-dd HH:mm')
            appVersions   = @($g | ForEach-Object AppVersion | Sort-Object -Unique)
        }
    } | Sort-Object { $_.lastSeen } -Descending)

$hangs = @($events | Where-Object Id -eq 1002 | ForEach-Object { [ordered]@{ time = $_.TimeCreated.ToString('yyyy-MM-dd HH:mm'); text = (($_.Message -split "`n")[0]).Trim() } })
$managed = @($events | Where-Object Id -eq 1026 | ForEach-Object {
        $lines = $_.Message -split "`n"
        [ordered]@{ time = $_.TimeCreated.ToString('yyyy-MM-dd HH:mm'); head = ($lines | Select-Object -First 4) -join ' | ' }
    })

$result.crashEvents = $crashes.Count
$result.hangEvents = $hangs.Count
$result.managedUnhandledEvents = $managed.Count
$result.crashBuckets = $buckets
$result.hangs = $hangs
$result.managedUnhandled = $managed

# ---- 2. Dumps -----------------------------------------------------------------------------------
$dumps = @()
if (Test-Path -LiteralPath $DumpDir) {
    $dumps = @(Get-ChildItem -LiteralPath $DumpDir -Filter "$ProcessName.exe.*.dmp" -File | Sort-Object LastWriteTime -Descending)
}
$result.dumpCount = $dumps.Count
$result.dumps = @($dumps | ForEach-Object { [ordered]@{ name = $_.Name; mb = [math]::Round($_.Length / 1MB, 1); time = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm') } })

# Identifier shapes only - see PRIVACY above. Anything that does not look like code is dropped.
$identifier = '(?:' +
    '(?:' + [regex]::Escape($ProcessName) + ')\.[A-Za-z_][\w]*(?:\.[A-Za-z_<][\w<>`+]*)+(?:\(\))?' +      # SwiftX.Services.Foo.Bar()
    '|InterfaceMarshaler(?:\.\w+)*' +
    '|WinRT\.[A-Za-z_]\w*(?:\.\w+)*' +
    '|Microsoft\.UI\.Xaml\.[A-Za-z_]\w*(?:\.\w+)*' +
    '|Windows\.(?:Services|ApplicationModel|Foundation|Storage|System|UI)\.[A-Za-z_]\w*(?:\.\w+)*' +
    '|System\.[A-Za-z]+Exception' +
    '|[A-Za-z]+\.dll' +
    ')'

$scanned = @()
foreach ($d in ($dumps | Select-Object -First $DumpsToScan)) {
    $counts = [DumpIdentifierScan]::Scan($d.FullName, $identifier, 8)
    $top = @($counts.GetEnumerator() | Where-Object { $_.Key -match [regex]::Escape($ProcessName) + '\.|Exception|InterfaceMarshaler|Store|Launcher|StoreContext' } |
            Sort-Object Value -Descending | Select-Object -First 40 | ForEach-Object { [ordered]@{ identifier = $_.Key; hits = $_.Value } })
    $scanned += [ordered]@{ dump = $d.Name; time = $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm'); topIdentifiers = $top }
}
$result.dumpScan = $scanned

# ---- 3. The app's own log -----------------------------------------------------------------------
$logInfo = [ordered]@{ path = $LogPath; exists = (Test-Path -LiteralPath $LogPath) }
if ($logInfo.exists) {
    $item = Get-Item -LiteralPath $LogPath
    $logInfo.bytes = $item.Length
    $logInfo.lastWrite = $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
    $hits = @(Select-String -LiteralPath $LogPath -Pattern 'FATAL|UNHANDLED|UNOBSERVED|failed|Exception' -ErrorAction SilentlyContinue)
    $logInfo.faultLines = $hits.Count
    $logInfo.lastFaults = @($hits | Select-Object -Last 10 | ForEach-Object { $_.Line.Substring(0, [math]::Min(220, $_.Line.Length)) })
    $logInfo.note = 'The log is shared by every SwiftX process and every test run, and is truncated at each process start: a fresh log is NOT evidence of a clean past.'
}
$result.appLog = $logInfo

Write-Json -Object $result -Path (Join-Path $OutDir 'crash-forensics.json') -Depth 8

# ---- Markdown -----------------------------------------------------------------------------------
$md = [Text.StringBuilder]::new()
[void]$md.AppendLine("# Crash forensics ($ProcessName, last $Days days)")
[void]$md.AppendLine()
[void]$md.AppendLine("Crash events: **$($crashes.Count)** in **$($buckets.Count)** distinct bucket(s); hang events: $($hangs.Count); .NET unhandled (1026): $($managed.Count); WER dumps on disk: $($dumps.Count).")
[void]$md.AppendLine()
if ($buckets.Count) {
    [void]$md.AppendLine('| Faulting module | Code | Offset | Count | First seen | Last seen | App versions |')
    [void]$md.AppendLine('|---|---|---|---:|---|---|---|')
    foreach ($b in $buckets) {
        [void]$md.AppendLine("| $($b.module) | $($b.exceptionCode) | $($b.offset) | $($b.count) | $($b.firstSeen) | $($b.lastSeen) | $($b.appVersions -join ', ') |")
    }
}
foreach ($s in $scanned) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("## Dump $($s.dump) ($($s.time)) - code identifiers recovered")
    foreach ($t in ($s.topIdentifiers | Select-Object -First 25)) { [void]$md.AppendLine("- ``$($t.identifier)`` x$($t.hits)") }
}
[void]$md.AppendLine()
[void]$md.AppendLine("## App log")
[void]$md.AppendLine("``$LogPath``: exists = $($logInfo.exists)" + $(if ($logInfo.exists) { ", $($logInfo.bytes) bytes, last write $($logInfo.lastWrite), $($logInfo.faultLines) fault-like lines" } else { '' }))
$md.ToString() | Set-Content -LiteralPath (Join-Path $OutDir 'crash-forensics.md') -Encoding utf8

Write-Host ("crash events {0} in {1} bucket(s); hangs {2}; .NET unhandled {3}; dumps {4}" -f $crashes.Count, $buckets.Count, $hangs.Count, $managed.Count, $dumps.Count)
foreach ($b in $buckets) {
    Write-Host ("  {0}  {1}  {2} @ {3}  x{4}  ({5} .. {6})  v{7}" -f $b.module, $b.exceptionCode, '', $b.offset, $b.count, $b.firstSeen, $b.lastSeen, ($b.appVersions -join '/'))
}
foreach ($s in $scanned) {
    Write-Host ("dump {0}: {1}" -f $s.dump, (($s.topIdentifiers | Select-Object -First 8 | ForEach-Object { $_.identifier }) -join '; '))
}
Write-Host "wrote $OutDir\crash-forensics.json / .md"
