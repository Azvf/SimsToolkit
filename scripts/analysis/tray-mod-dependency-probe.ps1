[CmdletBinding()]
param(
    [Parameter()]
    [string]$TrayPath = '',

    [Parameter()]
    [string]$ModsPath = '',

    [Parameter()]
    [ValidateRange(1, 100)]
    [int]$CandidateMinFrequency = 1,

    [Parameter()]
    [string]$TrayItemKey = '',

    [Parameter()]
    [ValidateSet('Legacy', 'StrictS4TI')]
    [string]$AnalysisMode = 'StrictS4TI',

    [Parameter()]
    [string]$S4tiPath = 'D:\SIM4\Sims 4 Tray Importer (S4TI)',

    [Parameter()]
    [switch]$ListTrayItems,

    [Parameter()]
    [ValidateRange(1, 5000)]
    [int]$ListTopN = 200,

    [Parameter()]
    [ValidateRange(1, 1000)]
    [int]$MinMatchCount = 1,

    [Parameter()]
    [ValidateRange(0, 16)]
    [int]$RawScanStep = 0,

    [Parameter()]
    [ValidateRange(1, 10000)]
    [int]$TopN = 200,

    [Parameter()]
    [int]$MaxPackageCount = 0,

    [Parameter()]
    [ValidateRange(1, 64)]
    [int]$ProbeWorkerCount = 8,

    [Parameter()]
    [switch]$PreviewTrayItems,

    [Parameter()]
    [ValidateRange(1, 5000)]
    [int]$PreviewTopN = 300,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$PreviewFilesPerItem = 12,

    [Parameter()]
    [string]$PreviewOutputCsv = '',

    [Parameter()]
    [string]$PreviewOutputHtml = '',

    [Parameter()]
    [string]$OutputCsv = '',

    [Parameter()]
    [switch]$ExportMatchedPackages,

    [Parameter()]
    [string]$ExportTargetPath = '',

    [Parameter()]
    [switch]$ExportUnusedPackages,

    [Parameter()]
    [string]$UnusedOutputCsv = '',

    [Parameter()]
    [ValidateSet('Low', 'Medium', 'High')]
    [string]$ExportMinConfidence = 'Low'
)

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# SSOT: apply defaults from SimsConfig
. (Join-Path $projectRoot 'modules\SimsConfig.ps1')
$cfg = $Script:SimsConfigDefault
if ([string]::IsNullOrEmpty($TrayPath)) { $TrayPath = $cfg.TrayRoot }
if ([string]::IsNullOrEmpty($ModsPath)) { $ModsPath = $cfg.ModsPath }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $projectRoot 'modules\SimsTrayDependencyProbe.psm1'
if (-not (Test-Path -LiteralPath $modulePath)) {
    throw "Module not found: $modulePath"
}
Import-Module -Name $modulePath -Force

function Get-SimsDefaultOutputRoot {
    $defaultOutputRoot = Join-Path $projectRoot 'output'
    if (-not (Test-Path -LiteralPath $defaultOutputRoot)) {
        [System.IO.Directory]::CreateDirectory($defaultOutputRoot) | Out-Null
    }

    return (Resolve-Path -LiteralPath $defaultOutputRoot).ProviderPath
}

function Get-SimsPreviewTypeCssClass {
    param([Parameter(Mandatory = $true)][string]$PresetType)

    switch ($PresetType) {
        'Lot' { return 'type-lot' }
        'Room' { return 'type-room' }
        'Household' { return 'type-household' }
        'Mixed' { return 'type-mixed' }
        'GenericTray' { return 'type-generic' }
        default { return 'type-unknown' }
    }
}

