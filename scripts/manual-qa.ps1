param(
    [string]$ProcessName = 'CodexQuotaRail.App',
    [ValidateRange(5, 600)]
    [int]$DurationSeconds = 60,
    [double]$MaximumCpuSeconds = 0.3,
    [double]$MaximumWorkingSetMb = 80
)

$ErrorActionPreference = 'Stop'
$processes = @(Get-Process -Name $ProcessName -ErrorAction Stop)
if ($processes.Count -ne 1) {
    throw "Expected exactly one $ProcessName process; found $($processes.Count)."
}

$process = $processes[0]
$process.Refresh()
$startCpu = $process.CPU
Start-Sleep -Seconds $DurationSeconds
$process.Refresh()
if ($process.HasExited) {
    throw "$ProcessName exited during sampling."
}

$result = [pscustomobject]@{
    ProcessId = $process.Id
    DurationSeconds = $DurationSeconds
    CpuSeconds = [math]::Round($process.CPU - $startCpu, 3)
    WorkingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 1)
    PrivateMemoryMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
    CpuPassed = ($process.CPU - $startCpu) -le $MaximumCpuSeconds
    MemoryPassed = ($process.WorkingSet64 / 1MB) -lt $MaximumWorkingSetMb
}

$result
if (-not $result.CpuPassed -or -not $result.MemoryPassed) {
    throw 'Idle performance did not meet the release threshold.'
}
