# 계획서 — Daylight Compare (Avalonia)

> 상태: **확정 (2026-11-12)** — §10의 결정이 모두 끝났다. 0·1단계 완료, 다음은 2단계(Compare Policy). 사용자 편의 원칙은 §6-1. Beyond Compare와 같은 역할(폴더·텍스트·바이너리·이미지·표 비교, 병합, 동기화)을 하는 데스크톱 앱.
> 각 단계는 실행 가능한 상태로 끝내고, 끝에 사용자가 직접 실행해 눈으로 확인한다. 한 번에 한 단계.
> 공통 규약: `jejezz/application-release-templates/conventions` (identity, versioning, tagging, workflow, packaging, about-dialog,
> icons, licensing, ui-ux, theming, settings, settings-screen, fonts, localization, window-placement, readme-guide).

---

## 1. 먼저 정한 것

### 1-1. 프레임워크: **Avalonia (.NET 10)**

`FRAMEWORKS.md` 기본 규칙: **데스크톱 전용 → Avalonia**. 게다가 비교 앱은 세션마다 창을 여는 **멀티 윈도우**가 자연스러워서
예외 조항(멀티 윈도우 → Avalonia)에도 해당한다. 스마트폰 판은 만들지 않는다.

Avalonia용 규약(`avalonia/`)은 아직 일반화되지 않았으므로, **형제 앱 두 개가 사실상의 규약**이다.

| 형제 | 가져올 것 |
|---|---|
| `daylight-commander-avalonia` | 에디터(AvaloniaEdit + TextMate), 원격(SFTP·FTP·WebDAV), 압축, 작업 큐, 테마·설정·정보 창·업데이트, AOT 게시 스크립트, 설치기 |
| `jacksbean-avalonia` | **정책 파일 패턴**(schema·검사·경고·깨진 파일 복구·실행 취소·모르는 키 보존), 스낵바, 단일 실행, GitHub 동기화 |

프레임워크와 무관한 규칙(이름·ID·저작권·버전·태그·라이선스·파일 이름)은 `conventions/`를 그대로 따른다.

### 1-2. 정체성 (identity.md) — 이름·식별자·아이콘 확정

**Daylight 패밀리**: `Daylight Commander`(파일 관리)와 짝을 이루는 두 번째 Daylight 앱이다. 이름 앞의 `Daylight`가 시그니처다. (아이콘 판 색은 Daylight 앱마다 다르다 — pdf 0°, terminal 148° 등.)

| 항목 | 값 |
|---|---|
| 표시 이름 | `Daylight Compare` ✅ 확정 |
| 파일 이름 | `DaylightCompare` — 실행 파일, 릴리스 산출물, 사용자 데이터 폴더(`%APPDATA%\DaylightCompare`) |
| 식별자 | `art.zoomon.daylightcompare` ✅ 확정 (접미사 없음 — 처음부터 Avalonia라 Flutter 판과 구분할 필요가 없다) |
| 저장소 | `daylight-compare-avalonia` |
| 게시자·저작권자 | `Jongyun Ahn`, `Copyright © 2026 Jongyun Ahn` |
| 첫 버전 | `0.1.0-rc.1` (`AppBuild` 1) — 첫 정식 릴리스는 `1.0.0` (macOS 패키징이 `0.y.z` 거부) |
| 아이콘 | ✅ **문서 두 장 + 양방향 화살표**(사용자가 준 참고 그림 기준): 뒤 문서(옅은 하늘색, →)와 앞 문서(파랑, ←)를 겹친 스티커형 글리프. 폴더 글리프는 Commander가 쓰므로 쓰지 않는다. 판은 `hue 205`(하늘색 계열, 고정 — 해시 값 4°는 daylight-pdf의 빨강과 겹쳐서), `prop_tile: true`, 고양이 배지가 앞 문서 오른쪽 아래를 가리므로 화살표는 문서 위쪽에 둔다. 글리프는 `tool/icon/draw_glyph.py`가 같은 좌표로 `source_glyph.svg`·`.png`를 함께 그린다(macOS 렌더러 없이). 그다음 `generate_icons.py <고양이 경로>` |
| 정체성 원본 | `Directory.Build.props` 하나 → `AppIdentity`/`AppVersion`이 어셈블리 메타데이터로 읽음 (형제와 같음) |

Daylight 패밀리로 묶이므로 UI도 Commander와 최대한 같게 둔다(앱 바·색·아이콘 세트·단축키 표기). 다른 점만 `UI_UX.md`에 적는다.

---

## 2. 재사용: "이미 잘 되는 것은 100% 가져온다"

### 2-1. 그대로 가져오는 것 (Daylight Commander)

| 부품 | 원본 | 이 앱에서 쓰는 곳 |
|---|---|---|
| `IFileSystem` + `LocalFileSystem` | `Services/Remote/` | **모든 비교의 입력 추상화.** 폴더 비교는 로컬·원격·압축을 구분하지 않는다 |
| `SftpFileSystem` (SSH.NET) | 〃 | SFTP 폴더/파일 비교 |
| `FtpFileSystem` (FluentFTP) | 〃 | FTP/FTPS 비교 |
| `WebDavFileSystem` | 〃 | WebDAV 비교 |
| `RemoteSessions`, `SettingsHostKeyStore` | 〃 | 접속 프로필, 호스트 키 확인 (주소록 그대로) |
| `RemoteOperations`, `OperationQueue` | `Services/` | 폴더 동기화·복사 실행(충돌 질문, 진행률, 취소) |
| AvaloniaEdit + TextMate, `EditorLanguages` | `App/Views/EditorWindow`, `Services/` | 텍스트 비교 창의 좌우 편집기, 구문 강조 |
| `TextFiles` (이진 판별·줄바꿈·BOM·변경 감지·저장) | `Services/` | 텍스트 비교 로딩·저장 |
| `ArchiveService` | `Services/` | zip 만들기·풀기 |
| `GlobPattern` | `Services/` | 정책의 파일 마스크 (`*.c;*.h;-*.bak`) |
| `ThemeService`, `UiScale`, `Strings`/`IAppStrings`, `DialogBase`, `SettingsWindow`, `AboutDialog`, `LicensesWindow`, `WindowPlacementHelper`, `Updates/` | `App/` | 앱 골격 전부 |
| `publish-aot.*`, `package-*.sh`, `installer/` | `scripts/`, `installer/` | AOT 게시·패키징 |

### 2-2. 가져와서 넓히는 것

| 부품 | 지금 | 넓힐 것 |
|---|---|---|
| `TextFiles.Decode` | UTF-8 (+BOM) 중심 | **CP949/EUC-KR, UTF-16 LE/BE, Shift-JIS** 감지·선택 (`CodePagesEncodingProvider`). 한국어 소스 비교에 필수 |
| `FolderComparison` | 한 단계, 이름+크기+시각 | 재귀, 정책 기반 판정(아래 §4), 백그라운드·점진 결과 — **새 엔진으로 대체**하고 원본은 참고만 |
| `ArchiveService` | zip 생성/해제 | **압축 파일을 폴더처럼** 여는 `ZipFileSystem`·`TarFileSystem`(읽기 전용 `IFileSystem`). tar/tar.gz는 .NET 내장 `System.Formats.Tar` (AOT 안전). 7z/rar은 나중(SharpCompress의 AOT 확인 후) |
| `SftpFileSystem` | 파일 전송 | **원격 해시**: SSH `exec`로 `sha256sum`을 서버에서 돌려 내려받지 않고 내용 비교 (§8 제안) |

### 2-3. 재사용 방식 — ✅ 확정: 복사로 시작

- **(가) 복사** — 형제 앱 방식. 파일 첫 줄에 출처 주석(`// From daylight-commander-avalonia <commit>`). 빠르지만 갈라진다.
- **(나) 공유 라이브러리** — `zoomon-avalonia-core`(가칭) 저장소로 `IFileSystem`·원격·테마·설정·정보 창·업데이트를 뽑고 세 앱이 git 서브모듈/로컬 NuGet으로 참조.
  ui-ux.md §2의 "토큰은 복사하지 않고 공유한다"(Flutter 쪽 TODO)와 같은 방향.

**확정: 0~1단계는 (가) 복사로 시작한다.** 복사한 파일은 첫 줄에 출처 주석(`// From daylight-commander-avalonia @ <커밋>`)을 남기고, 고친 곳은 형제에도 필요한지 적어 둔다. 이 앱이 세 번째 소비자가 되는 시점(2단계 끝)에 (나)로 뽑을지 다시 판단한다 — 세 앱에서 실제로 쓰는 모양을 본 뒤 뽑아야 API가 맞는다.

