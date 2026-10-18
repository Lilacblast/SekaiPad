<#
.SYNOPSIS
  Stage the official scrcpy 5.0 Android server into an unpacked SekaiPad preview.
.DESCRIPTION
  Never downloads or uploads anything. Refuses source trees, unknown filenames
  and overwriting previously staged binaries. Supply an expected SHA256 where possible.
.EXAMPLE
  .\tools\stage-scrcpy-server.ps1 -ServerFile 'C:\Downloads\scrcpy\scrcpy-server' -ReleaseDirectory 'C:\SekaiPad-preview\SekaiPad'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerFile,
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [string]$ExpectedSha256
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$source=(Resolve-Path -LiteralPath $ServerFile -ErrorAction Stop).Path
$release=(Resolve-Path -LiteralPath $ReleaseDirectory -ErrorAction Stop).Path
# The unpacked preview must be an ordinary directory outside a source tree.
# Detect both normal .git directories and linked-worktree .git files.
$walk=$release
while ($walk) {
    if (Test-Path -LiteralPath (Join-Path $walk '.git')) {
        throw 'Select a standalone extracted preview outside a Git source tree.'
    }
    $next=[System.IO.Path]::GetDirectoryName($walk.TrimEnd('\', '/'))
    if (!$next -or $next -eq $walk) { break }
    $walk=$next
}
if (((Get-Item -LiteralPath $release).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Release directory must be a direct directory, not a junction or symlink.'
}
if ([System.IO.Path]::GetFileName($source) -cne 'scrcpy-server') {
    throw 'Input must be the official scrcpy-server file (exact filename).'
}
foreach ($required in @('SekaiPad.exe','LICENSE','THIRD_PARTY_NOTICES.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $release $required) -PathType Leaf)) {
        throw "This is not an unpacked SekaiPad preview; missing $required"
    }
}
$size=(Get-Item -LiteralPath $source).Length
if ($size -lt 100KB -or $size -gt 100MB) {
    throw 'Unexpected scrcpy server size; verify the official upstream release.'
}
function Get-Sha256Hex([string]$path) {
    $stream=[System.IO.File]::OpenRead($path)
    try {
        $hasher=[System.Security.Cryptography.SHA256]::Create()
        try {
            return [System.BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        }
        finally { $hasher.Dispose() }
    }
    finally { $stream.Dispose() }
}
$sourceSha=Get-Sha256Hex $source
if ($ExpectedSha256) {
    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        $sourceSha -ine $ExpectedSha256) {
        throw 'Expected SHA256 did not match; refusing an unverified server.'
    }
} else {
    Write-Warning 'No expected SHA256 supplied: verify official release signature/checksum before use.'
}
$tools=Join-Path $release 'tools'
$dest=Join-Path $tools 'scrcpy-server'
if (Test-Path -LiteralPath $tools) {
    if (((Get-Item -LiteralPath $tools -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Preview tools directory must not be a junction or symlink.'
    }
}
else {
    New-Item -ItemType Directory -Path $tools -ErrorAction Stop | Out-Null
}
# CreateNew protects existing files against accidental overwrites/races.
$created=$false
try {
    $in=[System.IO.File]::Open($source,
        [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $out=[System.IO.File]::Open($dest,
            [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $created=$true
        try { $in.CopyTo($out); $out.Flush($true) }
        finally { $out.Dispose() }
    }
    finally { $in.Dispose() }

    $copySha=Get-Sha256Hex $dest
    if ($sourceSha -ne $copySha) {
        throw 'SHA256 mismatch after copying; incomplete copy removed.'
    }
}
catch {
    if ($created) { Remove-Item -LiteralPath $dest -ErrorAction SilentlyContinue }
    throw
}
Write-Host "LOCAL_SCRCPY_SERVER_STAGED=$dest"
Write-Host "SCRCPY_SERVER_SHA256=$copySha"
Write-Host 'No Git files, games, accounts, or network resources were modified.'
