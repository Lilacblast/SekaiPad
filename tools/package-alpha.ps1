<#
.SYNOPSIS
  Create an UNPUBLISHED, framework-dependent Windows x64 alpha preview zip.
.DESCRIPTION
  No Project SEKAI game resources, emulator images, scrcpy binaries,
  private runtimes, user data or CI artifact uploads.
#>
[CmdletBinding()]
param(
  [string]$Version='0.1.0-alpha',
  [string]$OutputDirectory='dist'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+-(alpha|beta)(\.[0-9A-Za-z.-]+)?$') {
  throw 'Stable packaging is disabled; use 0.1.0-alpha.'
}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet=$null
# Prefer the explicitly selected SDK over a PATH runtime-only dotnet host.
if ($env:DOTNET_ROOT) {
  $candidate=Join-Path $env:DOTNET_ROOT 'dotnet.exe'
  if (Test-Path $candidate) { $dotnet=$candidate }
}
if (-not $dotnet) {
  $command=Get-Command dotnet -ErrorAction SilentlyContinue
  if ($command) { $dotnet=$command.Source }
}
if (-not $dotnet) { throw '.NET SDK not found in PATH or DOTNET_ROOT.' }
$sdkList=@(& $dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or $sdkList.Count -eq 0 -or
    @($sdkList | Where-Object { $_ -match '^10\.0\.' }).Count -eq 0) {
  throw 'A supported .NET 10 SDK is required. Set DOTNET_ROOT to a .NET 10 SDK installation.'
}
$destRoot=[System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
$out=Join-Path $destRoot ("SekaiPad-"+$Version+"-win-x64.zip")
if (Test-Path $out) {throw "Archive already exists: $out"}
$temp=Join-Path $destRoot ('.sekaipad-build-'+[guid]::NewGuid().ToString('N'))
$stage=Join-Path $temp 'SekaiPad'
$guard=Join-Path $temp 'guard'
New-Item -ItemType Directory -Path $stage,$guard -Force | Out-Null
try {
  & $dotnet publish (Join-Path $root 'src/SekaiPad/SekaiPad.csproj') -c Release -r win-x64 --self-contained false -p:DebugType=none -p:DebugSymbols=false -o $stage
  if($LASTEXITCODE -ne 0){throw 'App publish failed'}
  & $dotnet publish (Join-Path $root 'src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj') -c Release -r win-x64 --self-contained false -p:DebugType=none -p:DebugSymbols=false -o $guard
  if($LASTEXITCODE -ne 0){throw 'CursorGuard publish failed'}
  foreach($file in @('SekaiPad.CursorGuard.exe','SekaiPad.CursorGuard.dll','SekaiPad.CursorGuard.deps.json','SekaiPad.CursorGuard.runtimeconfig.json')) {
    $source=Join-Path $guard $file
    if(!(Test-Path $source)) {throw "Missing safety-guard executable dependency $file"}
    Copy-Item $source $stage -Force
  }
  foreach($file in @('LICENSE','README.md','THIRD_PARTY_NOTICES.md','THIRD_PARTY_LICENSES.md')) {
    $source=Join-Path $root $file
    if(Test-Path $source) {Copy-Item $source $stage -Force}
  }
  $licenseRoot=Join-Path $stage 'licenses'
  New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
  foreach($licenseFile in @('RawInput.Touchpad.MIT','Apache-2.0',
                              'Google.Protobuf.BSD-3-Clause','Microsoft.DotNet.MIT',
                              'Grpc.DotNet.LICENSE.txt','Grpc.Tools.LICENSE.txt',
                              'Grpc.Tools.NOTICE.txt',
                               'Microsoft.Extensions.Logging.Abstractions.LICENSE.txt',
                               'Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt')) {
    $src=Join-Path $root ('licenses/'+$licenseFile)
    if(!(Test-Path -LiteralPath $src)) {throw "Missing mandatory third-party license: $licenseFile"}
    Copy-Item -LiteralPath $src -Destination $licenseRoot -Force
  }
  foreach($file in @('docs/SAFETY.md','docs/BUILDING.md')) {
    Copy-Item (Join-Path $root $file) (Join-Path $stage ([System.IO.Path]::GetFileName($file)))
  }
  # Users must obtain the upstream server separately; include only the safe
  # LOCAL copy helper, never the copyrighted server binary itself.
  $stagingTools=Join-Path $stage 'tools'
  New-Item -ItemType Directory -Path $stagingTools -Force | Out-Null
  Copy-Item (Join-Path $root 'tools/stage-scrcpy-server.ps1') $stagingTools
  # Do not ship debug symbols that may embed private developer file paths.
  Get-ChildItem $stage -Recurse -File -Filter '*.pdb' | Remove-Item -Force
  $blocked=@(Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -eq 'scrcpy-server' -or
    $_.Name -match '(?i)\.(apk|apks|xapk|aab|obb|vdi|qcow2|iso|img|pcap|har)$'
  })
  if ($blocked.Count -gt 0) {throw 'Forbidden third-party binaries/data in release stage.'}
  foreach($file in @('SekaiPad.exe','SekaiPad.dll','SekaiPad.CursorGuard.exe',
                     'LICENSE','THIRD_PARTY_NOTICES.md',
                     'licenses/RawInput.Touchpad.MIT','licenses/Apache-2.0',
                     'licenses/Google.Protobuf.BSD-3-Clause','licenses/Microsoft.DotNet.MIT',
                      'licenses/Microsoft.Extensions.Logging.Abstractions.LICENSE.txt',
                      'licenses/Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt')) {
    if(!(Test-Path (Join-Path $stage $file))) {throw "Required artifact missing: $file"}
  }
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'tools/verify-alpha-deps.ps1') -StageDirectory $stage
  if($LASTEXITCODE -ne 0){throw 'Alpha package dependency/license inventory gate failed'}
  Compress-Archive -Path $stage -DestinationPath $out -CompressionLevel Optimal
  Write-Host "ALPHA_PREVIEW_ZIP=$out"
  Write-Host "ZIP_SHA256=$((Get-FileHash $out -Algorithm SHA256).Hash)"
  Write-Host 'UNPUBLISHED. Complete all release gates before sharing the binary.'
}
finally {
  if(Test-Path $temp) {Remove-Item $temp -Recurse -Force}
}
