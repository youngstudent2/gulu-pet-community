#Requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateSet("Install", "Watchdog", "Recover")]
    [string]$Mode = "Install",

    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,

    [Parameter(Mandatory = $true)]
    [string]$StagingDirectory,

    [Parameter(Mandatory = $true)]
    [string]$BackupDirectory,

    [Parameter(Mandatory = $true)]
    [string]$StateDirectory,

    [Parameter(Mandatory = $true)]
    [Guid]$OperationId,

    [ValidateRange(0, 2147483647)]
    [int]$CurrentProcessId = 0,

    [ValidateRange(1, 600)]
    [int]$CurrentProcessExitTimeoutSeconds = 60,

    [ValidateRange(1, 300)]
    [int]$ReadyTimeoutSeconds = 45,

    [ValidateRange(0, 30)]
    [int]$MutexWaitSeconds = 2,

    [ValidateRange(0, 2147483647)]
    [int]$MainInstallerProcessId = 0,

    [long]$MainInstallerStartTimeUtcTicks = 0,

    [string]$RunOnceRegistryPath =
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce",

    [switch]$DisableRunOnce,

    [switch]$DisableWatchdog,

    [switch]$EnableTestFaultAfterOldMove
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:ReadyEventPrefix = "Local\GuluPet.Update.Ready."
$script:ReadyArgumentPrefix = "--post-update-ready-event="
$script:RollbackNoticeArgument = "--post-update-rollback-notice"
$script:ProductionRunOnceRegistryPath =
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"
$script:RunOnceRegistryPath = $RunOnceRegistryPath
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:InvariantCulture = [Globalization.CultureInfo]::InvariantCulture
$script:StartedAtUtc = [DateTimeOffset]::UtcNow
$script:OperationIdText = $OperationId.ToString("N")
$script:RunOnceValueName =
    "!GuluPet.Update.Recovery.$($script:OperationIdText)"
$script:LogPath = $null
$script:JournalPath = $null
$script:ResultPath = $null
$script:ResolvedInstall = $null
$script:ResolvedStaging = $null
$script:ResolvedBackup = $null
$script:ResolvedState = $null
$script:ResolvedScript = $null
$script:Journal = $null

function ConvertTo-GuluUtcText {
    param([Parameter(Mandatory = $true)][DateTimeOffset]$Value)

    return $Value.UtcDateTime.ToString("O", $script:InvariantCulture)
}

function Resolve-GuluFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Update paths cannot be empty."
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($resolved)
    if ([string]::Equals(
            $resolved,
            $root,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Update paths cannot target a volume root."
    }

    return $resolved.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Test-GuluPathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Boundary
    )

    if ([string]::Equals(
            $Candidate,
            $Boundary,
            [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $Candidate.StartsWith(
        $Boundary + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-GuluPlainDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "A required update directory is missing."
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Update directories cannot be reparse points."
    }
}

function Assert-GuluNoReparseAncestors {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = Get-Item -LiteralPath $Path -Force
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Update paths cannot traverse reparse points."
        }
        $current = $current.Parent
    }
}

function Assert-GuluPlainTree {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-GuluPlainDirectory -Path $Path
    foreach ($item in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Update package trees cannot contain reparse points."
        }
    }
}

function Write-GuluCreateOnlyText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value
    )

    $bytes = $script:Utf8NoBom.GetBytes($Value)
    $stream = New-Object IO.FileStream(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        4096,
        [IO.FileOptions]::WriteThrough)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function Set-GuluAtomicText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $parent = Split-Path -Parent $Path
    $temporary = Join-Path (
        $parent
    ) ".atomic-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    $backup = Join-Path (
        $parent
    ) ".atomic-$PID-$([Guid]::NewGuid().ToString('N')).bak"
    try {
        Write-GuluCreateOnlyText -Path $temporary -Value $Value
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $item = Get-Item -LiteralPath $Path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Atomic state targets cannot be reparse points."
            }
            [IO.File]::Replace($temporary, $Path, $backup)
            if (Test-Path -LiteralPath $backup -PathType Leaf) {
                Remove-Item -LiteralPath $backup -Force
            }
        }
        else {
            [IO.File]::Move($temporary, $Path)
        }
    }
    finally {
        foreach ($scratch in @($temporary, $backup)) {
            if (Test-Path -LiteralPath $scratch -PathType Leaf) {
                Remove-Item -LiteralPath $scratch -Force
            }
        }
    }
}

function Read-GuluUtf8Json {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Durable update state cannot be a reparse point."
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
        ConvertFrom-Json
}

