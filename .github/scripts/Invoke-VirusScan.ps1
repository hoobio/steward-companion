#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ScanDirectory,
    [Parameter(Mandatory)] [string] $ReportPath,
    [Parameter(Mandatory)] [string] $RunUrl
)

$ErrorActionPreference = 'Stop'

$limits = [ordered]@{ '--max-filesize' = '4000M'; '--max-scansize' = '4000M'; '--max-files' = '100000'; '--alert-exceeds-max' = 'yes' }
$files = @(Get-ChildItem -LiteralPath $ScanDirectory -File -Recurse | Sort-Object FullName)
if (-not $files) { throw "No files to scan in $ScanDirectory" }

$version = (& clamscan --version).Trim()
if ($LASTEXITCODE -ne 0) { throw "clamscan --version failed (exit $LASTEXITCODE)" }
$engine, $signatures, $signaturesDate = $version -split '/', 3

# The default 100MB limits skip a large package silently; they are raised here, and an exceeded limit alerts as a detection.
$arguments = @('--no-summary') + @($limits.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) + @($files.FullName)
$output = & clamscan @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | Write-Host
if ($exitCode -gt 1) { throw "clamscan failed (exit $exitCode)" }

$results = foreach ($file in $files) {
    $line = $output | Where-Object { "$_".StartsWith("$($file.FullName): ", [StringComparison]::Ordinal) } | Select-Object -Last 1
    $verdict = if ($line) { "$line".Substring($file.FullName.Length + 2) } else { 'NOT REPORTED' }
    [pscustomobject]@{
        Name   = $file.Name
        Size   = $file.Length
        Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Result = $verdict
        Clean  = $verdict -eq 'OK'
    }
}

$clean = $exitCode -eq 0 -and -not ($results | Where-Object { -not $_.Clean })
$e = { param($text) [Net.WebUtility]::HtmlEncode("$text") }
$rows = foreach ($r in $results) {
    $class = if ($r.Clean) { 'ok' } else { 'bad' }
    "<tr><td>$(& $e $r.Name)</td><td class=`"num`">$('{0:N0}' -f $r.Size)</td><td class=`"mono`">$($r.Sha256)</td><td class=`"$class`">$(& $e $r.Result)</td></tr>"
}
$limitRows = foreach ($l in $limits.GetEnumerator()) { "<tr><td class=`"mono`">$($l.Key)</td><td class=`"mono`">$($l.Value)</td></tr>" }
$overall = if ($clean) { 'No threats found' } else { 'Threats or scan problems found' }
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Steward virus scan</title>
<style>
body { font: 14px/1.5 system-ui, sans-serif; margin: 24px; color: #1f2430; background: #fff; }
h1 { font-size: 20px; margin: 0 0 4px; }
table { border-collapse: collapse; margin: 12px 0 20px; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ddd; vertical-align: top; }
.mono { font-family: ui-monospace, Consolas, monospace; font-size: 12px; word-break: break-all; }
.num { text-align: right; }
.ok { color: #1a7f37; font-weight: 600; }
.bad { color: #cf222e; font-weight: 600; }
</style>
</head>
<body>
<h1>Steward virus scan: <span class="$(if ($clean) { 'ok' } else { 'bad' })">$overall</span></h1>
<p>Scanned $(Get-Date -AsUTC -Format 'yyyy-MM-dd HH:mm') UTC by <a href="$(& $e $RunUrl)">this workflow run</a>.</p>
<table>
<tr><th>Engine</th><td>$(& $e $engine)</td></tr>
<tr><th>Signature database</th><td>version $(& $e $signatures), $(& $e $signaturesDate)</td></tr>
</table>
<h2>Files</h2>
<table>
<tr><th>File</th><th>Size (bytes)</th><th>SHA-256</th><th>Result</th></tr>
$($rows -join "`n")
</table>
<h2>Limits</h2>
<p>An exceeded limit counts as a detection.</p>
<table>
<tr><th>Option</th><th>Value</th></tr>
$($limitRows -join "`n")
</table>
</body>
</html>
"@
Set-Content -LiteralPath $ReportPath -Value $html -Encoding utf8NoBOM

$names = ($results.Name | ForEach-Object { "``$_``" }) -join ' '
$summary = if ($clean) { "no threats found in $names by $engine (signatures $signatures, $signaturesDate)." } else { "threats or scan problems found by $engine." }
if ($env:GITHUB_OUTPUT) {
    "summary=$summary" | Out-File -Append -LiteralPath $env:GITHUB_OUTPUT
    "clean=$($clean.ToString().ToLowerInvariant())" | Out-File -Append -LiteralPath $env:GITHUB_OUTPUT
}
if (-not $clean) { throw "ClamAV reported a detection or a file it did not scan: $(($results | Where-Object { -not $_.Clean }).Name -join ', ')" }
