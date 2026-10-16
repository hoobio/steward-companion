#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MsixPath,
    [Parameter(Mandatory)] [string] $ReportPath,
    [Parameter(Mandatory)] [string] $XmlPath,
    [Parameter(Mandatory)] [string] $RunUrl,
    [int] $TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$XmlPath = [IO.Path]::GetFullPath($XmlPath)
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$MsixPath = [IO.Path]::GetFullPath($MsixPath)

function Get-Value([System.Xml.XmlNode] $Node, [string[]] $Names) {
    foreach ($name in $Names) {
        $attribute = $Node.Attributes | Where-Object { $_.Name -ieq $name } | Select-Object -First 1
        if ($attribute -and $attribute.Value) { return $attribute.Value.Trim() }
        $child = $Node.ChildNodes | Where-Object { $_.LocalName -ieq $name } | Select-Object -First 1
        if ($child -and $child.InnerText.Trim()) { return $child.InnerText.Trim() }
    }
}

function ConvertTo-Outcome([string] $Value) {
    switch -Regex ($Value) {
        '^\s*$' { 'Unknown'; break }
        'warn' { 'Warning'; break }
        'fail' { 'Failed'; break }
        'pass' { 'Passed'; break }
        'skip|not\s*run|n/?a' { 'Skipped'; break }
        default { $Value }
    }
}

$result = 'Not run'
$detail = ''
$tests = @()
$rawXml = $null
$kitVersion = $null
$certificate = $null
$started = Get-Date

try {
    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
    if (-not (Test-Path -LiteralPath $appcert)) {
        $appcert = Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits') -Recurse -Filter appcert.exe -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $appcert) { throw 'appcert.exe is not installed on this runner' }
    $kitVersion = (Get-Item -LiteralPath $appcert).VersionInfo.ProductVersion
    Write-Host "Using $appcert ($kitVersion)"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
        $publisher = ([xml]$reader.ReadToEnd()).Package.Identity.Publisher
        $reader.Dispose()
    }
    finally { $zip.Dispose() }

    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'Steward certification kit run' `
        -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $cer = Join-Path $env:RUNNER_TEMP 'appcert.cer'
    Export-Certificate -Cert $certificate -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    $signed = Join-Path $env:RUNNER_TEMP ('appcert-' + (Split-Path $MsixPath -Leaf))
    Copy-Item -LiteralPath $MsixPath -Destination $signed -Force
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object { [version]($_.FullName -replace '.*\\(\d+\.\d+\.\d+\.\d+)\\.*', '$1') } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw 'signtool.exe is not installed on this runner, so the package could not be signed for the kit to install' }
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signed | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE), so the package could not be signed for the kit to install" }

    & $appcert reset | Write-Host
    Remove-Item -LiteralPath $XmlPath -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $appcert -ArgumentList @('test', '-appxpackagepath', "`"$signed`"", '-reportoutputpath', "`"$XmlPath`"") -NoNewWindow -PassThru
    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $process.Kill($true)
        throw "appcert.exe did not finish within $TimeoutMinutes minutes"
    }
    Write-Host "appcert.exe test exited with code $($process.ExitCode) after $([int]((Get-Date) - $started).TotalMinutes) minutes"
    if (-not (Test-Path -LiteralPath $XmlPath)) { throw "appcert.exe exited with code $($process.ExitCode) and wrote no report" }

    $rawXml = Get-Content -LiteralPath $XmlPath -Raw
    $report = [xml]$rawXml
    $overall = Get-Value $report.DocumentElement @('OVERALL_RESULT', 'OverallResult', 'RESULT')
    foreach ($test in $report.SelectNodes('//*[translate(local-name(), "test", "TEST") = "TEST"]')) {
        $requirement = if ($test.ParentNode -and $test.ParentNode.LocalName -ieq 'REQUIREMENT') { Get-Value $test.ParentNode @('TITLE', 'NAME') }
        $messages = @($test.SelectNodes('.//*[translate(local-name(), "message", "MESSAGE") = "MESSAGE"]') | ForEach-Object {
                $text = Get-Value $_ @('TEXT', 'Text')
                if (-not $text) { $text = $_.InnerText.Trim() }
                $text
            } | Where-Object { $_ })
        $tests += [pscustomobject]@{
            Requirement = $requirement
            Name        = Get-Value $test @('NAME', 'Name', 'DESCRIPTION', 'ID')
            Description = Get-Value $test @('DESCRIPTION', 'Description')
            Outcome     = ConvertTo-Outcome (Get-Value $test @('RESULT', 'Result'))
            Optional    = (Get-Value $test @('OPTIONAL')) -eq 'TRUE'
            Messages    = $messages
        }
    }

    $overallOutcome = ConvertTo-Outcome $overall
    $result = if ($overallOutcome -in 'Passed', 'Failed', 'Warning') {
        if ($overallOutcome -eq 'Warning') { 'Passed with warnings' } else { $overallOutcome }
    }
    elseif ($tests | Where-Object Outcome -eq 'Failed') { 'Failed' }
    elseif ($tests | Where-Object Outcome -eq 'Warning') { 'Passed with warnings' }
    elseif ($tests -and -not ($tests | Where-Object Outcome -notin 'Passed', 'Skipped')) { 'Passed' }
    else { 'Not run' }
    $detail = if ($result -eq 'Not run') { "the report holds no overall result ('$overall')" } else { "$(@($tests).Count) tests" }
}
catch {
    $result = 'Not run'
    $detail = $_.Exception.Message
    Write-Host "::warning::Windows App Certification Kit not run: $detail"
}
finally {
    Get-AppxPackage -Name Hoobi.Steward* -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    if ($certificate) {
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)", "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
    }
}

