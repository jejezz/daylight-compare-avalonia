using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DaylightCompare.Domain.Binary;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 16진수 + 문자 뷰 한쪽 (바이너리 비교의 좌우 패널). 보이는 행만 그린다 — 행은 <see cref="BinaryRows"/>가 계산하므로 GB 파일도 같다.
/// 한 행은 "오프셋  XX XX … XX  문자열"이고 고정폭이라 글자 칸 위치를 계산해 배경(차이)·커서·무시 구간 색을 칠한다.
/// 다른 바이트는 <b>같은 위치의 반대쪽 바이트와 비교</b>해서 칠한다(바뀐 구간 안에서도 같은 바이트는 칠하지 않는다). 한쪽에만 있는 구간의 반대쪽은 채움 줄.
/// </summary>
sealed class HexPane : Control
{
    // 한 행의 글자 배치 (칸 번호)
    const int OffsetDigits = 10;
    const int HexStart = OffsetDigits + 2;
    static int HexCol(int k) => HexStart + k * 3 + (k >= 8 ? 1 : 0);
    const int AsciiStart = HexStart + 16 * 3 + 1 + 2;
    const int LineChars = AsciiStart + 16;

    readonly bool _left;
    IByteSource? _source;
    IByteSource? _other;
    BinaryRows? _rows;
    IReadOnlyList<ByteRange> _ignore = [];
    long _top;
    long _addressBase;
    (long Row, int Col)? _cursor;
    Typeface _typeface = new(FontFamily.Default);
    double _fontSize = 13;
    double _cellW, _rowH;

