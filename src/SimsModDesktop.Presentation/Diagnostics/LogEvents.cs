namespace SimsModDesktop.Presentation.Diagnostics;

internal static class LogEvents
{
    public const string UiCommandInvoke = "ui.command.invoke";
    public const string UiCommandFail = "ui.command.fail";
    public const string UiCommandBlocked = "ui.command.blocked";
    public const string UiInteractionInvoke = "ui.interaction.invoke";
    public const string UiShortcutInvoke = "ui.shortcut.invoke";
    public const string UiDialogResult = "ui.dialog.result";
    public const string UiPageSwitchStart = "ui.page.switch.start";
    public const string UiPageSwitchDone = "ui.page.switch.done";
    public const string UiPageSwitchBlocked = "ui.page.switch.blocked";
    public const string UiPageSwitchMark = "ui.page.switch.mark";

    public const string TrayExportBatchStart = "trayexport.batch.start";
    public const string TrayExportBatchDone = "trayexport.batch.done";
    public const string TrayExportBatchFail = "trayexport.batch.fail";
    public const string TrayExportBatchCancel = "trayexport.batch.cancel";
    public const string TrayExportItemStart = "trayexport.item.start";
    public const string TrayExportItemStage = "trayexport.item.stage";
    public const string TrayExportItemDone = "trayexport.item.done";
    public const string TrayExportItemFail = "trayexport.item.fail";
    public const string TrayExportItemCancel = "trayexport.item.cancel";
    public const string TrayExportSnapshotBlocked = "trayexport.snapshot.blocked";
    public const string TrayExportRollbackStart = "trayexport.rollback.start";
    public const string TrayExportRollbackDone = "trayexport.rollback.done";

    public const string TextureCompressWhatIf = "texture.compress.whatif";
    public const string TextureCompressDecodeStart = "texture.compress.decode.start";
    public const string TextureCompressDecodeDone = "texture.compress.decode.done";
    public const string TextureCompressDecodeFail = "texture.compress.decode.fail";
    public const string TextureCompressResizeStart = "texture.compress.resize.start";
    public const string TextureCompressResizeDone = "texture.compress.resize.done";
    public const string TextureCompressResizeFail = "texture.compress.resize.fail";
    public const string TextureCompressEncodeStart = "texture.compress.encode.start";
    public const string TextureCompressEncodeDone = "texture.compress.encode.done";
    public const string TextureCompressEncodeFail = "texture.compress.encode.fail";

    public const string ShellOpsLaunchGameStart = "shell.ops.launchgame.start";
    public const string ShellOpsLaunchGameDone = "shell.ops.launchgame.done";
    public const string ShellOpsLaunchGameFail = "shell.ops.launchgame.fail";
    public const string ShellOpsClearCacheStart = "shell.ops.clearcache.start";
    public const string ShellOpsClearCacheDone = "shell.ops.clearcache.done";
    public const string ShellOpsClearCacheFail = "shell.ops.clearcache.fail";
    public const string ShellOpsPathsApplyStart = "shell.ops.paths.apply.start";
    public const string ShellOpsPathsApplyDone = "shell.ops.paths.apply.done";
    public const string ShellOpsPathsApplyFail = "shell.ops.paths.apply.fail";
}
