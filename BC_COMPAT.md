# Beyond Compare 호환 — 단축키·명령·필터·병합 흐름

이 앱의 사용자는 Beyond Compare(BC)에 익숙하다. **화면 모습은 달라도 되지만, 단축키·명령 이름과 범위·필터 문법·병합 과정·명령줄은
BC와 같거나 가까워야 한다.** 아이콘이 달라도 BC에 있는 기능은 제공한다. 새 기능을 만들 때는 먼저 이 표에서 BC의 이름·키·흐름을 찾는다.

출처: Scooter Software 온라인 도움말 BC5 (`https://www.scootersoftware.com/v5help/` — commandstext, commandsdir, commandstextmerge,
commandshex, commandspix, file_masks, dir_filtering_the_view, session*, command_line_reference).
**도움말은 기본 단축키를 거의 적지 않는다**(사용자가 Tools > Options > Commands에서 정하라고 함). 도움말이 적은 키는 폴더 비교의
Copy to Right / Left = `Ctrl+R` / `Ctrl+L` 하나뿐이다. 아래 "BC 키"의 나머지는 사용자 제출 사이트(defkey.com) 값이라 **확인 필요**(❓)로 표시한다 —
실제 BC5의 Tools > Options > Commands로 맞춰 본 뒤 ❓를 지운다.

상태: ✅ 같음 · 🔧 바꿀 것 · ➕ 없음(만들 것) · — 해당 없음

## 1. 텍스트 비교 (3단계)

