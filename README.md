# Daylight Compare

[![Latest Release](https://img.shields.io/github/v/release/jejezz/daylight-compare-avalonia?include_prereleases&label=release)](https://github.com/jejezz/daylight-compare-avalonia/releases)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](#다운로드-및-설치)
[![Built with Avalonia](https://img.shields.io/badge/built%20with-Avalonia%2012-8B44AC)](https://avaloniaui.net)
[![NativeAOT](https://img.shields.io/badge/.NET%2010-NativeAOT-512BD4)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**[English](README.en.md) | 한국어**

폴더 · 텍스트 · 바이너리 · 이미지를 **나란히 비교하고 병합하는 데스크톱 앱**입니다.
[Daylight Commander](https://github.com/jejezz/daylight-commander-avalonia)와 짝을 이루는 Daylight 패밀리 앱으로,
C#과 [Avalonia](https://avaloniaui.net)로 만들고 **.NET 10 NativeAOT**로 게시합니다. Windows · macOS · Linux를 지원합니다.

> **개발 중 (3단계: 텍스트 비교).** 지금 빌드는 텍스트 파일 두 개를 나란히 비교합니다(읽기 전용). 끌어다 놓기, 인코딩 자동 감지·복구,
> 주석·공백처럼 중요하지 않은 차이 구분을 지원합니다. `DaylightCompare <왼쪽> <오른쪽>`으로 바로 열 수도 있습니다.
> 단계별 계획은 [ROADMAP.md](ROADMAP.md)에 있습니다.

## 스크린샷

첫 비교 화면(3단계)이 나오면 채웁니다.

## 주요 기능 (계획)

- **폴더 비교** — 로컬 · SFTP · FTP · WebDAV · 압축 파일(zip/tar) 어느 조합이든, 크기 → 시각 → 해시 → 내용 순으로 판정
- **텍스트 비교 · 병합** — 줄/글자 단위 차이, 3-way 병합, `git difftool` / `git mergetool`
- **바이너리 비교** — 16진수 보기, 삽입 감지, Intel HEX / S-Record 변환
- **이미지 비교** — 나란히 · 차이 마스크 · 겹치기 · 깜빡이기, 허용 오차
- **Compare Policy** — 파일 형식별 규칙으로 주석 · 공백 같은 중요하지 않은 차이를 걸러 냄
- **원격 해시 비교** — SSH로 서버에서 해시만 받아, 내려받지 않고 원격 폴더를 비교

## 다운로드 및 설치

[Releases 페이지](https://github.com/jejezz/daylight-compare-avalonia/releases)에서 받을 수 있습니다.

| OS | 받을 파일 |
| --- | --- |
| Windows 10/11 (x64) | `*-windows-x64-setup.exe` (설치) 또는 `*-windows-x64.zip` (설치 없이 실행) |
| macOS 11+ (Apple Silicon) | `*-macos-arm64.dmg` (1.0.0부터) |
| Linux (x64) | `*-linux-x64.tar.gz` |

- **Windows**: 코드 서명이 아직 없어 SmartScreen이 경고하면 **추가 정보 → 실행**을 누릅니다.
- **macOS**: Apple 서명·공증이 없는 빌드입니다. 앱을 **우클릭 → 열기**로 한 번 열거나
  `xattr -dr com.apple.quarantine "/Applications/Daylight Compare.app"` 을 실행하세요.
- **Linux**: 압축을 풀고 `./install.sh`(제거는 `--remove`). X11 또는 XWayland에서 실행됩니다.

## 명령줄 · git difftool

```text
DaylightCompare [옵션] [<왼쪽> [<오른쪽>]]

  --wait, -w     비교 창을 닫을 때까지 기다리고, 결과를 종료 코드로 돌려줍니다
  --silent, -s   창 없이 비교만 합니다 (경로 둘)
  --help, -h     사용법
```

- 종료 코드: `0` 같음 · `1` 다름 · `2` 오류(읽지 못함, 폴더, 잘못된 인자). "같음"은 Compare Policy로 본 결과라서
  줄바꿈·BOM·공백처럼 중요하지 않은 차이만 있으면 `0`입니다. `--wait`는 창을 닫을 때 **저장한 뒤의 파일**로 판정합니다.
- 앱이 이미 떠 있으면 새로 띄우지 않고 그 앱에서 비교 창을 엽니다. `--wait`면 그 창을 닫을 때까지 기다립니다.
- 압축 파일 안의 파일은 `a.zip!/폴더/파일` 꼴로 줄 수 있습니다(읽기 전용).

**git difftool로 쓰기** (Windows, 기본 설치 위치. Git Bash에서 실행하세요 — Windows PowerShell 5.1은 안쪽 큰따옴표를 지웁니다):

```bash
git config --global diff.tool daylight
git config --global difftool.daylight.cmd '"C:/Program Files/Daylight Compare/DaylightCompare.exe" --wait "$LOCAL" "$REMOTE"'
git config --global difftool.prompt false
```

macOS는 `"/Applications/Daylight Compare.app/Contents/MacOS/DaylightCompare"`, Linux는 압축을 푼 폴더의 `DaylightCompare`를 씁니다.
그다음 `git difftool`(또는 `git difftool HEAD~1 -- 파일`)을 실행하면 바뀐 파일마다 비교 창이 하나씩 열립니다.

- `difftool.trustExitCode`는 켜지 마세요 — 켜면 차이가 있는 첫 파일(종료 코드 1)에서 git이 멈춥니다.
- `git difftool --dir-diff`(폴더 통째로)는 폴더 비교(4단계)가 생기면 됩니다. `git mergetool`은 7단계입니다.

## 소스에서 빌드

[.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```bash
dotnet run -c Release --project src/DaylightCompare.App
```

NativeAOT 게시(Windows는 Visual Studio C++ 빌드 도구, Linux는 `clang`·`zlib1g-dev` 필요):

```bash
powershell -File scripts/publish-aot.ps1     # Windows → publish/aot
bash scripts/publish-aot.sh linux-x64        # Linux / macOS (osx-arm64)
```

## 테스트

```bash
dotnet test DaylightCompare.slnx
```

SFTP/FTP/WebDAV 통합 테스트는 Python 서버(`asyncssh`, `pyftpdlib`, `wsgidav`, `cheroot`)를 띄웁니다.
패키지가 없거나 Python을 못 찾으면 해당 테스트는 건너뜁니다. 인터프리터는 환경변수 `DC_TEST_PYTHON`으로 지정할 수 있습니다.

## 앱 아이콘

글리프는 `tool/icon/draw_glyph.py`가 그리고(`assets/icon/source_glyph.svg`·`.png`), 플랫폼별 파일은
`python3 tool/icon/avalonia_icons.py <고양이 이미지 경로>`로 만듭니다. 고양이 원본(Icons8 유료)은 저장소에 두지 않습니다.

## 프로젝트 구조

`Presentation(App) → Services → Domain ← Data` — 프로젝트로 나누어 의존 방향을 컴파일러가 지킵니다.
계획·결정·위험은 [ROADMAP.md](ROADMAP.md), 작업 규약은 [CLAUDE.md](CLAUDE.md)에 있습니다.

## 설정 파일

`%APPDATA%\DaylightCompare\settings.json` (Windows), `~/Library/Application Support/DaylightCompare` (macOS),
`~/.config/DaylightCompare` (Linux). 비밀번호는 저장하지 않습니다.

## 라이선스

[MIT](LICENSE) © 2026 Jongyun Ahn