---

## 3. 기능 범위 — Beyond Compare 대응표

| Beyond Compare | 이 앱 | 단계 |
|---|---|---|
| Folder Compare (로컬·FTP·SFTP·WebDAV·압축) | 폴더 비교 — `IFileSystem` 무엇이든 좌우에 | 4 |
| Text Compare | 텍스트 비교 (줄·글자 단위, 중요/비중요 차이) | 3 |
| Text Merge (3-way) | 텍스트 병합 (base + 좌 + 우 → 출력), git mergetool | 7 |
| Hex Compare | 바이너리 비교 (16진수, 삽입 정렬) | 5 |
| Picture Compare | 이미지 비교 (나란히·차이 마스크·겹치기·깜빡이기·허용 오차) | 6 |
| Table Compare | 표 비교 (CSV/TSV, 키 열, 숫자 허용 오차) | 8 |
| Folder Sync / Merge | 폴더 동기화 (미러·갱신·양방향, 미리보기 후 실행) | 7 |
| Session Settings, File Formats, Rules | **Compare Policy** (§4) | 2, 9 |
| Saved Sessions | 세션 저장·최근 목록 | 4 |
| Snapshots (.bcss) | 폴더 스냅샷 (목록+해시를 파일로 저장, 나중에 비교) | 8 |
| Reports | HTML 보고서, unified diff(patch) 내보내기 | 8 |
| Command line / Scripting | CLI + git difftool/mergetool, 종료 코드 | 3b, 7 |
| MP3 / Registry / Version Compare | **하지 않음** (요청 생기면) | — |

---

## 4. Compare Policy (핵심 설계)

Beyond Compare의 "세션 설정 + 파일 형식 + 규칙"을 Jack's Bean의 **정책 파일**처럼 하나의 문서로 만든다.
사용자가 JSON을 몰라도 되게 편집 창(9단계)을 두지만, 파일 자체도 사람이 읽고 고칠 수 있게 한다.

### 4-1. 겹치는 순서 (뒤가 앞을 덮는다)

```
내장 기본값  →  사용자 정책(policy.json)의 defaults  →  파일 형식(format) 규칙  →  세션 덮어쓰기
```

- 파일 형식은 **마스크로 고른다**(`*.c;*.h`). 여러 형식이 맞으면 목록에서 위에 있는 것 하나만.
- 세션은 정책 일부만 덮어쓴다(예: 이 세션만 "시각 무시").

### 4-2. 정책이 정하는 것

| 구역 | 내용 |
|---|---|
| **folder.criteria** | 폴더 비교 판정 단계: `size` → `timestamp`(허용 오차 초, 시간대·DST 차이 무시) → `hash`(SHA-256/CRC32) → `content`(형식 규칙 적용 후 비교). 앞 단계에서 결정되면 멈춘다 |
| **folder.filters** | 포함/제외 마스크, 폴더 제외(`.git;node_modules;bin;obj`), 크기·날짜 필터, 숨김 파일 |
| **folder.pairing** | 이름 짝짓기: 대소문자, 확장자 무시, 이름 바꾸기 규칙(정규식 `a_(\d+).txt` ↔ `b_$1.txt`) |
| **formats[]** | 마스크 → 뷰어 종류(`text`/`binary`/`image`/`table`) + 아래 규칙 |
| **text.importance** | **문법(grammar)**: 줄 주석·블록 주석·문자열·공백을 정규식 토큰으로 정의 → "중요하지 않음"으로 표시할 토큰. 비중요 차이는 회색으로 보이고 "같다" 판정에서 빠진다 |
| **text.normalize** | 대소문자, 앞뒤/중간 공백, 줄바꿈 종류, 인코딩, 탭 너비 |
| **text.replacements** | 비교 전에만 적용하는 치환 (`v\d+\.\d+` → `vX` 등). 파일은 바꾸지 않는다 |
| **text.alignment** | 알고리즘(`histogram` 기본, `patience`, `myers`), 줄 이동 감지 |
| **converter** | 비교 전 변환: 내장(Intel HEX/S-Record → 바이너리, JSON 정렬 출력, XML 정렬) 또는 외부 명령(`pdftotext {in} {out}`) |
| **binary** | 비교 단위(바이트), 정렬 모드(고정 위치 / 삽입 감지), 무시 구간(헤더의 타임스탬프 오프셋 등) |
| **image** | 채널별 허용 오차, 알파 무시, 크기가 다를 때 정렬(좌상단/가운데/맞춤), 차이 % 기준 |
| **table** | 구분자, 머리글 줄, 키 열, 숫자 허용 오차, 열 무시 |

### 4-3. 파일 모양 (schema 1, 예시)

```jsonc
{
  "schema": 1,
  "defaults": {
    "folder": {
      "criteria": [ { "by": "size" }, { "by": "timestamp", "toleranceSec": 2, "ignoreHourOffsets": true }, { "by": "content" } ],
      "filters": { "excludeFolders": [".git", "node_modules", "bin", "obj"], "exclude": ["*.bak", "~*"] }
    },
    "text": { "alignment": "histogram", "normalize": { "lineBreaks": true, "trailingSpace": true } }
  },
  "formats": [
    { "id": "c-family", "masks": ["*.c", "*.h", "*.cpp", "*.cs"], "view": "text",
      "grammar": "c-like", "importance": { "unimportant": ["comment", "whitespace"] } },
    { "id": "firmware-hex", "masks": ["*.hex", "*.s19", "*.srec"], "view": "binary", "converter": "builtin:ihex-to-bin" },
    { "id": "images", "masks": ["*.png", "*.jpg", "*.bmp", "*.webp"], "view": "image", "image": { "tolerance": 2, "ignoreAlpha": false } },
    { "id": "csv", "masks": ["*.csv"], "view": "table", "table": { "header": true, "keyColumns": [0] } }
  ],
  "grammars": {
    "c-like": { "comment": ["//.*$", "/\\*[\\s\\S]*?\\*/"], "string": ["\"(\\\\.|[^\"])*\""], "whitespace": ["[ \\t]+"] }
  }
}
```

### 4-4. 저장소 규칙 (Jack's Bean `PolicyStore` 패턴 그대로)

- 위치: `%APPDATA%\DaylightCompare\policy.json` (macOS `~/Library/Application Support/DaylightCompare`, Linux `~/.config/DaylightCompare`).
- 모양이 틀린 형식·문법은 **건너뛰고 경고**, schema가 다르면 내장 기본 정책으로 대신하고 파일은 그대로(`IsFallback` + [기본 정책 다시 만들기], 이전 파일은 `.broken-날짜시각`).
- 쓰기는 임시 파일 → 교체, **모르는 키 보존**, 한 단계 실행 취소.
- 판정은 순수 함수(`Domain/Policy/PolicyResolver.Resolve(path, session) → EffectivePolicy`) — UI 없이 시험한다.
- (선택) GitHub 동기화: Jack's Bean의 Contents API 동기화를 그대로 붙일 수 있다. 9단계 이후 판단.

---

## 5. 비교 엔진 (Domain — UI·IO 없음, 전부 단위 시험)

| 엔진 | 설계 |
|---|---|
| **줄 diff** | 직접 구현: **Histogram diff**(git 기본과 같은 계열) + Myers 대비책. 의존성 없이 AOT 안전, 결과 품질을 우리가 통제. 시험은 git `diff --histogram` 출력과 대조하는 골든 파일 |
| **글자 diff** | 바뀐 줄 쌍 안에서 단어→글자 2단 diff, 비중요 토큰 표시 |
| **중요도** | 정책 문법으로 줄을 토큰화 → 비중요 토큰을 지운 "비교용 사본"으로 정렬하고, 화면에는 원문을 보인다 |
| **폴더** | 비동기 재귀 열거(`IFileSystem.ListAsync`) → 이름 짝짓기 → 판정 단계 파이프라인. 결과를 **점진적으로** 내보낸다(수만 개 파일에서 첫 화면이 바로 나오게). 내용 비교는 크기 같은 것만, 병렬도 제한(원격은 접속당 1~2) |
| **바이너리** | 64KB 블록 해시로 같은 구간 건너뛰기 → 다른 블록만 바이트 비교. 삽입 감지는 rolling hash(rsync 방식)로 재동기화. 큰 파일은 `MemoryMappedFile` |
| **이미지** | Avalonia에 들어 있는 SkiaSharp로 디코드 → 픽셀 차이 마스크, 차이 비율, 허용 오차 |
| **표** | CSV 파서(따옴표·줄바꿈 포함) → 키 열로 행 짝짓기 → 셀 비교 |
| **병합** | 3-way: base↔좌, base↔우 diff를 겹쳐 충돌 구간 계산(diff3). 2-way는 블록 복사 |

