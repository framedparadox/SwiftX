#Requires -Version 7.0
<#
.SYNOPSIS
  Copies measurement files into the report's data folder with the machine's identity scrubbed.

.DESCRIPTION
  Raw run output carries the user's profile path (scratch folders live under it), the user name and
  the computer name. A report is committed and may be public, so nothing identifying goes in. This
  rewrites, in text files only: C:\Users\<anything> -> %USERPROFILE%, the user name -> <user>, the
  computer name -> <machine>. It never edits the source files.

.EXAMPLE
  publish-data.ps1 -Source <run>\summary.json,<run>\summary.md -Destination docs\performance\2026-10-01-review\data\before
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Source,
    [Parameter(Mandatory)][string]$Destination,
    [long]$MaxBytes = 5MB
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
# [IO.File] resolves a relative path against the process's directory, which is not PowerShell's
# current location; resolve once so a relative -Destination lands where the caller meant.
$Destination = (Resolve-Path -LiteralPath $Destination).ProviderPath

$user = [regex]::Escape($env:USERNAME)
$machine = [regex]::Escape($env:COMPUTERNAME)

foreach ($path in $Source) {
    foreach ($item in (Get-Item -Path $path)) {
        if ($item.Length -gt $MaxBytes) { Write-Warning "skipped $($item.Name): $([math]::Round($item.Length / 1MB, 1)) MB is over the $([math]::Round($MaxBytes / 1MB)) MB limit"; continue }

        $text = Get-Content -LiteralPath $item.FullName -Raw
        # JSON escapes backslashes, so accept one or two; accept forward slashes too.
        $text = [regex]::Replace($text, '[A-Za-z]:(\\\\|\\|/)Users(\\\\|\\|/)[^\\"\s/<>|]+', '%USERPROFILE%')
        $text = [regex]::Replace($text, "(?i)\b$user\b", '<user>')
        $text = [regex]::Replace($text, "(?i)\b$machine\b", '<machine>')
        $target = Join-Path $Destination $item.Name
        [IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
        Write-Host "published $($item.Name) -> $target"
    }
}
