using DaylightCompare.Domain;
using DaylightCompare.Services;

namespace DaylightCompare.App.Localization;

/// <summary>
/// 화면에 보이는 모든 문구 (conventions localization.md의 ARB에 해당하는 C# 판).
/// <list type="bullet">
/// <item>한국어(<see cref="KoreanStrings"/>)가 기준이고 영어(<see cref="EnglishStrings"/>)는 <b>모든 항목</b>을 가져야 한다.
/// 인터페이스라서 빠뜨리면 컴파일이 실패한다 — Flutter의 "누락 번역은 CI에서 막는다"를 컴파일 단계에서 지킨다.</item>
/// <item>이름은 PascalCase에 화면 이름을 접두어로 붙인다 (<c>PaneEmpty</c>, <c>SettingsMode</c>).</item>
/// <item>값이 들어가는 문구는 메서드다 (<c>PaneItemCount(n)</c>). 복수형·어순은 언어가 정한다.</item>
/// <item>앱 표시 이름은 번역하지 않는다. <c>AppIdentity.DisplayName</c>을 인자로 받는다.</item>
/// <item>한국어 문체는 간결한 명사형이나 해요체, 영어 버튼·메뉴는 Title Case, 문장은 Sentence case (localization.md §7).</item>
/// </list>
/// </summary>
public interface IAppStrings
{
    // 공통
    string CommonCancel { get; }
    string CommonConfirm { get; }
    string SettingsTooltip { get; }

    // 홈 창
    string HomeEmptyTitle { get; }
    string HomeEmptyHint { get; }
    string HomeTagline { get; }
    string HomeOpenFiles { get; }
    string HomeFolderCompare { get; }
    string HomeFolderCompareHint { get; }
    string HomeTextCompare { get; }
    string HomeTextCompareHint { get; }
    string HomeDropOpenTooMany { get; }
    string FolderChooseBoth { get; }
    string CompareTypedFolder { get; }
    string FolderDropFoldersOnly { get; }

