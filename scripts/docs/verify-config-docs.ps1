Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

$shellSettingsPath = Join-Path $repoRoot "src/SimsModDesktop.Presentation/ViewModels/Shell/ShellSettingsController.cs"
$debugDocPath = Join-Path $repoRoot "src/SimsModDesktop/docs/DebugConfigTable.md"
$warmupDocPath = Join-Path $repoRoot "src/SimsModDesktop/docs/CacheWarmupSequence.md"
$workflowPath = Join-Path $repoRoot ".github/workflows/dotnet-ci.yml"
$readmePath = Join-Path $repoRoot "README.md"
$readmeZhPath = Join-Path $repoRoot "README.zh-CN.md"

foreach ($path in @($shellSettingsPath, $debugDocPath, $warmupDocPath, $workflowPath, $readmePath, $readmeZhPath)) {
    if (-not (Test-Path $path)) {
        throw "Required file missing: $path"
    }
}

function Get-HeadingSectionContent {
    param(
        [string]$Text,
        [string]$Heading
    )

    $pattern = "(?ms)^##\s+$([regex]::Escape($Heading))\s*\r?\n(.*?)(?=^##\s+|\z)"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) {
        throw "Heading section not found: ## $Heading"
    }

    return $match.Groups[1].Value
}

function Get-CodeDebugToggleKeys {
    param([string]$Text)

    $matches = [regex]::Matches($Text, 'new\s+DebugToggleDefinition\(\s*"([^"]+)"')
    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($match in $matches) {
        $keys.Add($match.Groups[1].Value.Trim())
    }
    return $keys | Sort-Object -Unique
}

function Get-DocDebugToggleKeys {
    param([string]$Text)

    $activeSection = Get-HeadingSectionContent -Text $Text -Heading "Active Toggles"
    $matches = [regex]::Matches($activeSection, '(?m)^\-\s+`([^`]+)`')
    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($match in $matches) {
        $keys.Add($match.Groups[1].Value.Trim())
    }
    return $keys | Sort-Object -Unique
}

function Get-ExpectedPlatformBadgeToken {
    param([string]$WorkflowText)

    $runMatches = [regex]::Matches($WorkflowText, '(?m)^\s*runs-on:\s*([^\s#]+)')
    if ($runMatches.Count -eq 0) {
        throw "No runs-on entries found in workflow."
    }

    $map = @{
        "windows-latest" = "Windows"
        "macos-latest" = "macOS"
        "ubuntu-latest" = "Linux"
    }
    $ordered = @("Windows", "macOS", "Linux")
    $found = New-Object System.Collections.Generic.HashSet[string]
    foreach ($match in $runMatches) {
        $raw = $match.Groups[1].Value.Trim()
        if ($map.ContainsKey($raw)) {
            [void]$found.Add($map[$raw])
        }
    }

    if ($found.Count -eq 0) {
        throw "No supported runner labels found in workflow."
    }

    $labels = New-Object System.Collections.Generic.List[string]
    foreach ($label in $ordered) {
        if ($found.Contains($label)) {
            $labels.Add($label)
        }
    }

    if ($labels.Count -eq 1) {
        return "platform-$($labels[0])-lightgrey"
    }

    return "platform-$([string]::Join('%20%7C%20', $labels))-lightgrey"
}

$shellText = Get-Content $shellSettingsPath -Raw
$debugDocText = Get-Content $debugDocPath -Raw
$warmupDocText = Get-Content $warmupDocPath -Raw
$workflowText = Get-Content $workflowPath -Raw
$readmeText = Get-Content $readmePath -Raw
$readmeZhText = Get-Content $readmeZhPath -Raw

$codeKeys = Get-CodeDebugToggleKeys -Text $shellText
$docKeys = Get-DocDebugToggleKeys -Text $debugDocText

$codeSet = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
$docSet = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
foreach ($key in $codeKeys) { [void]$codeSet.Add($key) }
foreach ($key in $docKeys) { [void]$docSet.Add($key) }

$missingInDoc = @($codeKeys | Where-Object { -not $docSet.Contains($_) })
$extraInDoc = @($docKeys | Where-Object { -not $codeSet.Contains($_) })
if ($missingInDoc.Count -gt 0 -or $extraInDoc.Count -gt 0) {
    throw "Debug toggle key drift detected. MissingInDoc=[$($missingInDoc -join ',')] ExtraInDoc=[$($extraInDoc -join ',')]"
}

if ($debugDocText -match 'startup\.tray_cache_warmup\.') {
    throw "DebugConfigTable.md still contains removed startup tray warmup keys."
}

if ($warmupDocText -notmatch 'There is no startup-time Tray dependency warmup\.') {
    throw "CacheWarmupSequence.md is missing the baseline statement about no startup tray warmup."
}

$expectedBadgeToken = Get-ExpectedPlatformBadgeToken -WorkflowText $workflowText
if ($readmeText -notmatch [regex]::Escape($expectedBadgeToken)) {
    throw "README.md platform badge does not match workflow runners. Expected token: $expectedBadgeToken"
}

if ($readmeZhText -notmatch [regex]::Escape($expectedBadgeToken)) {
    throw "README.zh-CN.md platform badge does not match workflow runners. Expected token: $expectedBadgeToken"
}

Write-Host "docs-governance checks passed."
