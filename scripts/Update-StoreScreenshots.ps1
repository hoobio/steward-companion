#Requires -Version 7
[CmdletBinding()]
param(
    [string]$Directory = (Join-Path $PSScriptRoot '..\docs\screenshots'),
    [string]$ProductId = '9PBKMZFKZHKX',
    [string]$KeyVault = 'hbicfgkvtaus01'
)
$ErrorActionPreference = 'Stop'
$api = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$ProductId"

function Get-StoreSecret([string]$Vault, [string]$Name) {
    $value = az keyvault secret show --vault-name $Vault --name "msstore-$Name" --query value -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Key Vault secret msstore-$Name could not be read" }
    $value
}

$images = @(Get-ChildItem $Directory -Filter *.png | Sort-Object Name)
if ($images.Count -eq 0 -or $images.Count -gt 10) { throw "Expected 1 to 10 PNGs in $Directory, found $($images.Count)" }

$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$(Get-StoreSecret $KeyVault 'tenant-id')/oauth2/token" -Body @{
    grant_type    = 'client_credentials'
    client_id     = Get-StoreSecret $KeyVault 'client-id'
    client_secret = Get-StoreSecret $KeyVault 'client-secret'
    resource      = 'https://manage.devcenter.microsoft.com'
}
$headers = @{ Authorization = "Bearer $($token.access_token)" }

$app = Invoke-RestMethod -Uri $api -Headers $headers
if ($app.pendingApplicationSubmission) { throw "Submission $($app.pendingApplicationSubmission.id) is already pending; publish or delete it first" }

$submission = Invoke-RestMethod -Method Post -Uri "$api/submissions" -Headers $headers -ContentType 'application/json'
Write-Output "Created submission $($submission.id)"

$entries = foreach ($image in $images) {
    [pscustomobject]@{
        fileName    = "screenshots/$($image.Name)"
        fileStatus  = 'PendingUpload'
        imageType   = 'Screenshot'
        description = (Get-Culture).TextInfo.ToTitleCase(($image.BaseName -replace '^\d+-', '' -replace '-', ' '))
    }
}
foreach ($listing in $submission.listings.PSObject.Properties) {
    $base = $listing.Value.baseListing
    $kept = @($base.images | ForEach-Object {
        if ($_.imageType -eq 'Screenshot') { $_.fileStatus = 'PendingDelete' }
        $_
    })
    $base.images = @($kept + $entries)
}
$null = Invoke-RestMethod -Method Put -Uri "$api/submissions/$($submission.id)" -Headers $headers -ContentType 'application/json' -Body ($submission | ConvertTo-Json -Depth 50)

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "store-screenshots-$($submission.id)"
New-Item -ItemType Directory -Force (Join-Path $staging 'screenshots') | Out-Null
$images | Copy-Item -Destination (Join-Path $staging 'screenshots')
$zip = "$staging.zip"
Compress-Archive -Path (Join-Path $staging 'screenshots') -DestinationPath $zip -Force
$null = Invoke-RestMethod -Method Put -Uri $submission.fileUploadUrl -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } -InFile $zip
Remove-Item -Recurse -Force $staging, $zip

$null = Invoke-RestMethod -Method Post -Uri "$api/submissions/$($submission.id)/commit" -Headers $headers -ContentType 'application/json'
do {
    Start-Sleep 30
    $status = Invoke-RestMethod -Uri "$api/submissions/$($submission.id)/status" -Headers $headers
    Write-Output "Status: $($status.status)"
} while ($status.status -eq 'CommitStarted')
if ($status.status -like '*Failed') { throw "Submission $($submission.id) failed: $($status.statusDetails | ConvertTo-Json -Depth 10 -Compress)" }
Write-Output "Submission $($submission.id) is $($status.status); it publishes once certification passes."