    // 비교 창 · 홈 창 고르기 · 끌어다 놓기
    string CompareWindowTitle(string left, string right);
    string CompareSwap { get; }
    string CompareFormatTooltip { get; }
    string CompareFormatLineBreak { get; }
    string CompareFormatSaveEncoding { get; }
    string CompareFormatMenuTooltip { get; }
    string CompareFormatPlain { get; }
    string CompareFormatAuto(string name);
    string CompareHeaderInfo(DateTime modifiedLocal, long length);
    string CompareShowBytes { get; }
    string CompareSectionPosition(int index, int count);
    string CompareSectionCount(int count);
    string CompareLineBreakMixed { get; }
    string CompareInsertMode { get; }
    string CompareOverwriteMode { get; }
    string CompareLoadTime(double seconds);
    string CompareBytesNoLine { get; }
    string SessionSaveMenu { get; }
    string SessionSaveTitle { get; }
    string SessionNamePrompt { get; }
    string SessionOverwrite { get; }
    string SessionOverwriteBody(string name);
    string SessionSaved(string name);
    string SessionRenameTitle { get; }
    string SessionRename { get; }
    string SessionDelete { get; }
    string SessionNameTaken { get; }
    string SessionTabs(int count);
    string SessionMissing(string path);
    string HomeSessionsTitle { get; }
    string HomeRestoreWindows(int count);
    string BinaryAlignFixed { get; }
    string BinaryAlignInsertions { get; }
    string BinaryAlignTooltip { get; }
    string BinaryGoTo { get; }
    string BinaryGoToPrompt { get; }
    string BinaryComparing { get; }
    string BinaryEmptyFile { get; }
    string BinaryDifferences(int count, long bytes);
    string BinaryLengthDiffers(long left, long right);
    string BinaryOpen { get; }
    string TabClose { get; }
    string TabCloseOthers { get; }
    string TabDetach { get; }
    string CompareOpen { get; }
    string CompareOpenLeftTitle { get; }
    string CompareOpenRightTitle { get; }
    string CompareEmptySide { get; }
    string CompareLoading { get; }
    string CompareIdentical { get; }
    string CompareEquivalent(int unimportant);
    string CompareDifferences(int important, int unimportant);
    string CompareBinary { get; }
    string CompareTooLarge(string size);
    string CompareLoadFailed(string detail);
    string CompareEncodingDamaged(string encoding, int errors, int firstLine);
    string CompareReopenEncoding { get; }
    string CompareRepairMixed(string primary, string secondary);
    string CompareRepaired(int lines, int broken);
    string CompareEncodingErrors(int errors);
    string CompareEncodingDiffers(string left, string right);
    string HomeLeft { get; }
    string HomeRight { get; }
    string HomeNotChosen { get; }
    string DropTooMany { get; }
    string DropNoCounterpart(string name);
    string CommonClose { get; }
    string FolderAllHidden { get; }
    string FolderFilterKeepOne { get; }
    string CompareCopySelectionToRight { get; }
    string CompareCopySelectionToLeft { get; }
    string CompareMergeSelectFirst { get; }
    string CompareMergeWholeFile { get; }
    string CompareMergeReselect { get; }
    string CompareMergeWrongSide(bool toRight);
    string CompareMergeCopied(int lines, bool toRight);
    string CompareMergeLines(int lines);
    string HomeRecentTitle { get; }
    string HomeRecentClear { get; }
    string HomeRecentRemove { get; }
    string HomeRecentMissing(string path);
    string HomeRecentYesterday(string time);
    string FolderLoading { get; }
    string FolderColName { get; }
    string FolderColSize { get; }
    string FolderColModified { get; }
    string FolderFilterPlaceholder { get; }
    string FolderFilterTooltip { get; }
    string FolderFilterExcludePlaceholder { get; }
    string FolderFilterExcludeTooltip { get; }
    string FolderFilterImport { get; }
    string FolderFilterExport { get; }
    string FolderFilterFileType { get; }
    string FolderFilterImportFailed { get; }
    string FolderViewFilterTooltip { get; }
    string FolderMenuActions { get; }
    string FolderOpen { get; }
    string FolderUpLevel { get; }
    string FolderNavBack { get; }
    string FolderNavForward { get; }
    string FolderCannotGoUp { get; }
    string FolderCompareNow { get; }
    string FolderFilterExcludeItem { get; }
    string FolderFilterIncludeItem { get; }
    string FolderCompareNeedBoth { get; }
    string FolderDelete { get; }
    string FolderDeleteLeft { get; }
    string FolderDeleteRight { get; }
    string FolderExpandAll { get; }
    string FolderCollapseAll { get; }
    string FolderSelectAll { get; }
    string FolderInvertSelection { get; }
    string FolderSelectNewer { get; }
    string FolderSelectOrphans { get; }
    string FolderSideLeft { get; }
    string FolderSideRight { get; }
    string FolderSideBoth { get; }
    string FolderRefresh { get; }
    string FolderAlwaysShowFolders { get; }
    string FolderToggles { get; }
    string FolderToggleSame { get; }
    string FolderToggleDifferent { get; }
    string FolderToggleLeftNewer { get; }
    string FolderToggleRightNewer { get; }
    string FolderToggleLeftOrphan { get; }
    string FolderToggleRightOrphan { get; }
    string FolderCustomFilter { get; }
    string FolderNothingSelected { get; }
    string FolderArchiveReadOnly { get; }
    string FolderCopyAction { get; }
    string FolderDeleteAction { get; }
    string FolderDeletePermanent { get; }
    string FolderChooseLeftTitle { get; }
    string FolderChooseRightTitle { get; }
    string HomeChooseFolder { get; }
    string FolderFileVsFolderLater { get; }
    string FolderWindowTitle(string left, string right);
    string FolderStatus(int same, int different, int leftOnly, int rightOnly);
    string FolderStatusPending(int pending);
    string FolderShowName(string id);
    string FolderCopyTitle(bool toRight);
    string FolderCopyBody(int count, int overwrites, string target);
    string FolderDeleteTitle(string side);
    string FolderDeleteBody(int count, bool toTrash);
    string FolderCopied(int count);
    string FolderDeleted(int count);
    string FolderOperationFailed(int count, string first);
    string CompareIgnoreUnimportant { get; }
    string CompareViewFilterTooltip { get; }
    string CompareCommandsTooltip { get; }
    string CompareReload { get; }
    string CompareRecompareNow { get; }
    string CompareShowAll { get; }
    string CompareShowDifferences { get; }
    string CompareShowSame { get; }
    string CompareShowContext { get; }
    string CompareContextLinesMenu { get; }
    string CompareMenuSession { get; }
    string CompareMenuFile { get; }
    string CompareMenuEdit { get; }
    string CompareMenuSearch { get; }
    string CompareMenuView { get; }
    string CompareInfo { get; }
    string CompareOpenFile { get; }
    string CompareOpenClipboard { get; }
    string CompareSaveFile { get; }
    string CompareSaveFileAs { get; }
    string CompareUndo { get; }
    string CompareRedo { get; }
    string CompareCopyLineToRight { get; }
    string CompareCopyLineToLeft { get; }
    string CompareSelectSection { get; }
    string CompareNextSection { get; }
    string ComparePrevSection { get; }
    string CompareNextLine { get; }
    string ComparePrevLine { get; }
    string CompareFindMenu { get; }
    string CompareReplaceMenu { get; }
    string CompareFindNextMenu { get; }
    string CompareFindPreviousMenu { get; }
    string CompareGoTo { get; }
    string CompareReplacePlaceholder { get; }
    string CompareReplaceOne { get; }
    string CompareReplaceAll { get; }
    string CommonOk { get; }
    string CompareGoToPrompt(string name);
    string CompareInfoSize(string size, string modified);
    string CompareInfoLines(int lines);
    string CompareReplaced(int count);
    string CompareScratchName { get; }
    string CompareScratchTooltip { get; }
    string CompareClipboardEmpty { get; }
    string CompareContextLines(int lines);
    string CompareFolded(int lines);
    string CompareFindPlaceholder { get; }
    string CompareFindMatchCase { get; }
    string CompareFindNone { get; }
    string CompareFindCount(int index, int count);
    string CompareFindInLeft { get; }
    string CompareFindInRight { get; }
    string CliUsageTitle { get; }
    string CliUsage { get; }
    string CliUnknownOption(string option);
    string CliTooManyPaths { get; }
    string CliUnsupportedOption(string option);
    string CliQuickCompareNeedsTwoPaths { get; }
    string CompareSave { get; }
    string CompareCopyToRight { get; }
    string CompareCopyToLeft { get; }
    string CompareSaveTitle(string name);
    string CompareSaveChangedOnDisk { get; }
    string CompareSaveLost(string encoding, string chars);
    string CompareSaveDamaged(int count);
    string CompareSaveBackupNote { get; }
    string CompareSaveAsUtf8 { get; }
    string CompareSaveAnyway { get; }
    string CommonSave { get; }
    string CompareDontSave { get; }
    string CompareUnsavedTitle { get; }
    string CompareUnsavedBody(string names);
    string CompareSaved(string name);
    string CompareSaveFailed(string detail);
    string CompareChangedOutside { get; }
    string CompareReloadDiscard { get; }
    string CompareReadOnly { get; }

