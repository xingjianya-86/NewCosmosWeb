param(
    [switch]$SkipWeb
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot | Split-Path -Parent

Push-Location $root
try {
    dotnet build NewCosmosWeb.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed' }

    dotnet test tests\NewCosmos.Core.Tests\NewCosmos.Core.Tests.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }

    if (-not $SkipWeb) {
        Push-Location web
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
        } finally { Pop-Location }
    }
} finally {
    Pop-Location
}

Write-Host 'Build OK'
