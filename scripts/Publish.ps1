# Builds the portable release exactly like CI: one self-contained exe, zipped, plus its SHA256.
# Usage: .\scripts\Publish.ps1 -Version 3.0.1  -> artifacts\dist\QuickStartLauncher-win-x64.zip(.sha256)
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out = Join-Path $root 'artifacts\publish'; $dist = Join-Path $root 'artifacts\dist'
Remove-Item $out, $dist -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'QuickStartLauncher.csproj') -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$files = Get-ChildItem $out -File
if ($files.Count -ne 1 -or $files[0].Name -ne 'QuickStartLauncher.exe') { throw "Publish output must be exactly QuickStartLauncher.exe, got: $($files.Name -join ', ')" }
New-Item -ItemType Directory $dist | Out-Null
$zip = Join-Path $dist 'QuickStartLauncher-win-x64.zip'
Compress-Archive -Path $files[0].FullName -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  QuickStartLauncher-win-x64.zip" -Encoding ascii -NoNewline
Get-ChildItem $dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length / 1MB, 1)}}
