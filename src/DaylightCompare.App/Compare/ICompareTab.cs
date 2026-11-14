using Avalonia.Controls;
using Avalonia.Input;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 비교 창(<see cref="CompareHost"/>)의 탭 하나에 들어가는 보기 — 파일 비교(<see cref="CompareView"/>)나 폴더 비교(<see cref="FolderCompareView"/>).
/// 창 수준의 일(제목·닫기·열림·키)은 이 약속으로 호스트가 맡는다.
/// </summary>
public interface ICompareTab
{
    Control View { get; }
    bool IsFolder { get; }
    /// <summary>창 제목 (앱 이름 앞부분). 고친 것이 있으면 앞에 ●.</summary>
    string WindowTitle { get; }
    /// <summary>탭 머리글에 보일 짧은 이름.</summary>
    string TabTitle { get; }
    string? TabToolTip { get; }
    bool IsDirty { get; }
    event Action? TitleChanged;
    /// <summary>처음 보일 때 한 번: 읽고 비교를 시작한다.</summary>
    void Start();
    /// <summary>탭을 닫아도 되나 (고친 것이 있으면 저장·버리기·취소를 묻는다).</summary>
    Task<bool> ConfirmCloseAsync();
    void Dispose();
    /// <summary>호스트가 터널로 받은 키를 먼저 건넨다 (처리했으면 <c>e.Handled</c>).</summary>
    void HandleKey(KeyEventArgs e);
}
