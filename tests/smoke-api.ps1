param(
    [string]$BaseUrl = 'http://127.0.0.1:8787',
    [Parameter(Mandatory = $true)][string]$Username,
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$AgentRegisterToken = $env:NC_AGENT_REGISTER_TOKEN
)

$ErrorActionPreference = 'Stop'
$deviceId = "smoke-" + [guid]::NewGuid().ToString('N')
$session = $null

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [hashtable]$Headers = @{}
    )
    $params = @{
        Method = $Method
        Uri = "$BaseUrl$Path"
        Headers = $Headers
        ContentType = 'application/json; charset=utf-8'
    }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress) }
    Invoke-RestMethod @params
}

Write-Host '1) health'
Invoke-Json -Method GET -Path '/health' | ConvertTo-Json -Compress

Write-Host '2) login'
$login = Invoke-Json -Method POST -Path '/api/auth/login' -Body @{
    username = $Username
    password = $Password
    deviceId = $deviceId
    deviceKind = 'web'
    deviceName = 'smoke-test'
    appVersion = '0.1.0'
}
$session = $login.sessionId
Write-Host ("   session={0} user={1}" -f $session, $login.user.username)
$auth = @{ Authorization = "Bearer $session"; 'X-NC-Device' = $deviceId }

Write-Host '3) me'
Invoke-Json -Method GET -Path '/api/auth/me' -Headers $auth | ConvertTo-Json -Compress

Write-Host '4) printers (before bind)'
Invoke-Json -Method GET -Path '/api/printers' -Headers $auth | ConvertTo-Json -Compress

if ($AgentRegisterToken) {
    Write-Host '5) agent register'
    $agent = Invoke-Json -Method POST -Path '/api/agent/register' -Headers @{
        'X-NC-Agent-Register' = $AgentRegisterToken
    } -Body @{
        machineName = "SMOKE-$env:COMPUTERNAME"
        agentVersion = '0.1.0'
        printers = @(
            @{ name = 'Smoke Printer A'; isDefault = $true },
            @{ name = 'Smoke Printer B'; isDefault = $false }
        )
    }
    Write-Host ("   deviceId={0}" -f $agent.deviceId)

    Write-Host '6) agent heartbeat'
    Invoke-WebRequest -Method POST -Uri "$BaseUrl/api/agent/heartbeat" -ContentType 'application/json' `
        -Headers @{ 'X-NC-Device-Key' = $agent.deviceKey } `
        -Body (@{ agentVersion = '0.1.0'; printers = @(@{ name = 'Smoke Printer A'; isDefault = $true }) } | ConvertTo-Json -Depth 6) | Out-Null

    Write-Host '7) bind printer'
    $binding = Invoke-Json -Method POST -Path '/api/printers/bind' -Headers $auth -Body @{
        deviceKey = $agent.deviceKey
        printerName = 'Smoke Printer A'
        isDefault = $true
    }
    Write-Host ("   bindingId={0}" -f $binding.id)

    Write-Host '8) printers (after bind)'
    Invoke-Json -Method GET -Path '/api/printers' -Headers $auth | ConvertTo-Json -Compress

    Write-Host '9) unbind'
    Invoke-WebRequest -Method DELETE -Uri "$BaseUrl/api/printers/$($binding.id)" -Headers $auth | Out-Null
}

Write-Host '10) logout'
Invoke-WebRequest -Method POST -Uri "$BaseUrl/api/auth/logout" -Headers $auth | Out-Null
Write-Host 'SMOKE OK'
