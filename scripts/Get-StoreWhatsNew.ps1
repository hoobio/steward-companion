function Get-StoreWhatsNew {
    param(
        [Parameter(Mandatory)][string]$ChangelogPath,
        [Parameter(Mandatory)][string]$Version,
        [int]$Limit = 1500
    )

    $section = $null
    $inVersion = $false
    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content -Encoding utf8 $ChangelogPath) {
        if ($line -match '^## \[?(\d+\.\d+\.\d+)') {
            if ($inVersion) { break }
            $inVersion = $Matches[1] -eq $Version
            continue
        }
        if (-not $inVersion) { continue }
        if ($line -match '^### (.+)') { $section = $Matches[1].Trim(); continue }
        if ($section -notin @('Features', 'Bug Fixes')) { continue }
        if ($line -notmatch '^\* ') { continue }

        $text = $line.Substring(2) -replace '^\*\*[^*]+\*\*:\s*', ''
        $text = $text -replace '^[^\x00-\x7F]+\s*', ''
        $text = ($text -replace '\s*\(\[.*$', '').Trim()
        if ($text) { $items.Add($text) }
    }

    if ($items.Count -eq 0) { return $null }

    $kept = [System.Collections.Generic.List[string]]::new()
    $len = 0
    foreach ($item in $items) {
        $bulleted = "- $item"
        $add = $bulleted.Length + $(if ($kept.Count -eq 0) { 0 } else { 1 })
        if ($len + $add -gt $Limit) { break }
        $kept.Add($bulleted)
        $len += $add
    }
    return ($kept -join "`n")
}

if ($MyInvocation.InvocationName -ne '.') {
    $result = Get-StoreWhatsNew -ChangelogPath (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Version '0.10.0'
    if (-not $result) { throw 'self-check failed: expected notes for 0.10.0' }
    Write-Host $result
    Write-Host "Length: $($result.Length)"
}
