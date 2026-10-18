<#
.SYNOPSIS
  Fail-closed inventory check for compiled Alpha preview dependencies.
.DESCRIPTION
  Inspect the framework-dependent app .deps.json and staged DLLs.
  Any new package, version or DLL needs a license review before release.
  This is an offline check and never contacts GitHub or NuGet.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$StageDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$stage=(Resolve-Path -LiteralPath $StageDirectory -ErrorAction Stop).Path
$depsFile=Join-Path $stage 'SekaiPad.deps.json'
if (!(Test-Path -LiteralPath $depsFile -PathType Leaf)) {
    throw 'Missing SekaiPad.deps.json in alpha package stage.'
}
$deps=Get-Content -LiteralPath $depsFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$deps.libraries) {throw 'Alpha package has no dependency inventory.'}

# All known packages are audited by exact name+version, not broad prefixes.
$approvedPackages=@{
    'Google.Protobuf/3.29.3'='Google.Protobuf.BSD-3-Clause'
    'Grpc.Core.Api/2.66.0'='Grpc.DotNet.LICENSE.txt'
    'Grpc.Net.Client/2.66.0'='Grpc.DotNet.LICENSE.txt'
    'Grpc.Net.Common/2.66.0'='Grpc.DotNet.LICENSE.txt'
    'Grpc.Tools/2.66.0'='Grpc.Tools.LICENSE.txt'
    'Microsoft.Extensions.Logging.Abstractions/6.0.0'='Microsoft.DotNet.MIT'
}
$foundPackages=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($lib in $deps.libraries.PSObject.Properties) {
    if ($lib.Value.type -eq 'project') {continue}
    if ($lib.Value.type -ne 'package') {
        throw "Unknown dependency type $($lib.Value.type): $($lib.Name)"
    }
    if (-not $approvedPackages.ContainsKey($lib.Name)) {
        throw "Unreviewed NuGet dependency/version in alpha release: $($lib.Name)"
    }
    [void]$foundPackages.Add($lib.Name)
    $license=Join-Path $stage ('licenses/'+$approvedPackages[$lib.Name])
    if (!(Test-Path -LiteralPath $license -PathType Leaf) -or
        (Get-Item -LiteralPath $license).Length -lt 800) {
        throw "Full third-party license missing for package $($lib.Name)"
    }
}
foreach($name in $approvedPackages.Keys) {
    # Grpc.Tools is a compile-time generator and is intentionally omitted
    # from the published .NET 10 runtime dependency graph. It remains in
    # the allowlist only if a TFM/SDK includes it in .deps.json; every
    # runtime dependency and assembly must still match the pinned inventory.
    if ($name -eq 'Grpc.Tools/2.66.0') { continue }
    if (-not $foundPackages.Contains($name)) {
        throw "Declared runtime dependency changed without review: $name"
    }
}
$approvedDlls=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($file in @('SekaiPad.dll','SekaiPad.CursorGuard.dll',
                   'Google.Protobuf.dll','Grpc.Core.Api.dll','Grpc.Net.Client.dll',
                   'Grpc.Net.Common.dll','Microsoft.Extensions.Logging.Abstractions.dll')) {
    [void]$approvedDlls.Add($file)
}
$allDlls=@(Get-ChildItem -LiteralPath $stage -Filter '*.dll' -File -Recurse)
foreach($file in $allDlls) {
    # Resolve-Path may return an 8.3 short parent (e.g. LILACB~1), while
    # Get-ChildItem returns its long name. Normalize before testing nesting.
    if ([IO.Path]::GetFullPath($file.DirectoryName) -ne [IO.Path]::GetFullPath($stage)) {
        throw "Unexpected nested DLL distribution: $($file.Name)"
    }
    if (-not $approvedDlls.Contains($file.Name)) {
        throw "Unreviewed compiled assembly bundled into alpha: $($file.Name)"
    }
}
foreach($name in $approvedDlls) {
    if (!(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) {
        throw "Expected reviewed DLL absent from package: $name"
    }
}
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md',
                   'licenses/RawInput.Touchpad.MIT','licenses/Apache-2.0',
                   'licenses/Google.Protobuf.BSD-3-Clause','licenses/Microsoft.DotNet.MIT',
                   'licenses/Grpc.DotNet.LICENSE.txt','licenses/Grpc.Tools.LICENSE.txt',
                   'licenses/Grpc.Tools.NOTICE.txt',
                    'licenses/Microsoft.Extensions.Logging.Abstractions.LICENSE.txt',
                    'licenses/Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) {
        throw "Mandatory legal notice absent: $name"
    }
}
$expectedNugetNotices=@{
    'Grpc.DotNet.LICENSE.txt'=@('cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30')
    'Grpc.Tools.LICENSE.txt'=@('590198e3f305f2c347fde64d637c65492bbef554db6c8364e149cd375e3797ee')
    'Grpc.Tools.NOTICE.txt'=@('693ff28ec216d5112ac1bbfe64ef539005867d1c7bd427b57d579683293b947f')
    'Microsoft.Extensions.Logging.Abstractions.LICENSE.txt'=@(
        'd7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b',
        'cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310')
    'Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt'=@(
        'a6344bfb229bc08d1071cec400eea216e307439f961c5943ec1b56d2245acb95',
        'df255d595f29db06c6d462ceb7c04b33d627e98ac2f1745e1f1fb9f08eaaecb0')
}
# Windows Server CI can lack Get-FileHash in a child Windows PowerShell
# process even though basic filesystem cmdlets are available. Hash directly
# with the built-in .NET crypto implementation; any read failure is fatal.
function Get-Sha256Hex([string]$filePath) {
    $stream=[System.IO.File]::OpenRead($filePath)
    try {
        $sha=[System.Security.Cryptography.SHA256]::Create()
        try {
            return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}
foreach($entry in $expectedNugetNotices.Keys) {
    $actual=Get-Sha256Hex (Join-Path $stage ('licenses/'+$entry))
    if ($actual -notin $expectedNugetNotices[$entry]) {
        throw "Unreviewed NuGet license or NOTICE revision: $entry"
    }
}
Write-Host "ALPHA_APPROVED_NUGET_DEPENDENCIES=$($foundPackages.Count)"
Write-Host "ALPHA_APPROVED_DLLS=$($allDlls.Count)"
Write-Host 'ALPHA_LICENSE_AND_DLL_INVENTORY_PASS'
