function Test-StoreSubmissionBusy {
    param([Parameter(Mandatory)][string]$StatusOutput)

    if ($StatusOutput -notmatch 'Submission Status = (\S+)') { return $false }
    $busyStates = 'CommitStarted', 'PreProcessing', 'PendingPublication', 'Publishing', 'Release'
    return $busyStates -contains $Matches[1]
}

function Get-StoreApiOnlySubmissionId {
    param([Parameter(Mandatory)][string]$Output)

    if ($Output -notmatch 'can only update, delete, and commit submissions that are created through the API') { return $null }
    if ($Output -match "Found (?:Flight )?Submission with Id '([^']+)'") { return $Matches[1] }
    return 'unknown'
}

function Wait-StoreSubmissionClear {
    param(
        [Parameter(Mandatory)][string]$ProductId,
        [string]$FlightId,
        [int]$TimeoutMinutes = 60,
        [int]$PollSeconds = 120
    )

    $statusArgs = if ($FlightId) { @('flights', 'submission', 'status', $ProductId, $FlightId) } else { @('submission', 'status', $ProductId) }
    $deleteArgs = if ($FlightId) { @('flights', 'submission', 'delete', $ProductId, $FlightId, '--no-confirm') } else { @('submission', 'delete', $ProductId, '--no-confirm') }

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($true) {
        $statusOut = & msstore @statusArgs | Out-String
        Write-Host $statusOut

        $apiOnlyId = Get-StoreApiOnlySubmissionId -Output $statusOut
        if ($apiOnlyId) {
            Write-Host "::error::Submission $apiOnlyId was created in Partner Center; delete it by hand there (Steward 9PBKMZFKZHKX) before this job can run again."
            exit 1
        }

        if (Test-StoreSubmissionBusy -StatusOutput $statusOut) {
            if ((Get-Date) -ge $deadline) {
                Write-Host "::error::Submission is still processing after $TimeoutMinutes minutes; giving up."
                exit 1
            }
            Write-Host "Submission still processing; waiting $PollSeconds seconds before checking again."
            Start-Sleep -Seconds $PollSeconds
            continue
        }

        $deleteOut = & msstore @deleteArgs | Out-String
        Write-Host $deleteOut
        $global:LASTEXITCODE = 0

        $apiOnlyId = Get-StoreApiOnlySubmissionId -Output $deleteOut
        if ($apiOnlyId) {
            Write-Host "::error::Submission $apiOnlyId was created in Partner Center; delete it by hand there (Steward 9PBKMZFKZHKX) before this job can run again."
            exit 1
        }

        if ($deleteOut -match 'cannot be deleted') {
            if ((Get-Date) -ge $deadline) {
                Write-Host "::error::Pending submission still refuses deletion after $TimeoutMinutes minutes; giving up."
                exit 1
            }
            Write-Host "Pending submission cannot be deleted yet; waiting $PollSeconds seconds before checking again."
            Start-Sleep -Seconds $PollSeconds
            continue
        }

        return
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (Test-StoreSubmissionBusy -StatusOutput 'Submission Status = Certification') { throw 'self-check failed: Certification should be deletable' }
    if (Test-StoreSubmissionBusy -StatusOutput 'Submission Status = PendingCommit') { throw 'self-check failed: PendingCommit should be deletable' }
    if (-not (Test-StoreSubmissionBusy -StatusOutput 'Submission Status = CommitStarted')) { throw 'self-check failed: CommitStarted should be busy' }
    if (Test-StoreSubmissionBusy -StatusOutput 'Submission Status = Published') { throw 'self-check failed: Published should not be busy' }
    if (Test-StoreSubmissionBusy -StatusOutput 'Submission Status = CommitFailed') { throw 'self-check failed: CommitFailed should not be busy' }
    if (Test-StoreSubmissionBusy -StatusOutput 'no status line here') { throw 'self-check failed: missing status line should not be busy' }

    $flightError = "Found Flight Submission with Id 'abc-123'`nIngestion API can only update, delete, and commit submissions that are created through the API."
    if ((Get-StoreApiOnlySubmissionId -Output $flightError) -ne 'abc-123') { throw 'self-check failed: expected to extract the flight submission id' }
    if (Get-StoreApiOnlySubmissionId -Output 'Existing submission deleted!') { throw 'self-check failed: normal delete output should not match' }

    Write-Host 'Wait-StoreSubmissionClear self-check passed.'
}
