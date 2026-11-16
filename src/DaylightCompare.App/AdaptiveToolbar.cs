using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DaylightCompare.App.Icons;

namespace DaylightCompare.App;

/// <summary>
/// 폭에 맞춰 모습을 바꾸는 툴바: 아이콘 위·글자 아래(탭 막대 모양)로 시작해, 폭이 모자라면
/// ① <see cref="PriorityProperty"/>가 큰(덜 중요한) 단추부터 글자를 떼어 아이콘만 남기고, ② 그래도 모자라면 같은 순서로 <c>»</c> 메뉴에 접는다.
/// 글자를 달 단추는 <see cref="Labeled"/>로 이름과 우선순위를 준다 (대상은 <c>Classes.tool</c> 단추, 내용이 <see cref="DuoIcon"/>인 것).
/// 구분선·드롭다운 같은 다른 자식은 그대로 두고 접지도 않는다. 오른쪽 끝에 맞춰 놓인다.
/// 시도하는 계산은 숫자만으로 하고, 결정이 난 뒤에만 단추의 모습을 바꾼다 (배치 중에 속성이 번갈아 바뀌어 무한히 다시 배치되는 일이 없게).
/// </summary>
public sealed class AdaptiveToolbar : Panel
{
    public static readonly AttachedProperty<string?> LabelProperty = AvaloniaProperty.RegisterAttached<AdaptiveToolbar, Control, string?>("Label");
    /// <summary>작을수록 중요하다 — 글자도 단추도 가장 나중에 잃는다.</summary>
    /// <summary>글자를 지금 언어로 다시 읽는 함수 (언어가 바뀌면 다시 부른다).</summary>
    public static readonly AttachedProperty<Func<string>?> LabelSourceProperty = AvaloniaProperty.RegisterAttached<AdaptiveToolbar, Control, Func<string>?>("LabelSource");
    public static readonly AttachedProperty<int> PriorityProperty = AvaloniaProperty.RegisterAttached<AdaptiveToolbar, Control, int>("Priority", 1);
    public static readonly AttachedProperty<bool> IsSeparatorProperty = AvaloniaProperty.RegisterAttached<AdaptiveToolbar, Control, bool>("IsSeparator");

    public static string? GetLabel(Control c) => c.GetValue(LabelProperty);
    public static void SetLabel(Control c, string? value) => c.SetValue(LabelProperty, value);
    public static int GetPriority(Control c) => c.GetValue(PriorityProperty);
    public static void SetPriority(Control c, int value) => c.SetValue(PriorityProperty, value);

    /// <summary>단추에 글자와 우선순위를 준다.</summary>
    public static T Labeled<T>(T tool, Func<string> label, int priority) where T : Control
    {
        tool.SetValue(LabelSourceProperty, label);
        SetLabel(tool, label());
        SetPriority(tool, priority);
        return tool;
    }

