param(
    [Parameter(Mandatory = $true)]
    [string]$ScriptPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet('organize','flatten','normalize','merge','finddup','traypreview','traydependencies')]
    [string]$Action
)

Write-Host "[smoke] action=$Action"
Write-Host "[smoke] script=$ScriptPath"

if (-not (Test-Path $ScriptPath)) {
    throw "Script path not found: $ScriptPath"
}

switch ($Action) {
    'organize' {
        & pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Action organize -WhatIf
    }
    'flatten' {
        & pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Action flatten -WhatIf
    }
    'normalize' {
        & pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Action normalize -WhatIf
    }
    'merge' {
        & pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Action merge -WhatIf
    }
    'finddup' {
        & pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Action finddup -WhatIf
    }
    'traypreview' {
        Write-Host "Tray preview is client-only in desktop app. Validate via UI run flow."
    }
    'traydependencies' {
        Write-Host "Tray dependencies are client-only in desktop app. Validate via UI run flow."
    }
}