| BC 명령 (메뉴) | BC 키 | 지금 우리 | 상태 |
|---|---|---|---|
| Next / Previous Difference **Section** (Search) | `Ctrl+N` / `Ctrl+P` ❓ | 같음 (+ `F8`, `Alt+↓↑`) | ✅ |
| Next / Previous Difference (줄 단위) | `Shift+Ctrl+N` / `Shift+Ctrl+P` ❓ | 같음 | ✅ |
| Copy to Right / Copy to Left (Edit) | `Ctrl+R` / `Ctrl+L` ❓ (폴더 비교는 도움말에 명시) | 같음 (`Ctrl+→/←`는 편집기의 단어 이동으로 돌려줌) | ✅ |
| Copy Line to Right / Left | `Shift+Ctrl+R` / `Shift+Ctrl+L` ❓ | 같음 | ✅ |
| Copy to Other Side | — | 모든 명령 메뉴 | ✅ |
| Reload Files (저장할지 묻는다) | `F5` ❓ | 같음 | ✅ |
| Recompare Files (고친 것 유지) | `Ctrl+F5` ❓ | 같음 (손을 멈추면 자동으로도) | ✅ |
| Swap Sides (Session) | ❓ (키 미확인) | `Ctrl+U` | ❓ 사용자 확인 |
| Open File | `Ctrl+O` ❓ | `Ctrl+O` | ✅ |
| Open Clipboard (File) | `Shift+Ctrl+V` ❓ | 같음 ("클립보드 열기") | ✅ |
| Save File / Save File As | `Ctrl+S` / `F12` ❓ | 같음 (붙여 넣은 글도 F12로 파일이 된다) | ✅ |
| Save All | 도움말에 없음 | `Ctrl+Shift+S` | ❓ BC의 `Shift+Ctrl+S`는 Save Session |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` ❓ | 같음 (+`Ctrl+Shift+Z`) | ✅ |
| Select Section | `Ctrl+D` ❓ | 같음 | ✅ |
| Find / Replace / Find Next / Find Previous | `Ctrl+F` / `Ctrl+H` / `F3` / `Shift+F3` ❓ | 같음 | ✅ |
| Go To (줄·열) | `Ctrl+G` ❓ | 같음 (줄) | ✅ |
| Toggle / Go To Bookmark 0–9 | `Shift+Ctrl+0–9` / `Ctrl+0–9` ❓ | 없음 | ➕ (나중) |
| Align With / Isolate | `F7` ❓ | 없음 | ➕ (정책의 정렬 설정과 함께, 나중) |
| Increase / Decrease Indent | `Shift+Ctrl+I` / `Shift+Ctrl+U` ❓ | 편집기 기본(Tab) | ➕ |
| Insert Line Before / After, Delete Line | `Shift+Ctrl+Enter` / `Ctrl+Enter` ❓ | 없음 | ➕ |
| Text Compare Info | `Ctrl+I` ❓ | 같음 (크기·시각·인코딩·줄바꿈·줄 수) | ✅ |
| Compare Selection to Clipboard, Convert File (공백 정리·줄바꿈 변환) | — | 없음 | ➕ (나중) |
| **보기 필터** Show All / Show Differences / Show Same / Show Context (문맥 줄 수는 옵션) | 키 미확인 | 툴바 드롭다운 하나 + 모든 명령 › 보기 | ✅ |
| Ignore Unimportant Differences (View) | 키 미확인 | 같은 이름의 토글(켜면 무시), `/iu` | ✅ |
| Line Numbers, Visible Whitespace, Word Wrap, Syntax Highlighting (View) | — | 줄 번호·강조만 | ➕ 공백 보이기 (줄바꿈은 행 맞춤과 충돌 — 검토) |
| Text Details 패널 (아래 줄 상세) | — | 없음 | ➕ |
| Next / Previous Difference Files, Copy File … and Open Next | `Ctrl+M` / `Ctrl+Alt+R/L` ❓ | — | 4단계(폴더 비교에서 연 비교) |
| 색 | 중요한 차이 빨강, 중요하지 않은 차이 파랑 | 같음 | ✅ |

**Session Settings ↔ Compare Policy**: BC의 Importance(중요하지 않은 글·문법 요소·대소문자·줄바꿈 비교), Alignment(Standard / Myers / Patience,
Never align differences), Format(형식·인코딩), Replacements가 우리 정책 파일의 `text` 규칙에 해당한다. 정책 편집 화면(5단계)은 이 탭 이름·순서를 따른다.

## 2. 폴더 비교 (4단계 — 처음부터 BC 방식으로)

- **상태 (4b)**: ✅ Copy to Right / Left(`Ctrl+R`/`Ctrl+L`) · Delete · Open · Next/Previous Difference(`Ctrl+N`/`Ctrl+P`) · Select All · Select Newer/Orphans(왼쪽·오른쪽·양쪽) · Invert Selection ·
  Expand/Collapse All · Refresh(`F5`) · Swap Sides · 보기 필터 열두 가지와 여섯 토글 · Always Show Folders · 필터 막대 · 색.
  ➕ 아직: Copy to Other Side, Move to …, Rename, New Folder, Compare Contents/Quick Compare, Compare To/Align With, Find Filename, Back/Forward/Up One Level, Synchronize, Suppress Filters, Columns, Legend, Select Newer의 "고른 것만" 등.
- 명령: Copy to Right / Left `Ctrl+R` / `Ctrl+L` (도움말 명시), Copy to Other Side, Move to …, Delete, Rename `F2`❓, New Folder `Insert`❓,
  Compare Contents(CRC · Binary · Rules-based), Quick Compare, Compare To `F7`❓, Align With `F6`❓, Expand / Collapse All,
  Select Newer / Orphans(Left·Right·Both), Invert Selection, Refresh `F5`❓ / Full Refresh `Ctrl+F5`❓, Refresh Selection `Shift+F5`❓,
  Next / Previous Difference `Ctrl+N` / `Ctrl+P`❓, Find Filename `Ctrl+F` / `F3`❓, Back / Forward `Alt+←/→`❓, Up One Level, Swap Sides, Synchronize(Update / Mirror).
- **보기 필터**: Show All · Show Differences · Show Same · Show No Orphans · Show Differences but No Orphans · Show Orphans ·
  Show Left/Right Newer · Show Left/Right Orphans (+ 조합). 표시 방식은 DropDown(기본: All / Differences / Same, 가운데 드롭다운에 조합).
  Always Show Folders, Compare Files and Folder Structure / Files Only / Ignore Folder Structure(Flatten), Suppress Filters, Legend.
- 색: 회색 = 모름·오래됨, 검정 = 같음, 보라 = 한쪽에만(orphan), 빨강 = 더 새것·다름, 청록 = 필터로 숨겼지만 보이는 것.
- **필터 문법 (BC와 같게)** — 필터 막대에 `;`로 구분:
  - `*` `?` `[az]` `[a-z]` `[!az]` `[[]`, `*.*`는 확장자 없는 파일도 포함, 끝이 `.`이면 `*`·`?`가 마침표를 안 넘는다(`*.` = 확장자 없음).
  - `f` 파일 포함, `p\` 폴더 포함, `-f` / `-p\` 제외 (예: `*.cpp;*.h;-*.obj;-.git\;-bin\`).
  - 상대 마스크: `p\f`(p 안의 f), `.\f`(기준 폴더 바로 아래), `...\f`(임의 깊이).
  - Session Settings > Name Filters: Include Files / Exclude Files / Include Folders / Exclude Folders 네 목록(줄마다 하나), 프리셋.
  - Other Filters: 수정 날짜·크기·속성·내용, "보호된 OS 파일 제외" 기본 켬.
  - ✅ 우리 `FileMask`가 이 문법을 따른다 (4a). 정책 파일·필터 막대·`/filters=`가 같은 문법을 쓴다.
- Comparison 탭: 크기 · 시각(초 허용 오차, 서머타임·시간대 무시) · 이름 대소문자 · 속성 / 내용 비교(CRC · Binary · Rules-based,
  "빠른 검사가 같으면 건너뛰기", "빠른 검사 결과 덮어쓰기") / 버전 비교.
  - ✅ (4a) 정책 `folder.criteria`의 크기 · 시각(허용 오차, 정확히 n시간 차이 무시) · 내용(규칙 기준) · 해시(SHA-256·CRC32). 내용 단계가 뒤에 있으면 빠른 검사 결과를 덮어쓴다.
    대소문자·확장자 무시·이름 바꾸기 짝짓기. 속성·버전 비교는 아직.

## 3. 텍스트 병합 (7단계 — BC 흐름 그대로)

- 화면: 왼쪽 · 가운데(기준) · 오른쪽 입력은 읽기 전용, 아래에 편집 가능한 **출력** 칸. 출력은 가운데 + 자동으로 받을 수 있는 변경으로 시작한다.
- 색: 왼쪽 변경 청록, 오른쪽 변경 자홍, 충돌 빨강(같은 줄 또는 두 줄 안에서 양쪽이 다르게 바꿈), 손으로 고친 출력 노랑. 같은 변경은 충돌 아님.
- 명령: Take Left / Take Right `Ctrl+L` / `Ctrl+R`❓, Take Center, Take Left Then Right / Right Then Left `Ctrl+B` / `Shift+Ctrl+B`❓,
  Take … Line `Shift+Ctrl+L/R`❓, Conflict(설정·해제), Clear Conflict Section and Next, Next / Previous Conflict Section `Alt+N` / `Alt+P`❓,
  Next / Previous Difference(Section) — 텍스트 비교와 같은 키, Save `Ctrl+S`, 입력/출력 칸 이동 `Alt+PgUp` / `Alt+PgDn`❓.
  Take 버튼은 툴바·칸 메뉴·**출력 칸의 구간 옆 버튼** 세 곳.
- 보기: Show All / Changes / Conflicts / Left Changes / Right Changes / Mergeable / Unchanged / Context, Ignore Same Changes, Favor Left / Right, Center Pane.
- 명령줄 `/automerge`, `/reviewconflicts`, `/force`, `/favorleft`, `/favorright`, `/mergeoutput=`.

## 4. 명령줄 (3b — BC 스위치를 받는다)

BC: Windows는 `/스위치`, macOS·Linux는 `-스위치`. 두 번째 실행은 떠 있는 앱에 넘기고 끝난다(우리와 같음). `BComp`는 비교가 끝날 때까지 기다린다(우리 `--wait`).

| BC | 뜻 | 우리 |
|---|---|---|
| `left right` / `left right center` / `… output` | 2개 비교, **3개 = 왼쪽 오른쪽 가운데**, 4개 = + 출력 | 2개 ✅, 3·4개는 7단계 |
| `/qc[=size\|crc\|binary]` (`/quickcompare`) | 창 없이 비교, 종료 코드로 결과 | ✅ (`--silent`는 없앰 — BC의 `/silent`는 뜻이 다르다) |
| `/title1=` `/title2=` (`/lefttitle` `/righttitle`) | 칸 제목 (git 임시 파일 이름 대신) | ✅ (`/title3` `/title4`는 7단계) |
| `/ro`, `/ro1`(`/lro`), `/ro2`(`/rro`) | 읽기 전용 | ✅ |
| `/fv="Text Compare"` 등 | 볼 화면 고르기 | Text Compare만 ✅, 나머지는 그 화면이 생기면 |
| `/filters="*.cpp;-*.obj"`, `/expandall` | 폴더 비교 필터 | 4단계 ("아직 지원하지 않는 BC 스위치"로 알린다) |
| `/iu` (`/ignoreunimportant`) | 중요하지 않은 차이 무시 | ✅ (창은 이 창만, `/qc`는 12 → 2) |
| `/automerge` `/mergeoutput=` `/favorleft` … | 병합 | 7단계 (위와 같이 알린다) |
| `/?` `/h` `/help` | 도움말 | ✅ |
| (우리만) `--wait` | 창을 닫을 때까지 기다림 — BC는 `BComp`가 이 일을 한다 | ✅ |

**종료 코드 (BC)**: 0 성공 · 1 바이너리 같음 · 2 규칙 기준 같음 · 11 바이너리 다름 · 12 비슷함 · 13 규칙 기준 다름 · 14 충돌 · 100 알 수 없는 오류 ·
101 충돌로 출력을 쓰지 않음 · 102~107(대기 실패·스크립트 오류 등).
✅ 우리도 같은 코드를 쓴다: 창으로 비교하면 0, `/qc`는 1(바이트 같음) · 2(정책으로 같음 — 인코딩·BOM·줄바꿈만 다름, 또는 `/iu`와 중요하지 않은 차이만) ·
11(바이트 다름, size·crc·binary 또는 텍스트가 아닌 파일) · 12(중요하지 않은 차이만) · 13(중요한 차이), 오류 100.

## 5. Hex 비교 (6단계) · 그림 비교 (8단계)

- Hex: Copy to Right / Left / Other Side(고른 바이트), Next / Previous Difference Bytes, Next / Previous Difference Files, Find / Replace / Find Next / Previous,
  Go To(바이트), 보기 Show All / Differences / Same, 현재 바이트 주소, 리틀/빅 엔디언 값, 나란히 / 위아래.
- 그림: Tolerance Mode, Mismatch Range Mode, Blend Mode, Blend Toggle, Ignore Unimportant Differences, Auto Scale, 회전·뒤집기,
  Reset Difference Offset(화살표로 1픽셀씩 어긋남 조정), Compare Metadata, 전체 화면, 확대·축소·실제 크기·창에 맞춤.