$e = { param($text) [Net.WebUtility]::HtmlEncode("$text") }
$class = switch ($result) { 'Passed' { 'ok' } 'Passed with warnings' { 'warn' } 'Failed' { 'bad' } default { 'none' } }
$rows = foreach ($t in $tests) {
    $c = switch ($t.Outcome) { 'Passed' { 'ok' } 'Warning' { 'warn' } 'Failed' { 'bad' } default { 'none' } }
    $details = @()
    if ($t.Description -and $t.Description -ne $t.Name) { $details += "<p>$(& $e $t.Description)</p>" }
    if ($t.Messages) { $details += '<ul>' + (($t.Messages | ForEach-Object { "<li>$(& $e $_)</li>" }) -join '') + '</ul>' }
    $outcome = if ($t.Optional -and $t.Outcome -ne 'Passed') { "$($t.Outcome) (optional test)" } else { $t.Outcome }
    "<tr><td>$(& $e $t.Requirement)</td><td>$(& $e $t.Name)</td><td class=`"$c`">$(& $e $outcome)</td><td>$($details -join '')</td></tr>"
}
$body = if ($tests) {
    "<table><tr><th>Requirement</th><th>Test</th><th>Result</th><th>Details</th></tr>`n$($rows -join "`n")`n</table>"
}
elseif ($rawXml) { "<h2>Report</h2><pre>$(& $e $rawXml)</pre>" }
else { '' }
$reason = if ($result -eq 'Not run') { "<p>Reason: $(& $e $detail)</p>" } else { '' }
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Steward certification</title>
<style>
body { font: 14px/1.5 system-ui, sans-serif; margin: 24px; color: #1f2430; background: #fff; }
h1 { font-size: 20px; margin: 0 0 4px; }
table { border-collapse: collapse; margin: 12px 0 20px; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ddd; vertical-align: top; }
td p, td ul { margin: 0 0 4px; }
pre { white-space: pre-wrap; word-break: break-all; font-size: 12px; background: #f6f8fa; padding: 12px; }
.ok { color: #1a7f37; font-weight: 600; }
.warn { color: #9a6700; font-weight: 600; }
.bad { color: #cf222e; font-weight: 600; }
.none { color: #57606a; font-weight: 600; }
</style>
</head>
<body>
<h1>Windows App Certification Kit: <span class="$class">$(& $e $result)</span></h1>
<p>Package <code>$(& $e (Split-Path $MsixPath -Leaf))</code>, kit $(& $e ($kitVersion ?? 'not found')), tested $(Get-Date -AsUTC -Format 'yyyy-MM-dd HH:mm') UTC by <a href="$(& $e $RunUrl)">this workflow run</a>.</p>
$reason
$body
<p>The Microsoft Store runs its own certification after submission; it publishes no report.</p>
</body>
</html>
"@
Set-Content -LiteralPath $ReportPath -Value $html -Encoding utf8NoBOM
Write-Host "Windows App Certification Kit: $result ($detail)"
if ($env:GITHUB_OUTPUT) {
    "result=$result" | Out-File -Append -LiteralPath $env:GITHUB_OUTPUT
    "detail=$($detail -replace '[\r\n]+', ' ')" | Out-File -Append -LiteralPath $env:GITHUB_OUTPUT
}
