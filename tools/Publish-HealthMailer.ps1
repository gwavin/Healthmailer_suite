param(
    [string]$Output = ".\publish\HealthMailer"
)

$ErrorActionPreference = 'Stop'

dotnet publish .\apps\HealthMailer\HealthMailer.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published HealthMailer installer/runtime EXE to $Output"
Write-Host "Run HealthMailer.exe --install to browse for the handoff folder and register the logon watcher."
