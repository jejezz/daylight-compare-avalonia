# Daylight Compare

[![Latest Release](https://img.shields.io/github/v/release/jejezz/daylight-compare-avalonia?include_prereleases&label=release)](https://github.com/jejezz/daylight-compare-avalonia/releases)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](#download-and-install)
[![Built with Avalonia](https://img.shields.io/badge/built%20with-Avalonia%2012-8B44AC)](https://avaloniaui.net)
[![NativeAOT](https://img.shields.io/badge/.NET%2010-NativeAOT-512BD4)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**English | [한국어](README.md)**

A desktop app that **compares and merges folders, text, binaries and images side by side**.
It is the companion to [Daylight Commander](https://github.com/jejezz/daylight-commander-avalonia) in the Daylight family,
written in C# with [Avalonia](https://avaloniaui.net) and published with **.NET 10 NativeAOT**. Runs on Windows, macOS and Linux.

> **In development (stage 3: text comparison).** The current build compares two text files side by side (read-only), with drag and drop,
> encoding detection and repair, and unimportant differences such as comments and whitespace. `DaylightCompare <left> <right>` opens a comparison directly.
> The staged plan is in [ROADMAP.md](ROADMAP.md) (Korean).

## Screenshots

Added once the first compare view (stage 3) lands.

## Features (planned)

- **Folder compare** — any mix of local, SFTP, FTP, WebDAV and archives (zip/tar), decided by size → time → hash → content
- **Text compare and merge** — line and character differences, 3-way merge, `git difftool` / `git mergetool`
- **Binary compare** — hex view, insertion detection, Intel HEX / S-Record conversion
- **Image compare** — side by side, difference mask, overlay, blink, tolerance
- **Compare policies** — per-format rules that filter out unimportant differences such as comments and whitespace
- **Remote hash compare** — hashes computed on the server over SSH, so remote folders compare without downloading

## Download and install

Get it from the [Releases page](https://github.com/jejezz/daylight-compare-avalonia/releases).

| OS | File |
| --- | --- |
| Windows 10/11 (x64) | `*-windows-x64-setup.exe` (installer) or `*-windows-x64.zip` (portable) |
| macOS 11+ (Apple Silicon) | `*-macos-arm64.dmg` (from 1.0.0) |
| Linux (x64) | `*-linux-x64.tar.gz` |

- **Windows**: the installer isn't code-signed yet; if SmartScreen warns, choose **More info → Run anyway**.
- **macOS**: the build isn't signed or notarized by Apple. **Right-click → Open** once, or run
  `xattr -dr com.apple.quarantine "/Applications/Daylight Compare.app"`.
- **Linux**: extract and run `./install.sh` (`--remove` uninstalls). Runs on X11 or XWayland.

## Command line · git difftool

The same switches as Beyond Compare (`/switch` on Windows, `-switch` on macOS/Linux, `--switch` anywhere).

```text
DaylightCompare [switches] [<left> [<right>]]

  /qc[=size|crc|binary]       Compare without a window; result in the exit code (rules-based if no value)
  /title1=name  /title2=name  Pane titles (instead of git temp file names)
  /ro  /ro1  /ro2             Read-only (both · left · right)
  /iu                         Ignore unimportant differences
  --wait                      Wait until the compare window closes (git difftool)
  /?                          Usage
```

- Exit codes match BC too: `0` success (window) · `1` binary same · `2` rules-based same (only encoding/BOM/line endings differ) ·
  `11` binary differences · `12` similar (only unimportant differences) · `13` rules-based differences · `100` error. Scripts written for BC keep working.
- BC switches not supported yet (`/filters`, `/automerge`, `/mergeoutput`, …) are reported as such — they arrive with folder compare (stage 4) and merge (stage 7).
- If the app is already running, the compare window opens there. With `--wait` it waits until that window closes.
- A file inside an archive can be given as `a.zip!/folder/file` (read-only).

**Use it as git difftool** (Windows, default install location. Run these in Git Bash — Windows PowerShell 5.1 strips the inner double quotes):

```bash
git config --global diff.tool daylight
git config --global difftool.daylight.cmd '"C:/Program Files/Daylight Compare/DaylightCompare.exe" --wait "$LOCAL" "$REMOTE" -title1="$MERGED (before)" -title2="$MERGED"'
git config --global difftool.prompt false
```

On macOS use `"/Applications/Daylight Compare.app/Contents/MacOS/DaylightCompare"`; on Linux, `DaylightCompare` in the extracted folder. In git config use the `-title1=` form even on Windows — Git for Windows (MSYS) may rewrite arguments starting with `/` into paths.
Then `git difftool` (or `git difftool HEAD~1 -- file`) opens one compare window per changed file.

- Like BC, a window comparison always exits with 0, so `difftool.trustExitCode` never stops git.
- `git difftool --dir-diff` (whole folders) works once folder compare (stage 4) lands. `git mergetool` is stage 7.

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run -c Release --project src/DaylightCompare.App
```

NativeAOT publish (Windows needs the Visual Studio C++ build tools, Linux needs `clang` and `zlib1g-dev`):

```bash
powershell -File scripts/publish-aot.ps1     # Windows → publish/aot
bash scripts/publish-aot.sh linux-x64        # Linux / macOS (osx-arm64)
```

## Tests

```bash
dotnet test DaylightCompare.slnx
```

The SFTP/FTP/WebDAV integration tests start Python servers (`asyncssh`, `pyftpdlib`, `wsgidav`, `cheroot`).
They are skipped when the packages or Python are missing; point `DC_TEST_PYTHON` at another interpreter if needed.

## App icon

`tool/icon/draw_glyph.py` draws the glyph (`assets/icon/source_glyph.svg` and `.png`), and
`python3 tool/icon/avalonia_icons.py <cat image path>` writes the platform files. The cat artwork (paid Icons8 asset) is not in the repository.

## Project layout

`Presentation(App) → Services → Domain ← Data`, split into projects so the compiler enforces the dependency direction.
Plans, decisions and risks are in [ROADMAP.md](ROADMAP.md); working rules are in [CLAUDE.md](CLAUDE.md).

## Settings file

`%APPDATA%\DaylightCompare\settings.json` (Windows), `~/Library/Application Support/DaylightCompare` (macOS),
`~/.config/DaylightCompare` (Linux). Passwords are never stored.

## License

[MIT](LICENSE) © 2026 Jongyun Ahn
