$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot | Split-Path -Parent
Push-Location (Join-Path $root 'web')
try {
    npm run dev
} finally {
    Pop-Location
}
