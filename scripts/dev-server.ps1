param(
    [string]$DbHost = '10.110.0.2',
    [int]$DbPort = 5432,
    [string]$DbName = 'new_cosmos_test',
    [string]$DbUser = 'new_cosmos',
    [string]$WebAddr = '127.0.0.1:8787'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot | Split-Path -Parent

$env:NC_DB_HOST = $DbHost
$env:NC_DB_PORT = $DbPort
$env:NC_DB_NAME = $DbName
$env:NC_DB_USER = $DbUser
if (-not $env:NC_DB_PASS) {
    throw '请先设置环境变量 NC_DB_PASS（不要把密码写进脚本）'
}
$env:ASPNETCORE_URLS = "http://$WebAddr"
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Push-Location $root
try {
    dotnet run --project src\NewCosmos.Server\NewCosmos.Server.csproj
} finally {
    Pop-Location
}