    public HexPane(bool left)
    {
        _left = left;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) ViewportChanged?.Invoke();
    }

    /// <summary>보이는 행 수나 크기가 바뀜 (스크롤 막대·개요 막대가 다시 그린다).</summary>
    public event Action? ViewportChanged;

    /// <summary>그림에서 눌린 바이트 (행, 칸) — 커서를 옮겨 달라는 요청.</summary>
    public event Action<long, int>? CursorRequested;

    /// <summary>마우스 휠: 행 단위로 얼마나 (아래로 굴리면 양수).</summary>
    public event Action<double>? WheelRequested;

    public double RowHeight => _rowH;
    public int VisibleRows => _rowH <= 0 ? 0 : Math.Max(1, (int)(Bounds.Height / _rowH));

    /// <summary>한 줄이 차지하는 너비 (패널 최소 너비 계산).</summary>
    public double LineWidth => _cellW * LineChars + 24;

    /// <summary>편집기와 같은 글꼴·크기 (설정의 편집기 글꼴, FsBody)를 자원에서 읽어 칸 크기를 다시 잰다. 바뀌었으면 true. (그리는 중에는 무효화할 수 없어서 값만 바꾼다.)</summary>
    bool UpdateFont()
    {
        var family = this.TryFindResource("AppEditorFont", ActualThemeVariant, out var f) && f is FontFamily ff ? ff : FontFamily.Default;
        var size = this.TryFindResource("FsBody", ActualThemeVariant, out var z) && z is double d ? d : 13;
        if (_cellW > 0 && _fontSize == size && _typeface.FontFamily.Equals(family)) return false;
        _typeface = new Typeface(family);
        _fontSize = size;
        _cellW = Format("0").Width;
        _rowH = Math.Ceiling(_fontSize * 1.55);
        return true;
    }

    FormattedText Format(string text, IBrush? brush = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush ?? Brushes.Black);

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateFont();
        return new Size(Math.Max(LineWidth, 0), 0);
    }

    public void SetData(BinaryRows? rows, IByteSource? source, IByteSource? other, IReadOnlyList<ByteRange> ignore)
    {
        (_rows, _source, _other, _ignore) = (rows, source, other, ignore);
        InvalidateVisual();
    }

    /// <summary>오프셋 칸에 더해 보일 시작 주소 (펌웨어 이미지는 그 주소, 그냥 파일은 0).</summary>
    public long AddressBase { get => _addressBase; set { if (_addressBase == value) return; _addressBase = value; InvalidateVisual(); } }

    public long TopRow
    {
        get => _top;
        set { if (_top == value) return; _top = value; InvalidateVisual(); }
    }

    public (long Row, int Col)? CursorCell
    {
        get => _cursor;
        set { _cursor = value; InvalidateVisual(); }
    }

    static IBrush? Brush(string key) => DiffViewState.Brush(key);

    public override void Render(DrawingContext dc)
    {
        if (UpdateFont()) Avalonia.Threading.Dispatcher.UIThread.Post(() => { InvalidateMeasure(); InvalidateVisual(); ViewportChanged?.Invoke(); });
        dc.FillRectangle(Brush("AppEdBg") ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (_rows is null || _source is null || _rowH <= 0 || _rows.RowCount == 0) return;
        var fg = Brush("AppEdFg") ?? Brushes.Black;
        var dim = Brush("AppEdMid") ?? Brushes.Gray;
        var changedRow = Brush("AppDiffChanged");
        var changedByte = Brush("AppDiffChangedStrong");
        var onlyRow = Brush(_left ? "AppDiffLeftOnly" : "AppDiffRightOnly");
        var onlyByte = Brush(_left ? "AppDiffLeftOnlyStrong" : "AppDiffRightOnlyStrong");
        var filler = Brush("AppDiffFiller");
        var cursorBrush = Brush("AppPrimary");
        var buf = new byte[BinaryRows.Width];
        var otherBuf = new byte[BinaryRows.Width];
        var rowsVisible = (int)Math.Ceiling(Bounds.Height / _rowH) + 1;
        for (var n = 0; n < rowsVisible; n++)
        {
            var rowIndex = _top + n;
            if (rowIndex >= _rows.RowCount) break;
            var row = _rows.At(rowIndex);
            var y = n * _rowH;
            var count = _left ? row.LeftCount : row.RightCount;
            var offset = _left ? row.LeftOffset : row.RightOffset;
            var otherCount = _left ? row.RightCount : row.LeftCount;
            var otherOffset = _left ? row.RightOffset : row.LeftOffset;
            var rowRect = new Rect(0, y, Bounds.Width, _rowH);

            // 행 바탕
            if (count == 0 && row.Kind is DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly)
                dc.FillRectangle(filler ?? Brushes.Transparent, rowRect); // 이 쪽에는 없는 줄 (채움)
            else if (row.Kind == DiffBlockKind.Changed) dc.FillRectangle(changedRow ?? Brushes.Transparent, rowRect);
            else if (row.Kind is DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly) dc.FillRectangle(onlyRow ?? Brushes.Transparent, rowRect);
            if (count == 0)
            {
                continue;
            }

            var read = _source.Read(offset, buf.AsSpan(0, count));
            var otherRead = otherCount > 0 && _other is not null ? _other.Read(otherOffset, otherBuf.AsSpan(0, otherCount)) : 0;
            count = Math.Min(count, read);

            // 문자열 한 줄과 글자 색
            var text = new char[LineChars];
            Array.Fill(text, ' ');
            var addr = (offset + _addressBase).ToString("X" + OffsetDigits, CultureInfo.InvariantCulture);
            for (var c = 0; c < OffsetDigits && c < addr.Length; c++) text[c] = addr[c];
            for (var k = 0; k < count; k++)
            {
                text[HexCol(k)] = HexDigit(buf[k] >> 4);
                text[HexCol(k) + 1] = HexDigit(buf[k] & 0xF);
                text[AsciiStart + k] = buf[k] is >= 0x20 and < 0x7F ? (char)buf[k] : '·';
            }
            var formatted = Format(new string(text), fg);
            formatted.SetForegroundBrush(dim, 0, OffsetDigits); // 오프셋은 흐리게

            // 바이트별: 차이 배경·무시 구간 글자색
            for (var k = 0; k < count; k++)
            {
                var pos = offset + k;
                var ignored = IsIgnored(pos);
                var different = row.Kind switch
                {
                    DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly => true,
                    DiffBlockKind.Changed => k >= otherRead || buf[k] != otherBuf[k],
                    _ => false,
                };
                var hexX = HexCol(k) * _cellW;
                var asciiX = (AsciiStart + k) * _cellW;
                if (different)
                {
                    var back = row.Kind == DiffBlockKind.Changed ? changedByte : onlyByte;
                    if (back is not null)
                    {
                        dc.FillRectangle(back, new Rect(hexX - 1, y, _cellW * 2 + 2, _rowH));
                        dc.FillRectangle(back, new Rect(asciiX, y, _cellW, _rowH));
                    }
                }
                if (ignored)
                {
                    formatted.SetForegroundBrush(dim, HexCol(k), 2);
                    formatted.SetForegroundBrush(dim, AsciiStart + k, 1);
                }
                if (_cursor is { } cur && cur.Row == rowIndex && cur.Col == k && cursorBrush is not null)
                {
                    var pen = new Pen(cursorBrush, 1.5);
                    dc.DrawRectangle(null, pen, new Rect(hexX - 1, y + 0.5, _cellW * 2 + 2, _rowH - 1), 2, 2);
                    dc.DrawRectangle(null, pen, new Rect(asciiX, y + 0.5, _cellW, _rowH - 1), 2, 2);
                }
            }
            dc.DrawText(formatted, new Point(0, y + (_rowH - formatted.Height) / 2));
        }
    }

    static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'A' + v - 10);

    bool IsIgnored(long pos)
    {
        foreach (var r in _ignore)
            if (pos >= r.Start && pos < r.Start + r.Length) return true;
        return false;
    }

    // --- 입력 ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_rows is null || _rowH <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var row = _top + (long)(p.Y / _rowH);
        if (row >= _rows.RowCount) return;
        var cell = (int)(p.X / _cellW);
        var col = cell >= AsciiStart ? cell - AsciiStart : HexColumnAt(cell);
        if (col is >= 0 and < BinaryRows.Width) CursorRequested?.Invoke(row, col);
        e.Handled = true;
    }

    /// <summary>글자 칸 번호가 가리키는 바이트 번호 (가운데 틈과 오프셋은 가장 가까운 바이트).</summary>
    static int HexColumnAt(int cell)
    {
        if (cell < HexStart) return 0;
        for (var k = 15; k >= 0; k--) if (cell >= HexCol(k)) return k;
        return 0;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        WheelRequested?.Invoke(-e.Delta.Y * 3);
        e.Handled = true;
    }
}
