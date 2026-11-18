# Script de empaquetado y generación del instalador oficial de Windows para myLocalCloud v1.0
# Desarrollada por android.ar — Desarrollador: Alejandro Ferrer

$ErrorActionPreference = "Stop"
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishAppDir = Join-Path $repoRoot "artifacts\publish-app"
$payloadDir = Join-Path $repoRoot "installer\MyLocalCloud.Installer\Payload"
$payloadZip = Join-Path $payloadDir "app_payload.zip"
$distDir = Join-Path $repoRoot "dist"

Write-Host "==> [1/4] Publicando aplicación principal myLocalCloud v1.0 (win-x64)..." -ForegroundColor Cyan
if (Test-Path $publishAppDir) { Remove-Item -Recurse -Force $publishAppDir }
dotnet publish "$repoRoot\src\MyLocalCloud.UI\MyLocalCloud.UI.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    --output $publishAppDir

Write-Host "==> [2/4] Empaquetando payload cifrado para el instalador..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
Compress-Archive -Path "$publishAppDir\*" -DestinationPath $payloadZip -CompressionLevel Optimal

Write-Host "==> [3/4] Compilando instalador nativo de Windows (myLocalCloud-Setup-v1.0.exe)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
dotnet publish "$repoRoot\installer\MyLocalCloud.Installer\MyLocalCloud.Installer.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    --output $distDir

# Copiar también el ejecutable portable junto al instalador
Copy-Item -Force "$publishAppDir\myLocalCloud.exe" "$distDir\myLocalCloud-Portable-v1.0.exe"

Write-Host "==> [4/4] ¡Instalador generado exitosamente en: $distDir\myLocalCloud-Setup-v1.0.exe!" -ForegroundColor Green
