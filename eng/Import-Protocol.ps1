param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$pin = Get-Content (Join-Path $repo 'protocol/protocol.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (!$ArchivePath) {
    $ArchivePath = Join-Path $cache $pin.file_name
    if (!(Test-Path -LiteralPath $ArchivePath)) { Invoke-WebRequest $pin.url -OutFile $ArchivePath }
}
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
    throw 'Protocol archive hash mismatch.'
}
$extracted = Join-Path $cache 'protocol-package'
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extracted -Force
$package = Join-Path $extracted ([IO.Path]::GetFileNameWithoutExtension($pin.file_name))
$manifest = Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.briosa_version -ne $pin.version -or $manifest.source_revision -ne $pin.source_revision -or
    $manifest.spatial_analyzer_target -ne $pin.sa_target) { throw 'Protocol identity mismatch.' }
if ($manifest.schema_version -ne 3 -or $manifest.compatibility.major -ne 1 -or $manifest.compatibility.revision -lt 0) {
    throw 'A contract-aware protocol artifact is required.'
}
foreach ($entry in $manifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw "Protocol file mismatch: $($entry.path)"
    }
}
$canonicalCases = Join-Path $package 'compatibility/selection-cases.json'
$exampleCases = Join-Path $repo 'tests/Bootstrap/Fixtures/selection-cases.json'
if ((Get-FileHash $canonicalCases -Algorithm SHA256).Hash -ne (Get-FileHash $exampleCases -Algorithm SHA256).Hash) {
    throw 'Raw bootstrap vectors differ from the pinned protocol artifact.'
}
$destination = Join-Path $cache 'protocol'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'proto') -Destination $destination -Recurse -Force
Write-Host 'Verified protocol imported. Generated C# remains build output.'
