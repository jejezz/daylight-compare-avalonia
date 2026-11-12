using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DaylightCompare.App.Dialogs;

/// <summary>글 한 줄을 받는 짧은 질문 창 (줄로 가기 등). 확인하면 입력한 글, 취소·닫기면 null.</summary>
public sealed class InputDialog : DialogBase
{
    readonly TaskCompletionSource<string?> _result = new();
    readonly TextBox _box = new() { MinWidth = 320 };

    public static Task<string?> ShowAsync(Window owner, string title, string prompt, string initial, string ok, string cancel)
    {
        var dialog = new InputDialog(title, prompt, initial, ok, cancel);
        _ = dialog.ShowDialog(owner);
        return dialog._result.Task;
    }

    InputDialog(string title, string prompt, string initial, string ok, string cancel)
    {
        Title = title;
        MinWidth = 380;
        var heading = new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var label = new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _box.Text = initial;
        var okButton = new Button { Content = ok, Classes = { "accent" } };
        var cancelButton = new Button { Content = cancel };
        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => Close();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, okButton },
        };
        Content = new StackPanel { Margin = new Thickness(24), Spacing = 8, Children = { heading, label, _box, row } };
        Dismissed += () => _result.TrySetResult(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _box.Focus();
            _box.SelectAll();
        });
    }

    void Accept()
    {
        _result.TrySetResult(_box.Text ?? "");
        CloseFromState();
    }
}