---

## 6. 구조

```
src/DaylightCompare.Domain     모델과 규칙: Diff/(Histogram, Myers, Intraline, Diff3), Policy/(문서·검사·Resolver·문법),
                           Folder/(짝짓기·판정·결과 트리), Binary/, Image/(픽셀 비교, Skia 미의존 인터페이스), Table/,
                           Settings/, Theme/, Window/, AppIdentity/AppVersion
src/DaylightCompare.Services   IFileSystem 계열(Local·SFTP·FTP·WebDAV·Zip·Tar), FolderCompareService(백그라운드·취소),
                           PolicyStore, SessionStore, Converters/(내장·외부 명령), TextFiles(인코딩), Reports/, Sync/(OperationQueue),
                           Cli/(인자 해석·종료 코드), Updates/
src/DaylightCompare.Data       JsonSettingsRepository, 정책·세션 JSON (소스 생성, NativeAOT 안전)
src/DaylightCompare.App        Avalonia UI
tests/*.Tests              xUnit. Domain은 골든 파일, Services는 가짜 파일 시스템·가짜 SFTP
```

의존 방향 `App → Services → Domain ← Data`, 프로젝트를 나눠 컴파일러가 지킨다 (형제와 같음). NativeAOT가 기본 배포, 의존성을 더할 때마다 `publish-aot`로 확인.

### 화면

- **홈 창**: 새 비교(폴더/텍스트/바이너리/이미지/표/병합) 카드, 저장한 세션, 최근 목록, 원격 접속 프로필. 빈 상태 규약(ui-ux §6).
- **비교 창 = 세션 하나**. 창 안에 탭으로 여러 파일 비교를 열 수 있다(폴더 비교에서 파일을 더블클릭하면 같은 창의 새 탭).
  독립 창 정책(Daylight `CLAUDE.md`): 비교·병합·정책 편집·설정은 창, 짧은 질문은 대화상자.
- 공통 단축키(ui-ux §7) + 비교 전용: 다음/이전 차이 `F8`/`Shift+F8`(또는 `Alt+↓/↑`), 왼쪽→오른쪽 복사 `Ctrl+→`, 반대 `Ctrl+←`, 다시 비교 `F5`,
  같은 것 숨기기 토글, 비중요 차이 보기 토글. 목록은 정보 창/도움말(F1)에.
- 끌어다 놓기: §6-1.
- 색: 차이 종류(추가·삭제·변경·비중요·충돌) 토큰을 theming.md 방식으로 테마에서 읽는다 (`{DynamicResource AppDiff...}`). 고대비 색 구성에서도 구분되게 모양(가장자리 막대·기호)을 함께 쓴다.

### 6-1. 사용자 편의 (원칙 — 모든 단계에 적용)

> 2026-11-12 사용자 요청: "사용자 편의성 강화가 중요하다." 각 단계의 끝나는 조건에 아래 해당 항목이 들어간다.

**① 파일·폴더 끌어다 놓기** — 규칙은 `Domain/Drop/DropPlanner`(시험 있음), 경로 분류는 `Services/DropClassifier`. 화면은 3단계(홈 창·비교 창)부터.
- 어느 창이든 창 전체가 놓는 자리다. 끌고 들어오면 왼쪽·오른쪽 절반을 강조하고(ui-ux §7), 놓을 수 없는 것이면 그렇다고 보인다.
- **두 개**를 한 번에 놓으면 어디에 놓든 양쪽을 채우고 바로 비교. **하나**는 놓은 쪽 절반만 바꾸고, 가운데에 놓으면 빈 쪽부터 채운다(양쪽이 차 있으면 새 비교).
- 폴더·압축 파일끼리 → 폴더 비교, 파일끼리 → 파일 비교, **파일 + 폴더 → 폴더 안의 같은 이름 파일과 비교**(없으면 알리고 그쪽만 비움).
- **파일 세 개 → 3-way 병합**(가운데가 공통 조상, 화면에서 역할 바꾸기). 네 개 이상은 이유와 함께 거절.
- 비교 창의 각 칸(경로 표시줄)에도 놓을 수 있고, 폴더 비교 안에서 항목을 반대쪽으로 끌면 복사(확인 대화상자, 7단계).

**② 인코딩 오류 복구** — 로직은 `Services/TextEncodings`·`TextFiles`(시험 있음), 화면은 3단계.
- 감지가 "깨진 곳 있음"이면(`EncodingAnalysis.IsDamaged`, `EditorTextFile.DecodeErrors`) 비교 칸 위에 안내 띠: *"UTF-8로 읽었지만 3줄에서 깨졌습니다"* +
  [깨진 곳으로 이동](`FindErrors`) · [다른 인코딩으로 다시 열기](`Previews`: 후보마다 오류 수와 미리보기) · [줄마다 섞인 인코딩 복구](`RepairMixed`, 예: UTF-8 + CP949).
- 상태 표시줄의 인코딩 이름을 누르면 같은 "다시 열기" 목록. 좌우 인코딩이 다르면 비교는 글자 기준으로 하고 "인코딩만 다름"을 따로 알린다.
- 어느 것으로도 안 읽히면 Latin-1(서유럽) 텍스트로 열고 편집·저장할 수 있다 — Commander처럼 통째로 읽기 전용이 되지 않는다. NUL이 있으면 바이너리 비교로 안내.
- 저장 전 손실 확인: 깨진 곳이 남았거나(� 저장) 고른 인코딩에 없는 글자가 있으면(`Unencodable`) 그 글자들을 보여 주고 [UTF-8로 저장]을 제안.
  원본은 저장 전에 백업(아래 ⑤).
- BOM·줄바꿈(CRLF/LF)·인코딩만 다른 파일은 기본 정책에서 "중요하지 않은 차이"로 두고, 띠에 한 줄로 알린다.

