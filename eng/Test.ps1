param([string]$Python = 'python', [string]$Node = 'node', [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    ./eng/Import-Protocol.ps1
    # Repeat the import in a scratch copy holding files an earlier import could have left.
    $check = Join-Path $repo ('artifacts/import-check-' + [guid]::NewGuid().ToString('N'))
    $pin = Get-Content protocol/protocol.lock.json -Raw | ConvertFrom-Json
    foreach ($path in @('eng/Import-Protocol.ps1', 'protocol/protocol.lock.json', "artifacts/$($pin.file_name)")) {
        $target = Join-Path $check $path
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $path -Destination $target
    }
    $stale = @('artifacts/protocol/proto/briosa/removed_operations.proto', 'artifacts/protocol-package/briosa-protocol-0.9.2-sa-2026.1.0529.7/manifest.json')
    foreach ($path in $stale) { New-Item -ItemType File -Path (Join-Path $check $path) -Force | Out-Null }
    & (Join-Path $check 'eng/Import-Protocol.ps1')
    foreach ($path in $stale) {
        if (Test-Path -LiteralPath (Join-Path $check $path)) { throw "Protocol import kept a stale file: $path" }
    }
    # An archive inside a folder the import clears must be refused before either folder is
    # cleared, also when its path runs through a link. A directory junction needs no elevation.
    $markers = @('artifacts/protocol/refused-import.marker', 'artifacts/protocol-package/refused-import.marker')
    foreach ($path in $markers) { New-Item -ItemType File -Path (Join-Path $check $path) | Out-Null }
    foreach ($name in 'protocol-package', 'protocol') {
        $inside = Join-Path $check "artifacts/$name/$($pin.file_name)"
        Copy-Item -LiteralPath "artifacts/$($pin.file_name)" -Destination $inside
        $alias = Join-Path $check "alias-$name"
        New-Item -ItemType Junction -Path $alias -Target (Split-Path $inside) | Out-Null
        $message = $null
        try { & (Join-Path $check 'eng/Import-Protocol.ps1') -ArchivePath (Join-Path $alias $pin.file_name) }
        catch { $message = $_.Exception.Message }
        finally { [IO.Directory]::Delete($alias) }
        if ($message -cne "Move the archive out of artifacts/$name; the import clears that folder.") {
            throw "Protocol import did not refuse an archive in artifacts/$name reached through a link: $message"
        }
        if (!(Test-Path -LiteralPath $inside)) { throw "Protocol import deleted its archive in artifacts/$name." }
    }
    foreach ($path in $markers) {
        if (!(Test-Path -LiteralPath (Join-Path $check $path))) { throw "A refused protocol import cleared $(Split-Path $path)." }
    }
    foreach ($project in @('point-inspection/grpc/PointInspection.Grpc.csproj', 'point-inspection/dotnet/PointInspection.csproj', 'tests/FakeServer/FakeServer.csproj')) {
        & $Dotnet restore $project --locked-mode
        if ($LASTEXITCODE) { throw "Restore failed: $project" }
        & $Dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE) { throw "Build failed: $project" }
    }
    Push-Location point-inspection/typescript
    try {
        $npmCli = Join-Path (Split-Path (Get-Command npm.cmd -ErrorAction Stop).Source -Parent) 'node_modules/npm/bin/npm-cli.js'
        & $Node $npmCli ci --ignore-scripts
        if ($LASTEXITCODE) { throw 'npm ci failed' }
        & $Node $npmCli run build
        if ($LASTEXITCODE) { throw 'TypeScript build failed' }
    } finally { Pop-Location }
    & $Python -m venv point-inspection/python/.venv
    if ($LASTEXITCODE) { throw 'venv creation failed' }
    $venvPython = Join-Path $repo 'point-inspection/python/.venv/Scripts/python.exe'
    & $venvPython -m pip install -r point-inspection/python/requirements.txt
    if ($LASTEXITCODE) { throw 'Python restore failed' }
    & $Python tests/verify_examples.py --dotnet $Dotnet --node $Node --python $venvPython
    if ($LASTEXITCODE) { throw 'Example verification failed' }
} finally { Pop-Location }
