# UI/UX — Daylight Compare

공통 규칙은 `application-release-templates/conventions/ui-ux.md`·`theming.md`, 모습은 Daylight Commander를 따른다. 이 문서는 **다른 점만** 적는다.

## 화면 구조

- **홈 창** (`HomeWindow`): 앱 바 44(제목 · 오른쪽 끝 `설정 | 정보`) + 본문. 본문은 48px `compare` 아이콘, 안내 두 줄,
  **새 비교 카드**(왼쪽·오른쪽 경로 + [열기…], [글 붙여 넣어 비교] [비교]). 양쪽을 고르면 바로 비교 창이 열리고 카드는 비워진다.
  창 어디에든 끌어다 놓을 수 있다(ROADMAP §6-1 ①). 저장한 세션·최근 목록은 4단계. Commander의 명령 모음·패널·트리는 없다.
- **비교 창** (`Compare/CompareWindow`, 창 하나 = 비교 하나):
  - 앱 바 44: 왼쪽에 결과 요약(차이 n곳 · 중요하지 않은 차이 m곳 · 인코딩이 다르면 그것도 · 저장함 같은 알림 4초), 오른쪽에
    BC 텍스트 비교의 묶음 순서: [보기 필터 ▾] · 중요하지 않은 차이 무시 | 왼쪽으로·오른쪽으로 복사 · 저장 | 이전·다음 차이 구간 | 좌우 바꾸기 · 파일 다시 읽기 ·
    찾기 · **모든 명령**(BC 메뉴 묶음 Session · File · Edit · Search · View를 단축키와 함께 — 툴바에 없는 명령도 여기서) | 설정 · 정보. 툴팁은 "이름 (BC 키)".
  - 찾기 막대(`Ctrl+F`, 앱 바 아래 한 줄): 찾을 글 · 이전/다음 · `Aa`(대소문자) · `n/m` · "왼쪽/오른쪽에서 찾는 중"(마지막으로 초점이 있던 쪽) · 닫기.
    입력할 때마다 찾고, 끝에서 처음으로 돌아간다. 접힌 곳 안이면 양쪽을 편다. `Ctrl+H`면 아래에 바꾸기 줄(바꿀 글 · [바꾸기] · [모두 바꾸기], 바꾸기 칸에서 `Enter` / `Ctrl+Enter`).
  - **보기 필터**(BC View): 모두 보기 · 차이 보기 · 같은 것 보기 · 문맥 보기(+ 문맥 줄 수 0~20). 숨기는 행을 **양쪽에서 같은 행 구간으로** 접고
    그 자리에 `⋯ 같은 n줄 ⋯` 한 행을 남긴다. 접기 여백의 [+]/[−]로 한쪽을 펴면 맞은편도 같이 펴진다.
    필터·문맥 줄 수·중요하지 않은 차이 무시는 설정(`compare_view_filter`, `compare_context_lines`, `compare_ignore_unimportant`)에 남아 다음 창도 그대로.
  - 고친 쪽은 경로 앞과 창 제목 앞에 `●`. 압축 파일 안의 파일은 인코딩 옆에 "읽기 전용".
  - 칸마다 경로 줄(파일 아이콘 · 경로 · 인코딩 이름 · [클립보드 열기] · [열기…]). 인코딩 이름을 누르면 "다른 인코딩으로 다시 열기".
  - **파일 없는 칸**(클립보드 비교): 고르지 않은 칸은 빈 편집기라 바로 쓰거나 붙여 넣을 수 있고, [클립보드 열기]는 그 칸을 클립보드의 글로 바꾼다.
    경로 자리에 "붙여 넣은 글". 저장하지 않으므로 `●`도, 닫을 때 묻는 것도 없다 — 남기려면 `F12`(다른 이름으로 저장).
  - 명령줄 `/title1`·`/title2`를 주면 경로 대신 그 이름(창 제목도), `/ro`·`/ro1`·`/ro2`면 그 칸은 읽기 전용.
  - 안내 띠: 바깥에서 파일이 바뀌었는데 여기서 고친 것이 있으면 [다시 읽기 (여기서 고친 것 버림)], 아니면 인코딩 문제가 있을 때
    [줄마다 복구 (A + B)] + [다른 인코딩으로 다시 열기] — 후보마다 깨진 곳 수와 미리보기.
  - 저장 확인 대화상자(물을 것이 있을 때만): 디스크에서 바뀜 · 지금 인코딩에 없는 글자 → [취소] [그대로 저장] [UTF-8로 저장] · 깨진 곳. 마지막 줄에
    "저장하기 전에 원래 파일을 백업 폴더에 남겨 둡니다".
  - 좌우 편집기(AvaloniaEdit, 고정폭 글꼴, 줄바꿈 없음, 바로 편집 — 손을 멈추면 0.5초 뒤 다시 비교) + 맨 오른쪽 14px 개요 막대. 왼쪽 편집기의 세로 스크롤 막대는 숨긴다
    (두 편집기가 함께 스크롤되므로 하나면 된다).
  - 빈 칸·바이너리·너무 큰 파일·읽기 실패는 그 칸 가운데에 한 줄 안내.
