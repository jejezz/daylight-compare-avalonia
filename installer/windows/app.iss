; Inno Setup script for the Avalonia (NativeAOT) Windows build
; (adapted from jejezz/application-release-templates desktop/ @ conventions-v1).
;
; Run by .github/workflows/release.yml's `build-windows` job on every `vX.Y.Z` tag push, which passes MyAppName /
; MyFileName / MyAppVersion / MyAppNumericVersion / MyAppExeName via `ISCC /D...` (read from Directory.Build.props).
; The fallbacks below are only for a local, manual compile — MyAppVersion 0.0.0 marks such a build.
; Build the app first: powershell -File scripts\publish-aot.ps1 (output: publish\aot).
;
; AppId is this app's own GUID. Never reuse another app's GUID, and never change it after the first release, or
; Windows treats the next version as a different application (breaking upgrade/uninstall). conventions/identity.md §5.

#ifndef MyAppName
  #define MyAppName "Daylight Compare"
#endif
#ifndef MyFileName
  #define MyFileName "DaylightCompare"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "0.0.0"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "DaylightCompare.exe"
#endif
#define MyAppPublisher "Jongyun Ahn"
#define MyAppURL "https://github.com/jejezz/daylight-compare-avalonia"
; The app's first release year.
#define MyFirstReleaseYear "2026"
#define SourceDir "..\..\publish\aot"

[Setup]
AppId={{36092366-69E2-4333-B1BB-0F9BFD78D0BA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) {#MyFirstReleaseYear} {#MyAppPublisher}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename={#MyFileName}-{#MyAppVersion}-windows-x64-setup
SetupIconFile=..\..\src\DaylightCompare.App\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The app holds a named mutex with its identifier (src/DaylightCompare.App/Cli/SingleInstance.cs), so setup asks to close it first.
AppMutex=art.zoomon.daylightcompare
; No license page: MIT apps have nothing to agree to (conventions/licensing.md §4).

[Languages]
; Installer text comes from Inno Setup's own translations — don't hardcode
; Korean (or English) strings below (conventions/packaging.md §3).
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; Debug symbols (.pdb) are not shipped.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