function Write-GuluLog {
    param([Parameter(Mandatory = $true)][string]$Message)

    if ($null -eq $script:LogPath) {
        return
    }
    $timestamp = ConvertTo-GuluUtcText -Value ([DateTimeOffset]::UtcNow)
    try {
        Add-Content `
            -LiteralPath $script:LogPath `
            -Value "$timestamp [$Mode] $Message" `
            -Encoding UTF8
    }
    catch {
        # The independent watchdog and main installer may append at the same
        # instant. Diagnostics must never block conservative recovery.
    }
}

function Assert-GuluStateIdentity {
    param([Parameter(Mandatory = $true)][object]$Value)

    if ([int]$Value.schemaVersion -ne 1 -or
        [string]$Value.operationId -cne $script:OperationIdText -or
        -not [string]::Equals(
            [string]$Value.installDirectory,
            $script:ResolvedInstall,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$Value.stagingDirectory,
            $script:ResolvedStaging,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$Value.backupDirectory,
            $script:ResolvedBackup,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Durable update state belongs to a different transaction."
    }
}

function Read-GuluJournal {
    $journal = Read-GuluUtf8Json -Path $script:JournalPath
    if ($null -eq $journal) {
        return $null
    }
    Assert-GuluStateIdentity -Value $journal
    $knownPhases = @(
        "BeforeSwap",
        "OldMoved",
        "NewMoved",
        "NewStarted",
        "Committed",
        "RolledBack")
    if ($knownPhases -cnotcontains [string]$journal.phase) {
        throw "The update journal contains an unknown phase."
    }
    return $journal
}

function Set-GuluJournalPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [AllowNull()][Nullable[int]]$NewProcessId,
        [AllowNull()][Nullable[long]]$NewProcessStartTimeUtcTicks,
        [AllowNull()][string]$ReadyEventName,
        [AllowNull()][Nullable[int]]$RestoredProcessId
    )

    if (@(
            "BeforeSwap",
            "OldMoved",
            "NewMoved",
            "NewStarted",
            "Committed",
            "RolledBack") -cnotcontains $Phase) {
        throw "The requested journal phase is invalid."
    }

    $mainPid = if ($null -ne $script:Journal) {
        [int]$script:Journal.mainInstallerProcessId
    }
    elseif ($Mode -ceq "Install") {
        $PID
    }
    else {
        0
    }
    $mainStartTicks = if ($null -ne $script:Journal) {
        [long]$script:Journal.mainInstallerStartTimeUtcTicks
    }
    elseif ($Mode -ceq "Install") {
        [Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().Ticks
    }
    else {
        0L
    }

    $journal = [pscustomobject][ordered]@{
        schemaVersion = 1
        operationId = $script:OperationIdText
        installDirectory = $script:ResolvedInstall
        stagingDirectory = $script:ResolvedStaging
        backupDirectory = $script:ResolvedBackup
        phase = $Phase
        updatedAtUtc = ConvertTo-GuluUtcText -Value ([DateTimeOffset]::UtcNow)
        mainInstallerProcessId = $mainPid
        mainInstallerStartTimeUtcTicks = $mainStartTicks
        newProcessId = $NewProcessId
        newProcessStartTimeUtcTicks = $NewProcessStartTimeUtcTicks
        readyEventName = $ReadyEventName
        restoredProcessId = $RestoredProcessId
    }
    Set-GuluAtomicText `
        -Path $script:JournalPath `
        -Value ($journal | ConvertTo-Json -Depth 4)
    $script:Journal = $journal
    Write-GuluLog -Message "Journal committed phase '$Phase'."
    return $journal
}

function New-GuluResult {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][bool]$RollbackPerformed,
        [AllowNull()][Nullable[int]]$LaunchedProcessId,
        [AllowNull()][Nullable[int]]$RestoredProcessId,
        [AllowNull()][string]$ReadyEventName,
        [Parameter(Mandatory = $true)][string]$BackupCleanupStatus,
        [Parameter(Mandatory = $true)][string]$FailedStagingCleanupStatus,
        [AllowNull()][string]$ErrorCode,
        [AllowNull()][string]$ErrorMessage
    )

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        operationId = $script:OperationIdText
        status = $Status
        phase = $Phase
        installDirectory = $script:ResolvedInstall
        stagingDirectory = $script:ResolvedStaging
        backupDirectory = $script:ResolvedBackup
        startedAtUtc = ConvertTo-GuluUtcText -Value $script:StartedAtUtc
        completedAtUtc = ConvertTo-GuluUtcText -Value ([DateTimeOffset]::UtcNow)
        launchedProcessId = $LaunchedProcessId
        restoredProcessId = $RestoredProcessId
        readyEventName = $ReadyEventName
        rollbackPerformed = $RollbackPerformed
        backupRetained = (
            Test-Path -LiteralPath $script:ResolvedBackup -PathType Container)
        backupCleanupStatus = $BackupCleanupStatus
        failedStagingRetained = (
            Test-Path -LiteralPath $script:ResolvedStaging -PathType Container)
        failedStagingCleanupStatus = $FailedStagingCleanupStatus
        logPath = $script:LogPath
        journalPath = $script:JournalPath
        errorCode = $ErrorCode
        errorMessage = $ErrorMessage
    }
}

function Write-GuluResultCreateOnly {
    param([Parameter(Mandatory = $true)][object]$Result)

    $json = $Result | ConvertTo-Json -Depth 5
    $temporary = Join-Path (
        $script:ResolvedState
    ) ".result-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        Write-GuluCreateOnlyText -Path $temporary -Value $json
        [IO.File]::Move($temporary, $script:ResultPath)
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
    return $json
}

function Set-GuluResult {
    param([Parameter(Mandatory = $true)][object]$Result)

    $json = $Result | ConvertTo-Json -Depth 5
    Set-GuluAtomicText -Path $script:ResultPath -Value $json
    return $json
}

function Read-GuluResult {
    $result = Read-GuluUtf8Json -Path $script:ResultPath
    if ($null -ne $result) {
        Assert-GuluStateIdentity -Value $result
        if (@("Succeeded", "RolledBack", "Failed") -cnotcontains
            [string]$result.status) {
            throw "The durable update result has an unknown status."
        }
    }
    return $result
}

function Get-GuluProcessPath {
    param([Parameter(Mandatory = $true)][Diagnostics.Process]$Process)

    try {
        $path = [string]$Process.Path
    }
    catch {
        throw "The updater could not inspect a relevant process path."
    }
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "The updater could not inspect a relevant process path."
    }
    return Resolve-GuluFullPath -Path $path
}

function Get-GuluInstalledProcesses {
    $expected = Resolve-GuluFullPath -Path (
        Join-Path $script:ResolvedInstall "GuluPet.exe")
    $matches = @()
    foreach ($candidate in @(Get-Process -Name "GuluPet" -ErrorAction SilentlyContinue)) {
        $candidatePath = Get-GuluProcessPath -Process $candidate
        if ([string]::Equals(
                $candidatePath,
                $expected,
                [StringComparison]::OrdinalIgnoreCase)) {
            $matches += $candidate
        }
    }
    return @($matches)
}

function Stop-GuluJournalOwnedNewProcess {
    param([Parameter(Mandatory = $true)][object]$Journal)

    if ($null -eq $Journal.newProcessId -or
        [int]$Journal.newProcessId -le 0 -or
        $null -eq $Journal.newProcessStartTimeUtcTicks -or
        [long]$Journal.newProcessStartTimeUtcTicks -le 0) {
        if (@(Get-GuluInstalledProcesses).Count -gt 0) {
            throw "A new-install process exists without durable ownership data."
        }
        return
    }

    $process = Get-Process `
        -Id ([int]$Journal.newProcessId) `
        -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }
    $expected = Resolve-GuluFullPath -Path (
        Join-Path $script:ResolvedInstall "GuluPet.exe")
    $actual = Get-GuluProcessPath -Process $process
    $actualStartTicks = $process.StartTime.ToUniversalTime().Ticks
    if (-not [string]::Equals(
            $actual,
            $expected,
            [StringComparison]::OrdinalIgnoreCase) -or
        $actualStartTicks -ne [long]$Journal.newProcessStartTimeUtcTicks) {
        throw "The journal-owned new PID identity no longer matches."
    }
    $process.Kill()
    if (-not $process.WaitForExit(10000)) {
        throw "The journal-owned new process did not stop."
    }
    Write-GuluLog -Message "Stopped the exact journal-owned new process."
}

function Ensure-GuluInstalledAppRunning {
    param([Parameter(Mandatory = $true)][bool]$RollbackNotice)

    $existing = @(Get-GuluInstalledProcesses)
    if ($existing.Count -gt 1) {
        throw "Multiple GuluPet processes are using the restored install."
    }
    if ($existing.Count -eq 1) {
        return [int]$existing[0].Id
    }

    $app = Join-Path $script:ResolvedInstall "GuluPet.exe"
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        throw "The restored install does not contain GuluPet.exe."
    }
    $arguments = if ($RollbackNotice) {
        $script:RollbackNoticeArgument
    }
    else {
        $null
    }
    $process = if ($null -eq $arguments) {
        Start-Process `
            -FilePath $app `
            -WorkingDirectory $script:ResolvedInstall `
            -PassThru
    }
    else {
        Start-Process `
            -FilePath $app `
            -ArgumentList $arguments `
            -WorkingDirectory $script:ResolvedInstall `
            -PassThru
    }
    Start-Sleep -Milliseconds 1000
    $process.Refresh()
    if ($process.HasExited) {
        throw "The restored GuluPet process exited immediately."
    }
    return [int]$process.Id
}

