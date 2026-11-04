param([string]$Tag = 'hermes-trip:dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet publish src\Trip\Trip.csproj -c Release --no-self-contained `
        -p:UseAppHost=false -o .build\publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'C# publish failed.' }
    docker build --quiet -t $Tag .
    if ($LASTEXITCODE -ne 0) { throw 'Container build failed.' }
    docker run --rm $Tag --check-config
    if ($LASTEXITCODE -ne 0) { throw 'Hermes configuration verification failed.' }
} finally {
    Pop-Location
}
