using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Avalonia.Input.Platform;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Drop;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;
using TextMateSharp.Grammars;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 텍스트 비교 창 (ROADMAP 3단계, 창 하나 = 비교 하나). 좌우 편집기는 <see cref="AlignedDiff"/>로 같은 행에 맞춘 글을 보이므로
/// 스크롤 위치만 같게 두면 늘 나란히 보인다.
/// <para>
/// <b>편집</b>: 양쪽 다 바로 고칠 수 있다(압축 파일 안의 파일은 읽기 전용). 화면 글에는 채움 줄이 섞여 있어서, 채움 줄의 시작에 앵커를 두고
/// "앵커가 있고 아직 빈 줄"을 빼서 실제 글을 꺼낸다(<see cref="DiffEdits.RealText"/>). 손을 멈추고 0.5초 뒤 다시 비교한다.
/// 다시 비교할 때 문서를 통째로 바꾸므로 편집기 자체의 실행 취소 대신 <b>실제 글 한 쌍</b>을 쌓는 실행 취소를 쓴다(Ctrl+Z / Ctrl+Y).
/// </para>
/// </summary>
public sealed partial class CompareView : UserControl, ICompareTab
{
    /// <summary>강조(TextMate)를 켜는 크기 상한 — 큰 생성 파일에서 화면이 느려지지 않게.</summary>
    const int HighlightMaxChars = 2 * 1024 * 1024;

    sealed class Side(bool left)
    {
        public readonly bool Left = left;
        public string? Path;
        /// <summary>BC <c>/title1</c>·<c>/title2</c>: 경로 대신 보일 이름 (git 임시 파일 이름 대신). 다른 파일을 열면 지운다.</summary>
        public string? Title;
        /// <summary>BC <c>/ro</c>·<c>/ro1</c>·<c>/ro2</c>.</summary>
        public bool ReadOnly;
        public CompareSource? Source;
        public TextEncodingKind? ForcedEncoding;
        public readonly TextEditor Editor = new();
        public RowNumberMargin Margin = null!;
        /// <summary>주소 창: 평소엔 경로(고친 것이 있으면 앞에 ●)를 보이고, 직접 고쳐 Enter를 누르면 그 파일을 연다 (BC의 경로 칸).</summary>
        public readonly TextBox PathText = new() { VerticalAlignment = VerticalAlignment.Center };
        /// <summary>머리글 둘째 줄: 파일의 수정 시각과 크기 (BC 머리글처럼).</summary>
        public readonly TextBlock InfoText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" }, IsVisible = false };
        public readonly TextBlock EncodingText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
        public readonly Border Overlay = new() { IsHitTestVisible = false, IsVisible = false };
        public readonly TextBlock OverlayText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        public readonly Border Banner = new() { IsVisible = false, Padding = new Thickness(12, 6) };
        public readonly TextBlock BannerText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        public readonly StackPanel BannerActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        public TextMate.Installation? TextMate;
        /// <summary>편집기 안의 스크롤 뷰어 (프로그램에서 스크롤할 때 이것의 오프셋을 바꾼다 — <see cref="CompareView.ScrollTo"/>).</summary>
        public ScrollViewer? Scroller;
        public string? HighlightedPath;
        /// <summary>주소 창을 고치는 중 (그동안은 표시용 글로 덮어쓰지 않는다).</summary>
        public bool PathEditing;
        public bool RepairRequested;
        public MixedRepair? Repair;

        /// <summary>지금의 실제 글 (LF). 고치면 바뀌고, 저장하면 <see cref="SavedText"/>가 된다.</summary>
        public string Real = "";
        public string SavedText = "";
        public bool EndsWithNewline;

        /// <summary>화면 글의 채움 줄 시작에 둔 앵커 (편집 뒤에도 그 줄을 따라간다).</summary>
        public readonly List<TextAnchor> Fillers = [];

        public FileSystemWatcher? Watcher;
        public DateTime IgnoreWatchUntil;
        public bool ChangedOutside;

        /// <summary>차이만 보기의 접기 (양쪽이 같은 행을 접어서 나란히 남는다).</summary>
        public FoldingManager Folding = null!;

        /// <summary>줄 바이트 표 (글이 바뀌면 다시 만든다 — <see cref="ByteMapFor"/>가 만든 때의 글과 원본).</summary>
        public string? LineBreakText;
        public object? LineBreakSource;
        public LineByteMap? ByteMap;
        public string? ByteMapFor;
        public object? ByteMapSource;

        public CompareSource.Text? Text => Source as CompareSource.Text;
        public bool HasText => Text is not null;

        /// <summary>파일이 없는 칸: 붙여 넣거나 바로 쓴 글을 비교한다 (클립보드 비교). 저장하지 않으므로 고쳐도 "고친 상태"가 아니다.</summary>
        public bool IsScratch => Path is null;
        public bool CanEdit => Text is not null && !ReadOnly && (Path is null || !ArchivePaths.IsInArchive(Path));
        /// <summary>읽었을 때의 저장 형식 (인코딩·BOM·줄 끝) — 머리글에서 바꾼 것이 있나 알아내는 기준.</summary>
        public (TextEncodingKind Encoding, bool Bom, string LineBreak)? LoadedFormat;
        public bool FormatChanged => Text is { } t && LoadedFormat is { } l && (t.File.Encoding, t.File.HasBom, t.File.LineBreak) != l;
        public bool Dirty => HasText && !IsScratch && (Real != SavedText || FormatChanged);
        public string Name => Title is { Length: > 0 } t ? t : Path is null ? Strings.Current.CompareScratchName : Locations.NameOf(Path.Replace(ArchivePaths.Separator, "/"));
    }

    readonly Side _left = new(true);
    readonly Side _right = new(false);
    readonly DiffViewState _leftState = new();
    readonly DiffViewState _rightState = new();
    readonly OverviewBar _overview = new();

    // --- 고른 부분만 합치기: 고른 행을 양쪽에서 틀로 감싸고, 두 편집기 사이 가운데 영역의 → / ← 로 그 행만 복사한다 ---
    // 파일 전체를 옮기는 일은 파일 비교에서 하지 않는다 (폴더 비교의 일). 무엇을 합칠지·되돌릴지는 모두 사용자가 정한다.
    readonly Border _mergeGutter = new() { BorderThickness = new Thickness(1, 0, 1, 0) };
    readonly Grid _mergeLayer = new() { ClipToBounds = true };
    readonly Button _copyRightButton = MergeButton(toRight: true);
    readonly Button _copyLeftButton = MergeButton(toRight: false);
    Button? _toolCopyRight, _toolCopyLeft;
    (Side Source, int First, int Last)? _merge;
    bool _selectionGuard;
    int _gutterAnchor = -1;