function Quote-GuluNativeArgument {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value.Contains('"')) {
        throw "Update command arguments cannot contain quote characters."
    }
    return '"' + $Value + '"'
}

function Get-GuluWindowsPowerShellPath {
    $path = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    ) "WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Windows PowerShell 5.1 is unavailable."
    }
    return $path
}

function Get-GuluRecoveryCommand {
    $powershell = Get-GuluWindowsPowerShellPath
    $parts = @(
        (Quote-GuluNativeArgument $powershell),
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-WindowStyle", "Hidden",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-GuluNativeArgument $script:ResolvedScript),
        "-Mode", "Recover",
        "-InstallDirectory", (Quote-GuluNativeArgument $script:ResolvedInstall),
        "-StagingDirectory", (Quote-GuluNativeArgument $script:ResolvedStaging),
        "-BackupDirectory", (Quote-GuluNativeArgument $script:ResolvedBackup),
        "-StateDirectory", (Quote-GuluNativeArgument $script:ResolvedState),
        "-OperationId", $script:OperationIdText,
        "-CurrentProcessId", "0",
        "-RunOnceRegistryPath", (Quote-GuluNativeArgument $script:RunOnceRegistryPath)
    )
    return $parts -join " "
}

function Register-GuluRunOnce {
    if ($DisableRunOnce) {
        Write-GuluLog -Message "RunOnce registration disabled by caller."
        return
    }
    $expected = Get-GuluRecoveryCommand
    if (-not (Test-Path -LiteralPath $script:RunOnceRegistryPath)) {
        New-Item -Path $script:RunOnceRegistryPath -Force | Out-Null
    }
    $key = Get-Item -LiteralPath $script:RunOnceRegistryPath
    $existing = $key.GetValue($script:RunOnceValueName, $null)
    if ($null -ne $existing) {
        if ([string]$existing -cne $expected) {
            throw "The transaction RunOnce value already has different content."
        }
        return
    }
    New-ItemProperty `
        -LiteralPath $script:RunOnceRegistryPath `
        -Name $script:RunOnceValueName `
        -PropertyType String `
        -Value $expected | Out-Null
    Write-GuluLog -Message "Registered exact per-operation RunOnce recovery."
}

