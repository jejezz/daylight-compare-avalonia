# UI/UX — Daylight Compare

공통 규칙은 `application-release-templates/conventions/ui-ux.md`·`theming.md`, 모습은 Daylight Commander를 따른다. 이 문서는 **다른 점만** 적는다.

## 화면 구조

- **홈 창** (`HomeWindow`): 앱 바 44(제목 · 오른쪽 끝 `설정 | 정보`) + 본문. 본문은 새 비교 카드 · 저장한 세션 · 최근 목록이 올 자리이고,
  0단계에서는 빈 상태(48px `compare` 아이콘 + 안내 두 줄)만 있다. Commander의 명령 모음·패널·트리는 없다.
- **비교 창** (3단계부터): 세션 하나 = 창 하나, 창 안에 파일 비교 탭. ROADMAP §6.

## 색 (예정)

차이 종류 토큰을 테마에 더한다: `AppDiffAdded`, `AppDiffRemoved`, `AppDiffChanged`, `AppDiffUnimportant`, `AppDiffConflict`.
고대비 색 구성에서도 구분되도록 색만 쓰지 않고 가장자리 막대·기호를 함께 쓴다.

## 단축키 (예정, 공통 단축키 외)

| 기능 | 키 |
|---|---|
| 다음 / 이전 차이 | `F8` / `Shift+F8` (또는 `Alt+↓` / `Alt+↑`) |
| 왼쪽 → 오른쪽 복사 | `Ctrl+→` |
| 오른쪽 → 왼쪽 복사 | `Ctrl+←` |
| 다시 비교 | `F5` |

지금 홈 창에 있는 것: `Ctrl+,` 설정, `Ctrl+W` 창 닫기 (macOS는 ⌘).
