param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [switch]$PublishSingleFile = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=== Block Vision PC Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration, Runtime: $Runtime, SelfContained: $SelfContained"

# Check dotnet
try {
    dotnet --version | Out-Null
} catch {
    Write-Error ".NET SDK not found! Install from https://dotnet.microsoft.com/download"
    exit 1
}

$publishArgs = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $SelfContained.ToString().ToLower(),
    "-p:PublishSingleFile=$($PublishSingleFile.ToString().ToLower())",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded"
)

# Core doesn't need publish, it's library
Write-Host "`nBuilding Core..." -ForegroundColor Yellow
dotnet build src/BlockVision.Core/BlockVision.Core.csproj -c $Configuration

Write-Host "`nPublishing SDK..." -ForegroundColor Yellow
dotnet build src/BlockVision.SDK/BlockVision.SDK.csproj -c $Configuration

Write-Host "`nPublishing App (WPF)..." -ForegroundColor Yellow
dotnet publish src/BlockVision.App/BlockVision.App.csproj @publishArgs -o publish/app

Write-Host "`nPublishing CLI..." -ForegroundColor Yellow
dotnet publish src/BlockVision.CLI/BlockVision.CLI.csproj @publishArgs -o publish/cli

# Copy to single dist folder
Write-Host "`nCreating dist..." -ForegroundColor Yellow
$dist = "dist/BlockVisionPC"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Copy-Item -Path "publish/app/*" -Destination $dist -Recurse -Force
Copy-Item -Path "publish/cli/*" -Destination "$dist/" -Force -ErrorAction SilentlyContinue

# Copy examples and docs
Copy-Item -Path "examples" -Destination "$dist/examples" -Recurse -Force
Copy-Item -Path "README.md" -Destination $dist -Force
Copy-Item -Path "INSTALL.md" -Destination $dist -Force
Copy-Item -Path "docs" -Destination "$dist/docs" -Recurse -Force

Write-Host "`n=== Build complete! ===" -ForegroundColor Green
Write-Host "Output: $dist" -ForegroundColor Green
Get-ChildItem $dist | Format-Table Name, Length, LastWriteTime

Write-Host "`nTo test:" -ForegroundColor Cyan
Write-Host "  .\dist\BlockVisionPC\BlockVisionPC.exe"
Write-Host "  .\dist\BlockVisionPC\bvpc.exe lock --reason 'Test'"
Write-Host "  .\dist\BlockVisionPC\bvpc.exe defender --threat EICAR"
