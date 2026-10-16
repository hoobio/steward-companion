#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Tag,
    [string] $VirusScanSummary,
    [string] $CertificationResult,
    [string] $CertificationDetail
)

$ErrorActionPreference = 'Stop'

$repo = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY"
$run = "$repo/actions/runs/$env:GITHUB_RUN_ID"
$commit = "$repo/commit/$env:GITHUB_SHA"
$short = $env:GITHUB_SHA.Substring(0, 7)
$assets = "$repo/releases/download/$Tag"

function Badge([string] $Label, [string] $Message, [string] $Colour, [string] $Link) {
    $part = { param($text) [Uri]::EscapeDataString($text.Replace('-', '--').Replace('_', '__')) }
    "[![$Label`: $Message](https://img.shields.io/badge/$(& $part $Label)-$(& $part $Message)-$Colour)]($Link)"
}

$scanBadge = if ($VirusScanSummary) {
    Badge -Label 'ClamAV' -Message 'no threats found' -Colour 'brightgreen' -Link "$assets/virus-scan-report.html"
}
else { Badge -Label 'ClamAV' -Message 'not run' -Colour 'lightgrey' -Link $run }
$certResult = if ($CertificationResult) { $CertificationResult } else { 'Not run' }
$certColour = switch ($certResult) { 'Passed' { 'brightgreen' } 'Passed with warnings' { 'yellow' } 'Failed' { 'red' } default { 'lightgrey' } }
$certLink = if ($CertificationResult) { "$assets/certification-report.html" } else { $run }

$badges = @(
    "[![Get it from Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9PBKMZFKZHKX)"
    "[![CodeQL]($repo/actions/workflows/codeql.yaml/badge.svg)]($repo/actions/workflows/codeql.yaml)"
    $scanBadge
    Badge -Label 'Windows App Certification Kit' -Message $certResult -Colour $certColour -Link $certLink
    Badge -Label 'commit' -Message $short -Colour 'blue' -Link $commit
    Badge -Label 'build' -Message "run $env:GITHUB_RUN_NUMBER" -Colour 'blue' -Link $run
    Badge -Label 'privacy' -Message 'no telemetry' -Colour 'blue' -Link "$repo/blob/main/PRIVACY.md"
) -join ' '

$certLine = "- **Windows App Certification Kit:** [$certResult]($certLink)"
if ($certResult -eq 'Not run' -and $CertificationDetail) { $certLine += " ($CertificationDetail)" }

$lines = @(
    $badges
    ''
    '- **Microsoft Store:** Microsoft scans, certifies and signs the Store version before it reaches you.'
    "- **Open source:** built from commit [``$short``]($commit) by [this public workflow run]($run). The files attached here come from that run."
    "- **Code analysis:** [CodeQL]($repo/actions/workflows/codeql.yaml) checks the source for security issues on every change."
)
$lines += if ($VirusScanSummary) { "- **Virus scan:** $VirusScanSummary The [scan report]($assets/virus-scan-report.html) lists every file with its SHA-256." } else { "- **Virus scan:** not run." }
$lines += @(
    $certLine
    "- **Privacy:** no telemetry or analytics. The [privacy policy]($repo/blob/main/PRIVACY.md) lists what Steward reads, stores and sends."
    ''
)
$lines -join "`n"
