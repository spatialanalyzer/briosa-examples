param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$pin = Get-Content (Join-Path $repo 'protocol/protocol.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$cache = [IO.Path]::GetFullPath($cache)

# Each import starts from empty folders, so no file from an earlier import survives.
# Only these two folders directly under artifacts/ are ever cleared, and never through a link.
function Reset-ImportFolder([ValidateSet('protocol', 'protocol-package')][string]$Name) {
    $path = [IO.Path]::GetFullPath((Join-Path $cache $Name))
    if ([IO.Path]::GetDirectoryName($path) -ne $cache -or [IO.Path]::GetFileName($path) -ne $Name) {
        throw "Refusing to clear ${path}: it is not artifacts/$Name."
    }
    foreach ($folder in @($cache, $path)) {
        if ((Test-Path -LiteralPath $folder) -and
            ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to clear ${path}: $folder is a link."
        }
    }
    if (Test-Path -LiteralPath $path) {
        if (!(Test-Path -LiteralPath $path -PathType Container)) { throw "Refusing to clear ${path}: it is not a folder." }
        # Directory.Delete removes a link inside the folder without following it.
        [IO.Directory]::Delete($path, $true)
    }
    New-Item -ItemType Directory -Path $path | Out-Null
    $path
}

if (!$ArchivePath) {
    $ArchivePath = Join-Path $cache $pin.file_name
    if (!(Test-Path -LiteralPath $ArchivePath)) {
        if (!$pin.url) { throw "Protocol $($pin.version) has no download URL. Place the archive at $ArchivePath or pass -ArchivePath." }
        Invoke-WebRequest $pin.url -OutFile $ArchivePath
    }
}
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).ProviderPath
foreach ($name in 'protocol', 'protocol-package') {
    if ($ArchivePath.StartsWith((Join-Path $cache $name) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Move the archive out of artifacts/$name; the import clears that folder."
    }
}
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
    throw 'Protocol archive hash mismatch.'
}
$extracted = Reset-ImportFolder 'protocol-package'
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extracted
$package = Join-Path $extracted ([IO.Path]::GetFileNameWithoutExtension($pin.file_name))
$manifest = Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.briosa_version -ne $pin.version -or $manifest.source_revision -ne $pin.source_revision -or
    $manifest.spatial_analyzer_target -ne $pin.sa_target) { throw 'Protocol identity mismatch.' }
if ($manifest.schema_version -ne 3 -or $manifest.compatibility.major -ne 3 -or $manifest.compatibility.revision -lt 0) {
    throw 'A contract-aware protocol artifact is required.'
}
foreach ($entry in $manifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw "Protocol file mismatch: $($entry.path)"
    }
}
$destination = Reset-ImportFolder 'protocol'
Copy-Item -LiteralPath (Join-Path $package 'proto') -Destination $destination -Recurse
# The builds read artifacts/protocol, so it must hold exactly the manifest's proto files.
$imported = @(Get-ChildItem -LiteralPath $destination -Recurse -File -Force |
    ForEach-Object { $_.FullName.Substring($destination.Length + 1).Replace('\', '/') } | Sort-Object)
$expected = @($manifest.files.path | Where-Object { $_.StartsWith('proto/') } | Sort-Object)
if (($imported -join "`n") -cne ($expected -join "`n")) { throw 'Imported protocol files differ from the archive manifest.' }
Write-Host 'Verified protocol imported. Generated C# remains build output.'
