param(
    [string]$Output = ".\publish\printRxer",
    [string]$TaskName = "printRxer",
    [switch]$DoNotStopRunningWatcher
)

$ErrorActionPreference = 'Stop'

$resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$publishedExe = Join-Path $resolvedOutput 'printRxer.exe'
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$taskWasRunning = $false

if (-not $DoNotStopRunningWatcher) {
    if ($task -and $task.State -eq 'Running') {
        $taskWasRunning = $true
        Write-Host "Stopping running printRxer scheduled task before publishing."
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }

    $running = Get-Process -Name 'printRxer' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq [System.IO.Path]::GetFullPath($publishedExe))
            } catch {
                $false
            }
        }

    if ($running) {
        Write-Host "Stopping running printRxer process(es) using $publishedExe before publishing."
        $running | Stop-Process -Force
    }
}

dotnet publish .\apps\PrintRxerV3\app\PrintRxerV3.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if ($taskWasRunning) {
    Write-Host "Restarting printRxer scheduled task."
    Start-ScheduledTask -TaskName $TaskName
}

Write-Host "Published printRxer runtime EXE to $Output"
Write-Host "Run printRxer.exe --watch to watch captured print jobs."