    /// <summary>
    /// 가운데 띠의 복사 단추 (원형). 화살표는 글자(→)가 아니라 도형으로 그린다 — 글자는 글꼴의 기준선 때문에 원 안에서 가운데가 어긋나고 크기도 맞추기 어렵다.
    /// 화살표 도형은 14×14 칸의 정가운데에 놓이고(가로 2~12, 세로 3~11), 선 색은 단추의 글자색(<c>OnPrimary</c>)을 따른다.
    /// </summary>
    static Button MergeButton(bool toRight)
    {
        var arrow = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(toRight ? "M2,7 L12,7 M8,3 L12,7 L8,11" : "M12,7 L2,7 M6,3 L2,7 L6,11"),
            Width = 14, Height = 14, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        var button = new Button
        {
            Name = toRight ? "CopyRightButton" : "CopyLeftButton",
            Classes = { "accent" }, Content = arrow, Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(13),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        };
        // accent 스타일은 단추 자신이 아니라 안의 내용 표시기의 글자색을 바꾸므로, 주색 위의 글자색 자원을 직접 따른다 (테마·색 구성이 바뀌어도 맞는다).
        arrow.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, arrow.GetResourceObservable("AppOnPrimary"));
        return button;
    }
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    /// <summary>BC View > Ignore Unimportant Differences (켜면 중요하지 않은 차이를 차이로 보이지 않는다).</summary>
    readonly ToggleButton _ignoreUnimportant = new() { Classes = { "tool" } };
    // 아래쪽 상태 표시줄 (BC): 구간 위치 | 양쪽 줄:칸·줄 끝 | 삽입/덮어쓰기 · 읽은 시간.
    readonly TextBlock _sectionText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _leftPos = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _rightPos = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _modeText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _loadText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Border _statusBar = new() { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 3), MinHeight = 26 };
    double? _loadSeconds;

    /// <summary>줄 단위 바이트 패널 (BC의 16진수 줄) — 커서가 있는 행의 양쪽 줄 바이트.</summary>
    readonly LineBytesPanel _bytes = new() { IsVisible = false };
    readonly ToggleButton _showBytes = new() { Classes = { "tool" } };
    readonly Button _save = new() { Classes = { "tool" }, IsEnabled = false };

    /// <summary>BC 보기 필터 (Show All / Differences / Same / Context) — 드롭다운 하나.</summary>
    readonly DropDownButton _viewFilter = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>파일 형식 드롭다운 (BC 머리글의 "Everything Else"): 비교 규칙과 문법 강조를 정하는 형식. null이면 이름으로 찾고(자동), 빈 글이면 형식 없음.</summary>
    readonly DropDownButton _formatButton = new() { VerticalAlignment = VerticalAlignment.Center };
    string? _formatId;
    string? _detectedFormat;
    CompareViewFilter _filter;
    int _contextLines;
    static readonly int[] ContextChoices = [0, 1, 2, 3, 5, 10, 20];

    // 바꾸기 (Ctrl+H): 찾기 막대 아래 줄.
    readonly TextBox _replaceBox = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _replaceRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Margin = new Thickness(24, 6, 0, 0) };

    // 찾기 막대 (Ctrl+F): 마지막으로 초점이 있던 쪽에서 찾는다.
    readonly Border _findBar = new() { IsVisible = false, Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
    readonly TextBox _findBox = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
    readonly ToggleButton _findCase = new() { Classes = { "tool" }, Content = "Aa" };
    readonly TextBlock _findCount = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 72 };
    readonly TextBlock _findWhere = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
    Side? _findSide;
    readonly Border _dropHint = new() { IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(2) };
    readonly DispatcherTimer _recompareTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Stack<(string Left, string Right)> _undo = new();
    readonly Stack<(string Left, string Right)> _redo = new();
    bool _syncing;
    bool _applying;
    bool _editBatchOpen;
    string? _notice;
    int _version;

    /// <summary>지금 비교 중인 경로 (좌우 바꾸기·다른 파일 열기 뒤의 값).</summary>
    public string? LeftPath => _left.Path;
    public string? RightPath => _right.Path;

    /// <summary>폴더 비교에서 연 파일 비교는 처음 쌍(과 그 좌우 바꾸기)을 최근 비교에 기록하지 않는다. 다른 파일을 고르면 풀린다.</summary>
    bool _skipHistory;

    /// <summary>새 비교 창(탭 호스트)을 열고 그 첫 탭으로 파일 비교를 둔다.</summary>
    public static CompareHost Open(Window? near, string? left, string? right, CompareOptions? options = null, bool recordHistory = true) =>
        CompareHost.Open(near, Create(left, right, options ?? CompareOptions.None, recordHistory));

    /// <summary>탭에 넣을 파일 비교 보기를 만든다 (아직 읽지 않는다 — 탭이 보일 때 <see cref="Start"/>).</summary>
    public static CompareView Create(string? left, string? right, CompareOptions options, bool recordHistory, SessionTab? restore = null) =>
        new(left, right, options, restore) { _skipHistory = !recordHistory };

    /// <summary>탭 호스트 창 (대화상자 주인·클립보드·파일 선택기).</summary>
    Window Owner => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("not attached");

    CompareView(string? left, string? right, CompareOptions options, SessionTab? restore = null)
    {
        _left.Title = options.LeftTitle;
        _right.Title = options.RightTitle;
        _left.ReadOnly = options.LeftReadOnly;
        _right.ReadOnly = options.RightReadOnly;
        _left.Path = left;
        _right.Path = right;

        foreach (var side in new[] { _left, _right }) { SetupEditor(side); ApplySchemeColors(side); }
        Content = BuildLayout();
        UpdateTitle(raise: false);

        _left.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(_left, _right);
        _right.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(_right, _left);
        // 개요 막대가 이 창의 (하나뿐인) 세로 스크롤 막대다: 손잡이를 끌면 양쪽 편집기가 함께 스크롤되고 캐럿은 움직이지 않는다.
        _overview.ScrollRequested += top => ScrollToRow((int)Math.Floor(top), center: false, moveCaret: false);
        _overview.WheelRequested += lines => ScrollTo(_right, null, Math.Max(0, _right.Editor.VerticalOffset + lines * _right.Editor.TextArea.TextView.DefaultLineHeight));
        var settings = App.Settings.Value;
        _filter = settings.CompareViewFilter;
        _contextLines = Math.Clamp(settings.CompareContextLines, 0, AppSettings.MaxContextLines);
        var ignore = options.IgnoreUnimportant ?? settings.CompareIgnoreUnimportant; // /iu는 이 창에만
        if (restore is not null)
        {
            // 저장한 세션의 보기 설정 (설정 파일의 값을 바꾸지 않고 이 비교에만).
            _filter = SessionTab.ViewFilterOf(restore.ViewFilter);
            _contextLines = Math.Clamp(restore.ContextLines, 0, AppSettings.MaxContextLines);
            ignore = restore.IgnoreUnimportant;
            _formatId = restore.FormatId;
        }
        _ignoreUnimportant.IsChecked = ignore;
        _showBytes.IsChecked = _bytes.IsVisible = settings.CompareShowBytes;
        _showBytes.IsCheckedChanged += (_, _) =>
        {
            _bytes.IsVisible = _showBytes.IsChecked == true;
            App.Settings.Update(x => x with { CompareShowBytes = _bytes.IsVisible });
            UpdateCaretUi();
        };
        _leftState.ShowUnimportant = _rightState.ShowUnimportant = !ignore;
        UpdateViewFilterButton();
        _ignoreUnimportant.IsCheckedChanged += (_, _) =>
        {
            var ignore = _ignoreUnimportant.IsChecked == true;
            _leftState.ShowUnimportant = _rightState.ShowUnimportant = !ignore;
            App.Settings.Update(x => x with { CompareIgnoreUnimportant = ignore });
            UpdateStatus();
            Redraw();
        };
        _findBox.TextChanged += (_, _) => Find(forward: true, fromSelectionStart: true);
        _findCase.IsCheckedChanged += (_, _) => Find(forward: true, fromSelectionStart: true);
        _recompareTimer.Tick += async (_, _) =>
        {
            _recompareTimer.Stop();
            await CommitEditsAsync();
        };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dropHint.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Strings.Changed += OnLanguageChanged;
        ThemeService.Applied += OnThemeApplied;
    }

    /// <summary>색상 구성표(또는 앱 테마)가 바뀌면 문법 색을 새 테마로 다시 입힌다 (바탕·글자·차이 색은 자원이라 저절로 바뀐다).</summary>
    void OnThemeApplied() => Dispatcher.UIThread.Post(() =>
    {
        foreach (var side in new[] { _left, _right })
        {
            ApplySchemeColors(side);
            if (side.HasText) InstallHighlighting(side, side.Real.Length);
        }
        Redraw();
    });

    /// <summary>구성표가 정한 선택 영역 색을 편집기에 준다 (구성표가 없으면 기본 색으로 되돌린다).</summary>
    void ApplySchemeColors(Side side)
    {
        var area = side.Editor.TextArea;
        if (ThemeService.EditorScheme is { } scheme && EditorSchemes.ColorsOf(scheme)?.Selection is { } selection)
            area.SelectionBrush = new SolidColorBrush(Color.FromUInt32(selection));
        else area.ClearValue(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty);
    }

    // --- 탭 호스트가 부르는 부분 (ICompareTab) ---

    public Control View => this;
    public bool IsFolder => false;
    public string TabIcon => "file";
    public event Action? TitleChanged;
    public string WindowTitle { get; private set; } = "";
    public string TabTitle { get; private set; } = "";
    public string? TabToolTip => string.Join('\n', new[] { _left.Path, _right.Path }.Where(p => !string.IsNullOrEmpty(p)));
    public bool IsDirty => _left.Dirty || _right.Dirty;

    /// <summary>지금의 비교를 세션에 적을 모양으로 (경로 + 보기 필터·문맥 줄 수·중요하지 않은 차이 무시·고정한 파일 형식).</summary>
    public SessionTab Capture() => new()
    {
        Kind = SessionTab.File,
        Left = _left.Path ?? "",
        Right = _right.Path ?? "",
        ViewFilter = SessionTab.ViewFilterName(_filter),
        ContextLines = _contextLines,
        IgnoreUnimportant = _ignoreUnimportant.IsChecked == true,
        FormatId = _formatId,
    };

    public void Announce(string text) => Notice(text);

    /// <summary>탭이 처음 보일 때 한 번 (창의 <c>Opened</c>를 대신한다).</summary>
    public async void Start()
    {
        await ReloadAsync();
        RecordHistory();
    }

    /// <summary>탭을 닫기 전에: 고친 것이 있으면 저장·버리기·취소를 묻는다.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (_editBatchOpen) { _left.Real = _left.CanEdit ? ExtractReal(_left) : _left.Real; _right.Real = _right.CanEdit ? ExtractReal(_right) : _right.Real; _editBatchOpen = false; }
        var dirty = new[] { _left, _right }.Where(x => x.Dirty).ToList();
        if (dirty.Count == 0) return true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(Owner, s.CompareUnsavedTitle, s.CompareUnsavedBody(string.Join(", ", dirty.Select(d => d.Name))),
            [s.CommonCancel, s.CompareDontSave, s.CommonSave], 2, 0);
        if (choice <= 0) return false;
        if (choice == 2)
            foreach (var side in dirty)
                if (!await SaveAsync(side)) return false;
        return true;
    }

    /// <summary>탭이 닫힐 때 (창이 닫히거나 탭을 닫은 뒤) 자원 정리.</summary>
    public void Dispose()
    {
        Strings.Changed -= OnLanguageChanged;
        ThemeService.Applied -= OnThemeApplied;
        _recompareTimer.Stop();
        foreach (var side in new[] { _left, _right })
        {
            side.TextMate?.Dispose();
            side.Watcher?.Dispose();
        }
    }

    /// <summary>같은 자리의 탭을 바이너리 비교로 바꾼다 (두 쪽이 모두 파일이고 비교 창 안에 있을 때). 바꿨으면 true.</summary>
    bool SwitchToBinary()
    {
        if (_left.Path is null || _right.Path is null || TopLevel.GetTopLevel(this) is not CompareHost host) return false;
        _version++; // 이 보기의 읽기를 끝낸다
        host.ReplaceTab(this, BinaryCompareView.Create(_left.Path, _right.Path, null, recordHistory: !_skipHistory));
        return true;
    }

    /// <summary>같은 자리의 탭을 그림 비교로 바꾼다 (정책이 이 파일을 그림으로 보라고 할 때 — <c>*.png</c> 등). 바꿨으면 true.</summary>
    bool SwitchToImage()
    {
        if (_left.Path is null || _right.Path is null || TopLevel.GetTopLevel(this) is not CompareHost host) return false;
        _version++;
        host.ReplaceTab(this, ImageCompareView.Create(_left.Path, _right.Path, null, recordHistory: !_skipHistory));
        return true;
    }

    /// <summary>두 쪽이 모두 파일이면 최근 비교 목록에 기록한다 (붙여 넣은 글은 경로가 없어 건너뛴다).</summary>
    void RecordHistory()
    {
        if (!_skipHistory) App.RecordHistory(_left.Path, _right.Path, folders: false);
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateStatus();
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
        UpdateViewFilterButton();
        UpdateFormatButton();
        ToolTip.SetTip(_formatButton, Strings.Current.CompareFormatMenuTooltip);
        ToolTip.SetTip(_showBytes, Strings.Current.CompareShowBytes);
        UpdateCaretUi();
        _findBox.PlaceholderText = Strings.Current.CompareFindPlaceholder;
        _replaceBox.PlaceholderText = Strings.Current.CompareReplacePlaceholder;
        UpdateFindWhere();
        ApplyFolds(); // 접힌 표시 문구
    });

    // --- 배치 ---

    Control BuildLayout()
    {
        var s = Strings.Current;
        Button Tool(string icon, string tip, Action click, Func<string>? label = null, int priority = 1)
        {
            var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
            if (label is not null) AdaptiveToolbar.Labeled(b, label, priority);
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        _ignoreUnimportant.Content = new DuoIcon { Icon = "visible-off" };
        AdaptiveToolbar.Labeled(_ignoreUnimportant, () => Strings.Current.ToolLabelIgnore, 6);
        ToolTip.SetTip(_ignoreUnimportant, s.CompareIgnoreUnimportant);
        _showBytes.Content = new DuoIcon { Icon = "memory" };
        AdaptiveToolbar.Labeled(_showBytes, () => Strings.Current.ToolLabelBytes, 7);
        ToolTip.SetTip(_showBytes, s.CompareShowBytes);
        _save.Content = new DuoIcon { Icon = "save" };
        AdaptiveToolbar.Labeled(_save, () => Strings.Current.CommonSave, 2);
        ToolTip.SetTip(_save, s.CompareSave);
        _save.Click += async (_, _) => await SaveAllAsync();
        ToolTip.SetTip(_viewFilter, s.CompareViewFilterTooltip);
        ToolTip.SetTip(_formatButton, s.CompareFormatMenuTooltip);
        _formatButton.Click += async (_, e) => { e.Handled = true; await ShowFormatMenuAsync(); };
        UpdateFormatButton();
        var commands = AdaptiveToolbar.Labeled(new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "view-list" } }, () => Strings.Current.ToolLabelCommands, 9);
        ToolTip.SetTip(commands, s.CompareCommandsTooltip);
        commands.Click += (_, _) => CommandsMenu().ShowAt(commands);
        var Separator = AdaptiveToolbar.Separator;
        // 툴바는 BC 텍스트 비교의 묶음 순서를 따른다: 보기 필터 · 중요하지 않은 차이 무시 | 복사 · 저장 | 이전·다음 구간 | 좌우 바꾸기 · 다시 읽기 | 찾기 · 모든 명령.
        // 복사 단추는 사용자가 고른 부분이 있을 때만 켜진다 (UpdateMergeUi) — 파일 전체를 복사하는 단추는 없다.
        var toolCopyLeft = _toolCopyLeft = Tool("back", Tip(s.CompareCopySelectionToLeft, "Ctrl+L"), () => _ = CopySelectionAsync(toRight: false), () => Strings.Current.ToolLabelCopyLeft, 2);
        var toolCopyRight = _toolCopyRight = Tool("forward", Tip(s.CompareCopySelectionToRight, "Ctrl+R"), () => _ = CopySelectionAsync(toRight: true), () => Strings.Current.ToolLabelCopyRight, 2);
        toolCopyLeft.IsEnabled = toolCopyRight.IsEnabled = false;
        var tools = new AdaptiveToolbar
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _viewFilter,
                _formatButton,
                _ignoreUnimportant,
                _showBytes,
                Separator(),
                toolCopyLeft,
                toolCopyRight,
                _save,
                Separator(),
                Tool("up", Tip(s.ComparePrevSection, "Ctrl+P"), () => GoDiff(next: false), () => Strings.Current.ToolLabelPrevDiff, 1),
                Tool("down", Tip(s.CompareNextSection, "Ctrl+N"), () => GoDiff(next: true), () => Strings.Current.ToolLabelNextDiff, 1),
                Separator(),
                Tool("swap", Tip(s.CompareSwap, "Ctrl+U"), () => _ = SwapAsync(), () => Strings.Current.ToolLabelSwap, 5),
                Tool("refresh", Tip(s.CompareReload, "F5"), () => _ = ReloadWithConfirmAsync(), () => Strings.Current.ToolLabelRefresh, 4),
                Tool("bookmark", s.SessionSaveMenu, () => _ = (TopLevel.GetTopLevel(this) as CompareHost)?.SaveSessionAsync(), () => Strings.Current.ToolLabelSession, 8),
                Tool("search", Tip(s.CompareFindMenu, "Ctrl+F"), () => OpenFind(), () => Strings.Current.ToolLabelFind, 3),
                commands,
                Separator(),
                Tool("settings", s.SettingsTooltip, () => SettingsWindow.Open(Owner), () => Strings.Current.ToolLabelSettings, 11),
                Tool("info", s.AboutTooltip, () => AboutDialog.Open(Owner), () => Strings.Current.ToolLabelAbout, 12),
            },
        };
        var appBar = new Border
        {
            Height = 52,
            Padding = new Thickness(14, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) { MaxWidth = 380 }, new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 12,
                Children = { _status, tools },
            },
        };
        Grid.SetColumn(tools, 1);
        appBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        appBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("52,Auto,Auto,Auto,*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,32,*,22"),
        };
        Place(grid, appBar, 0, 0, 4);
        Place(grid, BuildFindBar(), 1, 0, 4);
        foreach (var side in new[] { _left, _right })
        {
            var col = side.Left ? 0 : 2;
            Place(grid, PathRow(side), 2, col, side.Left ? 1 : 2);
            Place(grid, BannerFor(side), 3, col, side.Left ? 1 : 2);
            var body = new Grid { Children = { side.Editor, side.Overlay } };
            body.Bind(Panel.BackgroundProperty, this.GetResourceObservable("AppEdBg")); // 편집기 바탕 — 위의 틀 띠와 한 단 다르게
            side.Overlay.Child = side.OverlayText;
            side.OverlayText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppEdMid"));
            Place(grid, body, 4, col, 1);
        }
        // 두 편집기 사이의 가운데 영역: 고른 행 옆에 → / ← 가 뜬다 (BC 병합 도구의 가운데 띠처럼).
        _mergeGutter.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _mergeGutter.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        Place(grid, _mergeGutter, 2, 1, 1, rowSpan: 3);
        _mergeLayer.Children.Add(_copyRightButton);
        _mergeLayer.Children.Add(_copyLeftButton);
        _copyRightButton.Click += (_, _) => _ = CopySelectionAsync(toRight: true);
        _copyLeftButton.Click += (_, _) => _ = CopySelectionAsync(toRight: false);
        Place(grid, _mergeLayer, 4, 1, 1);
        Place(grid, _overview, 4, 3, 1);
        Place(grid, _bytes, 5, 0, 4);
        Place(grid, BuildStatusBar(), 6, 0, 4);
        _overview.State = _leftState;

        _dropHint.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppPrimary"));
        Place(grid, _dropHint, 2, 0, 1, rowSpan: 3);
        return grid;
    }

    Control BuildFindBar()
    {
        var s = Strings.Current;
        Button Tool(string icon, string tip, Action click)
        {
            var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        _findBox.PlaceholderText = s.CompareFindPlaceholder;
        ToolTip.SetTip(_findCase, s.CompareFindMatchCase);
        _findCount.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _findWhere.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var close = Tool("close", s.CommonClose, CloseFind);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new DuoIcon { Icon = "search", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                        _findBox,
                        Tool("up", Tip(s.CompareFindPreviousMenu, "Shift+F3"), () => Find(forward: false)),
                        Tool("down", Tip(s.CompareFindNextMenu, "F3"), () => Find(forward: true)),
                        _findCase,
                        _findCount,
                        _findWhere,
                    },
                },
                close,
            },
        };
        Grid.SetColumn(close, 2);
        // 바꾸기 줄 (BC Search > Replace, Ctrl+H): 찾을 쪽에서 지금 고른 일치를 바꾸거나 모두 바꾼다.
        _replaceBox.PlaceholderText = s.CompareReplacePlaceholder;
        var replaceOne = UiScale.Enable(new Button { Content = s.CompareReplaceOne, Classes = { "compact" } });
        var replaceAll = UiScale.Enable(new Button { Content = s.CompareReplaceAll, Classes = { "compact" } });
        replaceOne.Click += (_, _) => ReplaceOne();
        replaceAll.Click += (_, _) => ReplaceAll();
        _replaceRow.Children.AddRange([new DuoIcon { Icon = "replace", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, _replaceBox, replaceOne, replaceAll]);
        _findBar.Child = new StackPanel { Children = { row, _replaceRow } };
        _findBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _findBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        return _findBar;
    }

    // --- 보기 필터 (BC View > Show All / Show Differences / Show Same / Show Context) ---

    string FilterName(CompareViewFilter f) => f switch
    {
        CompareViewFilter.Differences => Strings.Current.CompareShowDifferences,
        CompareViewFilter.Same => Strings.Current.CompareShowSame,
        CompareViewFilter.Context => Strings.Current.CompareShowContext,
        _ => Strings.Current.CompareShowAll,
    };

    void UpdateViewFilterButton()
    {
        var s = Strings.Current;
        _viewFilter.Content = _filter == CompareViewFilter.Context ? $"{FilterName(_filter)} ({_contextLines})" : FilterName(_filter);
        var flyout = new MenuFlyout();
        foreach (var item in ViewFilterItems()) flyout.Items.Add(item);
        _viewFilter.Flyout = flyout;
    }

    /// <summary>보기 필터 항목들 (툴바 드롭다운과 명령 메뉴의 보기 묶음이 같이 쓴다).</summary>
    IEnumerable<Control> ViewFilterItems()
    {
        var s = Strings.Current;
        foreach (var f in new[] { CompareViewFilter.All, CompareViewFilter.Differences, CompareViewFilter.Same, CompareViewFilter.Context })
        {
            var item = new MenuItem { Header = FilterName(f), ToggleType = MenuItemToggleType.Radio, IsChecked = _filter == f, GroupName = "view-filter" };
            var target = f;
            item.Click += (_, _) => SetViewFilter(target);
            yield return item;
        }
        var lines = new MenuItem { Header = s.CompareContextLinesMenu };
        foreach (var n in ContextChoices)
        {
            var item = new MenuItem { Header = s.CompareContextLines(n), ToggleType = MenuItemToggleType.Radio, IsChecked = _contextLines == n, GroupName = "context-lines" };
            var count = n;
            item.Click += (_, _) =>
            {
                _contextLines = count;
                App.Settings.Update(x => x with { CompareContextLines = count });
                SetViewFilter(CompareViewFilter.Context);
            };
            lines.Items.Add(item);
        }
        yield return new Separator();
        yield return lines;
    }

    void SetViewFilter(CompareViewFilter filter)
    {
        _filter = filter;
        App.Settings.Update(x => x with { CompareViewFilter = filter });
        UpdateViewFilterButton();
        ApplyFolds();
    }

    // --- 모든 명령 (BC의 메뉴: Session · File · Edit · Search · View) ---

    /// <summary>
    /// 툴바에 없는 명령까지 BC 메뉴 묶음대로 모아 단축키와 함께 보인다. 아이콘은 BC와 달라도 기능과 키는 같게 (BC_COMPAT.md).
    /// </summary>
    MenuFlyout CommandsMenu()
    {
        var s = Strings.Current;
        MenuItem Item(string header, string? gesture, Action run, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (gesture is not null) item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => run();
            return item;
        }
        MenuItem Group(string header, params Control[] items)
        {
            var g = new MenuItem { Header = header };
            foreach (var i in items) g.Items.Add(i);
            return g;
        }
        var focused = FocusedSide();
        var view = Group(s.CompareMenuView, [.. ViewFilterItems(), new Separator(),
            Item(s.CompareIgnoreUnimportant, null, () => _ignoreUnimportant.IsChecked = _ignoreUnimportant.IsChecked != true),
            Item(s.CompareShowBytes, null, () => _showBytes.IsChecked = _showBytes.IsChecked != true)]);
        var flyout = new MenuFlyout();
        flyout.Items.Add(Group(s.CompareMenuSession,
            Item(s.CompareSwap, "Ctrl+U", () => _ = SwapAsync()),
            Item(s.CompareReload, "F5", () => _ = ReloadWithConfirmAsync()),
            Item(s.CompareRecompareNow, "Ctrl+F5", () => _ = RecompareNowAsync()),
            Item(s.CompareInfo, "Ctrl+I", () => _ = ShowInfoAsync()),
            new Separator(),
            Item(s.SessionSaveMenu, null, () => _ = (TopLevel.GetTopLevel(this) as CompareHost)?.SaveSessionAsync())));
        flyout.Items.Add(Group(s.CompareMenuFile,
            Item(s.CompareOpenFile, "Ctrl+O", () => _ = PickAsync(focused)),
            Item(s.CompareOpenClipboard, "Ctrl+Shift+V", () => _ = PasteAsync(focused)),
            Item(s.CompareSaveFile, "Ctrl+S", () => _ = SaveFocusedAsync(), focused.Dirty || (focused.Left ? _right : _left).Dirty),
            Item(s.CompareSaveFileAs, "F12", () => _ = SaveAsAsync(focused), focused.HasText)));
        flyout.Items.Add(Group(s.CompareMenuEdit,
            Item(s.CompareUndo, "Ctrl+Z", () => _ = UndoAsync(redo: false)),
            Item(s.CompareRedo, "Ctrl+Y", () => _ = UndoAsync(redo: true)),
            new Separator(),
            Item(s.CompareCopySelectionToRight, "Ctrl+R", () => _ = CopySelectionAsync(toRight: true)),
            Item(s.CompareCopySelectionToLeft, "Ctrl+L", () => _ = CopySelectionAsync(toRight: false)),
            Item(s.CompareCopyLineToRight, "Ctrl+Shift+R", () => _ = CopyLineAsync(toRight: true)),
            Item(s.CompareCopyLineToLeft, "Ctrl+Shift+L", () => _ = CopyLineAsync(toRight: false)),
            new Separator(),
            Item(s.CompareSelectSection, "Ctrl+D", SelectSection)));
        flyout.Items.Add(Group(s.CompareMenuSearch,
            Item(s.CompareNextSection, "Ctrl+N", () => GoDiff(next: true)),
            Item(s.ComparePrevSection, "Ctrl+P", () => GoDiff(next: false)),
            Item(s.CompareNextLine, "Ctrl+Shift+N", () => GoDiffLine(next: true)),
            Item(s.ComparePrevLine, "Ctrl+Shift+P", () => GoDiffLine(next: false)),
            new Separator(),
            Item(s.CompareFindMenu, "Ctrl+F", OpenFind),
            Item(s.CompareReplaceMenu, "Ctrl+H", () => OpenFind(replace: true)),
            Item(s.CompareFindNextMenu, "F3", () => Find(forward: true)),
            Item(s.CompareFindPreviousMenu, "Shift+F3", () => Find(forward: false)),
            Item(s.CompareGoTo, "Ctrl+G", () => _ = GoToLineAsync())));
        flyout.Items.Add(view);
        return flyout;
    }

    Side FocusedSide() => _left.Editor.TextArea.IsFocused ? _left : _right.Editor.TextArea.IsFocused ? _right : _findSide ?? _right;

    /// <summary>툴팁: 이름 (단축키). macOS는 Ctrl을 ⌘로 보인다.</summary>
    static string Tip(string name, string key) => $"{name} ({(OperatingSystem.IsMacOS() ? key.Replace("Ctrl+", "⌘") : key)})";

    static void Place(Grid grid, Control c, int row, int col, int colSpan, int rowSpan = 1)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        Grid.SetColumnSpan(c, colSpan);
        Grid.SetRowSpan(c, rowSpan);
        grid.Children.Add(c);
    }

    Control PathRow(Side side)
    {
        var open = UiScale.Enable(new Button { Content = Strings.Current.CompareOpen, Classes = { "compact" } });
        open.Click += async (_, _) => await PickAsync(side);
        var paste = UiScale.Enable(new Button { Content = Strings.Current.CompareOpenClipboard, Classes = { "compact" } });
        ToolTip.SetTip(paste, Tip(Strings.Current.CompareOpenClipboard, "Ctrl+Shift+V"));
        paste.Click += async (_, _) => await PasteAsync(side);
        var icon = new DuoIcon { Icon = "file", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        side.PathText.PlaceholderText = side.Left ? Strings.Current.CompareOpenLeftTitle : Strings.Current.CompareOpenRightTitle;
        // 고치기 시작하면 표시용 글(● 표시·제목·"아직 고르지 않음")을 실제 경로로 바꾸고, 나가면 표시용으로 되돌린다.
        side.PathText.GotFocus += (_, _) =>
        {
            if (side.PathEditing) return;
            side.PathEditing = true;
            side.PathText.Text = side.Path ?? "";
            side.PathText.SelectAll();
        };
        side.PathText.LostFocus += (_, _) =>
        {
            side.PathEditing = false;
            UpdateSideChrome(side);
        };
        side.PathText.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ApplyTypedPathAsync(side); }
            else if (e.Key == Key.Escape) { e.Handled = true; side.Editor.TextArea.Focus(); }
        }, RoutingStrategies.Tunnel);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 6),
            Children = { icon, side.PathText, side.EncodingText, paste, open },
        };
        Grid.SetColumn(side.PathText, 1);
        Grid.SetColumn(side.EncodingText, 2);
        Grid.SetColumn(paste, 3);
        Grid.SetColumn(open, 4);
        side.EncodingText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        side.EncodingText.PointerPressed += (_, _) => { if (side.HasText && !side.IsScratch) FormatFlyout(side).ShowAt(side.EncodingText); };
        side.InfoText.Margin = new Thickness(12, 0, 12, 6);
        side.InfoText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var border = new Border { Child = new StackPanel { Children = { row, side.InfoText } }, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        return border;
    }

    Control BannerFor(Side side)
    {
        side.Banner.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppDiffChanged"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { side.BannerText, side.BannerActions } };
        Grid.SetColumn(side.BannerActions, 1);
        side.Banner.Child = grid;
        return side.Banner;
    }

    void SetupEditor(Side side)
    {
        var e = side.Editor;
        e.IsReadOnly = true;
        e.Bind(TextEditor.FontFamilyProperty, this.GetResourceObservable("AppEditorFont")); // 설정의 편집기 글꼴 (고정폭)
        e.Bind(TextEditor.FontSizeProperty, this.GetResourceObservable("FsBody"));
        e.Bind(TextEditor.ForegroundProperty, this.GetResourceObservable("AppEdFg"));
        e.Background = Brushes.Transparent;
        e.ShowLineNumbers = false;
        e.WordWrap = false; // 줄바꿈하면 좌우 행이 어긋난다
        e.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        e.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; // 세로 스크롤 막대는 오른쪽 개요 막대 하나뿐 (휠·키보드는 그대로 된다)
        e.Options.EnableHyperlinks = false;
        e.Options.EnableEmailHyperlinks = false;
        e.Options.HighlightCurrentLine = false; // 현재 줄 강조가 차이 색과 섞여 색이 달라 보인다
        e.Document = new AvaloniaEdit.Document.TextDocument();
        var state = side.Left ? _leftState : _rightState;
        side.Margin = new RowNumberMargin(state, side.Left);
        e.TextArea.LeftMargins.Insert(0, side.Margin);
        e.TextArea.TextView.BackgroundRenderers.Add(new DiffBackgroundRenderer(state, side.Left));
        e.TextArea.TextView.LineTransformers.Add(new IntralineColorizer(state, side.Left));
        side.Folding = FoldingManager.Install(e.TextArea);
        // 접기 여백의 [+]/[−]로 한쪽을 펴거나 접으면 맞은편도 똑같이 (그래야 행이 나란히 남는다).
        foreach (var margin in e.TextArea.LeftMargins.OfType<FoldingMargin>())
            margin.AddHandler(PointerPressedEvent, (_, _) => Dispatcher.UIThread.Post(() => MirrorFolds(side)), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        e.TextArea.GotFocus += (_, _) =>
        {
            _findSide = side;
            UpdateFindWhere();
        };
        e.TextArea.TextView.BackgroundRenderers.Add(new MergeFrameRenderer(state));
        e.TextArea.SelectionChanged += (_, _) => OnSelectionChanged(side);
        // 글 자리를 누르면 이전에 줄 번호로 고른 범위는 풀린다 (끌어서 새로 고르면 그것이 새 범위가 된다).
        e.TextArea.AddHandler(PointerPressedEvent, (_, args) => OnTextAreaPressed(side, args), RoutingStrategies.Tunnel);
        side.Margin.Cursor = new Cursor(StandardCursorType.Hand); // 줄 번호 자리는 눌러서 줄을 고른다
        side.Margin.PointerPressed += (_, args) => OnGutterPressed(side, args);
        side.Margin.PointerMoved += (_, args) => OnGutterMoved(side, args);
        side.Margin.PointerReleased += (_, args) => { _gutterAnchor = -1; args.Pointer.Capture(null); };
        e.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_applying) { UpdateCaretUi(); return; } // 다시 비교를 화면에 옮기는 동안은 캐럿을 맞추지 않는다 — 패널만 새로 그린다
            var other = side.Left ? _right : _left;
            _syncing = true;
            other.Editor.TextArea.Caret.Line = Math.Min(e.TextArea.Caret.Line, other.Editor.Document.LineCount); // 같은 행 = 맞은편의 대응 줄
            _syncing = false;
            UpdateCaretUi();
        };
        e.TextArea.PropertyChanged += (_, args) => { if (args.Property == AvaloniaEdit.Editing.TextArea.OverstrikeModeProperty) UpdateStatusBar(); };
        e.Document.TextChanged += (_, _) => OnEdited(side);
        e.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextEditor.FontSizeProperty || args.Property == TextEditor.FontFamilyProperty)
                side.Margin.SetFont(e.FontFamily, e.FontSize * 0.9);
        };
    }

    // --- 읽기·비교 ---

    /// <summary>두 파일을 디스크에서 다시 읽고 비교한다. 고친 내용은 버려진다 (부르는 쪽이 먼저 확인한다).</summary>
    async Task ReloadAsync()
    {
        _recompareTimer.Stop();
        var version = ++_version;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _status.Text = Strings.Current.CompareLoading;
        // 정책이 이 파일을 바이너리로 보라고 하면(*.bin 등) 읽기 전에 바이너리 비교로 바꾼다.
        if (_left.Path is { } nameL && _right.Path is not null)
        {
            var policy = await App.Policy.LoadAsync();
            var viewKind = policy.Resolve(System.IO.Path.GetFileName(nameL.Replace(ArchivePaths.Separator, "/")), namesIgnoreCase: PolicyStore.NamesIgnoreCase, formatId: _formatId).View;
            if (viewKind == DaylightCompare.Domain.Policy.ViewKind.Binary && SwitchToBinary()) return;
            if (viewKind == DaylightCompare.Domain.Policy.ViewKind.Image && SwitchToImage()) return;
            if (version != _version) return;
        }
        // 파일이 없는 칸은 지금 글(붙여 넣거나 쓴 것)을 그대로 쓴다.
        var sources = await Task.WhenAll(
            _left.Path is { } lp ? CompareSources.LoadAsync(lp, _left.ForcedEncoding) : Task.FromResult(ScratchSource(_left.Real)),
            _right.Path is { } rp ? CompareSources.LoadAsync(rp, _right.ForcedEncoding) : Task.FromResult(ScratchSource(_right.Real)));
        if (version != _version) return;
        // NUL이 있어 글이 아니거나 텍스트로 열기에는 너무 큰 파일: 바이너리(16진수) 비교로 바꾼다 (BC도 이런 파일은 Hex 비교로 연다).
        if (sources.Any(x => x is CompareSource.Binary or CompareSource.TooLarge) && SwitchToBinary()) return;
        foreach (var (side, source) in new[] { (_left, sources[0]), (_right, sources[1]) })
        {
            side.Source = source;
            side.Repair = null;
            side.ChangedOutside = false;
            if (side.RepairRequested && side.Text is { } t)
            {
                // 섞인 인코딩 복구를 고른 쪽은 복구한 글을 쓴다 (다시 읽어도 유지). 복구한 글은 UTF-8로 저장된다.
                var (file, repair) = TextFiles.DecodeMixed(t.Bytes, t.File.Modified, t.File.Encoding, OtherEncoding(t.File.Encoding));
                side.Source = t with { File = file with { Text = TextFiles.NormalizeLineBreaks(file.Text) } };
                side.Repair = repair;
            }
            side.Real = side.Text?.File.Text ?? "";
            side.LoadedFormat = side.Text is { } lf ? (lf.File.Encoding, lf.File.HasBom, lf.File.LineBreak) : null;
            // 복구한 글은 디스크와 다르므로 "고친 상태"로 시작한다 — 저장해야 파일이 고쳐진다.
            side.SavedText = side.Repair is not null ? (sources[side.Left ? 0 : 1] as CompareSource.Text)?.File.Text ?? "" : side.Real;
            side.EndsWithNewline = side.Real.EndsWith('\n');
            Watch(side);
        }
        _undo.Clear();
        _redo.Clear();
        await RecompareAsync(version, keepCaret: null, jumpToFirst: true);
        if (version != _version) return;
        _loadSeconds = watch.Elapsed.TotalSeconds;
        UpdateCaretUi();
    }

    static CompareSource ScratchSource(string text) =>
        new CompareSource.Text("", new EditorTextFile(text, HasBom: false, LineBreak: "\n", DateTime.MinValue, text.Length, Binary: false), []);

    /// <summary>
    /// 클립보드의 글로 그 칸을 바꾼다 (파일 없는 칸이 된다 — ROADMAP §6-1 클립보드 비교). 고친 것이 있으면 먼저 묻는다.
    /// 빈 칸에는 그냥 붙여 넣어도(Ctrl+V) 된다.
    /// </summary>
    async Task PasteAsync(Side side)
    {
        var text = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text))
        {
            Notice(Strings.Current.CompareClipboardEmpty);
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        side.Path = null;
        side.Title = null;
        side.ForcedEncoding = null;
        side.RepairRequested = false;
        side.Real = TextFiles.NormalizeLineBreaks(text);
        await ReloadAsync();
    }

    /// <summary>지금의 실제 글로 다시 비교하고 화면 글을 바꾼다. <paramref name="keepCaret"/>이 있으면 그 쪽의 그 실제 줄로 캐럿을 옮긴다.</summary>
    async Task RecompareAsync(int version, (bool Left, int Line, int Column)? keepCaret, bool jumpToFirst = false)
    {
        var offset = _right.Editor.VerticalOffset;
        AlignedDiff? aligned = null;
        if (_left.HasText && _right.HasText)
        {
            var policy = await App.Policy.LoadAsync();
            var name = System.IO.Path.GetFileName(_left.Path ?? _right.Path ?? "");
            var effective = policy.Resolve(name, namesIgnoreCase: PolicyStore.NamesIgnoreCase, formatId: _formatId);
            _detectedFormat = _formatId is null ? effective.Format?.Id : _detectedFormat;
            _highlightExtension = HighlightExtensionOf(policy, effective);
            var (l, r) = (_left.Real, _right.Real);
            aligned = await Task.Run(() => new AlignedDiff(TextDiff.Compare(l, r, effective)));
            if (version != _version) return;
        }
        _applying = true;
        foreach (var (side, state) in new[] { (_left, _leftState), (_right, _rightState) })
        {
            state.Diff = aligned;
            state.Editing = false;
            state.Intraline.Clear();
            var text = aligned is null ? side.Real : side.Left ? aligned.LeftText : aligned.RightText;
            var doc = side.Editor.Document;
            if (doc.Text != text) doc.Text = text;
            doc.UndoStack.ClearAll(); // 실행 취소는 창이 실제 글 단위로 한다
            side.Fillers.Clear();
            if (aligned is not null)
                for (var row = 0; row < aligned.Rows.Count; row++)
                    if ((side.Left ? aligned.Rows[row].Left : aligned.Rows[row].Right) is null)
                    {
                        var anchor = doc.CreateAnchor(doc.GetLineByNumber(row + 1).Offset);
                        anchor.MovementType = AnchorMovementType.BeforeInsertion;
                        side.Fillers.Add(anchor);
                    }
            side.Editor.IsReadOnly = !side.CanEdit;
            if (side.HighlightedPath != HighlightKey(side)) InstallHighlighting(side, text.Length);
            UpdateSideChrome(side);
        }
        _applying = false;
        _merge = null; // 글이 다시 만들어지면 행 번호가 달라지므로 고른 범위를 풀고, 틀과 단추를 지운다
        UpdateMergeUi();
        ApplyFolds(redraw: false);
        UpdateTitle();
        UpdateStatus();
        UpdateFormatButton();
        Redraw();

        if (keepCaret is { } k && aligned is not null)
        {
            var row = k.Left ? aligned.RowOfLeftLine(k.Line) : aligned.RowOfRightLine(k.Line);
            var side = k.Left ? _left : _right;
            ScrollTo(_right, null, offset);
            Dispatcher.UIThread.Post(() =>
            {
                _syncing = true;
                side.Editor.TextArea.Caret.Line = Math.Min(row + 1, side.Editor.Document.LineCount);
                side.Editor.TextArea.Caret.Column = k.Column;
                _syncing = false;
                UpdateCaretUi();
            }, DispatcherPriority.Background);
        }
        else if (!jumpToFirst) Dispatcher.UIThread.Post(() => ScrollTo(_right, null, offset), DispatcherPriority.Background);
        else Dispatcher.UIThread.Post(() =>
        {
            if (aligned?.Next(-1) is { } first) ScrollToRow(first.StartRow, center: true);
        }, DispatcherPriority.Background);
    }

    void InstallHighlighting(Side side, int length)
    {
        side.TextMate?.Dispose();
        side.TextMate = null;
        side.HighlightedPath = HighlightKey(side);
        if (length > HighlightMaxChars || side.Path is null) return;
        // 형식을 고른 비교는 그 형식의 문법으로, 자동이면 파일 이름의 확장자로 강조한다.
        var ext = _formatId is null ? EditorLanguages.ExtensionFor(System.IO.Path.GetFileName(side.Path.Replace(ArchivePaths.Separator, "/"))) : _highlightExtension;
        if (ext is null) return;
        try
        {
            var options = new RegistryOptions(ThemeService.EditorScheme?.Theme ?? (ThemeService.IsDark(Application.Current!) ? ThemeName.DarkPlus : ThemeName.LightPlus));
            if (options.GetLanguageByExtension(ext) is not { } language) return;
            side.TextMate = side.Editor.InstallTextMate(EditorSchemes.Wrap(options, ThemeService.EditorScheme));
            side.TextMate.SetGrammar(options.GetScopeByLanguageId(language.Id));
            side.Editor.Background = Brushes.Transparent;
            // 차이 강조가 강조(TextMate) 색 위에 오도록 줄 안 강조를 맨 뒤로 옮긴다.
            var transformers = side.Editor.TextArea.TextView.LineTransformers;
            foreach (var m in transformers.OfType<IntralineColorizer>().ToList()) { transformers.Remove(m); transformers.Add(m); }
        }
        catch (Exception)
        {
            side.TextMate?.Dispose();
            side.TextMate = null;
        }
    }

    static TextEncodingKind OtherEncoding(TextEncodingKind primary) =>
        primary == TextEncodingKind.Utf8 ? TextEncodingKind.Cp949 : TextEncodingKind.Utf8;

    // --- 편집 ---

    void OnEdited(Side side)
    {
        if (_applying) return;
        if (!_editBatchOpen)
        {
            // 한 번에 몰아 친 글자들은 실행 취소 한 번으로 되돌린다 (다시 비교할 때까지가 한 묶음).
            _undo.Push((_left.Real, _right.Real));
            _redo.Clear();
            _editBatchOpen = true;
        }
        _leftState.Editing = _rightState.Editing = true;
        ClearMerge();
        _save.IsEnabled = true;
        _recompareTimer.Stop();
        _recompareTimer.Start();
    }

    /// <summary>화면 글에서 실제 글을 꺼낸다 (채움 줄 앵커 중 지워지지 않았고 아직 빈 줄인 것만 뺀다).</summary>
    static string ExtractReal(Side side)
    {
        var doc = side.Editor.Document;
        var fillers = side.Fillers.Where(a => !a.IsDeleted).Select(a => a.Line - 1).ToHashSet();
        if (fillers.Count == 0 && side.Fillers.Count == 0 && !side.HasText) return side.Real;
        return DiffEdits.RealText(doc.Text, fillers, side.EndsWithNewline);
    }

    /// <summary>캐럿이 있는 쪽의 실제 줄 번호 (화면 행에서 그 앞의 채움 줄을 뺀 것).</summary>
    (bool Left, int Line, int Column) CaretReal()
    {
        var side = _left.Editor.TextArea.IsFocused ? _left : _right;
        var row = side.Editor.TextArea.Caret.Line - 1;
        var fillersBefore = side.Fillers.Count(a => !a.IsDeleted && a.Line - 1 < row && side.Editor.Document.GetLineByNumber(a.Line).Length == 0);
        return (side.Left, Math.Max(0, row - fillersBefore), side.Editor.TextArea.Caret.Column);
    }

    /// <summary>고친 내용을 실제 글로 옮기고 다시 비교한다 (손을 멈춘 뒤, 또는 저장·복사·실행 취소 전).</summary>
    async Task CommitEditsAsync()
    {
        _recompareTimer.Stop();
        if (!_editBatchOpen) return;
        _editBatchOpen = false;
        var caret = CaretReal();
        foreach (var side in new[] { _left, _right })
            if (side.CanEdit) side.Real = ExtractReal(side);
        await RecompareAsync(++_version, caret);
    }

    async Task SetRealAsync(string left, string right)
    {
        var caret = CaretReal();
        _left.Real = left;
        _right.Real = right;
        await RecompareAsync(++_version, caret);
    }

    async Task UndoAsync(bool redo)
    {
        await CommitEditsAsync();
        var (from, to) = redo ? (_redo, _undo) : (_undo, _redo);
        if (from.Count == 0) return;
        to.Push((_left.Real, _right.Real));
        var (l, r) = from.Pop();
        await SetRealAsync(l, r);
    }

    // --- 고른 부분만 합치기 ---

    /// <summary>고른 행 범위에서 지금 합칠 수 있는 방향과 그 이유 (null이면 합칠 수 있다).</summary>
    string? MergeBlockedReason(bool toRight)
    {
        var s = Strings.Current;
        if (_merge is not { } m || _leftState.Diff is not { } d) return s.CompareMergeSelectFirst;
        if ((m.Source == _left) != toRight) return s.CompareMergeWrongSide(toRight);
        if (!(toRight ? _right : _left).CanEdit) return s.CompareReadOnly;
        if (DiffEdits.CoversWholeSource(d, m.First, m.Last, toRight)) return s.CompareMergeWholeFile;
        return null;
    }

    /// <summary>
    /// 사용자가 고른 행(<see cref="_merge"/>)<b>만</b> 반대쪽으로 복사한다. 고르지 않은 줄은 한 줄도 건드리지 않고,
    /// 고른 범위가 파일 전체면 거절한다 (파일 전체를 옮기는 일은 폴더 비교의 일). 되돌리기는 Ctrl+Z.
    /// </summary>
    async Task CopySelectionAsync(bool toRight)
    {
        var s = Strings.Current;
        if (_editBatchOpen)
        {
            // 고치던 글이 있으면 먼저 반영한다 (행 번호가 달라질 수 있으니 합칠 줄은 다시 고르게 한다).
            await CommitEditsAsync();
            Notice(s.CompareMergeReselect);
            return;
        }
        if (MergeBlockedReason(toRight) is { } reason) { Notice(reason); return; }
        var m = _merge!.Value;
        var d = _leftState.Diff!;
        var lines = DiffEdits.SourceLineCount(d, m.First, m.Last, toRight);
        _undo.Push((_left.Real, _right.Real));
        _redo.Clear();
        var (l, r) = DiffEdits.CopyRows(d, m.First, m.Last, toRight);
        ClearMerge();
        await SetRealAsync(l, r);
        Notice(s.CompareMergeCopied(lines, toRight));
    }

    /// <summary>BC Copy Line to Right / Left (Shift+Ctrl+R / L): 원본 쪽 캐럿이 있는 줄 하나만 — 고른 범위와 같은 길로 간다.</summary>
    async Task CopyLineAsync(bool toRight)
    {
        var source = toRight ? _left : _right;
        var row = source.Editor.TextArea.Caret.Line - 1;
        _merge = (source, row, row);
        UpdateMergeUi();
        await CopySelectionAsync(toRight);
    }

    void OnSelectionChanged(Side side)
    {
        if (_applying || _selectionGuard) return;
        var editor = side.Editor;
        if (editor.SelectionLength == 0)
        {
            if (_merge?.Source == side && _gutterAnchor < 0 && _gutterHeld != side) ClearMerge();
            return;
        }
        var doc = editor.Document;
        var start = editor.SelectionStart;
        var end = start + editor.SelectionLength;
        var first = doc.GetLineByOffset(Math.Min(start, doc.TextLength)).LineNumber - 1;
        var lastLine = doc.GetLineByOffset(Math.Min(end, doc.TextLength));
        var last = lastLine.LineNumber - 1;
        if (end == lastLine.Offset && last > first) last--; // 다음 줄 맨 앞에서 끝나면 그 줄은 고른 것이 아니다
        // 새로 고르면 반대쪽의 선택은 풀어서 어느 쪽이 원본인지 헷갈리지 않게 한다.
        var other = side.Left ? _right : _left;
        _selectionGuard = true;
        other.Editor.TextArea.ClearSelection();
        _selectionGuard = false;
        SetMerge(side, first, last);
    }

    Side? _gutterHeld;

    void SetMerge(Side source, int first, int last)
    {
        _merge = (source, Math.Min(first, last), Math.Max(first, last));
        UpdateMergeUi();
    }

    void ClearMerge()
    {
        if (_merge is null) return;
        _merge = null;
        UpdateMergeUi();
    }

    /// <summary>
    /// 글 자리를 누르면 이전에 줄 번호로 고른 범위는 풀린다. 단, 맞은편에는 줄이 있는데 이쪽에는 없는 행(채움 줄)을 누르면 그 행을 고른 것으로 본다 —
    /// 글이 없어서 끌어 고를 수 없는 "없는 줄"을 맞은편의 줄(예: 빈 줄)에 합쳐 그 줄을 없애려면 이렇게 고를 수 있어야 한다.
    /// </summary>
    void OnTextAreaPressed(Side side, PointerPressedEventArgs args)
    {
        if (_selectionGuard) return;
        ClearMerge();
        if (!args.GetCurrentPoint(side.Editor.TextArea).Properties.IsLeftButtonPressed || _leftState.Diff is not { } d) return;
        var tv = side.Editor.TextArea.TextView;
        var y = args.GetPosition(tv).Y + tv.VerticalOffset;
        if (tv.GetDocumentLineByVisualTop(Math.Max(0, y))?.LineNumber - 1 is not { } row || row < 0 || row >= d.Rows.Count) return;
        var r = d.Rows[row];
        if ((side.Left ? r.Left : r.Right) is not null || (side.Left ? r.Right : r.Left) is null) return; // 이쪽에 줄이 있거나, 맞은편에도 없으면 해당 없음
        // 누름을 편집기가 처리한 뒤에 고른다 (누르면서 선택이 바뀌어 방금 고른 것이 풀리지 않게).
        Dispatcher.UIThread.Post(() => SetMerge(side, row, row), DispatcherPriority.Input);
    }

    /// <summary>줄 번호 여백을 누르면 그 줄을 고른다 (Shift로 이어서 넓히고, 끌면 여러 줄). 빈 줄·채움 줄도 고를 수 있다.</summary>
    void OnGutterPressed(Side side, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(side.Margin).Properties.IsLeftButtonPressed || GutterLine(side, args) is not { } line) return;
        var anchor = args.KeyModifiers.HasFlag(KeyModifiers.Shift) && _merge is { } m && m.Source == side ? m.First : line;
        _gutterAnchor = anchor;
        _gutterHeld = side;
        args.Pointer.Capture(side.Margin);
        SelectGutterLines(side, anchor, line);
        args.Handled = true;
    }

    void OnGutterMoved(Side side, PointerEventArgs args)
    {
        if (_gutterAnchor < 0 || _gutterHeld != side || !args.GetCurrentPoint(side.Margin).Properties.IsLeftButtonPressed) return;
        if (GutterLine(side, args) is { } line) SelectGutterLines(side, _gutterAnchor, line);
    }

    /// <summary>여백 안 위치의 화면 행 (0부터).</summary>
    int? GutterLine(Side side, PointerEventArgs args)
    {
        var tv = side.Editor.TextArea.TextView;
        var y = args.GetPosition(side.Margin).Y + tv.VerticalOffset;
        return tv.GetDocumentLineByVisualTop(Math.Max(0, y))?.LineNumber - 1;
    }

    void SelectGutterLines(Side side, int firstRow, int lastRow)
    {
        var (a, b) = (Math.Min(firstRow, lastRow), Math.Max(firstRow, lastRow));
        var doc = side.Editor.Document;
        _selectionGuard = true;
        try
        {
            var other = side.Left ? _right : _left;
            other.Editor.TextArea.ClearSelection();
            var from = doc.GetLineByNumber(Math.Min(a + 1, doc.LineCount));
            var to = doc.GetLineByNumber(Math.Min(b + 1, doc.LineCount));
            side.Editor.Select(from.Offset, to.EndOffset - from.Offset); // 빈 줄만이면 길이 0 — 화면에는 틀이 고른 범위를 보인다
        }
        finally { _selectionGuard = false; }
        SetMerge(side, a, b);
    }

    /// <summary>고른 범위가 바뀔 때: 양쪽의 틀, 가운데 → / ← 단추의 자리·사용 가능 여부, 도구 모음 단추를 맞춘다.</summary>
    void UpdateMergeUi()
    {
        var m = _merge;
        foreach (var (side, state) in new[] { (_left, _leftState), (_right, _rightState) })
        {
            state.MergeRows = m is { } x ? (x.First, x.Last) : null;
            state.MergeIsSource = m?.Source == side;
        }
        var s = Strings.Current;
        string Tip(bool toRight)
        {
            var key = toRight ? "Ctrl+R" : "Ctrl+L";
            var name = toRight ? s.CompareCopySelectionToRight : s.CompareCopySelectionToLeft;
            if (MergeBlockedReason(toRight) is { } reason && m is not null && (m.Value.Source == _left) == toRight) return reason;
            return m is { } v && (v.Source == _left) == toRight && _leftState.Diff is { } d
                ? $"{name} — {s.CompareMergeLines(DiffEdits.SourceLineCount(d, v.First, v.Last, toRight))} ({key})"
                : $"{name} ({key})";
        }
        foreach (var (button, toRight) in new[] { (_copyRightButton, true), (_copyLeftButton, false) })
        {
            var mine = m is { } v && (v.Source == _left) == toRight;
            button.IsVisible = mine;
            button.IsEnabled = mine && MergeBlockedReason(toRight) is null;
            ToolTip.SetTip(button, Tip(toRight));
        }
        if (_toolCopyRight is not null) _toolCopyRight.IsEnabled = m is { } a && a.Source == _left && MergeBlockedReason(true) is null;
        if (_toolCopyLeft is not null) _toolCopyLeft.IsEnabled = m is { } b && b.Source == _right && MergeBlockedReason(false) is null;
        PositionMergeButtons();
        Redraw();
    }

    /// <summary>→ / ← 단추를 고른 첫 행 옆에 둔다. 고른 곳이 화면 밖이면 맨 위·맨 아래에 붙여 늘 누를 수 있게 한다.</summary>
    void PositionMergeButtons()
    {
        if (_merge is not { } m) return;
        var tv = _right.Editor.TextArea.TextView;
        var doc = _right.Editor.Document;
        if (doc.LineCount == 0 || tv.Bounds.Height <= 0) return;
        var line = Math.Clamp(m.First, 0, doc.LineCount - 1) + 1;
        var y = tv.GetVisualTopByDocumentLine(line) - tv.VerticalOffset;
        var top = Math.Clamp(y, 2, Math.Max(2, tv.Bounds.Height - 30));
        var button = m.Source == _left ? _copyRightButton : _copyLeftButton;
        button.Margin = new Thickness(0, top, 0, 0);
    }

    // --- 저장 (ROADMAP §6-1 ② 손실 확인, ③ 자동 백업) ---

    string BackupRoot => System.IO.Path.Combine(App.Policy.Directory, "backups");

    async Task SaveAllAsync()
    {
        await CommitEditsAsync();
        foreach (var side in new[] { _left, _right })
            if (side.Dirty && !await SaveAsync(side)) return;
    }

    async Task SaveFocusedAsync()
    {
        await CommitEditsAsync();
        var focused = _left.Editor.TextArea.IsFocused ? _left : _right;
        var side = focused.Dirty ? focused : (focused.Left ? _right : _left).Dirty ? (focused.Left ? _right : _left) : focused;
        if (side.Dirty) await SaveAsync(side);
    }

    /// <summary>한쪽을 저장한다. 물어볼 것이 있으면 묻고, 취소하면 false.</summary>
    async Task<bool> SaveAsync(Side side)
    {
        if (!side.CanEdit || side.Text is not { } text || side.Path is not { } path) return false;
        var s = Strings.Current;
        TextEncodingKind? encoding = null;
        var check = CompareSave.Check(path, text.File, side.Real);
        if (check.NeedsConfirmation)
        {
            var lines = new List<string>();
            if (check.ChangedOnDisk) lines.Add(s.CompareSaveChangedOnDisk);
            if (check.Lost.Count > 0) lines.Add(s.CompareSaveLost(TextEncodings.DisplayName(text.File.Encoding), string.Join(' ', check.Lost.Select(l => l.Text).Distinct())));
            if (check.Damaged > 0) lines.Add(s.CompareSaveDamaged(check.Damaged));
            lines.Add(s.CompareSaveBackupNote);
            var offerUtf8 = check.Lost.Count > 0 && text.File.Encoding != TextEncodingKind.Utf8;
            string[] buttons = offerUtf8 ? [s.CommonCancel, s.CompareSaveAnyway, s.CompareSaveAsUtf8] : [s.CommonCancel, s.CommonSave];
            var choice = await MessageDialog.ShowAsync(Owner, s.CompareSaveTitle(side.Name), string.Join("\n\n", lines), buttons, buttons.Length - 1, 0);
            if (choice <= 0) return false;
            if (offerUtf8 && choice == 2) encoding = TextEncodingKind.Utf8;
        }
        try
        {
            side.IgnoreWatchUntil = DateTime.UtcNow.AddSeconds(2);
            // 섞인 인코딩을 복구한 글은 UTF-8로 통일해 저장한다.
            var (saved, _) = await CompareSave.SaveAsync(path, side.Real, text.File, BackupRoot, encoding ?? (side.Repair is not null ? TextEncodingKind.Utf8 : null));
            side.Source = text with { File = saved with { Text = side.Real } };
            side.LoadedFormat = (saved.Encoding, saved.HasBom, saved.LineBreak);
            side.SavedText = side.Real;
            side.Repair = null;
            side.RepairRequested = false;
            side.ChangedOutside = false;
            UpdateSideChrome(side);
            UpdateTitle();
            Notice(s.CompareSaved(side.Name));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(Owner, s.CompareSaveTitle(side.Name), s.CompareSaveFailed(e.Message), [s.CommonClose], 0, 0);
            return false;
        }
    }

    // --- 바깥 변경 감지 (ROADMAP §6-1 ③ 자동 다시 비교) ---

    void Watch(Side side)
    {
        side.Watcher?.Dispose();
        side.Watcher = null;
        if (side.Path is not { } path || ArchivePaths.IsInArchive(path) || System.IO.Path.GetDirectoryName(path) is not { Length: > 0 } dir || !Directory.Exists(dir)) return;
        try
        {
            var w = new FileSystemWatcher(dir, System.IO.Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            void Changed(object? _, FileSystemEventArgs __) => Dispatcher.UIThread.Post(() => OnChangedOutside(side));
            w.Changed += Changed;
            w.Created += Changed;
            w.Renamed += (o, a) => Changed(o, a);
            w.EnableRaisingEvents = true;
            side.Watcher = w;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 감시를 못 해도 비교는 된다 (네트워크 드라이브 등).
        }
    }

    DispatcherTimer? _outsideTimer;

    void OnChangedOutside(Side side)
    {
        if (DateTime.UtcNow < side.IgnoreWatchUntil) return;
        // 한 번 저장에 이벤트가 여러 번 온다 → 잠깐 모아서 한 번만.
        _outsideTimer?.Stop();
        _outsideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _outsideTimer.Tick += async (_, _) =>
        {
            _outsideTimer!.Stop();
            if (side.Text is { } t && side.Path is { } p && !TextFiles.ChangedOnDisk(p, t.File)) return;
            if (_left.Dirty || _right.Dirty || _editBatchOpen)
            {
                side.ChangedOutside = true; // 고친 것이 있으면 덮어쓰지 않고 알린다
                UpdateSideChrome(side);
            }
            else await ReloadAsync();
        };
        _outsideTimer.Start();
    }

    // --- 표시 ---

    void UpdateSideChrome(Side side)
    {
        var s = Strings.Current;
        if (!side.PathEditing)
        {
            // 아무것도 없으면 비워 두어 "왼쪽(오른쪽) 파일 열기" 안내 글이 보이게 한다. 긴 경로는 끝(파일 이름)이 보이게 커서를 끝에 둔다.
            var shown = side.Title is { Length: > 0 } title ? title : side.Path ?? (side.Real.Length > 0 ? s.CompareScratchName : "");
            side.PathText.Text = shown.Length > 0 && side.Dirty ? "● " + shown : shown;
            side.PathText.CaretIndex = side.PathText.Text.Length;
        }
        ToolTip.SetTip(side.PathText, side.Path ?? (side.Real.Length > 0 ? s.CompareScratchTooltip : null));
        side.EncodingText.Text = side.Text is { } t && !side.IsScratch
            ? FormatLabel(t.File) + (side.HasText && !side.CanEdit ? " · " + s.CompareReadOnly : "")
            : "";
        ToolTip.SetTip(side.EncodingText, side.HasText && !side.IsScratch ? s.CompareFormatTooltip : null);
        side.InfoText.Text = side.Text is { } info && !side.IsScratch ? s.CompareHeaderInfo(info.File.Modified.ToLocalTime(), info.File.Length) : "";
        side.InfoText.IsVisible = side.InfoText.Text.Length > 0;
        side.EncodingText.Cursor = side.HasText && !side.IsScratch ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

        (side.Overlay.IsVisible, side.OverlayText.Text) = side.Source switch
        {
            null => (true, s.CompareEmptySide),
            CompareSource.Text when side.IsScratch && side.Real.Length == 0 => (true, s.CompareEmptySide),
            CompareSource.Binary => (true, s.CompareBinary),
            CompareSource.TooLarge big => (true, s.CompareTooLarge(BytesFormat.Format(big.Length))),
            CompareSource.Failed f => (true, s.CompareLoadFailed(f.Error.ToString())),
            _ => (false, ""),
        };

        // 안내 띠: 바깥 변경 > 인코딩 (ROADMAP §6-1 ②·③).
        side.BannerActions.Children.Clear();
        side.Banner.IsVisible = false;
        if (side.ChangedOutside)
        {
            side.Banner.IsVisible = true;
            side.BannerText.Text = s.CompareChangedOutside;
            var reload = UiScale.Enable(new Button { Content = s.CompareReloadDiscard, Classes = { "compact" } });
            reload.Click += async (_, _) => await ReloadAsync();
            side.BannerActions.Children.Add(reload);
            return;
        }
        if (side.Text is not { } text) return;
        if (side.Repair is { } repair)
        {
            side.Banner.IsVisible = true;
            side.BannerText.Text = s.CompareRepaired(repair.RepairedLines.Count, repair.BrokenLines.Count);
            side.BannerActions.Children.Add(EncodingMenuButton(side));
        }
        else if (text.File.DecodeErrors > 0)
        {
            side.Banner.IsVisible = true;
            var errors = TextEncodings.FindErrors(text.Bytes, text.File.Encoding, max: 1);
            side.BannerText.Text = s.CompareEncodingDamaged(TextEncodings.DisplayName(text.File.Encoding), text.File.DecodeErrors, errors.Count > 0 ? errors[0].Line : 1);
            if (text.File.Encoding is not (TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be))
            {
                var other = OtherEncoding(text.File.Encoding);
                var repairButton = UiScale.Enable(new Button { Content = s.CompareRepairMixed(TextEncodings.DisplayName(text.File.Encoding), TextEncodings.DisplayName(other)), Classes = { "compact" } });
                repairButton.Click += async (_, _) =>
                {
                    side.RepairRequested = true;
                    await ReloadAsync();
                };
                side.BannerActions.Children.Add(repairButton);
            }
            side.BannerActions.Children.Add(EncodingMenuButton(side));
        }
    }

    Button EncodingMenuButton(Side side)
    {
        var button = UiScale.Enable(new Button { Content = Strings.Current.CompareReopenEncoding, Classes = { "compact" } });
        button.Click += (_, _) => EncodingFlyout(side).ShowAt(button);
        return button;
    }

    /// <summary>"다른 인코딩으로 다시 열기": 후보마다 깨진 곳 수와 앞부분 미리보기 (<see cref="TextEncodings.Previews"/>).</summary>
    MenuFlyout EncodingFlyout(Side side)
    {
        var flyout = new MenuFlyout();
        if (side.Text is not { } text) return flyout;
        foreach (var p in TextEncodings.Previews(text.Bytes, previewChars: 60))
        {
            var preview = p.Preview.Replace('\n', ' ').Replace('\r', ' ');
            var item = new MenuItem { Header = $"{TextEncodings.DisplayName(p.Kind)} — {Strings.Current.CompareEncodingErrors(p.Errors)} · {preview}" };
            var kind = p.Kind;
            item.Click += async (_, _) =>
            {
                side.ForcedEncoding = kind;
                side.RepairRequested = false;
                await ReloadAsync();
            };
            flyout.Items.Add(item);
        }
        return flyout;
    }

    void UpdateTitle(bool raise = true)
    {
        var dirty = _left.Dirty || _right.Dirty ? "● " : "";
        WindowTitle = $"{dirty}{Strings.Current.CompareWindowTitle(_left.Name, _right.Name)}";
        TabTitle = $"{dirty}{(_left.Name == _right.Name ? _left.Name : Strings.Current.CompareWindowTitle(_left.Name, _right.Name))}";
        _save.IsEnabled = _left.Dirty || _right.Dirty;
        if (raise) TitleChanged?.Invoke();
    }

    /// <summary>잠깐 보이는 알림 (저장함 등) — 상태 표시 자리에 4초.</summary>
    void Notice(string text)
    {
        _notice = text;
        UpdateStatus();
        DispatcherTimer.RunOnce(() =>
        {
            if (_notice == text) { _notice = null; UpdateStatus(); }
        }, TimeSpan.FromSeconds(4));
    }

    void UpdateStatus()
    {
        var s = Strings.Current;
        if (_notice is { } n) { _status.Text = n; return; }
        if (_leftState.Diff is not { } d) { _status.Text = ""; return; }
        var important = d.Diff.DifferenceCount;
        var unimportant = d.Diff.Blocks.Count(b => b.Kind == DiffBlockKind.Unimportant);
        var text = d.Diff.IsIdentical ? s.CompareIdentical
            : important == 0 ? s.CompareEquivalent(unimportant)
            : s.CompareDifferences(important, unimportant);
        if (_left.Text is { } l && _right.Text is { } r && (l.File.Encoding != r.File.Encoding || l.File.HasBom != r.File.HasBom))
            text += "  ·  " + s.CompareEncodingDiffers(TextEncodings.DisplayName(l.File.Encoding, l.File.HasBom), TextEncodings.DisplayName(r.File.Encoding, r.File.HasBom));
        _status.Text = text;
    }

    void Redraw()
    {
        foreach (var side in new[] { _left, _right })
        {
            side.Editor.TextArea.TextView.Redraw();
            side.Margin.InvalidateVisual();
        }
        UpdateViewport();
        UpdateCaretUi();
    }

    // --- 줄 단위 바이트 패널 ---

    /// <summary>줄 바이트 표. 읽은 그대로이면 원래 바이트로, 고쳤으면 지금의 글을 같은 인코딩·줄 끝으로 다시 인코딩해서 만든다 (글이 바뀔 때만 다시 만든다).</summary>
    LineByteMap? ByteMapOf(Side side)
    {
        if (side.Text is not { } t) return null;
        if (side.ByteMap is not null && ReferenceEquals(side.ByteMapFor, side.Real) && ReferenceEquals(side.ByteMapSource, t)) return side.ByteMap;
        byte[] bytes;
        try
        {
            var untouched = side.Repair is null && !side.FormatChanged && side.Real == TextFiles.NormalizeLineBreaks(t.File.Text);
            bytes = untouched ? t.Bytes : TextEncodings.Encode(TextFiles.ApplyLineBreak(side.Real, t.File.LineBreak), t.File.Encoding, t.File.HasBom);
        }
        catch (Exception) { return null; }
        side.ByteMap = LineByteMap.Build(bytes, t.File.Encoding, t.File.HasBom);
        (side.ByteMapFor, side.ByteMapSource) = (side.Real, t);
        return side.ByteMap;
    }

    ByteLine BytesAt(Side side, int? line)
    {
        var s = Strings.Current;
        var name = side.Left ? s.HomeLeft : s.HomeRight;
        if (line is not { } n || ByteMapOf(side) is not { } map) return new ByteLine(name, default, false, 0);
        var (offset, length) = map.Range(n);
        return new ByteLine($"{name} {n + 1}  ·  0x{offset:X}  ·  {length:N0} B", map.Line(n), true, n == 0 ? map.BomLength : 0);
    }

    /// <summary>캐럿이 움직이거나 비교가 바뀔 때 캐럿에 딸린 표시를 모두 새로 한다.</summary>
    void UpdateCaretUi()
    {
        UpdateStatusBar();
        UpdateBytePanel();
    }

    Control BuildStatusBar()
    {
        foreach (var t in new[] { _sectionText, _leftPos, _rightPos, _modeText, _loadText })
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _statusBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _statusBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var panes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(24, 0), Children = { _leftPos, _rightPos } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _modeText, _loadText } };
        Grid.SetColumn(panes, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(_sectionText);
        grid.Children.Add(panes);
        grid.Children.Add(right);
        _statusBar.Child = grid;
        return _statusBar;
    }

    /// <summary>줄 끝 종류 (읽은 파일 기준): CRLF · LF · CR, 섞여 있으면 "줄 끝 혼합", 줄 끝이 없으면 빈 글.</summary>
    string LineBreakOf(Side side)
    {
        if (side.Text is not { } t) return "";
        if (side.FormatChanged) return LineBreakShort(t.File.LineBreak); // 저장할 줄 끝을 바꿨다
        if (ReferenceEquals(side.LineBreakSource, t) && side.LineBreakText is { } cached) return cached;
        var map = LineByteMap.Build(t.Bytes, t.File.Encoding, t.File.HasBom);
        var kinds = (map.CrLfCount > 0 ? 1 : 0) + (map.LfCount > 0 ? 1 : 0) + (map.CrCount > 0 ? 1 : 0);
        var text = kinds > 1 ? Strings.Current.CompareLineBreakMixed
            : map.CrLfCount > 0 ? "CRLF" : map.LfCount > 0 ? "LF" : map.CrCount > 0 ? "CR" : "";
        (side.LineBreakSource, side.LineBreakText) = (t, text);
        return text;
    }

    string PositionOf(Side side, AlignedRow? row)
    {
        var s = Strings.Current;
        var name = side.Left ? s.HomeLeft : s.HomeRight;
        var real = row is { } r ? (side.Left ? r.Left : r.Right) : null;
        var caret = side.Editor.TextArea.Caret;
        var line = real is { } n ? (n + 1).ToString() : "—";
        var breaks = LineBreakOf(side);
        return $"{name}  {line}:{caret.Column}" + (breaks.Length > 0 ? $"  {breaks}" : "");
    }

    void UpdateStatusBar()
    {
        var s = Strings.Current;
        var diff = _leftState.Diff;
        var row = diff is not null ? CurrentRow() : -1;
        AlignedRow? current = diff is not null && row >= 0 && row < diff.Rows.Count ? diff.Rows[row] : null;
        _overview.CaretRow = diff is not null && row >= 0 && row < diff.Rows.Count ? row : null;
        _leftPos.Text = PositionOf(_left, current);
        _rightPos.Text = PositionOf(_right, current);
        if (diff is null) _sectionText.Text = "";
        else
        {
            var sections = diff.Ranges.Where(x => x.Kind is DiffBlockKind.Changed or DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly).ToList();
            var index = sections.FindIndex(x => row >= x.StartRow && row < x.StartRow + x.RowCount);
            _sectionText.Text = index >= 0 ? s.CompareSectionPosition(index + 1, sections.Count) : s.CompareSectionCount(sections.Count);
        }
        _modeText.Text = (_right.Editor.TextArea.OverstrikeMode || _left.Editor.TextArea.OverstrikeMode) ? s.CompareOverwriteMode : s.CompareInsertMode;
        _loadText.Text = _loadSeconds is { } sec ? s.CompareLoadTime(sec) : "";
    }

    void UpdateBytePanel()
    {
        if (!_bytes.IsVisible || _leftState.Diff is not { } diff) return;
        var row = CurrentRow();
        if (row < 0 || row >= diff.Rows.Count) return;
        var r = diff.Rows[row];
        _bytes.Show(BytesAt(_left, r.Left), BytesAt(_right, r.Right), r.Kind == DiffBlockKind.Unimportant, Strings.Current.CompareBytesNoLine);
    }

    // --- 스크롤·이동 ---

    /// <summary>
    /// 편집기를 이 위치로 스크롤한다. <c>TextEditor.ScrollToVerticalOffset</c>는 이 구성에서 아무것도 하지 않아서(오른쪽 스크롤 막대를 끌어도 왼쪽이 안 따라오고
    /// 개요 막대를 눌러도 안 움직였다) 편집기 안 <see cref="ScrollViewer"/>의 오프셋을 직접 바꾼다.
    /// </summary>
    static void ScrollTo(Side side, double? x, double? y)
    {
        side.Scroller ??= side.Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (side.Scroller is not { } sv) return;
        sv.Offset = new Vector(x ?? sv.Offset.X, y ?? sv.Offset.Y);
    }

    void SyncScroll(Side from, Side to)
    {
        if (_syncing) return;
        _syncing = true;
        ScrollTo(to, from.Editor.HorizontalOffset, from.Editor.VerticalOffset);
        _syncing = false;
        UpdateViewport();
        PositionMergeButtons();
    }

    void UpdateViewport()
    {
        var tv = _right.Editor.TextArea.TextView;
        var doc = _right.Editor.Document;
        if (tv.DefaultLineHeight <= 0 || doc.LineCount == 0) return;
        // 접힌 줄이 있으면 화면 높이와 행 수가 비례하지 않으므로 맨 위·맨 아래에 보이는 행을 직접 찾는다.
        var top = tv.GetDocumentLineByVisualTop(tv.VerticalOffset)?.LineNumber ?? 1;
        var bottom = tv.GetDocumentLineByVisualTop(tv.VerticalOffset + Math.Max(0, tv.Bounds.Height - 1))?.LineNumber ?? doc.LineCount;
        _overview.Viewport = (top - 1, Math.Max(1, bottom - top + 1));
        _overview.InvalidateVisual();
    }

    void ScrollToRow(int row, bool center, bool moveCaret = true)
    {
        var lines = _right.Editor.Document.LineCount;
        if (lines == 0) return;
        var line = Math.Clamp(row + 1, 1, lines);
        var tv = _right.Editor.TextArea.TextView;
        var offset = tv.GetVisualTopByDocumentLine(line) - (center ? tv.Bounds.Height / 3 : 0);
        ScrollTo(_right, null, Math.Max(0, offset));
        if (!moveCaret) return;
        _syncing = true;
        _right.Editor.TextArea.Caret.Line = line;
        _left.Editor.TextArea.Caret.Line = Math.Min(line, _left.Editor.Document.LineCount);
        _syncing = false;
        UpdateCaretUi();
    }

    // --- 차이만 보기 (같은 줄 접기) ---

    /// <summary>차이만 보기가 켜져 있으면 양쪽에서 같은 행 구간을 접는다 (<see cref="AlignedDiff.CollapsibleRuns"/>). 꺼져 있으면 모두 편다.</summary>
    void ApplyFolds(bool redraw = true)
    {
        var aligned = _leftState.Diff;
        // Show Differences = 문맥 없이, Show Context = 문맥 n줄, Show Same = 차이를 접는다.
        var runs = aligned is null ? [] : _filter switch
        {
            CompareViewFilter.Differences => aligned.CollapsibleRuns(0),
            CompareViewFilter.Context => aligned.CollapsibleRuns(_contextLines),
            CompareViewFilter.Same => aligned.CollapsibleRuns(0, showSame: true),
            _ => (IReadOnlyList<(int StartRow, int RowCount)>)[],
        };
        foreach (var side in new[] { _left, _right })
        {
            side.Folding.Clear();
            var doc = side.Editor.Document;
            foreach (var (start, count) in runs)
            {
                if (start + count > doc.LineCount) break;
                var first = doc.GetLineByNumber(start + 1);
                var last = doc.GetLineByNumber(start + count);
                var fold = side.Folding.CreateFolding(first.Offset, last.EndOffset);
                fold.Title = Strings.Current.CompareFolded(count);
                fold.IsFolded = true;
            }
        }
        if (redraw) Dispatcher.UIThread.Post(() =>
        {
            SyncScroll(_right, _left);
            Redraw();
        }, DispatcherPriority.Background);
    }

    /// <summary><paramref name="from"/> 쪽의 접힘 상태를 맞은편에 그대로 옮긴다 (두 쪽은 같은 순서로 같은 행 구간을 접는다).</summary>
    void MirrorFolds(Side from)
    {
        var to = from.Left ? _right : _left;
        var a = from.Folding.AllFoldings.ToList();
        var b = to.Folding.AllFoldings.ToList();
        var changed = false;
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (b[i].IsFolded != a[i].IsFolded) { b[i].IsFolded = a[i].IsFolded; changed = true; }
        if (!changed) return;
        Dispatcher.UIThread.Post(() =>
        {
            SyncScroll(from, to);
            Redraw();
        }, DispatcherPriority.Background);
    }

    /// <summary><paramref name="offset"/>이 접힌 곳 안이면 양쪽의 그 접기를 편다 (찾기·이동이 숨은 줄에 닿을 때).</summary>
    void Unfold(Side side, int offset)
    {
        var line = side.Editor.Document.GetLineByOffset(offset).LineNumber;
        foreach (var s in new[] { _left, _right })
        {
            var doc = s.Editor.Document;
            if (line > doc.LineCount) continue;
            var at = doc.GetLineByNumber(line).Offset;
            foreach (var fold in s.Folding.GetFoldingsContaining(at)) fold.IsFolded = false;
        }
    }

    // --- 찾기 (Ctrl+F) ---

    void OpenFind() => OpenFind(replace: false);

    /// <summary>찾기 막대를 연다. <paramref name="replace"/>면 바꾸기 줄도 (BC Ctrl+H).</summary>
    void OpenFind(bool replace)
    {
        _findSide = _left.Editor.TextArea.IsFocused ? _left : _right.Editor.TextArea.IsFocused ? _right : _findSide ?? _right;
        var selected = _findSide.Editor.SelectedText;
        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace || _replaceRow.IsVisible && _findBar.IsVisible;
        if (selected.Length > 0 && !selected.Contains('\n')) _findBox.Text = selected;
        UpdateFindWhere();
        Dispatcher.UIThread.Post(() =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        });
    }

    void CloseFind()
    {
        _findBar.IsVisible = false;
        _replaceRow.IsVisible = false;
        _findCount.Text = "";
        (_findSide ?? _right).Editor.TextArea.Focus();
    }

    StringComparison FindComparison => _findCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>지금 고른 글이 찾는 글이면 바꾸고 다음 일치로 간다. 아니면 먼저 다음 일치를 고른다.</summary>
    void ReplaceOne()
    {
        var side = _findSide ??= _right;
        var needle = _findBox.Text ?? "";
        if (needle.Length == 0) return;
        if (!side.CanEdit) { Notice(Strings.Current.CompareReadOnly); return; }
        if (string.Equals(side.Editor.SelectedText, needle, FindComparison))
            side.Editor.Document.Replace(side.Editor.SelectionStart, side.Editor.SelectionLength, _replaceBox.Text ?? "");
        Find(forward: true);
    }

    /// <summary>찾을 쪽의 모든 일치를 바꾼다 (실행 취소 한 번으로 되돌린다 — 한 편집 묶음).</summary>
    void ReplaceAll()
    {
        var side = _findSide ??= _right;
        var needle = _findBox.Text ?? "";
        if (needle.Length == 0) return;
        if (!side.CanEdit) { Notice(Strings.Current.CompareReadOnly); return; }
        var doc = side.Editor.Document;
        var hay = doc.Text;
        var matches = new List<int>();
        for (var i = hay.IndexOf(needle, FindComparison); i >= 0; i = hay.IndexOf(needle, i + needle.Length, FindComparison)) matches.Add(i);
        if (matches.Count == 0) { _findCount.Text = Strings.Current.CompareFindNone; return; }
        var replacement = _replaceBox.Text ?? "";
        doc.BeginUpdate();
        try
        {
            for (var k = matches.Count - 1; k >= 0; k--) doc.Replace(matches[k], needle.Length, replacement);
        }
        finally { doc.EndUpdate(); }
        _findCount.Text = "";
        Notice(Strings.Current.CompareReplaced(matches.Count));
    }

    void UpdateFindWhere() =>
        _findWhere.Text = _findSide is null ? "" : _findSide.Left ? Strings.Current.CompareFindInLeft : Strings.Current.CompareFindInRight;

    /// <summary>
    /// 찾을 쪽에서 다음(앞) 일치로 간다. <paramref name="fromSelectionStart"/>면 지금 고른 곳 자체부터 (입력할 때마다 찾기).
    /// 끝까지 가면 처음으로 돌아간다. 접힌 곳 안이면 편다.
    /// </summary>
    void Find(bool forward, bool fromSelectionStart = false)
    {
        var s = Strings.Current;
        var needle = _findBox.Text ?? "";
        var side = _findSide ??= _right;
        if (needle.Length == 0) { _findCount.Text = ""; return; }
        var hay = side.Editor.Document.Text;
        var comparison = _findCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<int>();
        for (var i = hay.IndexOf(needle, comparison); i >= 0 && matches.Count < 100_000; i = hay.IndexOf(needle, i + Math.Max(1, needle.Length), comparison))
            matches.Add(i);
        if (matches.Count == 0) { _findCount.Text = s.CompareFindNone; return; }
        var start = side.Editor.SelectionLength > 0 ? side.Editor.SelectionStart : side.Editor.CaretOffset;
        var index = forward
            ? matches.FindIndex(m => fromSelectionStart ? m >= start : m > start)
            : matches.FindLastIndex(m => m < start);
        if (index < 0) index = forward ? 0 : matches.Count - 1;
        var offset = matches[index];
        Unfold(side, offset);
        side.Editor.Select(offset, needle.Length);
        var line = side.Editor.Document.GetLineByOffset(offset).LineNumber;
        _syncing = true;
        var other = side.Left ? _right : _left;
        other.Editor.TextArea.Caret.Line = Math.Min(line, other.Editor.Document.LineCount);
        _syncing = false;
        Dispatcher.UIThread.Post(() =>
        {
            var tv = _right.Editor.TextArea.TextView;
            var top = tv.GetVisualTopByDocumentLine(line);
            if (top < tv.VerticalOffset || top > tv.VerticalOffset + tv.Bounds.Height - tv.DefaultLineHeight * 2)
                ScrollToRow(line - 1, center: true, moveCaret: false);
            side.Editor.TextArea.Caret.BringCaretToView(); // 긴 줄이면 가로로도 보이게
        }, DispatcherPriority.Background);
        _findCount.Text = s.CompareFindCount(index + 1, matches.Count);
    }

    int CurrentRow() => (_left.Editor.TextArea.IsFocused ? _left : _right).Editor.TextArea.Caret.Line - 1;

    void GoDiff(bool next)
    {
        if (_leftState.Diff is not { } d) return;
        var row = CurrentRow();
        var include = IncludeUnimportant(d);
        var target = next ? d.Next(row, include) : d.Previous(row, include);
        // 지금 차이 구간 안에 있으면 그 구간 끝/처음을 기준으로 다음 것을 찾는다.
        if (next && d.RangeAt(row) is { } cur && target?.StartRow == cur.StartRow) target = d.Next(cur.StartRow + cur.RowCount - 1, include);
        if (target is { } t) ScrollToRow(t.StartRow, center: true);
    }

    /// <summary>중요하지 않은 차이도 이동 대상인가: 무시하지 않고, 중요한 차이가 하나도 없을 때.</summary>
    bool IncludeUnimportant(AlignedDiff d) => _ignoreUnimportant.IsChecked != true && d.Diff.DifferenceCount == 0;

    /// <summary>BC Next / Previous Difference (줄 단위, Shift+Ctrl+N / P): 구간 안에서도 한 행씩.</summary>
    void GoDiffLine(bool next)
    {
        if (_leftState.Diff is not { } d) return;
        var row = CurrentRow();
        var include = IncludeUnimportant(d);
        if ((next ? d.NextDifferenceRow(row, include) : d.PreviousDifferenceRow(row, include)) is { } target)
            ScrollToRow(target, center: true);
    }

    /// <summary>BC Recompare Files (Ctrl+F5): 고친 것을 그대로 두고 지금 다시 비교한다 (Reload Files와 달리 디스크를 읽지 않는다).</summary>
    async Task RecompareNowAsync()
    {
        if (_editBatchOpen) { await CommitEditsAsync(); return; }
        await RecompareAsync(++_version, CaretReal());
    }

    /// <summary>BC Select Section (Ctrl+D): 캐럿이 있는 차이 구간을 초점 쪽에서 고른다.</summary>
    void SelectSection()
    {
        if (_leftState.Diff is not { } d) return;
        var side = FocusedSide();
        if (d.RangeAt(CurrentRow()) is not { } range) return;
        var doc = side.Editor.Document;
        var first = doc.GetLineByNumber(Math.Min(range.StartRow + 1, doc.LineCount));
        var last = doc.GetLineByNumber(Math.Min(range.StartRow + range.RowCount, doc.LineCount));
        side.Editor.Select(first.Offset, last.EndOffset - first.Offset);
    }

    /// <summary>BC Go To (Ctrl+G): 초점 쪽 파일의 실제 줄 번호로 간다.</summary>
    async Task GoToLineAsync()
    {
        var side = FocusedSide();
        var s = Strings.Current;
        var input = await InputDialog.ShowAsync(Owner, s.CompareGoTo, s.CompareGoToPrompt(side.Name), "", s.CommonOk, s.CommonCancel);
        if (input is null || !int.TryParse(input.Trim(), out var line) || line < 1) return;
        var row = _leftState.Diff is { } d ? (side.Left ? d.RowOfLeftLine(line - 1) : d.RowOfRightLine(line - 1)) : line - 1;
        var doc = side.Editor.Document;
        if (row + 1 <= doc.LineCount) Unfold(side, doc.GetLineByNumber(row + 1).Offset);
        ScrollToRow(row, center: true);
        side.Editor.TextArea.Focus();
    }

    /// <summary>BC Text Compare Info (Ctrl+I): 양쪽 파일의 크기·시각·인코딩·줄바꿈·줄 수.</summary>
    async Task ShowInfoAsync()
    {
        var s = Strings.Current;
        string Describe(Side side)
        {
            if (side.IsScratch) return $"{side.Name}\n{s.CompareInfoLines(DiffEdits.Split(side.Real).Lines.Count)}";
            if (side.Text is not { } t) return $"{side.Path}\n—";
            var lineBreak = LineBreakName(t.File.LineBreak);
            return string.Join("\n",
                side.Path,
                s.CompareInfoSize(BytesFormat.Format(t.File.Length), t.File.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                $"{TextEncodings.DisplayName(t.File.Encoding, t.File.HasBom)} · {lineBreak}",
                s.CompareInfoLines(DiffEdits.Split(side.Real).Lines.Count));
        }
        await MessageDialog.ShowAsync(Owner, s.CompareInfo, $"{s.HomeLeft}\n{Describe(_left)}\n\n{s.HomeRight}\n{Describe(_right)}", [s.CommonClose], 0, 0);
    }

    /// <summary>
    /// BC Save File As (F12): 다른 이름으로 저장한다. 붙여 넣은 글(파일 없는 칸)은 이것으로 파일이 된다 — UTF-8, 이 OS의 줄바꿈.
    /// 저장한 뒤 그 칸은 새 파일을 비교한다.
    /// </summary>
    async Task SaveAsAsync(Side side)
    {
        await CommitEditsAsync();
        if (!side.HasText) return;
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Current.CompareSaveFileAs,
            SuggestedFileName = side.Path is null ? "pasted.txt" : System.IO.Path.GetFileName(side.Path.Replace(ArchivePaths.Separator, "/")),
            SuggestedStartLocation = await PickLocations.StartAsync(Owner.StorageProvider),
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var template = side.Text is { } t && !side.IsScratch
            ? t.File
            : new EditorTextFile("", HasBom: false, LineBreak: Environment.NewLine, DateTime.MinValue, 0, Binary: false);
        try
        {
            var saved = await TextFiles.SaveAsync(path, side.Real, template with { DecodeErrors = 0 });
            side.Path = path;
        _skipHistory = false;
        side.Title = null;
            side.Title = null;
            side.Source = new CompareSource.Text(path, saved with { Text = side.Real }, await File.ReadAllBytesAsync(path));
            side.SavedText = side.Real;
            side.Repair = null;
            side.RepairRequested = false;
            Watch(side);
            UpdateSideChrome(side);
            UpdateTitle();
            Notice(Strings.Current.CompareSaved(side.Name));
            await RecompareAsync(++_version, CaretReal()); // 이름이 바뀌면 정책(파일 형식)·강조가 달라질 수 있다
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(Owner, Strings.Current.CompareSaveFileAs, Strings.Current.CompareSaveFailed(e.Message), [Strings.Current.CommonClose], 0, 0);
        }
    }

    /// <summary>고친 것이 있으면 버려도 되는지 묻는다 (다시 읽기·바꾸기 전).</summary>
    async Task<bool> ConfirmDiscardAsync()
    {
        await CommitEditsAsync();
        var dirty = new[] { _left, _right }.Where(x => x.Dirty).ToList();
        if (dirty.Count == 0) return true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(Owner, s.CompareUnsavedTitle, s.CompareUnsavedBody(string.Join(", ", dirty.Select(d => d.Name))),
            [s.CommonCancel, s.CompareDontSave, s.CommonSave], 2, 0);
        if (choice <= 0) return false;
        if (choice == 2)
            foreach (var side in dirty)
                if (!await SaveAsync(side)) return false;
        return true;
    }

    async Task SwapAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        (_left.Path, _right.Path) = (_right.Path, _left.Path);
        (_left.Title, _right.Title) = (_right.Title, _left.Title);
        (_left.ReadOnly, _right.ReadOnly) = (_right.ReadOnly, _left.ReadOnly);
        (_left.Real, _right.Real) = (_right.Real, _left.Real); // 파일 없는 칸의 글도 함께 옮긴다
        (_left.ForcedEncoding, _right.ForcedEncoding) = (_right.ForcedEncoding, _left.ForcedEncoding);
        (_left.RepairRequested, _right.RepairRequested) = (_right.RepairRequested, _left.RepairRequested);
        await ReloadAsync();
        RecordHistory();
    }

    async Task PickAsync(Side side)
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side.Left ? Strings.Current.CompareOpenLeftTitle : Strings.Current.CompareOpenRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(Owner.StorageProvider),
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: false);
        if (!await ConfirmDiscardAsync()) return;
        side.Path = path;
        side.ForcedEncoding = null;
        side.RepairRequested = false;
        await ReloadAsync();
        RecordHistory();
    }

    /// <summary>
    /// 주소 창에 쓴 경로를 연다. 파일이 없거나 폴더면 알리고 지금 파일을 그대로 둔다.
    /// 있는지는 백그라운드에서 본다 (네트워크 드라이브에서 UI가 멈추지 않게). 압축 파일 안 경로(<c>a.zip!/x</c>)도 받는다.
    /// </summary>
    async Task ApplyTypedPathAsync(Side side)
    {
        var path = (side.PathText.Text ?? "").Trim().Trim('"');
        if (path.Length == 0 || path == side.Path) { side.Editor.TextArea.Focus(); return; }
        var s = Strings.Current;
        var problem = await Task.Run(async () =>
        {
            if (ArchivePaths.TryParse(path) is { } inner)
                return File.Exists(inner.Archive) && await new ArchiveFileSystem(inner.Archive).StatAsync(path) is { IsDirectory: false } ? null : s.HomeRecentMissing(path);
            if (Directory.Exists(path)) return s.CompareTypedFolder;
            return File.Exists(path) ? null : s.HomeRecentMissing(path);
        });
        if (problem is not null)
        {
            await MessageDialog.ShowAsync(Owner, AppIdentity.DisplayName, problem, [s.CommonClose], 0, 0);
            // 지금 파일은 그대로. 고쳐 다시 쓸 수 있게 쓴 글을 주소 창에 되살린다 (Esc나 다른 곳을 누르면 지금 경로로 돌아간다).
            side.PathText.Focus();
            side.PathText.Text = path;
            side.PathText.CaretIndex = path.Length;
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        PickLocations.Remember(path, isFolder: false);
        side.Path = path;
        side.Title = null;
        side.ForcedEncoding = null;
        side.RepairRequested = false;
        _skipHistory = false;
        side.PathEditing = false;
        side.Editor.TextArea.Focus();
        await ReloadAsync();
        RecordHistory();
    }

    // --- 끌어다 놓기 (ROADMAP §6-1 ①) ---

    DropTarget TargetAt(DragEventArgs e)
    {
        var x = e.GetPosition(this).X;
        return x < Bounds.Width * 0.45 ? DropTarget.Left : x > Bounds.Width * 0.55 ? DropTarget.Right : DropTarget.Window;
    }

    static IReadOnlyList<DroppedItem> ItemsOf(DragEventArgs e) =>
        DropClassifier.Classify(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? []);

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var items = ItemsOf(e);
        if (items.Count == 0 || items.Count > 3) { e.DragEffects = DragDropEffects.None; _dropHint.IsVisible = false; return; }
        e.DragEffects = DragDropEffects.Copy;
        var target = items.Count == 2 ? DropTarget.Window : TargetAt(e);
        Grid.SetColumn(_dropHint, target == DropTarget.Right ? 2 : 0);
        Grid.SetColumnSpan(_dropHint, target == DropTarget.Window ? 3 : target == DropTarget.Right ? 2 : 1);
        _dropHint.IsVisible = true;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        _dropHint.IsVisible = false;
        var plan = DropPlanner.Plan(ItemsOf(e), TargetAt(e), Item(_left), Item(_right));
        await ApplyDropAsync(Owner, plan, async (l, r) =>
        {
            if (!await ConfirmDiscardAsync()) return;
            _left.Path = l;
            _skipHistory = false;
            _left.Title = _right.Title = null;
            _right.Path = r;
            foreach (var side in new[] { _left, _right }) { side.ForcedEncoding = null; side.RepairRequested = false; }
            await ReloadAsync();
            RecordHistory();
        });
    }

    static DroppedItem? Item(Side side) => side.Path is { } p ? new DroppedItem(p, DroppedKind.File) : null;

    /// <summary>
    /// 놓기 결과를 처리한다 (홈 창과 함께 쓴다): 파일 비교는 <paramref name="apply"/>로 양쪽을 정하고, 아직 없는 기능(폴더 비교·병합)과
    /// 거절은 안내 대화상자로 알린다. 파일 + 폴더면 폴더 안의 같은 이름 파일을 찾는다.
    /// </summary>
    internal static async Task ApplyDropAsync(Window owner, DropPlan plan, Func<string?, string?, Task> apply, bool allowPendingFolders = false)
    {
        var s = Strings.Current;
        async Task Tell(string message) => await MessageDialog.ShowAsync(owner, AppIdentity.DisplayName, message, [s.CommonClose], 0, 0);
        switch (plan)
        {
            case DropPlan.Pending p when allowPendingFolders || p.Left is not { IsContainer: true } && p.Right is not { IsContainer: true }:
                await apply(p.Left?.Path, p.Right?.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.Folders } c:
                FolderCompareView.Open(owner, c.Left.Path, c.Right.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.Files } c:
                await apply(c.Left.Path, c.Right.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.FileAgainstFolder } c:
            {
                var (file, container, fileOnLeft) = DropPlanner.Split(c);
                var name = System.IO.Path.GetFileName(file.Path);
                var counterpart = DropPlanner.Counterpart(container, name);
                var exists = container.Kind == DroppedKind.Archive
                    ? await new ArchiveFileSystem(container.Path).StatAsync(counterpart) is { IsDirectory: false }
                    : File.Exists(counterpart);
                if (!exists) { await Tell(s.DropNoCounterpart(name)); return; }
                await apply(fileOnLeft ? file.Path : counterpart, fileOnLeft ? counterpart : file.Path);
                break;
            }
            case DropPlan.Rejected { Reason: DropRejection.Empty }:
                break;
            case DropPlan.Rejected or DropPlan.Merge:
                await Tell(s.DropTooMany);
                break;
            default: // 파일과 폴더의 짝은 아직 지원하지 않는다
                await Tell(s.FolderFileVsFolderLater);
                break;
        }
    }

    // --- 단축키 (UI_UX.md) ---

    public void HandleKey(KeyEventArgs e)
    {
        var primary = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var handled = true;
        if (_left.PathText.IsFocused || _right.PathText.IsFocused) return; // 주소 창의 키는 주소 창이 받는다 (Enter·Esc는 PathRow)
        if (_findBox.IsFocused || _replaceBox.IsFocused)
        {
            // 찾기·바꾸기 칸에서는 글자 편집 키(Ctrl+Z·Ctrl+←→·Ctrl+L 등)를 칸에 맡기고, 찾기 키만 받는다.
            switch (e.Key)
            {
                case Key.Enter when _replaceBox.IsFocused: if (primary) ReplaceAll(); else ReplaceOne(); break;
                case Key.Enter: Find(forward: !shift); break;
                case Key.F3: Find(forward: !shift); break;
                case Key.Escape: CloseFind(); break;
                case Key.F when primary: _findBox.Focus(); _findBox.SelectAll(); break;
                case Key.H when primary: _replaceRow.IsVisible = true; _replaceBox.Focus(); break;
                case Key.N when primary: if (shift) GoDiffLine(next: true); else GoDiff(next: true); break;
                case Key.P when primary: if (shift) GoDiffLine(next: false); else GoDiff(next: false); break;
                default: handled = false; break;
            }
            e.Handled = handled;
            return;
        }
        // 키는 Beyond Compare 기본값을 따른다 (BC_COMPAT.md §1). F8·Alt+↑↓는 덧붙인 키.
        switch (e.Key)
        {
            case Key.N when primary: if (shift) GoDiffLine(next: true); else GoDiff(next: true); break;
            case Key.P when primary: if (shift) GoDiffLine(next: false); else GoDiff(next: false); break;
            case Key.F8: GoDiff(next: !shift); break;
            case Key.Down when alt: GoDiff(next: true); break;
            case Key.Up when alt: GoDiff(next: false); break;
            case Key.R when primary: _ = shift ? CopyLineAsync(toRight: true) : CopySelectionAsync(toRight: true); break;
            case Key.L when primary: _ = shift ? CopyLineAsync(toRight: false) : CopySelectionAsync(toRight: false); break;
            case Key.D when primary && !shift: SelectSection(); break;
            case Key.F when primary: OpenFind(); break;
            case Key.H when primary: OpenFind(replace: true); break;
            case Key.F3: if (_findBox.Text is { Length: > 0 }) Find(forward: !shift); else OpenFind(); break;
            case Key.G when primary: _ = GoToLineAsync(); break;
            case Key.I when primary && !shift: _ = ShowInfoAsync(); break;
            case Key.Escape when _findBar.IsVisible: CloseFind(); break;
            case Key.Escape when _merge is not null: ClearMerge(); break;
            case Key.V when primary && shift: _ = PasteAsync(FocusedSide()); break;
            case Key.S when primary: _ = shift ? SaveAllAsync() : SaveFocusedAsync(); break;
            case Key.F12: _ = SaveAsAsync(FocusedSide()); break;
            case Key.Z when primary && !shift: _ = UndoAsync(redo: false); break;
            case Key.Z when primary && shift: _ = UndoAsync(redo: true); break;
            case Key.Y when primary: _ = UndoAsync(redo: true); break;
            case Key.F5 when primary: _ = RecompareNowAsync(); break;
            case Key.F5: _ = ReloadWithConfirmAsync(); break;
            case Key.U when primary && !shift: _ = SwapAsync(); break;
            case Key.O when primary: _ = PickAsync(_right.Path is null || _left.Path is not null && _right.Editor.TextArea.IsFocused ? _right : _left); break;
            case Key.OemComma when primary: SettingsWindow.Open(Owner); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    async Task ReloadWithConfirmAsync()
    {
        if (await ConfirmDiscardAsync()) await ReloadAsync();
    }
}

/// <summary>명령줄(BC 스위치)로 연 비교 창의 선택 — 칸 제목, 읽기 전용, 중요하지 않은 차이 무시(null이면 설정을 따른다).</summary>
public sealed record CompareOptions(string? LeftTitle = null, string? RightTitle = null, bool LeftReadOnly = false, bool RightReadOnly = false, bool? IgnoreUnimportant = null)
{
    public static readonly CompareOptions None = new();
}