function New-SimsTrayPreviewHtml {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][string]$TrayPath,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$ResolvedTrayItemKey,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $cards = New-Object System.Text.StringBuilder
    foreach ($row in $Rows) {
        $keyText = [string]$row.TrayItemKey
        $typeText = [string]$row.PresetType
        $latestText = if ($row.LatestWriteTimeLocal -is [DateTime] -and $row.LatestWriteTimeLocal -gt [DateTime]::MinValue) {
            ([DateTime]$row.LatestWriteTimeLocal).ToString('yyyy-MM-dd HH:mm:ss')
        }
        else {
            ''
        }

        $fileListMarkup = ''
        if (-not [string]::IsNullOrWhiteSpace([string]$row.FileListPreview)) {
            $listItems = @(
                [string]$row.FileListPreview -split '\|' | ForEach-Object {
                    if ([string]::IsNullOrWhiteSpace($_)) { return }
                    "<li>$([System.Net.WebUtility]::HtmlEncode($_))</li>"
                }
            ) -join ''
            if (-not [string]::IsNullOrWhiteSpace($listItems)) {
                $fileListMarkup = "<details><summary>Files</summary><ul>$listItems</ul></details>"
            }
        }

        $searchBlob = [System.Net.WebUtility]::HtmlEncode(("{0} {1} {2} {3}" -f $keyText, $typeText, [string]$row.Extensions, [string]$row.ResourceTypes).ToLowerInvariant())
        $cssClass = Get-SimsPreviewTypeCssClass -PresetType $typeText

        $card = @"
<article class="card $cssClass" data-type="$([System.Net.WebUtility]::HtmlEncode($typeText))" data-search="$searchBlob">
  <div class="card-head">
    <span class="badge">$([System.Net.WebUtility]::HtmlEncode($typeText))</span>
    <span class="date">$([System.Net.WebUtility]::HtmlEncode($latestText))</span>
  </div>
  <h3>$([System.Net.WebUtility]::HtmlEncode($keyText))</h3>
  <div class="meta">
    <span>Files: $([System.Net.WebUtility]::HtmlEncode([string]$row.FileCount))</span>
    <span>Size: $([System.Net.WebUtility]::HtmlEncode([string]$row.TotalMB)) MB</span>
  </div>
  <div class="line"><strong>Extensions:</strong> $([System.Net.WebUtility]::HtmlEncode([string]$row.Extensions))</div>
  <div class="line"><strong>Types:</strong> $([System.Net.WebUtility]::HtmlEncode([string]$row.ResourceTypes))</div>
  $fileListMarkup
</article>
"@
        [void]$cards.AppendLine($card)
    }

    $generated = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $trayItemDisplay = if ([string]::IsNullOrWhiteSpace($ResolvedTrayItemKey)) { 'All' } else { $ResolvedTrayItemKey }

    $template = @'
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>TS4 Tray Offline Preview</title>
  <style>
    :root {
      --bg: #f7f3ec;
      --ink: #2b2b2b;
      --muted: #6c6a67;
      --card: #fffef9;
      --line: #ddd3c3;
      --lot: #f4b942;
      --room: #3ea99f;
      --household: #f06d61;
      --mixed: #7a73d1;
      --generic: #7f8c8d;
      --unknown: #a0a0a0;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      background: radial-gradient(1200px 800px at 20% -10%, #fff4d5 0%, var(--bg) 50%, #efe8da 100%);
      color: var(--ink);
    }
    .wrap {
      max-width: 1280px;
      margin: 0 auto;
      padding: 24px;
    }
    .hero {
      background: linear-gradient(135deg, #2f4858, #33658a);
      color: #f6f8fa;
      border-radius: 16px;
      padding: 20px;
      box-shadow: 0 8px 30px rgba(0,0,0,.15);
    }
    .hero h1 {
      margin: 0 0 8px;
      font-size: 1.6rem;
      letter-spacing: .02em;
    }
    .hero p {
      margin: 4px 0;
      color: #d6e2ec;
      font-size: .92rem;
      word-break: break-all;
    }
    .controls {
      display: grid;
      grid-template-columns: 1fr 180px;
      gap: 10px;
      margin: 16px 0 20px;
    }
    .controls input, .controls select {
      width: 100%;
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 10px 12px;
      font-size: .95rem;
      background: #fff;
      color: var(--ink);
    }
    .count {
      color: var(--muted);
      font-size: .9rem;
      margin-bottom: 10px;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(290px, 1fr));
      gap: 12px;
    }
    .card {
      background: var(--card);
      border: 1px solid var(--line);
      border-radius: 14px;
      padding: 12px;
      box-shadow: 0 2px 10px rgba(0,0,0,.05);
    }
    .card-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
    }
    .badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: .74rem;
      font-weight: 600;
      color: #17212b;
      background: var(--unknown);
    }
    .type-lot .badge { background: var(--lot); }
    .type-room .badge { background: var(--room); }
    .type-household .badge { background: var(--household); }
    .type-mixed .badge { background: var(--mixed); color: #fff; }
    .type-generic .badge { background: var(--generic); color: #fff; }
    .date {
      color: var(--muted);
      font-size: .75rem;
      white-space: nowrap;
    }
    .card h3 {
      font-size: .92rem;
      margin: 0 0 8px;
      word-break: break-all;
    }
    .meta {
      display: flex;
      gap: 10px;
      color: var(--muted);
      font-size: .82rem;
      margin-bottom: 6px;
    }
    .line {
      font-size: .82rem;
      color: #3e3d3b;
      margin: 4px 0;
      word-break: break-word;
    }
    details {
      margin-top: 8px;
      border-top: 1px dashed var(--line);
      padding-top: 7px;
    }
    details summary {
      cursor: pointer;
      color: #1f4e63;
      font-size: .82rem;
      user-select: none;
    }
    details ul {
      margin: 6px 0 0;
      padding-left: 18px;
      font-size: .78rem;
      color: #4e4b46;
    }
    @media (max-width: 760px) {
      .controls { grid-template-columns: 1fr; }
      .hero h1 { font-size: 1.3rem; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="hero">
      <h1>The Sims 4 Tray Offline Preview</h1>
      <p><strong>TrayPath:</strong> __TRAY_PATH__</p>
      <p><strong>TrayItemKey:</strong> __TRAY_ITEM__</p>
      <p><strong>Generated:</strong> __GENERATED__</p>
    </section>

    <div class="controls">
      <input id="query" type="text" placeholder="Filter by key, extension, resource type...">
      <select id="typeFilter">
        <option value="All">All Types</option>
        <option value="Lot">Lot</option>
        <option value="Room">Room</option>
        <option value="Household">Household</option>
        <option value="Mixed">Mixed</option>
        <option value="GenericTray">GenericTray</option>
        <option value="Unknown">Unknown</option>
      </select>
    </div>
    <div class="count"><span id="visibleCount">0</span> / __TOTAL_COUNT__ items visible</div>
    <section id="grid" class="grid">
__CARDS__
    </section>
  </div>

  <script>
    const query = document.getElementById('query');
    const typeFilter = document.getElementById('typeFilter');
    const visibleCount = document.getElementById('visibleCount');
    const cards = Array.from(document.querySelectorAll('.card'));

    function refresh() {
      const q = query.value.trim().toLowerCase();
      const t = typeFilter.value;
      let shown = 0;

      for (const card of cards) {
        const typeOk = (t === 'All') || (card.dataset.type === t);
        const search = (card.dataset.search || '').toLowerCase();
        const textOk = q.length === 0 || search.includes(q);
        const show = typeOk && textOk;
        card.style.display = show ? '' : 'none';
        if (show) { shown += 1; }
      }

      visibleCount.textContent = String(shown);
    }

    query.addEventListener('input', refresh);
    typeFilter.addEventListener('change', refresh);
    refresh();
  </script>
</body>
</html>
'@

    $html = $template.
        Replace('__TRAY_PATH__', [System.Net.WebUtility]::HtmlEncode($TrayPath)).
        Replace('__TRAY_ITEM__', [System.Net.WebUtility]::HtmlEncode($trayItemDisplay)).
        Replace('__GENERATED__', [System.Net.WebUtility]::HtmlEncode($generated)).
        Replace('__TOTAL_COUNT__', [string]$Rows.Count).
        Replace('__CARDS__', $cards.ToString())

    [System.IO.File]::WriteAllText($OutputPath, $html, [System.Text.Encoding]::UTF8)
}

if ($PreviewTrayItems) {
    $previewRows = @(
        Get-SimsTrayPreviewItems -TrayPath $TrayPath -TrayItemKey $TrayItemKey -TopN $PreviewTopN -IncludeFiles -MaxFilesPerItem $PreviewFilesPerItem
    )

    $resolvedTrayPath = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
    Write-Host ("Tray: {0}" -f $resolvedTrayPath)
    if (-not [string]::IsNullOrWhiteSpace($TrayItemKey)) {
        Write-Host ("TrayItemKey: {0}" -f $TrayItemKey)
    }
    Write-Host ("PreviewRows: {0}" -f $previewRows.Count)
    Write-Host ''

    if ($previewRows.Count -gt 0) {
        $previewRows | Select-Object -First 30 TrayItemKey, PresetType, FileCount, TotalMB, LatestWriteTimeLocal, Extensions | Format-Table -AutoSize
    }
    else {
        Write-Host 'No tray preview rows found.'
    }

    if ([string]::IsNullOrWhiteSpace($PreviewOutputCsv)) {
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $outputRoot = Get-SimsDefaultOutputRoot
        $PreviewOutputCsv = Join-Path $outputRoot ("tray_preview_{0}.csv" -f $stamp)
    }

    if ([string]::IsNullOrWhiteSpace($PreviewOutputHtml)) {
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $outputRoot = Get-SimsDefaultOutputRoot
        $PreviewOutputHtml = Join-Path $outputRoot ("tray_preview_{0}.html" -f $stamp)
    }

    $csvDir = Split-Path -Path $PreviewOutputCsv -Parent
    if (-not [string]::IsNullOrWhiteSpace($csvDir) -and -not (Test-Path -LiteralPath $csvDir)) {
        [System.IO.Directory]::CreateDirectory($csvDir) | Out-Null
    }
    $htmlDir = Split-Path -Path $PreviewOutputHtml -Parent
    if (-not [string]::IsNullOrWhiteSpace($htmlDir) -and -not (Test-Path -LiteralPath $htmlDir)) {
        [System.IO.Directory]::CreateDirectory($htmlDir) | Out-Null
    }

    $previewRows | Export-Csv -LiteralPath $PreviewOutputCsv -NoTypeInformation -Encoding UTF8
    New-SimsTrayPreviewHtml -Rows $previewRows -TrayPath $resolvedTrayPath -ResolvedTrayItemKey $TrayItemKey -OutputPath $PreviewOutputHtml

    Write-Host ''
    Write-Host ("PreviewCSV: {0}" -f $PreviewOutputCsv)
    Write-Host ("PreviewHTML: {0}" -f $PreviewOutputHtml)
    return
}

if ($ListTrayItems) {
    $items = @(Get-SimsTrayItemSummary -TrayPath $TrayPath | Select-Object -First $ListTopN)
    Write-Host ("Tray: {0}" -f (Resolve-Path -LiteralPath $TrayPath).ProviderPath)
    Write-Host ("TrayItemRows: {0}" -f $items.Count)
    Write-Host ''

    if ($items.Count -gt 0) {
        $items | Select-Object TrayItemKey, FileCount, TotalMB, ResourceTypes, Extensions | Format-Table -AutoSize
    }
    else {
        Write-Host 'No tray item groups found.'
    }

    if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $outputRoot = Get-SimsDefaultOutputRoot
        $OutputCsv = Join-Path $outputRoot ("tray_item_summary_{0}.csv" -f $stamp)
    }

    $items | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
    Write-Host ''
    Write-Host ("CSV: {0}" -f $OutputCsv)
    return
}

$report = Find-SimsTrayModDependencies -TrayPath $TrayPath -ModsPath $ModsPath -CandidateMinFrequency $CandidateMinFrequency -TrayItemKey $TrayItemKey -MinMatchCount $MinMatchCount -TopN $TopN -MaxPackageCount $MaxPackageCount -RawScanStep $RawScanStep -IncludeUnusedPackages:$ExportUnusedPackages -AnalysisMode $AnalysisMode -ProbeWorkerCount $ProbeWorkerCount -S4tiPath $S4tiPath

Write-Host ("AnalysisMode: {0}" -f $report.AnalysisMode)
if ($report.AnalysisMode -eq 'StrictS4TI') {
    Write-Host ("S4TI: {0}" -f $report.S4tiPath)
}
Write-Host ("Tray: {0}" -f $report.TrayPath)
if (-not [string]::IsNullOrWhiteSpace($report.TrayItemKey)) {
    Write-Host ("TrayItemKey: {0}" -f $report.TrayItemKey)
}
Write-Host ("Mods: {0}" -f $report.ModsPath)
Write-Host ("TrayFiles: {0} | CandidateRaw: {1} | CandidateFiltered: {2} | RawScanStep: {3}" -f $report.TrayFileCount, $report.CandidateCountRaw, $report.CandidateCountFiltered, $report.RawScanStep)
if ($report.AnalysisMode -eq 'StrictS4TI') {
    Write-Host ("SearchKeys => Objects: {0} | ResourceKeys: {1} | LotTraits: {2} | CandidateInstances: {3}" -f $report.SearchObjectCount, $report.SearchResourceKeyCount, $report.SearchLotTraitCount, $report.SearchCandidateInstanceCount)
}
Write-Host ("ProbeWorkers: {0}" -f $report.ProbeWorkerCount)
Write-Host ("Packages: {0} | Processed: {1} | ParseErrors: {2}" -f $report.PackageCount, $report.PackageProcessed, $report.ParseErrorCount)
Write-Host ("ResultRows: {0}" -f @($report.Results).Count)
Write-Host ("UnusedTotal: {0} bytes ({1} GB)" -f $report.UnusedPackageTotalBytes, $report.UnusedPackageTotalGB)
if ($ExportUnusedPackages) {
    Write-Host ("UnusedRows: {0}" -f @($report.UnusedResults).Count)
}
Write-Host ''

if (@($report.Results).Count -gt 0) {
    if ($report.AnalysisMode -eq 'StrictS4TI') {
        $report.Results | Select-Object -First 30 PackagePath, MatchInstanceCount, MatchObjectCount, MatchResourceKeyCount, MatchLotTraitCount, MatchCandidateInstanceCount, Confidence, PackageInstanceCount, MatchRatePct | Format-Table -AutoSize
    }
    else {
        $report.Results | Select-Object -First 30 PackagePath, MatchInstanceCount, WeightedScore, Confidence, PackageInstanceCount, MatchRatePct | Format-Table -AutoSize
    }
}
else {
    Write-Host 'No dependency candidates found with current thresholds.'
}

if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $outputRoot = Get-SimsDefaultOutputRoot
    $OutputCsv = Join-Path $outputRoot ("tray_mod_dependency_probe_{0}.csv" -f $stamp)
}

$report.Results | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host ''
Write-Host ("CSV: {0}" -f $OutputCsv)

if ($report.ParseErrorCount -gt 0) {
    $errPath = [System.IO.Path]::ChangeExtension($OutputCsv, '.parse_errors.csv')
    $report.ParseErrors | Export-Csv -LiteralPath $errPath -NoTypeInformation -Encoding UTF8
    Write-Host ("ParseErrorCSV: {0}" -f $errPath)
}

if ($ExportUnusedPackages) {
    if ([string]::IsNullOrWhiteSpace($UnusedOutputCsv)) {
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $outputRoot = Get-SimsDefaultOutputRoot
        $UnusedOutputCsv = Join-Path $outputRoot ("tray_mod_unused_packages_{0}.csv" -f $stamp)
    }

    $report.UnusedResults | Export-Csv -LiteralPath $UnusedOutputCsv -NoTypeInformation -Encoding UTF8
    Write-Host ("UnusedCSV: {0}" -f $UnusedOutputCsv)
}

if ($ExportMatchedPackages) {
    if ([string]::IsNullOrWhiteSpace($ExportTargetPath)) {
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $suffix = if ([string]::IsNullOrWhiteSpace($report.TrayItemKey)) { 'all' } else { $report.TrayItemKey.Replace('0x', '') }
        $outputRoot = Get-SimsDefaultOutputRoot
        $ExportTargetPath = Join-Path $outputRoot ("tray_dependency_export_{0}_{1}" -f $suffix, $stamp)
    }

    if (-not (Test-Path -LiteralPath $ExportTargetPath)) {
        [System.IO.Directory]::CreateDirectory($ExportTargetPath) | Out-Null
    }
    $resolvedExportPath = (Resolve-Path -LiteralPath $ExportTargetPath).ProviderPath

    $confidenceRank = @{
        Low = 1
        Medium = 2
        High = 3
    }

    $threshold = $confidenceRank[$ExportMinConfidence]
    $rows = @($report.Results | Where-Object { $confidenceRank[$_.Confidence] -ge $threshold })

    $manifest = New-Object System.Collections.Generic.List[object]
    $copied = 0
    $replaced = 0
    $skippedOlder = 0
    $skippedMissing = 0

    foreach ($row in $rows) {
        $sourcePath = [string]$row.PackagePath
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            $manifest.Add([pscustomobject]@{
                    PackagePath = $sourcePath
                    TargetPath = ''
                    Action = 'MissingSource'
                    MatchInstanceCount = $row.MatchInstanceCount
                    WeightedScore = $row.WeightedScore
                    Confidence = $row.Confidence
                })
            $skippedMissing++
            continue
        }

        $sourceFile = Get-Item -LiteralPath $sourcePath -ErrorAction Stop
        $targetPath = Join-Path $resolvedExportPath $sourceFile.Name
        $action = 'Copy'
        if (Test-Path -LiteralPath $targetPath) {
            $targetFile = Get-Item -LiteralPath $targetPath -ErrorAction Stop
            if ($sourceFile.LastWriteTimeUtc -gt $targetFile.LastWriteTimeUtc) {
                $action = 'Replace'
            }
            else {
                $action = 'SkipOlder'
            }
        }

        switch ($action) {
            'Copy' {
                Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
                $copied++
            }
            'Replace' {
                Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
                $replaced++
            }
            'SkipOlder' {
                $skippedOlder++
            }
        }

        $manifest.Add([pscustomobject]@{
                PackagePath = $sourcePath
                TargetPath = $targetPath
                Action = $action
                MatchInstanceCount = $row.MatchInstanceCount
                WeightedScore = $row.WeightedScore
                Confidence = $row.Confidence
            })
    }

    $manifestPath = Join-Path $resolvedExportPath 'export_manifest.csv'
    $manifest | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8

    Write-Host ''
    Write-Host ("ExportTarget: {0}" -f $resolvedExportPath)
    Write-Host ("ExportRows: {0} | Copied: {1} | Replaced: {2} | SkipOlder: {3} | MissingSource: {4}" -f $rows.Count, $copied, $replaced, $skippedOlder, $skippedMissing)
    Write-Host ("ExportManifest: {0}" -f $manifestPath)
}