**③ 더 제안하는 것** (들어갈 단계)
| 제안 | 이유 | 단계 |
|---|---|---|
| **탐색기·Finder 연동**: 우클릭 [왼쪽으로 고르기] → 다른 항목에서 [고른 것과 비교] (Beyond Compare의 Select Left / Compare to) | 창을 열지 않고 두 번 클릭으로 시작 | 3b (Windows 레지스트리 · macOS 서비스 · Linux `.desktop` 동작) |
| **클립보드 비교**: 한쪽 칸에 붙여 넣은 글을 바로 비교 (파일 없이) | 로그·설정 조각 비교가 잦다 | 3 |
| **실행 중이면 새 탭으로**: CLI·탐색기·끌어다 놓기로 다시 열면 떠 있는 창의 새 탭 | 창이 흩어지지 않게 | 3b (Jack's Bean `SingleInstance`) |
| **파일이 바뀌면 자동 다시 비교**(감시) + 스크롤 위치 유지, 편집 중이면 묻기 | 빌드 산출물·로그 비교 | 3 |
| **최근 쌍 · 세션 복원**: 홈 창에 최근 비교 쌍(한 번 클릭), 다시 켜면 열려 있던 비교 복원 | 같은 쌍을 반복 비교 | 3·4 |
| **좌우 바꾸기**(`Ctrl+U`, Commander와 같은 키) · 경로 한쪽에 맞추기 | 방향을 잘못 놓았을 때 | 3 |
| **차이만 보기 / 문맥 n줄** 토글, 다음·이전 차이, 개요 막대 클릭 이동 | 긴 파일 탐색 | 3 |
| **되돌리기**: 복사·편집·동기화 각각 한 단계 이상 실행 취소, 덮어쓰기 전 자동 백업(`.bak` 또는 휴지통) | 비교 도구의 가장 큰 사고는 잘못 덮어쓰기 | 3·7 |
| **큰 파일·느린 원격**: 진행률·취소, 결과는 나오는 대로, UI는 절대 멈추지 않음 | ui-ux §6 로딩 규칙 | 4·5 |
| **이유 있는 오류**: 권한·연결·인코딩 오류를 스낵바 + [복사] + 해결 버튼(다시 연결, 다른 인코딩…) | ui-ux §6 | 모든 단계 |
| **명령 팔레트**(`Ctrl+Shift+P`): 모든 명령을 이름으로 찾기 | 기능이 많아질수록 발견성이 중요 | 8 |
| **첫 실행 안내**: 빈 홈 창에 "두 파일을 끌어다 놓으세요" + 예제 비교 열기 | 처음 쓰는 사람 | 3 |

---

## 7. 단계

### 0. 앱 골격 (기능 코드 전에 — 새 앱 시작 체크리스트)
- [x] 정체성 → `Directory.Build.props`(`0.1.0-rc.1`, 빌드 1), `LICENSE`(MIT), Inno Setup `AppId` 새 GUID
- [x] GitHub 비공개 저장소 `jejezz/daylight-compare-avalonia`, 첫 커밋 `563a7bf`
- [x] **`v0.1.0-rc.1` 태그 → 파이프라인 전체 성공 (2026-11-12)**: check·Windows·Linux·macOS 빌드(세 OS 단위 테스트 통과)·release, 산출물 4개 + `SHA256SUMS.txt`. macOS·Linux에서 실제로 실행해 보는 것은 아직
- [x] 솔루션(Domain·Services·Data·App + Domain/Data 테스트), Daylight Commander 골격 복사(기준 커밋 `.copied-from`): 테마(시스템/라이트/다크, 색 구성 6종), SeoulNamsan, ko/en `IAppStrings`, 설정 창(외형), 정보 창·라이선스 창, 앱 바 오른쪽 `설정 | 정보`, 창 위치 기억(`PlacementMath`), 업데이트. 파일 관리자 전용 코드·문구·설정은 뺐다
- [ ] `FocusTint`(포커스 받으면 앱 바가 물듦) — Commander에도 없다. Commander와 함께 넣을지 판단
- [x] 앱 아이콘: `tool/icon/draw_glyph.py` → `tool/icon/avalonia_icons.py`가 `app_icon.ico`·`app_icon.png`·`installer/macos/app_icon_1024.png`·`installer/linux/app_icon.png` 생성
- [x] Phosphor Duotone UI 아이콘 (Commander의 `PhosphorIcons.g.cs`·`gen-phosphor-icons.py` 그대로)
- [x] 릴리스 워크플로(check → Windows/Linux/macOS → release), 릴리스 노트 헤더, Inno Setup, Linux `install.sh`·`.desktop`(Development;Utility), macOS `Info.plist`(`NSLocalNetworkUsageDescription`)
- [x] README(한 `README.md` / 영 `README.en.md`), `CLAUDE.md`(규약 한 줄 + `conventions-v1`), `UI_UX.md`(다른 점만)
- [x] 확인: 빌드 경고 0, 테스트 30개 통과, Windows NativeAOT 게시(실행 파일 34MB) 후 실행해 창이 뜨는 것 확인
- 끝나는 조건: **`v0.1.0-rc.1` 태그로 파이프라인이 끝까지 돈다.** 빈 홈 창이 세 OS에서 뜬다.

### 1. 공통 부품 이식
- [x] `IFileSystem` 계열(Local·SFTP·FTP·WebDAV) + 원격 세션·호스트 키(설정 `network_profiles`·`known_host_keys`) + `FileOperations`·`OperationQueue`·`Trash` 이식,
  출처 주석 `// From daylight-commander-avalonia @ 4686eb7`. Commander의 서비스 테스트(원격 통합 테스트 포함)도 함께 가져왔다
- [x] 인코딩 확장: `TextEncodings` — BOM → BOM 없는 UTF-16 → UTF-8 → CP949/Shift-JIS(자주 쓰는 한글·가나 바이트 영역으로 판정) → Latin-1.
  `TextFiles.Decode/LoadAsync(…, forced)`로 수동 선택, 저장은 읽은 인코딩 그대로(`EditorTextFile.Encoding`), `CanEncode`로 손실 확인. 선택 UI는 3단계
- [x] `ArchiveFileSystem`(읽기 전용, zip·jar·apk·nupkg / tar / tar.gz·tgz) — 경로 `<압축 파일>!/<안쪽>`(`ArchivePaths`), 폴더 항목 없는 압축 파일도 경로로 폴더를 만든다,
  `..` 이름은 뺀다. .NET 내장(`System.IO.Compression`, `System.Formats.Tar`)만 쓴다. 7z·rar은 나중
- 확인: 테스트 198개 통과(원격 통합 테스트 37개는 asyncssh·pyftpdlib·wsgidav·cheroot가 있는 파이썬을 `DC_TEST_PYTHON`으로 줘서 실제 서버로 돌림), Windows AOT 게시 성공.
  다만 앱 화면이 아직 원격·압축 코드를 쓰지 않아 AOT가 그 코드를 잘라 낸다 — 실제 AOT 확인은 4단계(폴더 비교 화면)에서. (SSH.NET·FluentFTP의 AOT 동작은 Commander가 이미 확인)
- [x] (사용자 편의 §6-1 선행 작업) 인코딩 오류 복구 로직: 손상 텍스트 감지(`Analyze`), 깨진 곳 찾기(`FindErrors`), 다른 인코딩 미리보기(`Previews`),
  줄 단위 섞인 인코딩 복구(`RepairMixed`/`TextFiles.DecodeMixed`), 저장 손실 찾기(`Unencodable`). 끌어다 놓기 규칙 `DropPlanner` + `DropClassifier`
- Commander에도 필요한 수정 2건: 앱 바 [정보] 툴팁 리소스 누락(0단계), SFTP 통합 테스트의 순서 의존(공용 서버 폴더를 테스트마다 비움)
- 끝나는 조건: 가짜·실제 SFTP/FTP, zip/tar.gz 목록을 시험으로 읽는다.

### 2. Compare Policy 도메인 + 저장소
- [x] 문서 모델 `Domain/Policy/ComparePolicy`(schema 1)·구역별 규칙 `PolicyRules`(folder·text·binary·image·table·converter·grammar)·`FileMask`(`;` 구분, `-` 제외, `**`).
  읽을 때 관대: 틀린 형식·문법·정규식·값은 건너뛰거나 기본값 + `Warnings`, schema가 다르면 `FormatException` → `IsFallback`
- [x] 실효 정책 `ComparePolicy.Resolve(이름, 상대 경로, 세션)`: 내장 defaults → 사용자 defaults → 첫 맞는 형식 → 세션을 **JSON에서 깊게 합친 뒤** 타입으로 읽는다
  (형식·세션은 바꿀 키만 적으면 된다). 형식에 `grammar`·`importance`·`encoding`을 바로 적어도 된다. 사용자가 지운 내장 형식은 되살리지 않는다
- [x] 내장 기본 정책 `BuiltinPolicy`: C 계열(C/C++/C#/Java/Kotlin/JS/TS/Dart/Go/Rust/Swift…), `#` 주석(Python·셸·YAML·CMake…), INI, XML·XAML·HTML, JSON, SQL,
  Markdown·텍스트, CSV/TSV(표), 이미지, 펌웨어 Intel HEX·S-Record(바이너리 + 변환기), 실행 파일·압축 등 바이너리. 문법 6개(c-like·hash·ini·xml·json·sql)
- [x] `Services/Policy/PolicyStore`(Jack's Bean @ 1af8cde 패턴): 없으면 내장 정책으로 만들기, 깨진 파일은 그대로 두고 내장 정책 사용,
  [기본 정책 다시 만들기](`.broken-날짜시각`), [기본 정책으로 되돌리기](모르는 최상위 키 보존·`.backup-날짜시각`), 형식·문법·defaults 고치기(모르는 키 보존, 틀리면 저장 거절),
  한 단계 실행 취소/다시 실행. 주석·끝 쉼표가 있는 JSON도 읽는다. 앱이 시작할 때 `policy.json`을 만든다(`App.Policy`)
- [x] 세션 덮어쓰기: `Resolve(session: {...})` — 세션 저장 자체는 4단계
- [x] 확인: 테스트 254개 통과(도메인 56·데이터 15·서비스 183 — 원격 통합 37 포함), Windows AOT 게시 후 실행해 `policy.json`이 만들어지고 정규식이 읽히는 모양으로 저장됨을 확인
- [x] **재사용 방식 재검토 → 공유 라이브러리는 아직 뽑지 않는다(권장, 사용자 확인 필요).** 이유: ① 복사한 부품 중 고친 것이 이미 있다(`TextFiles` 인코딩, 문구·설정 정리,
  SFTP 테스트) — 지금 뽑으면 Commander가 그 변경을 함께 받아야 한다. ② 원격·압축 코드는 아직 화면에서 쓰지 않아 API가 굳지 않았다. ③ Commander는 복사 이후
  바뀐 것이 없어 갈라짐 비용이 아직 작다(`4686eb7` 이후 해당 파일 변경 없음). → **4단계(폴더 비교 화면) 끝에 다시 본다.** 그때 바뀌지 않은 부품(Remote·OperationQueue·
  Updates·Theme)부터 `zoomon-avalonia-core`로 뽑는다.

### 3. 텍스트 비교 ★ 첫 실사용 가치
- [x] 3a. 엔진 (`Domain/Diff`, 2026-11-12):
  - `SequenceDiff`: Histogram(기본, 드문 줄을 닻으로·같은 점수면 가운데 닻) · Patience · Myers(선형 메모리 가운데 뱀). 재귀 대신 작업 스택.
    무작위 500회씩 시험 — Myers는 동적 계획법 LCS와 길이가 같고, Histogram·Patience는 올바른 공통 부분 열. 20만 줄 0.5초 안
  - `GrammarTokenizer`: 본문 전체를 한 번에 토큰화(먼저 시작·긴 것 우선 → 문자열 안의 `//`는 주석 아님, 여러 줄 주석)
  - `TextDiff`: 줄마다 비교 열쇠(중요하지 않은 토큰 빼기 · 공백은 낱말 사이만 빈칸 하나 · 뒤 공백 · 치환 · 대소문자) → 짝짓기 →
    `Equal`/`Unimportant`/`Changed`/`LeftOnly`/`RightOnly` 구간. 주석만 있는 줄이 늘거나 줄을 나눈 것처럼 열쇠를 이어 붙이면 같은 경우도 Unimportant.
    `IsIdentical`(글자까지) · `IsEquivalent`(중요한 차이 없음 — 폴더 비교의 "내용" 판정)
  - `IntralineDiff`: 단어 단위 → 양쪽 한 단어씩 바뀌면 글자 단위로 좁힘(공통이 절반 미만이면 단어 전체), 주석·공백 안의 강조는 Unimportant
  - 5만 줄 C 파일 비교 3초 안(시험). git `diff --histogram`과 같은 결과를 내는지는 대조하지 않았다 — 같은 계열이지만 git의 들여쓰기 휴리스틱 등은 없다
- [x] 3a. 뷰 (읽기 전용 판, 2026-11-12): `Compare/CompareWindow` — 좌우 AvaloniaEdit + **채움 줄로 행 맞춤**(`Domain/Diff/AlignedDiff`, 위험 항목 해결:
  같은 행이므로 스크롤 위치만 맞추면 된다), 함께 스크롤·캐럿 행 맞춤, 차이 바탕·줄 안 강조·중요하지 않은 부분 흐리게, 실제 줄 번호 여백 + 차이 기호(`~ − +`),
  개요 막대(누르기·끌기로 이동, 보이는 범위 표시), 다음/이전 차이(F8·Alt+↑↓), 다시 비교(F5), 좌우 바꾸기(Ctrl+U), 중요하지 않은 차이 보이기 토글,
  TextMate 구문 강조(2M 글자까지), 경로 줄 [열기…], 칸 안내(빈 칸·바이너리·너무 큼·실패), 결과 요약.
  사용자 편의: **끌어다 놓기**(홈 창·비교 창, `DropPlanner`), **인코딩 안내 띠**(깨진 곳 줄 번호 · 줄마다 복구 · 다른 인코딩으로 다시 열기 + 미리보기).
  홈 창에 새 비교 카드(왼쪽·오른쪽 고르기). 실행 인자로 파일 두 개를 주면 비교 창을 바로 연다(3b의 시작).
  화면 시험 `tests/DaylightCompare.App.Tests`(Headless + Skia로 그려 PNG 확인). AOT 실행 파일로 비교 창이 뜨는 것 확인.
  - 고친 것: 2바이트 코드 페이지(CP949·Shift-JIS) 디코더가 깨진 바이트 뒤의 줄바꿈을 삼켜 줄이 합쳐지던 문제 → 줄마다 읽는다
- [x] 3a. 편집·저장 (2026-11-12):
  - **바로 편집**: 양쪽 모두(압축 파일 안은 읽기 전용). 채움 줄 시작에 앵커를 두고 "앵커가 있고 아직 빈 줄"만 빼서 실제 글을 꺼낸다(`DiffEdits.RealText`) —
    채움 줄에 글을 쓰면 실제 줄이 된다. 손을 멈추고 0.5초 뒤 다시 비교, 캐럿은 같은 실제 줄로 돌아온다
  - **실행 취소/다시 실행**(Ctrl+Z, Ctrl+Y·Ctrl+Shift+Z): 다시 비교할 때 문서를 바꾸므로 편집기 자체의 기록 대신 실제 글 한 쌍을 쌓는다(한 번에 친 글자 묶음 = 한 단계)
  - ~~블록 복사~~ → 아래 "고른 부분만 합치기"로 바꿨다 (차이 구간 통째 복사는 없앴다)
  - **저장**(Ctrl+S 초점 쪽, Ctrl+Shift+S·앱 바 저장 = 고친 쪽 모두): 읽은 인코딩·BOM·줄바꿈 그대로. 저장 전 확인(`CompareSave.Check`) —
    디스크에서 바뀜 · 지금 인코딩에 없는 글자(→ [UTF-8로 저장]) · 깨진 곳(�). **덮어쓰기 전 자동 백업** `<사용자 데이터>/backups/날짜/이름.시각.bak`, 30일 보관.
    섞인 인코딩을 복구한 글은 UTF-8로 저장
  - 고친 쪽은 경로 앞·창 제목에 `●`. 닫기·다시 읽기·좌우 바꾸기·다른 파일 열기 전에 [저장]/[저장 안 함]/[취소]
  - **바깥 변경 감지**(`FileSystemWatcher`): 고친 것이 없으면 자동으로 다시 읽어 비교, 있으면 안내 띠 [다시 읽기 (여기서 고친 것 버림)]. 내가 저장한 직후 2초는 무시
  - 화면 시험: 복사 → 실행 취소 → 다시 실행 → 저장 + 백업, 채움 줄에 쓰기, 닫기 확인, 바깥 변경 자동 다시 비교
- [x] **고른 부분만 합치기** (2026-11-13, 사용자 결정 — 파일 비교의 병합 방식을 바꿨다):
  - 문제: Ctrl+R/L·← →가 캐럿이 있는 **차이 구간 전체**를 복사해서, 파일 내용이 전부 다르면 사실상 파일 전체 복사가 됐다. 사용자가 필요한 줄만 골라 합칠 수 없었다
  - 규칙: **파일 비교에서 파일 전체 복사는 절대 없다.** 합치는 것은 사용자가 고른 줄뿐이고, 합칠지·되돌릴지·버릴지는 전부 사용자의 몫
  - 방식: 줄 번호를 누르거나 글을 끌어 고르면 양쪽 편집기가 같은 행을 틀로 감싸고(고른 쪽 실선·대상 점선), 가운데 띠에 →/←가 뜬다. 누르면 그 줄만 복사
    (`DiffEdits.CopyRows` — 고른 행 범위의 원본 줄로 대상의 그 행들을 바꾼다; 원본에 없는 행은 대상에서 뺀다). 키는 `Ctrl+R`/`Ctrl+L`(고른 부분), `Ctrl+Shift+R/L`(캐럿 줄 하나), `Esc`(풀기)
  - 안전장치: 고르지 않으면 아무것도 안 한다. 고른 범위가 원본 쪽 파일의 모든 줄이면 거절(`CoversWholeSource`). 옛 `CopyBlock`·"Copy to Other Side"는 없앴다
  - **파일 전체 복사는 폴더 비교의 일**: 폴더 비교 창의 Copy to Right / Left(4b, `Ctrl+R`/`Ctrl+L`)가 파일·폴더를 통째로 옮긴다. 파일 비교 창에서는 하지 않는다 (이미 구현됨 — 이 항목은 문서 정리)
  - 아직: 줄 안의 단어만 골라 합치기(글자 단위), 고른 줄을 양쪽 모두 반영하는 "양쪽 순서대로"(BC의 Take Left Then Right — 병합 단계에서)
- [x] 3a. 편의 (2026-11-12):
  - **차이만 보기**(Ctrl+D, 문맥 0~20줄): `AlignedDiff.CollapsibleRuns` — 중요한 차이에서 먼 같은 행 구간을 AvaloniaEdit 접기로 **양쪽 같은 행에서** 접는다
    (행 맞춤은 그대로 — 글을 지우지 않으므로 편집·복사·저장도 그대로). 접기 여백으로 한쪽을 펴면 맞은편도 같이. 스크롤·개요 막대는 접힌 높이를 따른다. 설정에 남는다
  - **찾기**(Ctrl+F·F3): 마지막으로 초점이 있던 쪽, 대소문자 구분 토글, n/m, 접힌 곳이면 편다. 찾기 칸에서는 비교 창 단축키(Ctrl+→ 복사 등)를 막는다
  - **클립보드 비교**: 파일이 없는 칸은 빈 편집기 — 바로 쓰거나 붙여 넣으면 비교한다. 경로 줄 [붙여넣기](Ctrl+Shift+V)는 그 칸을 클립보드 글로 바꾼다.
    홈 창 [글 붙여 넣어 비교](2026-11-14 홈 개편 뒤로는 [텍스트 비교] 카드가 여는 빈 창). 붙여 넣은 글은 저장하지 않는다(다른 이름으로 저장은 필요하면 나중에)
  - 가운데 연결선은 하지 않는다 — 채움 줄로 행이 맞으므로 필요가 적다
- [x] 3b. **CLI + git difftool** (2026-11-12):
  - `DaylightCompare [--wait] [--silent] [<왼쪽> [<오른쪽>]]` — 해석은 `Domain/Cli/CommandLine`(옵션은 `--`꼴만, `--` 뒤는 모두 경로), 상대 경로는 실행한 폴더 기준(압축 안 경로는 압축 파일 부분만)
  - **종료 코드** `Domain/Cli/ExitCode`: 0 같음 / 1 다름 / 2 오류. 판정은 `Services/Cli/QuickCompare` — 둘 다 텍스트면 Compare Policy로(`IsEquivalent`), 아니면 바이트로.
    `--wait` 창은 닫을 때 저장한 뒤의 디스크 내용으로 판정
  - **단일 실행**(`App/Cli/SingleInstance`, Jack's Bean 패턴): 이름 있는 뮤텍스(= 식별자, 설치 프로그램 `AppMutex`도 같은 이름)로 본체를 정하고,
    다른 실행은 **이름 있는 파이프**(`Services/Cli/InstanceChannel`, 같은 사용자만, 모든 OS)로 인자를 넘긴 뒤 본체가 돌려준 종료 코드로 끝난다.
    인자 없이 다시 실행하면 떠 있는 창을 앞으로. Windows는 넘기기 전에 `AllowSetForegroundWindow`로 본체가 앞으로 나올 수 있게 한다
  - `--wait`: 본체가 있으면 거기 연 창이 닫힐 때까지 기다린다. 없으면 **본체가 되지 않고** 비교 창 하나만 띄웠다가 닫으면 끝난다(git difftool이 파일마다 띄웠다 닫는 창이 앱 수명을 쥐지 않게)
  - `--silent`: 창 없이 비교만(스크립트용) — 잘못된 인자여도 창을 띄우지 않고 2. 잘못된 인자·`--help`는 창에서 사용법 대화상자
  - README에 `git config` 안내(`trustExitCode`는 끄라고 — 다른 파일에서 git이 멈춘다). AOT 실행 파일로 `--silent` 종료 코드 0/1/2 확인
  - **바꾼 것**: "떠 있는 창의 새 탭" → 지금은 떠 있는 앱의 **새 비교 창**. 탭은 폴더 비교에서 파일 비교를 여는 흐름과 함께 4단계에서 정한다(비교 창을 컨트롤로 나누는 큰 작업) → **4d에서 했다**: `CompareHost`(탭 호스트 창) 안에 `CompareView`·`FolderCompareView`(UserControl)
  - **미룬 것**: 탐색기 연동(우클릭 [왼쪽으로 고르기] → [고른 것과 비교]) — 규약상 설치 프로그램(.iss)에 한국어 메뉴 이름을 넣을 수 없어서,
    앱이 설정에서 켜면 HKCU에 지금 언어로 등록하는 방식으로 따로 한다(3c 또는 4단계). `--dir-diff`는 4단계, mergetool은 7단계
- [x] **Beyond Compare 호환** (2026-11-12, 사용자 결정 — BC에 익숙한 사용자가 쓴다): 대응표 [BC_COMPAT.md](BC_COMPAT.md)를 만들고 텍스트 비교를 맞췄다.
  - 키를 BC 기본값으로(Ctrl+N/P 구간, Shift+Ctrl+N/P 줄, Ctrl+R/L 복사, Shift+Ctrl+R/L 줄 복사, Ctrl+D 구간 선택, F5/Ctrl+F5, F12, Ctrl+G, Ctrl+H, Ctrl+I, Shift+Ctrl+V).
    BC 도움말이 기본 키를 거의 적지 않아 사용자가 실제 BC5와 맞춰 본다(❓)
  - 보기 필터를 BC처럼 하나로(Show All / Differences / Same / Context), "Ignore Unimportant Differences" 토글, 모든 명령 메뉴(BC 메뉴 묶음)
  - 색을 BC 뜻으로(중요 = 빨강, 중요하지 않음 = 파랑)
  - 명령줄을 BC 스위치·종료 코드로(`/qc`, `/title1` `/title2`, `/ro*`, `/iu`, `/?`, 0·1·2·11·12·13·100). `--silent`는 없앰
  - 앞으로의 단계(폴더 비교 필터 문법·보기 필터·명령, 병합 흐름, Hex·그림 명령)도 BC_COMPAT.md를 먼저 따른다
- 끝나는 조건: 실제 프로젝트 파일로 git difftool을 써서 쓸 만하다고 사용자가 확인. (구현은 모두 끝남 — 남은 것은 이 확인)

### 4. 폴더 비교
Beyond Compare 방식으로 (BC_COMPAT.md §2). 3단계처럼 나눈다: **4a** 엔진·필터 문법 → **4b** 폴더 비교 창(BC 명령·보기 필터·색) → **4c** 원격 해시·세션·Commander 연동.
- [x] 4a. 엔진 (2026-11-12):
  - **필터 문법을 BC와 같게** (`FileMask`): `;` · `-` 제외 · `*` `?` `[az]` `[a-z]` `[!az]` `[[]` · `*.*`은 확장자 없는 것도 · 끝이 `.`이면 마침표를 넘지 않음(`*.`) ·
    끝이 `\`이면 **폴더 마스크**(`-.git\;-bin\`, 파일에는 쓰지 않음) · 상대 마스크 `p\f` · `.\f` · `...\f` (덧붙여 `**`). 정책 `excludeFolders`는 폴더 마스크로 읽는다
  - `Domain/Folder`: 결과 나무 `FolderNode`(상태 같음·다름·왼쪽/오른쪽 고아·모름·오류 + 더 새것인 쪽), 짝짓기 `FolderPairing`(대소문자·확장자 무시·이름 바꾸기,
    폴더 먼저 이름순), 빠른 검사 `FolderJudge`(크기 → 시각 → 내용; 뒤에 규칙 기준 내용 단계가 있으면 크기·시각이 달라도 내용이 정한다 = BC "Override quick test results",
    시각 허용 오차·정확히 n시간 차이 무시)
  - `Services/Folder/FolderComparer`: 어느 `IFileSystem`이든(로컬·SFTP·FTP·WebDAV·압축) 넓이 우선으로 읽어 짝짓고 바로 알린다(점진 결과), 그다음 내용 비교를 4개씩 —
    규칙 기준(바이트 같으면 같음, 텍스트면 Compare Policy로 중요하지 않은 차이 무시, 32MB 넘으면 바이트) 또는 해시(SHA-256·CRC32). 취소, 폴더 상태는 아래에서 위로
    (다른 것이 하나라도 보이면 바로 다름)
  - 시험: 필터 문법, 짝짓기·빠른 검사, 로컬 두 폴더·압축 대 폴더·**로컬 대 SFTP**(40개 파일, 줄바꿈만 다른 것은 같음)
- [x] 4b. 폴더 비교 창 (2026-11-13): `Compare/FolderCompareWindow`
  - 좌우 나무를 **같은 줄에 나란히**(가상화 `ListBox`, 줄 높이 26, 이름·크기·수정한 날짜), 없는 쪽은 회색 칸. BC 색: 같음 = 보통, 다름·더 새것 = 빨강(더 오래된 쪽은 흐리게),
    한쪽에만 = 보라, 아직 모름 = 회색. 같지 않은 폴더는 펴고 같은 폴더는 접은 채 시작, ▸/▾·←/→·더블클릭으로 접고 펴기. 읽는 대로 보이고(0.15초마다 줄 목록을 다시 만든다) 선택·스크롤은 지킨다
  - **보기 필터를 BC처럼**(`FolderShow` 여섯 토글 + BC 이름 열두 개: Show All/Differences/Same/No Orphans/Differences but No Orphans/Orphans/Left·Right Newer/…/Left·Right Orphans) 드롭다운 + 토글 메뉴,
    Always Show Folders. 설정(`folder_show`, `folder_always_show_folders`)에 남는다. 필터 막대는 BC 문법(4a)이고 Enter로 다시 비교
  - **BC 명령·키**: Copy to Right / Left `Ctrl+R` / `Ctrl+L`(확인 뒤 덮어쓰기, 양쪽에 있는 폴더는 안의 같지 않은 파일만), Delete(휴지통 — 눌러 둔 쪽에서), Open `Enter`/더블클릭(파일 짝 → 텍스트 비교 창),
    Next / Previous Difference `Ctrl+N` / `Ctrl+P`, Select All `Ctrl+A`, Select Newer / Orphans(왼쪽·오른쪽·양쪽), Invert Selection, Expand / Collapse All, Refresh `F5`, Swap `Ctrl+U`, 폴더 바꾸기. "모든 명령" 메뉴(BC 묶음)
  - 진입: 홈 창 [폴더 열기]·[폴더 비교] 카드(빈 창), 폴더 하나 또는 둘을 끌어다 놓기, 명령줄에 폴더 둘(`DaylightCompare 왼 오`), 압축 파일도 폴더처럼. `/qc`는 폴더 둘이면 폴더 비교로(모두 같으면 2, 다른 것이 있으면 13)
  - **창 안 모달 시작** (UI_UX.md 정책 A): `Dialogs/DialogHost`(스크림 + 가운데 상자, 주 동작 `accent` 하나 + 보조 `text`, 되돌릴 수 없으면 `accent danger`, `Esc`) + `Button.text` 스타일. 폴더 비교 창의 복사·삭제 확인이 처음 쓴다.
    기존 `MessageDialog` 등의 이전은 UI_UX.md §7의 별도 작업
  - 아직: 원격(SFTP·FTP·WebDAV)과 압축 파일 안으로의 복사·삭제(연결 화면이 생길 때 — 엔진은 이미 된다), 이동·이름 바꾸기·새 폴더, 파일 짝 끌어다 놓기, 복사 뒤 바뀐 줄만 다시 비교(지금은 전체 다시 비교)
- [x] **폴더 비교가 멈추던 문제 수정** (2026-11-13, 네트워크 드라이브에 한쪽이 있을 때 앱이 통째로 멈춤): 원인 — 엔진의 `await` 이어지기가 UI 스레드로 돌아와 파일마다의 읽기·해석·diff가
  UI 스레드를 점유했고(일반 우선순위라 입력·그리기보다 먼저 처리됨), 폴더 종류 확인·마지막 고른 폴더 확인 같은 디스크 호출도 UI 스레드에서 했다. 수정 — `FolderComparer.RunAsync`·
  `CompareSources.LoadAsync`를 항상 백그라운드로, 폴더 비교 창은 열 때 디스크를 만지지 않고 첫 비교에서 경로를 정하며, 홈 창·명령줄의 종류 확인·고르기 창 시작 위치 확인도 백그라운드로.
  시험 `FolderCompareResponsivenessTests`(수정 전 UI가 3.5초 멈춤 → 통과), 10만 개 항목 줄 목록 만들기 24ms 확인. 아직: 폴더를 **차례로** 읽어 느린 네트워크에서는 완료까지 오래 걸린다(멈추지는 않는다) — 여러 폴더를 동시에 읽기는 다음에
- [ ] **원격 해시**: SFTP 쪽은 SSH `exec`로 `sha256sum`(없으면 `shasum -a 256`, 둘 다 없으면 내려받아 비교)을 서버에서 돌려 해시만 받는다. 서버 두 대끼리도
- [ ] **Daylight 패밀리 연동**: Commander의 폴더 비교에 [자세히 비교] → `DaylightCompare <왼쪽> <오른쪽>` 호출 (Commander 저장소 쪽 작은 작업, 코드 의존 없음)
- [ ] 뷰: 좌우 트리(가상화), 상태 아이콘·색, 같은 것/고아/다른 것 거르기, 폴더 접고 펴기, 선택 복사·삭제(작업 큐), 파일 더블클릭 → 형식별 뷰어 탭
- [ ] 로컬·SFTP·FTP·WebDAV·압축 어느 조합이든
- [x] **최근 비교 목록** (2026-11-13): 비교 창(파일·폴더)이 열릴 때와 한쪽을 바꿀 때(고르기·좌우 바꾸기·끌어다 놓기) 기록 → 홈 창에 한 줄씩(`CompareHistoryList` — 맨 앞, 같은 쌍은 하나로, 좌우를 바꾼 쌍은 다른 비교, 최대 50개; 설정 `compare_history`).
  줄을 누르면 바로 다시 연다, 없어진 경로는 흐리게(백그라운드 확인), [×]·[모두 지우기]. 붙여 넣은 글처럼 경로가 없는 비교와 **같은 파일·폴더를 양쪽에 놓은 비교(자기 자신과의 비교)는 기록하지 않는다** — 이미 쌓인 것은 설정을 읽을 때 정리한다(`CompareHistoryList.IsSamePath`·`WithoutSelfComparisons`).
  **고르기 창은 마지막으로 고른 곳에서 시작**(`last_pick_dir`, 폴더를 골랐으면 그 부모 — 이웃 폴더를 이어서 고르기 쉽다)
- [x] **홈 창 개편** (2026-11-14, `v0.1.0`): Daylight PDF 홈과 같은 구성·치수 — 앱 머리 → 점선 놓기 구역([파일 열기] [폴더 열기]) → 최근 비교 → 카드 [폴더 비교] [텍스트 비교](빈 창).
  하나를 놓거나 열면 종류에 맞는 창이 왼쪽만 채워 열리고(`HomeWindow.OpenPathsAsync`), 둘이면 바로 비교. 최근 비교가 있으면 놓기 구역이 한 줄로 준다. 폴더 비교 창은 빈 쪽을 허용(주소 창·목록 자리에 놓아 채움),
  텍스트 비교 주소 창은 직접 고쳐 Enter로 연다. 모든 단추 여백을 Flutter 단추와 맞춤(UI_UX.md, 규약 `ui-ux.md` "버튼 안쪽 여백과 글자 위치"). macOS 앱 아이콘(`installer/macos/AppIcon.icns`)
- [x] 이름 있는 세션 저장(BC Sessions — 필터·보기 설정까지 함께)·다시 켜면 열려 있던 비교 복원 (2026-11-14): 세션 = 비교 창 하나(탭들 + 보던 탭 + 탭마다 필터·보기 설정). 설정 `sessions`·`last_windows`, 명령 메뉴 [세션 저장…], 홈 창 [저장한 세션]·[이전 창 복원]. 원격 해시·Commander 연동은 뒤로 미룬다(사용자 결정)
- 끝나는 조건: 로컬 대 SFTP로 수천 파일 폴더를 비교, 첫 결과가 1초 안에 보인다.

### 5. 바이너리 비교
- [x] 엔진 (2026-11-14, 5a): `Domain/Binary` — 64KB 블록 통째 비교로 같은 구간 건너뛰기, 다른 블록만 바이트 비교, 고정 위치/삽입 감지(롤링 해시 재동기화: 4KB→64KB→1MB 창을 넓혀 가며 y+x가 가장 작은 자리), 무시 구간(`MaskedByteSource`). 바이트는 `IByteSource`로 읽는다(IO 없음), 로컬 파일은 `FileByteSource`(`RandomAccess`, 큰 파일도 메모리에 올리지 않는다)
- [x] 뷰 (2026-11-14, 5b): `BinaryCompareView` — 좌우 `HexPane`(보이는 행만 그림, 행은 `BinaryRows`가 계산해 GB도 같다)·개요 막대·상태 표시줄(구간 위치·양쪽 위치와 값·엔디언), `Ctrl+N/P` 차이 이동·`Ctrl+G` 위치 이동·고정/삽입 감지 전환·좌우 바꾸기·다시 읽기, 탭·세션 저장(`kind: binary`). 읽기 전용
- [x] 여는 길 (5c): 텍스트로 열 수 없는 파일(NUL·너무 큼)과 정책 `view: "binary"` 이름(`*.bin` 등)은 같은 자리의 탭이 바이너리 비교로 바뀐다(`CompareHost.ReplaceTab`)
- [x] 내장 변환기 (2026-11-14, 5d): Intel HEX / S-Record → 바이너리 이미지(주소 공백 채움 값 = 정책 `binary.fill`). `Domain/Binary/FirmwareFormats`(체크섬 확인·경고, 확장 주소 02·04, 시작 주소 03·05 무시, 최대 512MB), 두 이미지는 합친 주소 범위로 맞춰 주소끼리 비교, 오프셋 칸·`Ctrl+G`는 주소. 외부 명령 변환기는 아직
- [x] 무시 구간(헤더 타임스탬프·CRC 오프셋)을 정책 `binary.ignoreRanges`로 (5a·5b에서 적용). 구조 템플릿(헤더 필드 이름 붙이기)은 1.0 뒤
- 끝나는 조건: 수백 MB 펌웨어 이미지 두 개를 비교, 스크롤이 끊기지 않는다.

### 6. 이미지 비교
- [x] 그림 비교 (2026-11-14, 6단계): `Domain/Imaging`(픽셀 비교·허용 오차·투명도 무시·크기가 다를 때 왼쪽 위/가운데/크기 맞춤·차이 묶음 찾기, 순수 데이터) + `ImageCompareView`(나란히·차이·겹치기·깜빡이기, 같은 배율·위치로 함께 확대·이동, 차이 묶음 이동, 포인터 아래 픽셀 좌표·색, 세션 저장). 정책 `image` 구역이 기본값. 아직: EXIF 회전, 영역 선택·눈금
- 끝나는 조건: UI 스크린샷 회귀 비교에 쓸 만하다.

### 7. 병합과 동기화
- [ ] 텍스트 3-way 병합(diff3, 충돌 구간 선택·편집, 출력 파일), **git mergetool**(`--merge 왼 오 base 출력`)
- [ ] 폴더 동기화: 미러(왼→오), 갱신(새것만), 양방향 — **미리보기 목록**을 보여 주고 확인 후 실행. 삭제는 휴지통 우선(`Trash` 재사용), 덮어쓰기 확인 대화상자(ui-ux §6)
- 끝나는 조건: 실제 git 충돌 하나를 이 앱으로 푼다, 로컬→SFTP 미러가 미리보기대로 된다.

### 8. 표 비교 · 스냅샷 · 보고서
- [ ] CSV/TSV 표 비교(키 열, 숫자 허용 오차)
- [ ] 폴더 스냅샷 저장(`*.dcsnap`: 경로·크기·시각·해시) 후 현재 폴더와 비교
- [ ] HTML 보고서, unified diff(patch) 내보내기

### 9. 정책 편집 창
- [ ] 폼으로 형식(마스크·뷰어·문법·중요도)·폴더 판정·필터 편집, 미리보기("이 파일에는 이 형식이 적용됩니다")
- [ ] 문법 시험기: 예제 텍스트에 토큰 색칠
- [ ] (선택) 정책 GitHub 동기화

### 10. 1.0 마무리
- [ ] 세 OS 확인, README 스크린샷·데모 GIF, `flutter-app-release-check`에 해당하는 점검 목록(버전·태그·워크플로·번역·README·시험)을 Avalonia에 맞게 돌리고 `v1.0.0`

### 11. 구조 비교 (1.0 뒤)
- [ ] JSON/YAML/XML을 트리로 정규화(키 순서·서식 무시)해 경로 단위 차이(`$.devices[3].baud`)로 보인다. 정책 `view: "structure"`

---

## 8. 추가 제안 (Beyond Compare에 없거나 약한 것) — ✅ 채택 여부 확정

1. ✅ **원격 해시 비교** (채택, 4단계) — SSH로 서버에서 `sha256sum`을 돌려 해시만 받는다. 내려받지 않고 원격 폴더 내용을 비교(서버 두 대끼리도). SSH.NET의 `SshClient`로 바로 된다. 폴더 비교 판정 단계 `hash`의 원격 구현.
2. ⏸ **구조 비교(JSON/YAML/XML)** (채택, 1.0 뒤 11단계) — 키 순서·서식을 무시하고 트리로 비교, "경로 `$.devices[3].baud` 값이 다름" 식으로 보인다. 설정 파일 비교에 강하다. (8단계 뒤)
3. ✅ **펌웨어 친화** (채택, 5단계) — Intel HEX/S-Record 변환, 바이너리 무시 구간, (나중에) 구조 템플릿(헤더 필드에 이름 붙여 보기). allwinner·RS485 작업에 바로 쓰인다.
4. ✅ **Daylight 패밀리 연동** (채택, 4단계) — 같은 집안이라 우선순위를 올린다. Commander의 "폴더 비교"에서 [자세히 비교]를 누르면 이 앱이 두 패널 경로로 열린다(CLI 호출만, 서로 코드 의존 없음). Jack's Bean 정책에 이 앱을 비교 도구로 등록할 수도 있다.
5. ⏸ **공유 코어 라이브러리** (복사로 시작, 2단계 끝에 재검토 — §2-3) — §2-3 (나). 세 번째 Avalonia 앱이 생기는 지금이 뽑을 때이고, 결과는 `application-release-templates`의 `avalonia/` 일반화의 재료가 된다.
6. ✖ **AI 요약** (보류 — 요청이 생기면 다시 본다) — 큰 diff를 "무엇이 바뀌었나" 몇 줄로. 외부 전송이 생기므로 기본 꺼짐, 명시적으로 켜야 함.

---

## 9. 위험과 확인할 것

| 위험 | 대응 |
|---|---|
| AvaloniaEdit 두 개를 줄 단위로 맞춰 스크롤·빈 줄(패딩) 삽입 | 실제 문서에 빈 줄을 넣지 않고 **시각적 간격**(BackgroundRenderer + 줄 높이 조정)으로 맞춘다. 1주 안에 시제품으로 먼저 확인(3단계 첫 작업) |
| 수만 파일 폴더 트리 성능 | 처음부터 가상화(`TreeDataGrid` 또는 평탄화한 `ListBox`), 결과 점진 반영 |
| NativeAOT와 의존성(SkiaSharp 직접 사용, SharpCompress) | 의존성 추가마다 `publish-aot` 확인, 안 되면 대체(7z는 미룸) |
| 원격 비교 중 연결 끊김 | 판정 단계별 재시도·부분 결과 유지, 상태 표시 |
| 인코딩 오판(CP949 vs UTF-8) | 감지 결과를 상태 표시줄에 보이고 수동 변경, 정책에 형식별 기본 인코딩 |
| 동기화의 데이터 손실 | 항상 미리보기 → 확인, 휴지통 우선, 원격 삭제는 별도 확인 |
| Avalonia 규약 미정 | 형제 두 앱을 기준으로 하고, 다른 점은 이 앱 README/`CLAUDE.md`에 이유와 함께 적는다(FRAMEWORKS.md 예외 규칙) |

---

## 10. 정할 것 (사용자) — 모두 확정

1. ~~앱 이름~~ → **`Daylight Compare` 확정** (Daylight 패밀리)
2. ~~식별자~~ → **`art.zoomon.daylightcompare` 확정**
3. ~~재사용 방식~~ → **복사로 시작 확정** (2단계 끝에 공유 라이브러리 여부 재검토)
4. ~~3단계 범위~~ → **3b(CLI·git difftool)를 텍스트 비교와 같이 한다** 확정
5. ~~추가 제안~~ → **원격 해시·펌웨어·패밀리 연동 채택, 구조 비교는 1.0 뒤, AI 요약 보류** 확정 (§8)
