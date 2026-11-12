# Daylight Compare (Avalonia)

릴리스·버전·패키징·정보 창·아이콘·라이선스·UI/UX·글꼴·언어·테마는
https://github.com/jejezz/application-release-templates/tree/main/conventions
규약을 따른다. 이 앱에 적용된 규약 버전: conventions-v1

Beyond Compare와 같은 역할(폴더·텍스트·바이너리·이미지 비교, 병합, 동기화)을 하는 C#/Avalonia(.NET 10) 데스크톱 앱. Daylight 패밀리.
계획·결정·위험은 [ROADMAP.md](ROADMAP.md). **지금 위치: 0단계(앱 골격) — 빈 홈 창·설정·정보·업데이트만 있다.**

- **형제 `../daylight-commander-avalonia`가 기준이다.** 원격(SFTP·FTP·WebDAV)·작업 큐·에디터·압축·테마·설정·정보 창·업데이트는
  새로 만들지 않고 거기서 **복사**해 온다(ROADMAP §2-3, 복사로 시작 확정). 복사한 파일은 첫 줄 근처에 출처를 남기고
  (`// From daylight-commander-avalonia @ <커밋>`), 고친 곳이 Commander에도 필요하면 적어 둔다. 0단계의 기준 커밋은 `.copied-from`.
  2단계 끝에 공유 라이브러리로 뽑을지 다시 판단한다. 정책 파일 패턴은 `../jacksbean-avalonia`의 `PolicyStore`를 따른다.
- **Look and feel**은 규약 `conventions/ui-ux.md`, `theming.md`와 Daylight Commander의 모습을 따른다. 다른 점만 [UI_UX.md](UI_UX.md)에 적는다.
  색은 `{DynamicResource App...}`로만 읽는다.
- **창 정책**: 비교·병합·정책 편집·설정은 독립 `Window`, 짧은 질문은 대화상자.
- 구조: `Presentation(App) → Services → Domain ← Data`. 프로젝트로 나눠 의존 방향을 컴파일러가 지킨다. 비교 엔진(diff·판정·정책 해석)은
  UI·IO 없는 Domain에 두고 골든 파일로 시험한다.
- **NativeAOT가 기본 배포 형태.** 의존성을 추가할 때마다 `scripts\publish-aot.ps1`로 AOT 게시가 되는지 확인한다.
  JSON은 `System.Text.Json` 소스 생성기, P/Invoke는 `LibraryImport`.
- 정체성·버전은 `Directory.Build.props` 하나 (`AppIdentity`/`AppVersion`이 어셈블리 메타데이터로 읽음).
  표시 이름 `Daylight Compare`, 식별자 `art.zoomon.daylightcompare`(릴리스 후 변경 금지), 파일·설정 폴더 이름 `DaylightCompare`,
  Inno Setup `AppId` GUID는 `installer/windows/app.iss` (변경 금지). 첫 정식 릴리스 전 버전은 `0.1.0-rc.N`.
- 화면 문구는 `IAppStrings`(ko/en, `Localization/`), XAML은 `{DynamicResource Str.*}`, 코드는 `Strings.Current`로 읽는다
  (새 문구는 인터페이스 → 한국어 → 영어 순, XAML에서 쓰면 `Strings.PublishResources`에도 더한다).
- 앱 아이콘: `tool/icon/draw_glyph.py`(글리프) → `tool/icon/avalonia_icons.py <고양이 경로>`(ico·png). 판 색 `hue 205` 고정(`assets/icon/cat.json`).
- 테스트: `dotnet test DaylightCompare.slnx`. 실행: `dotnet run -c Release --project src\DaylightCompare.App`.
- 커밋·푸시는 사용자가 요청할 때만. 앱 창은 요청받을 때만 띄운다. 한 번에 한 단계.