    string SettingsWindowTitle(string appName);
    string SettingsSectionAppearance { get; }
    string SettingsMode { get; }
    string SettingsModeSystem { get; }
    string ThemeLight { get; }
    string ThemeDark { get; }
    string SettingsPalette { get; }
    string PaletteSaturn { get; }
    string PaletteTeal { get; }
    string PaletteAmber { get; }
    string PaletteRose { get; }
    string PaletteSlate { get; }
    string PaletteHighContrast { get; }
    string SettingsFont { get; }
    string FontDefaultOption(string name);
    string FontSearchHint { get; }
    /// <summary>설정 "편집기 글꼴" (고정폭 글꼴만).</summary>
    string SettingsEditorFont { get; }
    string SettingsEditorFontHint { get; }
    string SettingsEditorScheme { get; }
    string SettingsEditorSchemeHint { get; }
    string EditorSchemeFollowApp { get; }
    string SettingsFontSize { get; }
    string SettingsFontSizeHint { get; }
    string SettingsLanguage { get; }
    string LanguageSystemShort { get; }
    string AboutTooltip { get; }

    // macOS 앱 메뉴 (메뉴 막대의 앱 이름 메뉴) — macOS 표준 이름
    string MacMenuAbout(string app);
    string MacMenuSettings { get; }
    string MacMenuHide(string app);
    string MacMenuHideOthers { get; }
    string MacMenuShowAll { get; }
    string MacMenuQuit(string app);
    string AboutTagline { get; }
    string AboutDescription { get; }
    string AboutVersion(string version, int build);
    string AboutOpenSourceLicenses { get; }
    string AboutRepository { get; }
    string AboutClose { get; }
    string UpdateCheckButton { get; }
    string UpdateChecking { get; }
    string UpdateAvailableTitle { get; }
    string UpdateAvailableBody(string appName, string current, string latest);
    string UpdateReleaseNotes { get; }
    string UpdateMoreChanges(int count);
    string UpdateViewReleaseNotes { get; }
    string UpdateNow { get; }
    string UpdateLater { get; }
    string UpdateSkipVersion { get; }
    string UpdateDownloading { get; }
    string UpdateVerifying { get; }
    string UpdateDownloadProgress(string received, string total);
    string UpdateCancel { get; }
    string UpdateFailedTitle { get; }
    string UpdateCheckFailedTitle { get; }
    string UpdateErrorNetwork { get; }
    string UpdateErrorChecksum { get; }
    string UpdateErrorInstall { get; }
    string UpdateErrorGeneric { get; }
    string UpdateUpToDateTitle { get; }
    string UpdateUpToDateBody(string version);
    string UpdateUnavailableTitle { get; }
    string UpdateUnavailableBody(string latest);
    string UpdateOpenReleasePage { get; }
    string UpdateMacosOpenedTitle { get; }
    string UpdateMacosOpenedBody(string appName);
    string UpdateWindowsInstallTitle { get; }
    string UpdateWindowsInstallBody(string appName);
    string UpdateQuitApp(string appName);
    string UpdateLinuxInstallTitle { get; }
    string UpdateLinuxInstallBody(string appName);
    string UpdateQuitAndInstall { get; }
    string UpdateBusyTitle { get; }
    string UpdateBusyBody { get; }
    string LicensesTitle { get; }
    string LicensesProject { get; }
    string LicensesNote { get; }
}
