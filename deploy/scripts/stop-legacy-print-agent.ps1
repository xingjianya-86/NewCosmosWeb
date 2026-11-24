param(
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$processes = @(Get-Process -Name 'RustCosmosPrintAgent' -ErrorAction SilentlyContinue)
if ($processes.Count -eq 0) {
    Write-Host 'RustCosmosPrintAgent 未运行。'
} else {
    foreach ($p in $processes) {
        if ($WhatIf) {
            Write-Host "[WhatIf] 停止进程 $($p.Id) $($p.ProcessName)"
        } else {
            Stop-Process -Id $p.Id -Force
            Write-Host "已停止进程 $($p.Id) $($p.ProcessName)"
        }
    }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = Get-ItemProperty -Path $runKey -Name 'RustCosmosPrintAgent' -ErrorAction SilentlyContinue
if ($null -ne $value) {
    if ($WhatIf) {
        Write-Host '[WhatIf] 移除 HKCU Run: RustCosmosPrintAgent'
    } else {
        Remove-ItemProperty -Path $runKey -Name 'RustCosmosPrintAgent'
        Write-Host '已移除 HKCU Run: RustCosmosPrintAgent'
    }
} else {
    Write-Host 'HKCU Run 中没有 RustCosmosPrintAgent。'
}

Write-Host '完成。其他固定 PC 如仍安装旧 Rust 打印代理，请逐台执行本脚本。'
