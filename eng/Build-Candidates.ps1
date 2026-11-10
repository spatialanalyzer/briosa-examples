# Candidate phase only. Until Server 0.10.0 and the 0.5.0 clients are published, the
# tutorials pin the contract-major-3 protocol checkpoint and the client candidates of
# spatialanalyzer/briosa#363. This script builds them from their pinned commits into
# artifacts/ (git-ignored): the protocol archive the way the client CI rebuilds it, and
# each package the way briosa's retained-client gate packs a source candidate.
#
# -Dotnet needs the SDK that briosa-dotnet's global.json pins (10.0.302), and npm.cmd on
# PATH must belong to -Node, as in Test.ps1. NuGet pack output is not byte-reproducible,
# so after a rebuild re-lock the .NET tutorial once:
#   dotnet restore point-inspection/dotnet/PointInspection.csproj --force-evaluate
param(
    [string]$Dotnet = 'dotnet', [string]$Node = 'node', [string]$Python = 'python', [string]$Buf = 'buf',
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'briosa-examples-candidates')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = '2026.1.0529.7'
$pin = Get-Content (Join-Path $repo 'protocol/protocol.lock.json') -Raw | ConvertFrom-Json
$clients = [ordered]@{
    'briosa-dotnet' = '3823369030dc0a286d698a4795b9442b4c7f9bf1' # spatialanalyzer/briosa-dotnet#46
    'briosa-js' = 'feda30f3bd0bdae8cf9b02ae307146de259e2167'     # spatialanalyzer/briosa-js#46
    'briosa-py' = '6a04ff934d554be0767bea997f9517676674b8a4'     # spatialanalyzer/briosa-py#46
}
$output = Join-Path $repo 'artifacts/candidates'

function Get-Source([string]$Name, [string]$Revision) {
    # A clean clone outside this repository, so no examples configuration applies to its build.
    $path = Join-Path $WorkDirectory $Name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    git init --quiet $path
    if ($LASTEXITCODE) { throw "Clone failed: $Name" }
    git -C $path config core.longpaths true
    git -C $path fetch --quiet --depth 1 "https://github.com/spatialanalyzer/$Name.git" $Revision
    if ($LASTEXITCODE) { throw "Fetch failed: $Name $Revision" }
    git -C $path -c advice.detachedHead=false checkout --quiet --detach FETCH_HEAD
    # Checkout can exit 0 after failing to write a file, so also require a clean tree.
    if ($LASTEXITCODE -or (git -C $path rev-parse HEAD) -ne $Revision -or (git -C $path status --porcelain)) {
        throw "Checkout failed: $Name $Revision"
    }
    Join-Path $path "targets/$target"
}

# Replace earlier candidates, including the extracted .NET package, which NuGet would reuse.
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $output 'nuget'), (Join-Path $output 'npm'), (Join-Path $output 'python') -Force | Out-Null
$npmCli = Join-Path (Split-Path (Get-Command npm.cmd -ErrorAction Stop).Source -Parent) 'node_modules/npm/bin/npm-cli.js'

$briosa = Get-Source 'briosa' $pin.source_revision
& (Join-Path $briosa 'eng/New-ProtocolArtifact.ps1') -Version $pin.version -SourceRevision $pin.source_revision `
    -OutputDirectory (Join-Path $WorkDirectory 'protocol') -BufPath $Buf
Copy-Item -LiteralPath (Join-Path $WorkDirectory "protocol/$($pin.file_name)") -Destination (Join-Path $repo 'artifacts') -Force

Push-Location (Get-Source 'briosa-dotnet' $clients['briosa-dotnet'])
try {
    & $Dotnet restore Briosa.DotNet.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Candidate restore failed: briosa-dotnet' }
    & $Dotnet pack src/Briosa.Client/Briosa.Client.csproj -c Release --no-restore -o artifacts/package
    if ($LASTEXITCODE) { throw 'Candidate pack failed: briosa-dotnet' }
    Copy-Item artifacts/package/*.nupkg (Join-Path $output 'nuget')
} finally { Pop-Location }

Push-Location (Get-Source 'briosa-js' $clients['briosa-js'])
try {
    & $Node $npmCli ci --ignore-scripts
    if ($LASTEXITCODE) { throw 'Candidate tooling install failed: briosa-js' }
    & $Node $npmCli run build
    if ($LASTEXITCODE) { throw 'Candidate build failed: briosa-js' }
    & $Node $npmCli pack --pack-destination (Join-Path $output 'npm')
    if ($LASTEXITCODE) { throw 'Candidate pack failed: briosa-js' }
} finally { Pop-Location }

$venv = Join-Path $WorkDirectory 'build-venv'
& $Python -m venv --clear $venv
if ($LASTEXITCODE) { throw 'venv creation failed' }
$venvPython = Join-Path $venv 'Scripts/python.exe'
& $venvPython -m pip install --disable-pip-version-check build==1.2.2.post1
if ($LASTEXITCODE) { throw 'Candidate tooling install failed: briosa-py' }
Push-Location (Get-Source 'briosa-py' $clients['briosa-py'])
try {
    & $venvPython -m build
    if ($LASTEXITCODE) { throw 'Candidate build failed: briosa-py' }
    Copy-Item dist/*.whl (Join-Path $output 'python')
} finally { Pop-Location }

$built = @(Get-Item -LiteralPath (Join-Path $repo "artifacts/$($pin.file_name)")) + @(Get-ChildItem $output -File -Recurse)
foreach ($file in $built) {
    Write-Host ('{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name)
}
