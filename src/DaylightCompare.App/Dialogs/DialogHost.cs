using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaylightCompare.App.Dialogs;

/// <summary>
/// 창 안의 모달 (UI_UX.md "창 크롬과 대화상자 단추" — A. 인-윈도 모달). OS 창을 따로 만들지 않고, 부모 창 최상위에 얹은 스크림 + 가운데 상자로 띄운다.
/// 창의 맨 위 <see cref="Grid"/>에 마지막 자식으로 넣는다(행·열을 모두 덮는다). 제목은 본문 첫 줄 하나(타이틀바 없음),
/// 주 동작은 맨 오른쪽 <c>accent</c> 알약 하나, 나머지는 <c>text</c> 단추. 되돌릴 수 없는 파괴 동작은 <c>accent danger</c>. <c>Esc</c>·바깥 눌림은 취소.
/// 결과는 <c>Task&lt;int&gt;</c> (고른 단추 번호) — <see cref="MessageDialog.ShowAsync"/>와 같은 모양이라 호출하는 쪽이 같다.
/// </summary>
public sealed class DialogHost : Grid
{
    readonly Border _scrim = new() { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) };
    readonly Border _box = new() { Focusable = true, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    TaskCompletionSource<int>? _pending;
    int _cancelIndex;
    IInputElement? _previousFocus;

    public DialogHost()
    {
        IsVisible = false;
        Children.Add(_scrim);
        Children.Add(_box);
        _box.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _box.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        _scrim.PointerPressed += (_, e) => { Finish(_cancelIndex); e.Handled = true; };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || !IsVisible) return;
            Finish(_cancelIndex);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public bool IsOpen => _pending is not null;

    /// <param name="buttons">왼쪽에서 오른쪽 순서. 주 동작(<paramref name="primaryIndex"/>)은 맨 오른쪽에 두는 것이 규칙이다.</param>
    /// <param name="danger">주 동작이 되돌릴 수 없는 파괴 동작이면 위험색.</param>
    public Task<int> ShowAsync(string title, string message, string[] buttons, int primaryIndex, int cancelIndex, bool danger = false)
    {
        if (_pending is { } old) old.TrySetResult(_cancelIndex);
        _cancelIndex = cancelIndex;
        _pending = new TaskCompletionSource<int>();
        _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

        var heading = new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        Button? primary = null;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i] };
            if (i == primaryIndex)
            {
                button.Classes.Add("accent");
                if (danger) button.Classes.Add("danger");
                primary = button;
            }
            else button.Classes.Add("text");
            button.Click += (_, _) => Finish(index);
            row.Children.Add(button);
        }
        _box.MaxWidth = message.Length > 240 || message.Contains('\n') ? 640 : 440;
        _box.MinWidth = 320;
        _box.Child = new StackPanel { Spacing = 8, Children = { heading, body, row } };
        IsVisible = true;
        var task = _pending.Task;
        Dispatcher.UIThread.Post(() => (primary as IInputElement ?? _box).Focus());
        return task;
    }

    void Finish(int index)
    {
        if (_pending is not { } pending) return;
        _pending = null;
        IsVisible = false;
        _box.Child = null;
        pending.TrySetResult(index);
        if (_previousFocus is { } f) Dispatcher.UIThread.Post(() => f.Focus());
    }
}