    /// <summary>묶음 사이의 세로 선. 맨 앞·맨 뒤·연달아 놓이게 되면 저절로 빠진다.</summary>
    public static Border Separator()
    {
        var line = new Border { Width = 1, Height = 24, Margin = new Thickness(4, 0) };
        line.SetValue(IsSeparatorProperty, true);
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("AppStrokeStrong"));
        return line;
    }

    const double Spacing = 6;
    const double IconOnlySize = 30;
    const double LabeledHeight = 42;
    const double LabeledMinWidth = 40;
    const double LabelFontSize = 11;

    static AdaptiveToolbar()
    {
        LabelProperty.Changed.AddClassHandler<Control>((c, _) => (c.Parent as AdaptiveToolbar)?.OnLabelChanged(c));
    }

    sealed class Tool
    {
        public required Button Button;
        public required DuoIcon Icon;
        public required TextBlock Text;
        public required StackPanel Stack;
        public required Grid Face;
        public required Border Underline;
        public bool Labeled;
    }

    sealed class Item
    {
        public required Control Control;
        public Tool? Tool;
        public bool IsSeparator;
        public double Plain, WithLabel;
        public bool UseLabel, Hidden;
        public int Priority;
        public double Width => Tool is null ? Plain : UseLabel ? WithLabel : Plain;
    }

    readonly Dictionary<Control, Tool> _tools = [];
    readonly Button _more;
    List<Item> _sequence = [];
    List<Item> _hidden = [];
    double _total;

    public AdaptiveToolbar()
    {
        ClipToBounds = true;
        _more = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "caret-down" } };
        _more.Click += (_, _) => ShowOverflow();
        Children.Add(_more);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiScale.Changed += OnScaleChanged;
        Strings.Changed += OnLanguageChanged;
        OnLanguageChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiScale.Changed -= OnScaleChanged;
        Strings.Changed -= OnLanguageChanged;
        base.OnDetachedFromVisualTree(e);
    }

    void OnScaleChanged() => InvalidateMeasure();

    void OnLanguageChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        foreach (var child in Children)
            if (child.GetValue(LabelSourceProperty) is { } source) SetLabel(child, source());
    });

    void OnLabelChanged(Control c)
    {
        if (_tools.TryGetValue(c, out var tool)) tool.Text.Text = GetLabel(c);
        InvalidateMeasure();
    }

    // --- 배치 ---

    double TextWidth(string text) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this)), UiScale.Scaled(LabelFontSize), null).Width;

    Tool? ToolOf(Control c)
    {
        if (_tools.TryGetValue(c, out var existing)) return existing;
        if (c is not Button button || GetLabel(c) is not { Length: > 0 } label || button.Content is not DuoIcon icon) return null;
        var text = new TextBlock { Text = label, FontSize = LabelFontSize, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { text } };
        var underline = new Border { Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false };
        underline.Bind(Border.BackgroundProperty, underline.GetResourceObservable("AppPrimary"));
        var tool = new Tool { Button = button, Icon = icon, Text = text, Stack = stack, Underline = underline, Face = new Grid { Children = { stack, underline } } };
        if (button is ToggleButton toggle)
        {
            underline.IsVisible = toggle.IsChecked == true;
            toggle.IsCheckedChanged += (_, _) => underline.IsVisible = toggle.IsChecked == true && tool.Labeled;
        }
        _tools[c] = tool;
        return tool;
    }

    /// <summary>보이는 항목을 차례로 훑어 구분선을 정리한다 (맨 앞·맨 뒤·연달은 것은 뺀다).</summary>
    static List<Item> Sequence(List<Item> items)
    {
        var result = new List<Item>();
        foreach (var item in items)
        {
            if (item.Hidden) continue;
            if (item.IsSeparator && (result.Count == 0 || result[^1].IsSeparator)) continue;
            result.Add(item);
        }
        while (result.Count > 0 && result[^1].IsSeparator) result.RemoveAt(result.Count - 1);
        return result;
    }

    double WidthOf(List<Item> items, bool anyHidden, double moreWidth)
    {
        var seq = Sequence(items);
        var w = seq.Sum(i => i.Width) + Math.Max(0, seq.Count - 1) * Spacing;
        return anyHidden ? w + (seq.Count > 0 ? Spacing : 0) + moreWidth : w;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = UiScale.Scaled(LabeledHeight);
        var moreWidth = UiScale.Scaled(IconOnlySize);
        var items = new List<Item>();
        foreach (var child in Children)
        {
            if (child == _more || !child.IsVisible) continue;
            var item = new Item { Control = child, IsSeparator = child.GetValue(IsSeparatorProperty), Priority = GetPriority(child), Tool = ToolOf(child) };
            if (item.Tool is { } tool)
            {
                item.Plain = UiScale.Scaled(IconOnlySize);
                item.WithLabel = Math.Max(UiScale.Scaled(LabeledMinWidth), Math.Ceiling(TextWidth(tool.Text.Text ?? "")) + 14);
                item.UseLabel = true;
            }
            else
            {
                child.Measure(Size.Infinity);
                item.Plain = child.DesiredSize.Width;
            }
            items.Add(item);
        }

        var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        var tools = items.Where(i => i.Tool is not null).ToList();
        bool Over() => WidthOf(items, items.Any(i => i.Hidden), moreWidth) > room;
        // ① 덜 중요한 것부터 글자를 뗀다. ② 그래도 모자라면 같은 순서로 접는다.
        foreach (var item in tools.OrderByDescending(i => i.Priority).ThenByDescending(i => items.IndexOf(i)))
        {
            if (!Over()) break;
            item.UseLabel = false;
        }
        foreach (var item in tools.OrderByDescending(i => i.Priority).ThenByDescending(i => items.IndexOf(i)))
        {
            if (!Over()) break;
            item.Hidden = true;
        }

        _hidden = [.. items.Where(i => i.Hidden)];
        _sequence = Sequence(items);
        _total = WidthOf(items, _hidden.Count > 0, moreWidth);

        // 결정된 모습을 단추에 입힌다 (바뀐 것만).
        foreach (var item in items)
            if (item.Tool is { } tool) Apply(tool, item.UseLabel && !item.Hidden);
        foreach (var item in _sequence)
        {
            if (item.Tool is null) item.Control.Measure(Size.Infinity);
            else item.Control.Measure(new Size(item.Width, height));
        }
        _more.Measure(new Size(moreWidth, height));
        _more.IsVisible = _hidden.Count > 0;
        return new Size(Math.Min(_total, room), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = Math.Max(0, finalSize.Width - _total);
        foreach (var item in _sequence)
        {
            var c = item.Control;
            var h = c.DesiredSize.Height;
            c.Arrange(new Rect(x, (finalSize.Height - h) / 2, item.Width, h));
            x += item.Width + Spacing;
        }
        // 접힌 단추와 안 쓰는 항목은 잘리는 곳 밖에 둔다 (보이지도, 눌리지도 않는다).
        foreach (var child in Children)
        {
            if (child == _more || _sequence.Any(i => i.Control == child)) { child.IsHitTestVisible = true; continue; }
            child.IsHitTestVisible = false;
            child.Arrange(new Rect(-10000, 0, Math.Max(1, child.DesiredSize.Width), Math.Max(1, child.DesiredSize.Height)));
        }
        if (_hidden.Count > 0) _more.Arrange(new Rect(Math.Min(x, finalSize.Width - _more.DesiredSize.Width), (finalSize.Height - _more.DesiredSize.Height) / 2, _more.DesiredSize.Width, _more.DesiredSize.Height));
        else _more.Arrange(new Rect(-10000, 0, 1, 1));
        return finalSize;
    }

    /// <summary>단추를 글자 있는 모양(아이콘 위·글자 아래) 또는 아이콘만 있는 모양으로.</summary>
    static void Apply(Tool tool, bool labeled)
    {
        if (tool.Labeled == labeled && tool.Button.Content is not null) return;
        var b = tool.Button;
        b.Content = null;
        tool.Stack.Children.Remove(tool.Icon);
        if (labeled)
        {
            tool.Stack.Children.Insert(0, tool.Icon);
            b.Content = tool.Face;
            UiScale.SetFree(b, true);
            b.Width = double.NaN;
            b.Height = UiScale.Scaled(LabeledHeight);
            b.Padding = new Thickness(4, 0);
            tool.Underline.IsVisible = b is ToggleButton { IsChecked: true };
        }
        else
        {
            b.Content = tool.Icon;
            b.ClearValue(TemplatedControl.PaddingProperty);
            UiScale.SetFree(b, false);
            b.Width = UiScale.Scaled(IconOnlySize);
            b.Height = UiScale.Scaled(IconOnlySize);
            tool.Underline.IsVisible = false;
        }
        tool.Labeled = labeled;
    }

    // --- » 메뉴 ---

    void ShowOverflow()
    {
        var flyout = new MenuFlyout();
        foreach (var item in _hidden)
        {
            if (item.Tool is not { } tool) continue;
            var b = tool.Button;
            var menu = new MenuItem { Header = ToolTip.GetTip(b) as string ?? GetLabel(b), Icon = new DuoIcon { Icon = tool.Icon.Icon }, IsEnabled = b.IsEnabled };
            if (b is ToggleButton toggle)
            {
                menu.ToggleType = MenuItemToggleType.CheckBox;
                menu.IsChecked = toggle.IsChecked == true;
                menu.Click += (_, _) => toggle.IsChecked = toggle.IsChecked != true;
            }
            else menu.Click += (_, _) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            flyout.Items.Add(menu);
        }
        flyout.ShowAt(_more);
    }
}
