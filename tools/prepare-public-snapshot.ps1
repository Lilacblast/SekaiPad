<#
.SYNOPSIS
  Export an explicit, allowlisted, source-only review tree.
.DESCRIPTION
  Exports the reviewed source-file allowlist with canonical LF text and a
  SHA256 publication manifest. Add new source paths to the allowlist after review.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\','/')
$out=[System.IO.Path]::GetFullPath($OutDirectory)
if (Test-Path -LiteralPath $out) { throw "Output already exists: $out" }
if ($out.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or
    $out.Equals($root,[StringComparison]::OrdinalIgnoreCase)) {
  throw 'Public snapshots must live outside the private Git source tree.'
}
$dirty=@(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) {throw 'Git is unavailable'}
if ($dirty.Count -gt 0) {throw 'Working tree is dirty: commit and review before exporting.'}
& python (Join-Path $root 'tools/audit-publication.py') --repo $root --strict
if ($LASTEXITCODE -ne 0) {throw 'Private history audit blocked; review findings first.'}

$listPath=Join-Path $root 'tools/public-source-allowlist.txt'
if (!(Test-Path -LiteralPath $listPath)) {
  throw 'Missing explicit reviewed source allowlist.'
}
$rules=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$chosen=[System.Collections.Generic.List[string]]::new()
$blocked='(?i)(^|/)(runtime|downloads|screenshots|captures|recordings|secrets|bin|obj|\.git)(/|$)|\.(apk|apks|aab|xapk|obb|iso|vdi|vmdk|qcow2|img|exe|dll|pdb|jar|zip|7z|png|jpe?g|webp|mp4|log|dmp|pcap|har)$'
foreach ($raw in (Get-Content -LiteralPath $listPath -Encoding UTF8)) {
  $rel=$raw.Trim()
  if (!$rel -or $rel.StartsWith('#')) {continue}
  if ([IO.Path]::IsPathRooted($rel) -or $rel.Contains('\') -or
      $rel.Contains(':') -or $rel -match '(^|/)(\.|\.\.)(/|$)' -or
      $rel.StartsWith('/') -or $rel.EndsWith('/') -or $rel -match $blocked) {
    throw "Unsafe public-source allowlist entry: $rel"
  }
  if (!$rules.Add($rel)) {throw "Duplicate source allowlist entry: $rel"}
  $chosen.Add($rel)
}
foreach ($mandatory in @('LICENSE','README.md','THIRD_PARTY_NOTICES.md',
                         'NuGet.Config','tools/audit-publication.py',
                         'tools/audit-public-snapshot.py',
                         'tools/public-source-allowlist.txt','tools/package-alpha.ps1',
                         'tools/verify-alpha-deps.ps1',
                         'tools/audit-alpha-zip.py',
                         'tools/audit-third-party-provenance.py',
                         'src/SekaiPad/SekaiPad.csproj',
                         'src/SekaiPad.CursorGuard/SekaiPad.CursorGuard.csproj',
                         'licenses/Apache-2.0','licenses/RawInput.Touchpad.MIT',
                         'licenses/Google.Protobuf.BSD-3-Clause',
                         'licenses/Microsoft.DotNet.MIT',
                         'licenses/Grpc.DotNet.LICENSE.txt',
                         'licenses/Grpc.Tools.LICENSE.txt',
                         'licenses/Grpc.Tools.NOTICE.txt',
                          'licenses/Microsoft.Extensions.Logging.Abstractions.LICENSE.txt',
                          'licenses/Microsoft.Extensions.Logging.Abstractions.THIRD-PARTY-NOTICES.txt')) {
  if (!$rules.Contains($mandatory)) { throw "Missing required allowlist entry: $mandatory" }
}
$tracked=@(& git -C $root -c core.quotePath=false ls-files)
if ($LASTEXITCODE -ne 0) {throw 'Could not read tracked Git paths'}
$trackedSet=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($file in $tracked) { [void]$trackedSet.Add($file.Replace('\','/')) }
foreach($rel in $chosen) {
  if (!$trackedSet.Contains($rel)) {throw "Untracked or missing reviewed source: $rel"}
  $source=Join-Path $root ($rel.Replace('/','\'))
  $item=Get-Item -LiteralPath $source -Force -ErrorAction Stop
  if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Directory/reparse-point cannot be published: $rel"
  }
  if ($item.Length -gt 2MB) {throw "Source exceeds 2 MiB single-file ceiling: $rel"}
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$manifest=[System.Collections.Generic.List[string]]::new()
$utf8=[System.Text.UTF8Encoding]::new($false,$true)
foreach($rel in $chosen) {
  $src=Join-Path $root ($rel.Replace('/','\'))
  $dest=Join-Path $out ($rel.Replace('/','\'))
  $parent=Split-Path -Parent $dest
  New-Item -ItemType Directory -Force -Path $parent | Out-Null
  # Git normalizes mixed LF/CRLF source files while checking out on Windows.
  # Compute the *public* hash from stable, strict UTF-8/LF bytes only.
  $raw=[System.IO.File]::ReadAllBytes($src)
  $text=$utf8.GetString($raw)
  if ($text.IndexOf([char]0) -ge 0) {throw "Binary/NUL bytes in allowlisted source: $rel"}
  $canonical=$utf8.GetBytes($text.Replace("`r`n","`n").Replace("`r","`n"))
  [System.IO.File]::WriteAllBytes($dest,$canonical)
  $sha=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant()
  $manifest.Add("$sha  $rel")
}
$manifestPath=Join-Path $out 'PUBLICATION_MANIFEST.sha256'
[IO.File]::WriteAllLines($manifestPath,$manifest,(New-Object System.Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath (Join-Path $out '.git')) {
  throw 'Git history cannot be exported in the public source tree.'
}
& python (Join-Path $out 'tools/audit-public-snapshot.py') --root $out --strict
if ($LASTEXITCODE -ne 0) { throw 'Exact source snapshot audit failed; review output.' }
& python (Join-Path $out 'tools/audit-third-party-provenance.py') --root $out
if ($LASTEXITCODE -ne 0) { throw 'Pinned upstream copyrights or source revision changed.' }
Write-Host "PUBLIC_SNAPSHOT_REVIEW_DIR=$out"
Write-Host "PUBLIC_SNAPSHOT_FILE_COUNT=$($chosen.Count)"
Write-Host 'SOURCE_EXPORT_READY'