- **좌우 맞춤**: 한쪽에만 있는 줄의 맞은편에 채움 줄(빈 행, 회색 바탕)을 넣어 두 편집기의 행을 같게 한다(`AlignedDiff`). 줄 번호 여백은 화면 행이 아니라
  **파일의 실제 줄 번호**를 보이고 채움 줄은 비운다.

## 색

`Domain/Theme/DiffColors` → `ThemeService`가 자원으로 올린다 (색 구성·라이트/다크·고대비마다 다시 계산).
**뜻은 Beyond Compare와 같다**: 중요한 차이 = 빨강, 중요하지 않은 차이 = 파랑 (BC_COMPAT.md §1).

| 자원 | 뜻 | 기본 |
|---|---|---|
| `AppDiffChanged` / `…Strong` | 바뀐 줄 / 줄 안에서 바뀐 단어·글자 | 빨강 16~22% / 42~50% |
| `AppDiffLeftOnly` / `AppDiffRightOnly` (`…Strong`) | 한쪽에만 있는 줄 | 빨강 (BC처럼 바뀜과 같은 색 — 쪽은 기호로) |
| `AppDiffUnimportant` / `AppDiffUnimportantText` | 중요하지 않은 차이의 줄 바탕 / 글자 | 파랑 10~16% / 파란 글자 |
| `AppDiffFiller` | 채움 줄 | 글자색 6~8% |
| `AppDiffMarker` | 줄 번호 옆 기호, 개요 막대의 중요하지 않은 차이 | 회청색 |

- 색만으로 구분하지 않는다: 줄 번호 오른쪽에 기호 `~`(바뀜) · `−`(왼쪽에만) · `+`(오른쪽에만). 고대비 색 구성은 바탕 농도를 높인다.
- 현재 줄 강조는 끈다 — 차이 색과 섞여 다른 색처럼 보인다.

## 단축키 — Beyond Compare 기본 키 (BC_COMPAT.md §1)

BC 도움말은 기본 키를 거의 적지 않아서, ❓는 사용자가 실제 BC5(Tools > Options > Commands)와 맞춰 볼 키다.

| BC 명령 | 키 | 상태 |
|---|---|---|
| Next / Previous Difference Section | `Ctrl+N` / `Ctrl+P` ❓ (덧붙인 키 `F8`/`Shift+F8`, `Alt+↓`/`Alt+↑`) | ✅ |
| Next / Previous Difference (줄 단위) | `Ctrl+Shift+N` / `Ctrl+Shift+P` ❓ | ✅ |
| Copy to Right / Left | `Ctrl+R` / `Ctrl+L` ❓ | ✅ |
| Copy Line to Right / Left | `Ctrl+Shift+R` / `Ctrl+Shift+L` ❓ | ✅ |
| Select Section | `Ctrl+D` ❓ | ✅ |
| Reload Files / Recompare Files | `F5` / `Ctrl+F5` ❓ | ✅ |
| Swap Sides | `Ctrl+U` ❓ (BC 키 미확인 — 지금은 Commander와 같은 키) | ✅ |
| Open File / Open Clipboard | `Ctrl+O` / `Ctrl+Shift+V` ❓ | ✅ |
| Save File / Save File As / 고친 쪽 모두 저장 | `Ctrl+S` / `F12` ❓ / `Ctrl+Shift+S` (BC에서는 Save Session — 세션이 생기면 다시 본다) | ✅ |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y`, `Ctrl+Shift+Z` | ✅ |
| Find / Replace / Find Next / Find Previous | `Ctrl+F` / `Ctrl+H` / `F3` / `Shift+F3` ❓, 닫기 `Esc` | ✅ |
| Go To (줄) | `Ctrl+G` ❓ | ✅ |
| Text Compare Info | `Ctrl+I` ❓ | ✅ |
| 보기 필터, Ignore Unimportant Differences | 툴바·모든 명령 메뉴 (BC 기본 키 미확인) | ✅ |
| Align With, Bookmarks, Insert/Delete Line, Indent | BC_COMPAT.md §1 | ➕ 나중 |

`Ctrl+W` 창 닫기, `Ctrl+,` 설정 (macOS는 ⌘).

## 끌어다 놓기

창 왼쪽 45% / 가운데 / 오른쪽 45%로 나눠 놓는 자리를 정한다(`DropTarget`). 끄는 동안 그 자리를 주색 2px 테두리로 보인다. 두 개를 놓으면 위치와 상관없이 양쪽.
규칙은 `Domain/Drop/DropPlanner`(ROADMAP §6-1 ①).

## 화면 시험

`tests/DaylightCompare.App.Tests`(Avalonia Headless + Skia, xUnit v3)가 창을 띄우지 않고 비교 창·홈 창을 그려 본다.
`DCMP_SHOTS` 환경 변수에 폴더를 주면 PNG를 남긴다 — 화면을 바꾼 뒤 눈으로 확인할 때 쓴다.
