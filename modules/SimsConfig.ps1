<#
.SYNOPSIS
Single Source of Truth (SSOT) for SimsToolkit defaults. Dot-source before param block.
#>
$Script:SimsConfigDefault = @{
    ModsRoot           = 'J:\Sims Mods\The Sims 4\Mods\[人物美化]'
    TrayRoot           = 'J:\Sims Mods\The Sims 4\Tray'
    FlattenRootPath    = 'J:\Sims Mods\The Sims 4\Mods\[人物美化]\General Character CC'
    NormalizeRootPath  = 'J:\Sims Mods\The Sims 4\Mods\[人物美化]\General Character CC'
    MergeTargetPath    = 'J:\Sims Mods\The Sims 4\Mods2\[人物美化]\General Character CC'
    FindDupRootPath    = 'J:\Sims Mods\The Sims 4\Mods\[人物美化]\General Character CC'
    ModsPath           = 'J:\Sims Mods\The Sims 4\Mods\[人物美化]\General Character CC'
    PrefixHashBytes    = 102400
    HashWorkerCount    = 8
    ModExtensions      = @('.package', '.ts4script')
    ArchiveExtensions  = @('.zip', '.rar', '.7z')
}
