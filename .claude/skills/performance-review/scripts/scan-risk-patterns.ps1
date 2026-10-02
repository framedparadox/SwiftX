#Requires -Version 7.0
<#
.SYNOPSIS
  Static scan of the C# sources for the patterns that turn into crashes, hangs, leaks and
  slowdowns in a long-running WinUI 3 desktop app. Read-only.

.DESCRIPTION
  This is a *triage* tool, not a verdict. Each rule is a regular expression plus, where one is
  cheap to state, a "guard" that recognises the mitigated form so it is reported as guarded
  instead of hiding it (a guarded hit is still evidence of where the risk lives). Every
  unguarded hit must be read in context and classed as confirmed, false positive, or accepted.

  Comments are ignored: the codebase documents the very hazards it avoids, and a rule that
  fires on its own warnings is useless.

  Outputs <OutDir>\risk-scan.json and risk-scan.md; prints a one-screen summary.

.EXAMPLE
  scan-risk-patterns.ps1 -OutDir docs\performance\2026-10-01\data
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$SourceDir = 'src',
    [string]$OutDir
)

. "$PSScriptRoot\_common.ps1"
$repo = Resolve-RepoRoot -Hint $RepoRoot
$srcRoot = Join-Path $repo $SourceDir
if (-not $OutDir) { $OutDir = Join-Path (Get-DefaultWorkRoot) 'scan' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---- Rules --------------------------------------------------------------------------------------
# Guard receives ($lines, $i, $fileText) and returns $true when the hit is the mitigated form.
function Next-Lines($lines, $i, $n) { $end = [math]::Min($lines.Count - 1, $i + $n); ($lines[$i..$end] -join "`n") }

$rules = @(
    @{ Id = 'CR01'; Sev = 'Medium'; Cat = 'crash'; Title = 'async void'
        Pattern = '\basync\s+void\b'
        Why = 'An exception escaping an async void method cannot be caught by the caller; it is raised on the synchronization context and takes the process down (or lands in UnhandledException).'
        Guard = { param($l, $i, $t) (Next-Lines $l $i 30) -match '\btry\b' }
        GuardNote = 'a try block follows within 30 lines' }

    @{ Id = 'CR02'; Sev = 'High'; Cat = 'hang'; Title = 'Blocking on a Task'
        Pattern = '\.Result\b(?!\s*\()(?!\s+[A-Za-z_])|\.GetAwaiter\(\)\s*\.GetResult\(\)|\.Wait\(\s*\)|\.WaitAll\('
        Why = 'Blocking a UI thread on a task that needs that thread to finish is a deadlock; blocking it for I/O is a freeze.'
        Guard = $null }

    @{ Id = 'CR03'; Sev = 'Medium'; Cat = 'crash'; Title = 'Fire-and-forget Task'
        Pattern = '^\s*(_\s*=\s*)?Task\.(Run|Factory\.StartNew)\('
        Why = 'A task nobody awaits swallows its exception until the finalizer raises UnobservedTaskException; work may be silently lost.'
        Guard = { param($l, $i, $t) $t -match 'UnobservedTaskException' }
        GuardNote = 'process installs an UnobservedTaskException logger (visibility, not a fix)' }

    @{ Id = 'CR04'; Sev = 'Medium'; Cat = 'hang'; Title = 'Thread without IsBackground'
        Pattern = '\bnew\s+Thread\('
        Why = 'A foreground thread keeps the process alive after the last window closes - the app appears to quit but stays in Task Manager.'
        Guard = { param($l, $i, $t) (Next-Lines $l $i 8) -match 'IsBackground\s*=\s*true' }
        GuardNote = 'IsBackground = true set nearby' }

    @{ Id = 'CR05'; Sev = 'High'; Cat = 'crash'; Title = 'Managed COM interop / raw interface pointers'
        Pattern = '\[ComImport\]|Marshal\.GetObjectForIUnknown|Marshal\.GetTypedObjectForIUnknown|Marshal\.GetComInterfaceForObject'
        Why = 'Managed [ComImport] interfaces marshalled through WinRT.Runtime have produced 0xc000027b stowed-exception fail-fasts here before (IImageList). Fail-fasts skip every managed handler.'
        Guard = $null }

    @{ Id = 'CR06'; Sev = 'Medium'; Cat = 'crash'; Title = 'Explicit process termination'
        Pattern = 'Environment\.(Exit|FailFast)\(|Process\.GetCurrentProcess\(\)\.Kill\('
        Why = 'Skips finalizers and unsaved-state handling; FailFast cannot be caught at all.'
        Guard = $null }

    @{ Id = 'CR07'; Sev = 'High'; Cat = 'crash'; Title = 'P/Invoke handle or pointer declared as 32-bit'
        Pattern = 'extern\b[^;]*\(\s*[^)]*\b(int|uint|long|ulong)\s+(h[A-Z]\w*|hwnd|hWnd|handle|lParam|wParam|lpParam|ptr|pointer)\b'
        Why = 'On x64 a handle or LPARAM/WPARAM is 64 bits; declaring it int/uint truncates it and corrupts or crashes the call.'
        Guard = $null }

    @{ Id = 'CR08'; Sev = 'Medium'; Cat = 'crash'; Title = 'Unbounded whole-file read'
        Pattern = 'File\.(ReadAllText|ReadAllBytes|ReadAllLines)(Async)?\('
        Why = 'A mis-picked multi-GB file becomes an OutOfMemoryException from a UI command.'
        Guard = { param($l, $i, $t) $t -match 'InputLimits|MaxBytes|\.Length\s*[<>]' }
        GuardNote = 'file consults InputLimits / a size check' }

    @{ Id = 'CR09'; Sev = 'Medium'; Cat = 'crash'; Title = 'XML parsing without a DTD policy'
        Pattern = 'XDocument\.(Parse|Load)\(|new\s+XmlDocument\b|XmlReader\.Create\(|new\s+XmlSerializer\('
        Why = 'Entity expansion (billion laughs) and external entities turn a small input into gigabytes or a network fetch.'
        Guard = { param($l, $i, $t) $t -match 'DtdProcessing' }
        GuardNote = 'file sets DtdProcessing' }

    # The publisher list is the project's long-lived singletons and static event owners. It is the one
    # part of this script that is project knowledge: when a new app-lifetime object grows an event,
    # add its field/type name here. A subscriber that is a window and a publisher that is not a
    # window means the subscriber can be closed while the publisher keeps the delegate - and with
    # it the whole window - alive.
    @{ Id = 'CR10'; Sev = 'High'; Cat = 'leak'; Title = 'Subscription to a long-lived event with no matching unsubscribe'
        Pattern = '\b(_manager|manager|Manager|App\.Manager|OrbitMetrics|OrbitStore|Loc|ThemeHelper|FrostedGlassBackdrop|HoverMotion|SystemEvents|NetworkChange|Application\.Current|_toolWindows|ToolWindows)\.([A-Z][A-Za-z]+)\s*\+='
        Why = 'The publisher outlives the subscriber (app-lifetime manager or a static event), so every closed window stays rooted - its whole visual tree - and its handler keeps running against dead UI. Lambdas cannot be unsubscribed at all.'
        Special = 'pairing' }

    @{ Id = 'PF01'; Sev = 'Medium'; Cat = 'hang'; Title = 'Thread.Sleep'
        Pattern = '\bThread\.Sleep\('
        Why = 'On the UI thread this is a freeze of exactly that length.'
        Guard = $null }

    @{ Id = 'PF02'; Sev = 'High'; Cat = 'leak'; Title = 'HttpClient created per use'
        Pattern = '\bnew\s+HttpClient\b'
        Why = 'A client per call exhausts sockets and defeats connection reuse; share one.'
        Guard = { param($l, $i, $t) $l[$i] -match '\bstatic\b' -or ($i -gt 0 -and $l[$i - 1] -match '\bstatic\b') -or (Next-Lines $l ([math]::Max(0, $i - 3)) 3) -match 'static\s+(readonly\s+)?HttpClient' }
        GuardNote = 'shared static client' }

    @{ Id = 'PF03'; Sev = 'Medium'; Cat = 'perf'; Title = 'Timer firing faster than 100 ms'
        Pattern = 'FromMilliseconds\(\s*(\d{1,2})\s*\)|Interval\s*=\s*TimeSpan\.FromMilliseconds\(\s*\d{1,2}\s*\)|SetTimer\([^)]*,\s*(\d{1,2})u?\s*,'
        Why = 'A tray app is idle 99% of its life; a sub-100 ms timer keeps the CPU out of deep idle states and costs battery all day.'
        Guard = $null }

    @{ Id = 'PF04'; Sev = 'Low'; Cat = 'perf'; Title = 'BitmapImage without a decode size'
        Pattern = '\bnew\s+BitmapImage\b'
        Why = 'Without DecodePixelWidth/Height the full image is decoded at native size however small it is drawn.'
        Guard = { param($l, $i, $t) (Next-Lines $l $i 10) -match 'DecodePixel' }
        GuardNote = 'DecodePixel* set nearby' }

    @{ Id = 'PF05'; Sev = 'Low'; Cat = 'perf'; Title = 'Explicit GC.Collect'
        Pattern = '\bGC\.Collect\('
        Why = 'Forcing collections hides leaks and stalls the app; legitimate only in tests and diagnostics.'
        Guard = $null }

    @{ Id = 'PF06'; Sev = 'Medium'; Cat = 'hang'; Title = 'Recursive directory enumeration'
        Pattern = 'SearchOption\.AllDirectories'
        Why = 'On a user-chosen root this walks the whole disk (or a network share) on whichever thread calls it.'
        Guard = $null }

    @{ Id = 'PF07'; Sev = 'Medium'; Cat = 'hang'; Title = 'Synchronous existence check that can touch the network'
        Pattern = '\b(File|Directory)\.Exists\('
        Why = 'On a UNC path or a disconnected mapped drive this blocks for the SMB timeout (seconds to tens of seconds). Called on the UI thread it freezes the app.'
        Guard = { param($l, $i, $t) (Next-Lines $l ([math]::Max(0, $i - 6)) 8) -match 'Task\.Run|ThreadPool|await\s+Task' }
        GuardNote = 'inside / next to a Task.Run' }

    @{ Id = 'PF08'; Sev = 'Low'; Cat = 'perf'; Title = 'Synchronous file I/O in a UI-layer file'
        Pattern = '\bFile\.(WriteAllText|WriteAllBytes|AppendAllText|Copy|Delete|Move)\('
        Why = 'Disk and antivirus latency lands on the UI thread when this runs from an event handler.'
        Guard = $null
        OnlyUnder = 'Window\.xaml\.cs$|ToolPages|Controls' }
)

# ---- Scan ---------------------------------------------------------------------------------------
$files = Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$findings = [System.Collections.Generic.List[object]]::new()

function Strip-Comment([string]$line) {
    $t = $line.TrimStart()
    if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { return '' }
    # Cut a trailing // comment, but not the // inside a string such as "https://...".
    $quote = $false
    for ($k = 0; $k -lt $line.Length - 1; $k++) {
        if ($line[$k] -eq '"' -and ($k -eq 0 -or $line[$k - 1] -ne '\')) { $quote = -not $quote }
        elseif (-not $quote -and $line[$k] -eq '/' -and $line[$k + 1] -eq '/') { return $line.Substring(0, $k) }
    }
    return $line
}

foreach ($file in $files) {
    $rel = $file.FullName.Substring($repo.Length).TrimStart('\', '/')
    $raw = Get-Content -LiteralPath $file.FullName
    $text = $raw -join "`n"
    $code = $raw | ForEach-Object { Strip-Comment $_ }

    foreach ($rule in $rules) {
        if ($rule.ContainsKey('OnlyUnder') -and $rel -notmatch $rule.OnlyUnder) { continue }
        for ($i = 0; $i -lt $code.Count; $i++) {
            $m = [regex]::Match($code[$i], $rule.Pattern)
            if (-not $m.Success) { continue }

            $guarded = $false
            $note = $null

            if ($rule.ContainsKey('Special') -and $rule.Special -eq 'pairing') {
                # Subscriptions to something other than this/a local: require a matching -= for the
                # same event name somewhere in the same file.
                $publisher = $m.Groups[1].Value
                $event = $m.Groups[2].Value
                if ($text -match [regex]::Escape($publisher) + '\.' + [regex]::Escape($event) + '\s*-=') {
                    $guarded = $true; $note = 'matching -= found in the same file'
                }
                elseif ($code[$i] -match '\+=\s*(\(|async\s*\(|delegate\b|[A-Za-z_]+\s*=>)') {
                    $note = 'lambda / anonymous handler: it cannot be unsubscribed, so the subscriber is pinned for the publisher''s lifetime'
                }
            }
            elseif ($rule.Guard) {
                if (& $rule.Guard $code $i $text) { $guarded = $true; $note = $rule.GuardNote }
            }

            $findings.Add([pscustomobject]@{
                    rule     = $rule.Id
                    severity = $rule.Sev
                    category = $rule.Cat
                    title    = $rule.Title
                    file     = ($rel -replace '\\', '/')
                    line     = $i + 1
                    code     = $code[$i].Trim()
                    guarded  = $guarded
                    note     = $note
                })
        }
    }
}

# ---- Report -------------------------------------------------------------------------------------
$sevOrder = @{ High = 0; Medium = 1; Low = 2 }
$byRule = $findings | Group-Object rule | Sort-Object { $sevOrder[($rules | Where-Object Id -eq $_.Name).Sev] }, Name

Write-Json -Object @{
    generated = (Get-Date).ToString('o')
    filesScanned = $files.Count
    totalHits = $findings.Count
    unguarded = @($findings | Where-Object { -not $_.guarded }).Count
    findings = $findings
} -Path (Join-Path $OutDir 'risk-scan.json') -Depth 5

$md = [Text.StringBuilder]::new()
[void]$md.AppendLine('# Static risk scan')
[void]$md.AppendLine()
[void]$md.AppendLine("Scanned $($files.Count) files under ``$SourceDir``: **$($findings.Count)** hits, **$(@($findings | Where-Object { -not $_.guarded }).Count)** unguarded. Triage tool, not a verdict - read each unguarded hit in context.")
[void]$md.AppendLine()
[void]$md.AppendLine('| Rule | Severity | Title | Hits | Unguarded |')
[void]$md.AppendLine('|---|---|---|---:|---:|')
foreach ($g in $byRule) {
    $r = $rules | Where-Object Id -eq $g.Name
    $ug = @($g.Group | Where-Object { -not $_.guarded }).Count
    [void]$md.AppendLine("| $($r.Id) | $($r.Sev) | $($r.Title) | $($g.Count) | $ug |")
}
foreach ($g in $byRule) {
    $r = $rules | Where-Object Id -eq $g.Name
    $ug = @($g.Group | Where-Object { -not $_.guarded })
    if (-not $ug.Count) { continue }
    [void]$md.AppendLine()
    [void]$md.AppendLine("## $($r.Id) $($r.Title) ($($r.Sev))")
    [void]$md.AppendLine()
    [void]$md.AppendLine("_$($r.Why)_")
    [void]$md.AppendLine()
    foreach ($f in ($ug | Select-Object -First 25)) { [void]$md.AppendLine("- ``$($f.file):$($f.line)`` ``$($f.code)``") }
    if ($ug.Count -gt 25) { [void]$md.AppendLine("- ... and $($ug.Count - 25) more (see risk-scan.json)") }
}
$md.ToString() | Set-Content -LiteralPath (Join-Path $OutDir 'risk-scan.md') -Encoding utf8

Write-Host ("scanned {0} files: {1} hits, {2} unguarded" -f $files.Count, $findings.Count, @($findings | Where-Object { -not $_.guarded }).Count)
foreach ($g in $byRule) {
    $r = $rules | Where-Object Id -eq $g.Name
    Write-Host ("  {0} {1,-6} {2,-58} hits {3,3}  unguarded {4,3}" -f $r.Id, $r.Sev, $r.Title, $g.Count, @($g.Group | Where-Object { -not $_.guarded }).Count)
}
Write-Host "wrote $OutDir\risk-scan.json / .md"