function Remove-GuluRunOnceTerminal {
    if ($DisableRunOnce) {
        return
    }
    if (-not (Test-Path -LiteralPath $script:RunOnceRegistryPath)) {
        return
    }
    $key = Get-Item -LiteralPath $script:RunOnceRegistryPath
    if ($null -ne $key.GetValue($script:RunOnceValueName, $null)) {
        Remove-ItemProperty `
            -LiteralPath $script:RunOnceRegistryPath `
            -Name $script:RunOnceValueName
        Write-GuluLog -Message "Removed terminal per-operation RunOnce recovery."
    }
}

function Start-GuluWatchdog {
    if ($DisableWatchdog) {
        Write-GuluLog -Message "Watchdog disabled by caller."
        return $null
    }
    $powershell = Get-GuluWindowsPowerShellPath
    $mainStartTicks = [Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().Ticks
    $parts = @(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-WindowStyle", "Hidden",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-GuluNativeArgument $script:ResolvedScript),
        "-Mode", "Watchdog",
        "-InstallDirectory", (Quote-GuluNativeArgument $script:ResolvedInstall),
        "-StagingDirectory", (Quote-GuluNativeArgument $script:ResolvedStaging),
        "-BackupDirectory", (Quote-GuluNativeArgument $script:ResolvedBackup),
        "-StateDirectory", (Quote-GuluNativeArgument $script:ResolvedState),
        "-OperationId", $script:OperationIdText,
        "-CurrentProcessId", $CurrentProcessId.ToString(),
        "-MainInstallerProcessId", $PID.ToString(),
        "-MainInstallerStartTimeUtcTicks", $mainStartTicks.ToString(),
        "-MutexWaitSeconds", "30",
        "-RunOnceRegistryPath", (Quote-GuluNativeArgument $script:RunOnceRegistryPath)
    )
    if ($DisableRunOnce) {
        $parts += "-DisableRunOnce"
    }
    $watchdog = Start-Process `
        -FilePath $powershell `
        -ArgumentList ($parts -join " ") `
        -WorkingDirectory $script:ResolvedState `
        -WindowStyle Hidden `
        -PassThru
    Write-GuluLog -Message "Started independent watchdog PID $($watchdog.Id)."
    return $watchdog
}

function Wait-GuluMainInstallerExit {
    if ($MainInstallerProcessId -le 0 -or
        $MainInstallerStartTimeUtcTicks -le 0) {
        throw "Watchdog mode requires the main installer identity."
    }
    $main = Get-Process -Id $MainInstallerProcessId -ErrorAction SilentlyContinue
    if ($null -eq $main) {
        return
    }
    if ($main.StartTime.ToUniversalTime().Ticks -ne
        $MainInstallerStartTimeUtcTicks) {
        return
    }
    [void]$main.WaitForExit()
}

function Get-GuluTopology {
    foreach ($path in @(
            $script:ResolvedInstall,
            $script:ResolvedStaging,
            $script:ResolvedBackup)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            throw "A transaction directory path is occupied by a file."
        }
    }
    $installExists = Test-Path -LiteralPath $script:ResolvedInstall -PathType Container
    $stagingExists = Test-Path -LiteralPath $script:ResolvedStaging -PathType Container
    $backupExists = Test-Path -LiteralPath $script:ResolvedBackup -PathType Container
    foreach ($path in @(
            $(if ($installExists) { $script:ResolvedInstall }),
            $(if ($stagingExists) { $script:ResolvedStaging }),
            $(if ($backupExists) { $script:ResolvedBackup }))) {
        if ($null -ne $path) {
            Assert-GuluPlainDirectory -Path $path
        }
    }
    return [pscustomobject]@{
        Install = [bool]$installExists
        Staging = [bool]$stagingExists
        Backup = [bool]$backupExists
    }
}

function Remove-GuluFailedStaging {
    if (-not (Test-Path -LiteralPath $script:ResolvedStaging -PathType Container)) {
        return "AlreadyAbsent"
    }
    try {
        Assert-GuluPlainTree -Path $script:ResolvedStaging
        Remove-Item -LiteralPath $script:ResolvedStaging -Recurse -Force
        if (Test-Path -LiteralPath $script:ResolvedStaging) {
            throw "The failed staging directory still exists after cleanup."
        }
        Write-GuluLog -Message "Removed exact failed staging for this operation."
        return "Removed"
    }
    catch {
        Write-GuluLog -Message "Failed staging cleanup was retained for audit."
        return "RetainedCleanupFailed"
    }
}

function Remove-GuluCommittedBackup {
    if (-not (Test-Path -LiteralPath $script:ResolvedBackup -PathType Container)) {
        return "AlreadyAbsent"
    }
    try {
        Assert-GuluPlainTree -Path $script:ResolvedBackup
        Remove-Item -LiteralPath $script:ResolvedBackup -Recurse -Force
        if (Test-Path -LiteralPath $script:ResolvedBackup) {
            throw "The committed backup still exists after cleanup."
        }
        Write-GuluLog -Message "Removed exact committed backup for this operation."
        return "Removed"
    }
    catch {
        Write-GuluLog -Message "Committed backup cleanup was retained for audit."
        return "RetainedCleanupFailed"
    }
}

function Complete-GuluRolledBackState {
    param(
        [Parameter(Mandatory = $true)][object]$Journal,
        [AllowNull()][string]$ErrorMessage
    )

    $topology = Get-GuluTopology
    $rollbackPerformed = $false
    if ($topology.Install -and $topology.Staging -and -not $topology.Backup) {
        # Either nothing was moved, or a previous recovery completed both
        # inverse renames before its terminal journal write.
    }
    elseif (-not $topology.Install -and $topology.Staging -and $topology.Backup) {
        [IO.Directory]::Move($script:ResolvedBackup, $script:ResolvedInstall)
        $rollbackPerformed = $true
    }
    elseif ($topology.Install -and -not $topology.Staging -and $topology.Backup) {
        Stop-GuluJournalOwnedNewProcess -Journal $Journal
        [IO.Directory]::Move($script:ResolvedInstall, $script:ResolvedStaging)
        [IO.Directory]::Move($script:ResolvedBackup, $script:ResolvedInstall)
        $rollbackPerformed = $true
    }
    else {
        throw "The journal and transaction directory topology are inconsistent."
    }

    Assert-GuluPlainTree -Path $script:ResolvedInstall
    if (Test-Path -LiteralPath $script:ResolvedBackup) {
        throw "Rollback did not consume the exact backup directory."
    }
    $restoredProcessId = Ensure-GuluInstalledAppRunning -RollbackNotice $true
    $rolledBackJournal = Set-GuluJournalPhase `
        -Phase "RolledBack" `
        -NewProcessId $Journal.newProcessId `
        -NewProcessStartTimeUtcTicks $Journal.newProcessStartTimeUtcTicks `
        -ReadyEventName ([string]$Journal.readyEventName) `
        -RestoredProcessId $restoredProcessId

    $preliminary = New-GuluResult `
        -Status "RolledBack" `
        -Phase "RolledBack" `
        -RollbackPerformed $rollbackPerformed `
        -LaunchedProcessId $Journal.newProcessId `
        -RestoredProcessId $restoredProcessId `
        -ReadyEventName ([string]$Journal.readyEventName) `
        -BackupCleanupStatus "NotApplicable" `
        -FailedStagingCleanupStatus "Pending" `
        -ErrorCode "RecoveredInterruptedUpdate" `
        -ErrorMessage $ErrorMessage
    $existingResult = Read-GuluResult
    $json = if ($null -eq $existingResult) {
        Write-GuluResultCreateOnly -Result $preliminary
    }
    else {
        Set-GuluResult -Result $preliminary
    }
    $stagingCleanup = Remove-GuluFailedStaging
    $final = New-GuluResult `
        -Status "RolledBack" `
        -Phase "RolledBack" `
        -RollbackPerformed $rollbackPerformed `
        -LaunchedProcessId $Journal.newProcessId `
        -RestoredProcessId $restoredProcessId `
        -ReadyEventName ([string]$Journal.readyEventName) `
        -BackupCleanupStatus "NotApplicable" `
        -FailedStagingCleanupStatus $stagingCleanup `
        -ErrorCode "RecoveredInterruptedUpdate" `
        -ErrorMessage $ErrorMessage
    try {
        $json = Set-GuluResult -Result $final
    }
    catch {
        Write-GuluLog -Message "Could not finalize failed-staging cleanup status."
    }
    Remove-GuluRunOnceTerminal
    return [pscustomobject]@{
        Json = $json
        Status = "RolledBack"
        Journal = $rolledBackJournal
    }
}

function Complete-GuluCommittedState {
    param([Parameter(Mandatory = $true)][object]$Journal)

    $topology = Get-GuluTopology
    if (-not $topology.Install -or $topology.Staging) {
        throw "Committed journal topology is inconsistent."
    }
    Assert-GuluPlainTree -Path $script:ResolvedInstall
    $launchedProcessId = Ensure-GuluInstalledAppRunning -RollbackNotice $false
    $preliminary = New-GuluResult `
        -Status "Succeeded" `
        -Phase "Committed" `
        -RollbackPerformed $false `
        -LaunchedProcessId $launchedProcessId `
        -RestoredProcessId $null `
        -ReadyEventName ([string]$Journal.readyEventName) `
        -BackupCleanupStatus "Pending" `
        -FailedStagingCleanupStatus "NotApplicable" `
        -ErrorCode $null `
        -ErrorMessage $null
    $existingResult = Read-GuluResult
    $json = if ($null -eq $existingResult) {
        Write-GuluResultCreateOnly -Result $preliminary
    }
    else {
        Set-GuluResult -Result $preliminary
    }
    $backupCleanup = Remove-GuluCommittedBackup
    $final = New-GuluResult `
        -Status "Succeeded" `
        -Phase "Committed" `
        -RollbackPerformed $false `
        -LaunchedProcessId $launchedProcessId `
        -RestoredProcessId $null `
        -ReadyEventName ([string]$Journal.readyEventName) `
        -BackupCleanupStatus $backupCleanup `
        -FailedStagingCleanupStatus "NotApplicable" `
        -ErrorCode $null `
        -ErrorMessage $null
    try {
        $json = Set-GuluResult -Result $final
    }
    catch {
        Write-GuluLog -Message "Could not finalize backup cleanup status."
    }
    Remove-GuluRunOnceTerminal
    return [pscustomobject]@{
        Json = $json
        Status = "Succeeded"
        Journal = $Journal
    }
}

function Invoke-GuluRecoveryStateMachine {
    param(
        [AllowNull()][object]$Journal,
        [AllowNull()][string]$ErrorMessage
    )

    if ($null -eq $Journal) {
        $topology = Get-GuluTopology
        if (-not ($topology.Install -and $topology.Staging -and
                -not $topology.Backup)) {
            throw "Recovery without a journal found an unknown topology."
        }
        $Journal = Set-GuluJournalPhase `
            -Phase "BeforeSwap" `
            -NewProcessId $null `
            -NewProcessStartTimeUtcTicks $null `
            -ReadyEventName $null `
            -RestoredProcessId $null
    }

    if ([string]$Journal.phase -ceq "Committed") {
        return Complete-GuluCommittedState -Journal $Journal
    }
    if ([string]$Journal.phase -ceq "RolledBack") {
        $existing = Read-GuluResult
        $topology = Get-GuluTopology
        if ($topology.Install -and -not $topology.Staging -and
            -not $topology.Backup -and $null -ne $existing) {
            $null = Ensure-GuluInstalledAppRunning -RollbackNotice $true
            Remove-GuluRunOnceTerminal
            return [pscustomobject]@{
                Json = ($existing | ConvertTo-Json -Depth 5)
                Status = [string]$existing.status
                Journal = $Journal
            }
        }
    }
    return Complete-GuluRolledBackState `
        -Journal $Journal `
        -ErrorMessage $ErrorMessage
}

function Assert-GuluFreshPackage {
    Assert-GuluPlainTree -Path $script:ResolvedInstall
    Assert-GuluPlainTree -Path $script:ResolvedStaging
    if (Test-Path -LiteralPath $script:ResolvedBackup) {
        throw "The create-only backup directory already exists."
    }
    $newApp = Join-Path $script:ResolvedStaging "GuluPet.exe"
    if (-not (Test-Path -LiteralPath $newApp -PathType Leaf)) {
        throw "The staged update does not contain GuluPet.exe."
    }
    $newAppItem = Get-Item -LiteralPath $newApp -Force
    if (($newAppItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The staged application cannot be a reparse point."
    }
    $forbidden = @(
        Get-ChildItem -LiteralPath $script:ResolvedStaging -File -Recurse |
            Where-Object {
                $_.Extension -ieq ".pdb" -or
                $_.Name -ieq "gulu-test.exe" -or
                $_.Name -ieq "START-GULU-TEST.cmd" -or
                $_.Name -ieq "START-GULU-TEST-HIDDEN.cmd" -or
                $_.Name -ieq "TEST-CONTROL.md" -or
                $_.Name -like "GuluPet.TestCli*"
            })
    if ($forbidden.Count -gt 0) {
        throw "The staged update contains test tooling or PDB files."
    }
}

function Wait-GuluCurrentApplicationExit {
    if ($CurrentProcessId -le 0) {
        throw "Install mode requires the current GuluPet PID."
    }
    $current = Get-Process -Id $CurrentProcessId -ErrorAction SilentlyContinue
    if ($null -ne $current) {
        $expected = Resolve-GuluFullPath -Path (
            Join-Path $script:ResolvedInstall "GuluPet.exe")
        $actual = Get-GuluProcessPath -Process $current
        if (-not [string]::Equals(
                $actual,
                $expected,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "The current PID does not belong to this GuluPet install."
        }
        if (-not $current.WaitForExit(
                $CurrentProcessExitTimeoutSeconds * 1000)) {
            throw "The current GuluPet process did not exit before timeout."
        }
    }
    if (@(Get-GuluInstalledProcesses).Count -gt 0) {
        throw "A GuluPet process is still using the install directory."
    }
}

function Invoke-GuluFreshInstall {
    Assert-GuluFreshPackage
    Register-GuluRunOnce
    $script:Journal = Set-GuluJournalPhase `
        -Phase "BeforeSwap" `
        -NewProcessId $null `
        -NewProcessStartTimeUtcTicks $null `
        -ReadyEventName $null `
        -RestoredProcessId $null
    $null = Start-GuluWatchdog
    Wait-GuluCurrentApplicationExit

    [IO.Directory]::Move($script:ResolvedInstall, $script:ResolvedBackup)
    if ($EnableTestFaultAfterOldMove) {
        # Deliberately terminate before the OldMoved journal write. Recovery
        # must reconcile BeforeSwap plus the exact old-moved filesystem shape.
        Write-GuluLog -Message "Injecting immediate process termination after first rename."
        [Environment]::Exit(97)
    }
    $script:Journal = Set-GuluJournalPhase `
        -Phase "OldMoved" `
        -NewProcessId $null `
        -NewProcessStartTimeUtcTicks $null `
        -ReadyEventName $null `
        -RestoredProcessId $null

    [IO.Directory]::Move($script:ResolvedStaging, $script:ResolvedInstall)
    $script:Journal = Set-GuluJournalPhase `
        -Phase "NewMoved" `
        -NewProcessId $null `
        -NewProcessStartTimeUtcTicks $null `
        -ReadyEventName $null `
        -RestoredProcessId $null

    $readyEventName = $script:ReadyEventPrefix +
        [Guid]::NewGuid().ToString("N")
    $createdReadyEvent = $false
    $readyEvent = New-Object Threading.EventWaitHandle(
        $false,
        [Threading.EventResetMode]::ManualReset,
        $readyEventName,
        [ref]$createdReadyEvent)
    if (-not $createdReadyEvent) {
        $readyEvent.Dispose()
        throw "The post-update ready event name unexpectedly collided."
    }

    $startedProcess = $null
    try {
        $installedApp = Join-Path $script:ResolvedInstall "GuluPet.exe"
        $startedProcess = Start-Process `
            -FilePath $installedApp `
            -ArgumentList ($script:ReadyArgumentPrefix + $readyEventName) `
            -WorkingDirectory $script:ResolvedInstall `
            -PassThru
        $startedTicks = $startedProcess.StartTime.ToUniversalTime().Ticks
        $script:Journal = Set-GuluJournalPhase `
            -Phase "NewStarted" `
            -NewProcessId $startedProcess.Id `
            -NewProcessStartTimeUtcTicks $startedTicks `
            -ReadyEventName $readyEventName `
            -RestoredProcessId $null

        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $ready = $false
        while ($stopwatch.Elapsed.TotalSeconds -lt $ReadyTimeoutSeconds) {
            if ($readyEvent.WaitOne(100)) {
                $ready = $true
                break
            }
            $startedProcess.Refresh()
            if ($startedProcess.HasExited) {
                throw "The updated application exited before signaling ready."
            }
        }
        $stopwatch.Stop()
        if (-not $ready) {
            throw "The updated application did not signal ready before timeout."
        }
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "The updated application exited while completing readiness."
        }

        $script:Journal = Set-GuluJournalPhase `
            -Phase "Committed" `
            -NewProcessId $startedProcess.Id `
            -NewProcessStartTimeUtcTicks $startedTicks `
            -ReadyEventName $readyEventName `
            -RestoredProcessId $null
        return Complete-GuluCommittedState -Journal $script:Journal
    }
    finally {
        $readyEvent.Dispose()
    }
}

function Get-GuluMutexName {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(
            $script:ResolvedInstall.ToUpperInvariant())
        try {
            $hash = $sha256.ComputeHash($bytes)
        }
        finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
    finally {
        $sha256.Dispose()
    }
    try {
        $suffix = -join ($hash[0..15] | ForEach-Object { $_.ToString("x2") })
        return "Local\GuluPet.Update.Install.$suffix"
    }
    finally {
        [Array]::Clear($hash, 0, $hash.Length)
    }
}

# Resolve and validate the immutable transaction identity before any mutation.
$script:ResolvedInstall = Resolve-GuluFullPath -Path $InstallDirectory
$script:ResolvedStaging = Resolve-GuluFullPath -Path $StagingDirectory
$script:ResolvedBackup = Resolve-GuluFullPath -Path $BackupDirectory
$script:ResolvedState = Resolve-GuluFullPath -Path $StateDirectory
$script:ResolvedScript = Resolve-GuluFullPath -Path $PSCommandPath

$testFaultEnvironment = [Environment]::GetEnvironmentVariable(
    "GULUPET_UPDATE_TEST_FAULTS",
    [EnvironmentVariableTarget]::Process)
if ($EnableTestFaultAfterOldMove -or $DisableWatchdog) {
    if ($testFaultEnvironment -cne "1") {
        throw "Installer fault controls require the explicit fixture environment."
    }
}
$expectedTestRunOncePath =
    "HKCU:\Software\GuluPet\UpdateTests\$($script:OperationIdText)\RunOnce"
if (-not [string]::Equals(
        $script:RunOnceRegistryPath,
        $script:ProductionRunOnceRegistryPath,
        [StringComparison]::OrdinalIgnoreCase) -and
    -not ($testFaultEnvironment -ceq "1" -and
        [string]::Equals(
            $script:RunOnceRegistryPath,
            $expectedTestRunOncePath,
            [StringComparison]::OrdinalIgnoreCase))) {
    throw "RunOnce registry path is outside the production or exact fixture boundary."
}

$targets = @(
    $script:ResolvedInstall,
    $script:ResolvedStaging,
    $script:ResolvedBackup)
if (@($targets | Select-Object -Unique).Count -ne 3) {
    throw "Install, staging, and backup directories must be distinct."
}
foreach ($boundary in $targets) {
    if (Test-GuluPathWithin `
            -Candidate $script:ResolvedState `
            -Boundary $boundary) {
        throw "Durable update state must be outside swapped directories."
    }
    if (Test-GuluPathWithin `
            -Candidate $script:ResolvedScript `
            -Boundary $boundary) {
        throw "The installer must run from outside swapped directories."
    }
}

$installParent = Split-Path -Parent $script:ResolvedInstall
if (-not [string]::Equals(
        $installParent,
        (Split-Path -Parent $script:ResolvedStaging),
        [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        $installParent,
        (Split-Path -Parent $script:ResolvedBackup),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Install, staging, and backup must be direct siblings."
}
$installLeaf = [IO.Path]::GetFileName($script:ResolvedInstall)
if ([string]::IsNullOrWhiteSpace($installLeaf) -or
    $installLeaf -ceq "." -or $installLeaf -ceq "..") {
    throw "The install directory name is invalid."
}
if ([IO.Path]::GetFileName($script:ResolvedStaging) -cne
        "gulu-update-staging-$($script:OperationIdText)" -or
    [IO.Path]::GetFileName($script:ResolvedBackup) -cne
        "gulu-update-backup-$($script:OperationIdText)") {
    throw "Staging and backup names must be generated from the operation ID."
}
Assert-GuluPlainDirectory -Path $installParent
Assert-GuluNoReparseAncestors -Path $installParent

if (-not (Test-Path -LiteralPath $script:ResolvedState)) {
    New-Item -ItemType Directory -Path $script:ResolvedState | Out-Null
}
Assert-GuluPlainDirectory -Path $script:ResolvedState
Assert-GuluNoReparseAncestors -Path $script:ResolvedState
$script:LogPath = Join-Path (
    $script:ResolvedState
) "update-install-$($script:OperationIdText).log"
$script:JournalPath = Join-Path (
    $script:ResolvedState
) "update-journal-$($script:OperationIdText).json"
$script:ResultPath = Join-Path (
    $script:ResolvedState
) "update-result-$($script:OperationIdText).json"
if (-not (Test-Path -LiteralPath $script:LogPath)) {
    Write-GuluCreateOnlyText -Path $script:LogPath -Value ""
}
Write-GuluLog -Message "Installer entry started."

if ($Mode -ceq "Watchdog") {
    Wait-GuluMainInstallerExit
}

$mutex = $null
$mutexAcquired = $false
$outputJson = $null
$exitCode = 2
try {
    $createdMutex = $false
    $mutex = New-Object Threading.Mutex(
        $false,
        (Get-GuluMutexName),
        [ref]$createdMutex)
    try {
        $mutexAcquired = $mutex.WaitOne($MutexWaitSeconds * 1000)
    }
    catch [Threading.AbandonedMutexException] {
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw "Another installer already owns this installation."
    }

    $existingResult = Read-GuluResult
    $script:Journal = Read-GuluJournal
    if ($null -ne $existingResult -and
        ([string]$existingResult.status -ceq "Succeeded" -or
            [string]$existingResult.status -ceq "RolledBack")) {
        if ($null -eq $script:Journal) {
            throw "A terminal result is missing its transaction journal."
        }
        $completedTerminal = Invoke-GuluRecoveryStateMachine `
            -Journal $script:Journal `
            -ErrorMessage ([string]$existingResult.errorMessage)
        $outputJson = $completedTerminal.Json
        if ([string]$existingResult.status -ceq "Succeeded" -or
            $Mode -cne "Install") {
            $exitCode = 0
        }
        else {
            $exitCode = 1
        }
    }
    elseif ($null -ne $existingResult -and $null -ne $script:Journal) {
        $recoveredExisting = Invoke-GuluRecoveryStateMachine `
            -Journal $script:Journal `
            -ErrorMessage ([string]$existingResult.errorMessage)
        $outputJson = $recoveredExisting.Json
        $exitCode = if ($Mode -ceq "Install") { 1 } else { 0 }
    }
    elseif ($null -ne $existingResult) {
        $outputJson = $existingResult | ConvertTo-Json -Depth 5
        if ([string]$existingResult.status -ceq "Succeeded" -or
            $Mode -cne "Install") {
            $exitCode = 0
        }
        else {
            $exitCode = 1
        }
    }
    else {
        if ($Mode -cne "Install" -or $null -ne $script:Journal) {
            $recovered = Invoke-GuluRecoveryStateMachine `
                -Journal $script:Journal `
                -ErrorMessage "Recovered an interrupted update transaction."
            $outputJson = $recovered.Json
            $exitCode = if ($Mode -ceq "Install") { 1 } else { 0 }
        }
        else {
            try {
                $completed = Invoke-GuluFreshInstall
                $outputJson = $completed.Json
                $exitCode = 0
            }
            catch {
                $failureMessage = [string]$_.Exception.Message
                Write-GuluLog -Message "Fresh install failed: $failureMessage"
                $script:Journal = Read-GuluJournal
                if ($null -ne $script:Journal -and
                    [string]$script:Journal.phase -cne "Committed") {
                    try {
                        $recovered = Invoke-GuluRecoveryStateMachine `
                            -Journal $script:Journal `
                            -ErrorMessage $failureMessage
                        $outputJson = $recovered.Json
                        $exitCode = 1
                    }
                    catch {
                        Write-GuluLog -Message (
                            "Recovery remains nonterminal and will be retried: " +
                            $_.Exception.Message)
                        throw
                    }
                }
                else {
                    $failed = New-GuluResult `
                        -Status "Failed" `
                        -Phase "Validate" `
                        -RollbackPerformed $false `
                        -LaunchedProcessId $null `
                        -RestoredProcessId $null `
                        -ReadyEventName $null `
                        -BackupCleanupStatus "NotAttempted" `
                        -FailedStagingCleanupStatus "NotAttempted" `
                        -ErrorCode "UpdateInstallFailed" `
                        -ErrorMessage $failureMessage
                    $outputJson = Write-GuluResultCreateOnly -Result $failed
                    $exitCode = 1
                }
            }
        }
    }
}
catch {
    Write-GuluLog -Message "Installer entry failed without a terminal state."
    Write-Error $_.Exception.Message
    $exitCode = 2
}
finally {
    if ($mutexAcquired -and $null -ne $mutex) {
        $mutex.ReleaseMutex()
    }
    if ($null -ne $mutex) {
        $mutex.Dispose()
    }
}

if ($null -ne $outputJson) {
    Write-Output $outputJson
}
exit $exitCode
