# Until Server 0.10.0 is released, protocol/protocol.lock.json pins the contract-major-3
# checkpoint of spatialanalyzer/briosa#363, which has no download URL. This script builds
# that archive from its pinned source commit into artifacts/ (git-ignored), the way the
# client CI rebuilds the checkpoint. eng/Import-Protocol.ps1 then verifies its SHA-256.
# It needs Git and buf 1.72.0. Delete it when the lock pins the release archive.
param(
    [string]$Buf = 'buf',
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'briosa-examples-checkpoint')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$pin = Get-Content (Join-Path $repo 'protocol/protocol.lock.json') -Raw | ConvertFrom-Json

# A clean clone outside this repository, so no examples configuration applies to it.
$source = Join-Path $WorkDirectory 'briosa'
if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source -Recurse -Force }
git init --quiet $source
if ($LASTEXITCODE) { throw 'Clone failed: briosa' }
git -C $source config core.longpaths true
git -C $source fetch --quiet --depth 1 https://github.com/spatialanalyzer/briosa.git $pin.source_revision
if ($LASTEXITCODE) { throw "Fetch failed: briosa $($pin.source_revision)" }
git -C $source -c advice.detachedHead=false checkout --quiet --detach FETCH_HEAD
# Checkout can exit 0 after failing to write a file, so also require a clean tree.
if ($LASTEXITCODE -or (git -C $source rev-parse HEAD) -ne $pin.source_revision -or (git -C $source status --porcelain)) {
    throw "Checkout failed: briosa $($pin.source_revision)"
}

$output = Join-Path $WorkDirectory 'protocol'
& (Join-Path $source "targets/$($pin.sa_target)/eng/New-ProtocolArtifact.ps1") -Version $pin.version `
    -SourceRevision $pin.source_revision -OutputDirectory $output -BufPath $Buf
New-Item -ItemType Directory -Path (Join-Path $repo 'artifacts') -Force | Out-Null
$archive = Join-Path $repo "artifacts/$($pin.file_name)"
Copy-Item -LiteralPath (Join-Path $output $pin.file_name) -Destination $archive -Force
Write-Host ('{0}  {1}' -f (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(), $pin.file_name)
