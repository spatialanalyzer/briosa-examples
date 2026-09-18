param([string]$Python = 'python', [string]$Node = 'node', [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    ./eng/Import-Protocol.ps1
    foreach ($project in @('point-inspection/grpc/PointInspection.Grpc.csproj', 'point-inspection/dotnet/PointInspection.csproj', 'tests/FakeServer/FakeServer.csproj')) {
        & $Dotnet restore $project --locked-mode
        if ($LASTEXITCODE) { throw "Restore failed: $project" }
        & $Dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE) { throw "Build failed: $project" }
    }
    Push-Location point-inspection/typescript
    try {
        npm.cmd ci --ignore-scripts
        if ($LASTEXITCODE) { throw 'npm ci failed' }
        npm.cmd run build
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
