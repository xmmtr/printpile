# BatchPrint

![Version](https://img.shields.io/badge/version-4.8.0.0-blue) ![Runtime](https://img.shields.io/badge/.NET-8.0%20WPF-5C2D91) ![Platform](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6) ![License](https://img.shields.io/badge/License-PolyForm%20NC%201.0.0-orange)

**BatchPrint** is a Windows desktop batch-printing tool: drag PDF, Word, Excel, PPT, image, and text files into the main window and print them in order — with per-file control over every parameter. This repository hosts its **full source code**, licensed under PolyForm Noncommercial 1.0.0.

Current source version **4.8.0.0** — the release where **update check actually works** (it queries the official update server, verified end-to-end on real hardware).

[简体中文](README.md) | **English**

## Table of Contents

- [Features](#features)
- [Version History](#version-history)
- [Update Check](#update-check)
- [Download & Install](#download--install)
- [Build from Source](#build-from-source)
- [Project Layout](#project-layout)
- [Privacy & Security](#privacy--security)
- [License](#license)
- [FAQ](#faq)

## Features

### Core printing
- **Mixed-format batches**: PDF / Word / Excel / PPT / images (jpg/png/bmp/tiff, etc.) / text queued together and printed in list order;
- **Bulk import**: drag & drop, add folder (recursive), or Ctrl+V paste;
- **Dual engine**: PDF is printed directly by the built-in pdfium engine (300 DPI raster output); Office documents are silently converted to PDF through locally installed Microsoft Office or WPS via COM — zero popups, your documents are never modified;
- **Isolated conversion worker** (v3.0): Office conversion runs in a separate worker process, so a conversion crash can never take down the main app; full logging;
- **Failure isolation**: one failed file never blocks the rest; retry individually;
- **Print settings**: duplex (one-sided / long-edge / short-edge), color or grayscale, orientation, paper size (A4/A3/A5/B5/Letter/Legal), scaling (fit-to-page / custom 1–400%), pages per sheet (1/2/4/6), live paper preview.

### Per-file control
- **Per-file parameters** (v4.4+): copies, page ranges (e.g. `1-3` or `1,3-5`), duplex, scaling, color per file — or set the whole batch at once and override individual rows;
- **Printer routing**: send the whole batch to one printer, or route files to different printers;
- **Print-time lock & ETA** (v4.5): controls are locked while printing; remaining time is estimated from page counts and historical timings;
- **Page-number watermark / page ranges** (v4.3+).

### Data & efficiency
- **Print history**: local SQLite history, searchable, one-click reprint (last 200 entries);
- **Print templates**: save frequently used parameter sets and apply them in one click (up to 5);
- **UI**: Simplified Chinese / English; light and dark themes;
- **Portable**: `settings.json`, `logs\`, and `data\` live inside the program folder — move the folder and you move everything.

### Update check (framework in v4.7, fully working since v4.8)
- Location: main window → **About** → **Check for updates**;
- Queries the official update server for the version manifest (**read-only**, nothing is uploaded) → shows version, date, notes, and a download button when a new version exists;
- Falls back to a "check failed" branch after an 8-second timeout (printing is never affected) — simply click again to retry.

## Version History

| Version | Date | Notes | Status |
| --- | --- | --- | --- |
| v1.0 | 2026-09-06 | First WPF release: batch printing, per-file copies/page ranges, print settings with live paper preview | ✅ Released (see Releases) |
| v2.3 | 2026-09-07 | Silent Office→PDF printing, bilingual UI, light/dark themes | ✅ Released (see Releases) |
| v3.0 | 2026-09-16 | Isolated conversion worker; crash-resilient; upgraded logging with crash diagnostics | ✅ Released (see Releases, zip & installer) |
| v3.1 / v3.2 | 2026-09-16 | COM/MiniPdf dual engine, engine auto-detection, silent Print-to-PDF | Archived |
| v4.0 – v4.5 | from 2026-09-17 | Print templates, SQLite history, per-file parameter cards, print-time lock, ETA | Archived |
| **V4.7 (4.7.0.0)** | 2026-09-27 | **First standalone open-source release**: split from the main line, PolyForm license, update-check framework (pointed at a local dev endpoint at the time) | Archived |
| **V4.8 (4.8.0.0, current source)** | 2026-09-28 | **Update check fully working**: default update server switched to the official endpoint (`https://batchprint-api.xmmtr.workers.dev`), verified end-to-end; official Inno Setup installer published | ✅ Current |

## Update Check

1. The client fetches `GET /updates/manifest.json` from the official server (read-only, no telemetry);
2. Parsing rules: only `active=true` entries, entries with invalid versions or URLs are skipped, nothing usable means "already up to date";
3. Download-URL whitelist: only the official backend domain and gitee.com are allowed (protects against a tampered manifest redirecting users);
4. Downloads open in the system browser — nothing is installed silently.

## Download & Install

- **GitHub Releases** (this repository): official installer (Inno Setup) and portable zip;
- Installer: double-click to install (defaults to `%LOCALAPPDATA%\Programs\BatchPrint`, no admin rights needed, Start-menu & desktop shortcuts, built-in uninstaller);
- Portable zip: extract anywhere and run `BatchPrintCNWpf.exe`;
- Requirements: Windows 10/11 (x64); self-contained — **no .NET runtime needed**; printing Office documents requires locally installed Microsoft Office or WPS (PDF/images have no such requirement).

## Build from Source

### Prerequisites
- Windows 10/11 (x64)
- .NET 8 SDK (8.0.4xx or later; `TreatWarningsAsErrors` is on — warnings fail the build)

### Build
```bat
git clone https://github.com/xmmtr/BatchPrint.git
cd BatchPrint
dotnet build BatchPrintCN.sln -c Release
```

### Publish (self-contained portable folder)
Both the main app and the conversion worker **must** be published as `win-x64` self-contained into the same folder, with matching runtime versions (otherwise the worker fails with `0x80008096`):

```bat
dotnet publish BatchPrintCN-WPF\BatchPrintCN-WPF.csproj -c Release -r win-x64 --self-contained true -o ..\publish
dotnet publish BatchPrintCN-Worker\BatchPrintCN-Worker.csproj -c Release -r win-x64 --self-contained true -o ..\publish
```

### Self-test (in the publish folder; all four exit codes must be 0)
```bat
BatchPrintCNWpf.exe --selftest
BatchPrintCNWpf.exe --formtest
BatchPrintCNWpf.exe --dialogtest
BatchPrintCNWpf.exe --themetest
```

Official releases ship only after all four self-tests pass. The installer is built with Inno Setup 6 (bilingual wizard, auto-matched to system language).

## Project Layout

```text
BatchPrintCN.sln                 Solution (three projects)
├─ BatchPrintCN-WPF\             WPF main app (UI / settings / history / templates / update check)
│  ├─ Localization\              zh-CN.xaml / en-US.xaml (keys are 1:1 across languages)
│  ├─ SettingsService.cs         Settings (portable settings.json)
│  ├─ UpdateChecker.cs           Update check (manifest parsing / version compare / URL whitelist)
│  ├─ AboutPage / HistoryPage / SettingsPage
│  ├─ NOTICE / LICENSE-PolyForm.md (shipped with binaries)
├─ BatchPrintCN\                 Engine layer (print planning / engine dispatch / history / templates / SQLite)
├─ BatchPrintCN-Worker\          Conversion worker (silent Office/WPS → PDF)
├─ Directory.Build.props         Single source of version numbers (currently 4.8.0 / 4.8.0.0)
└─ docs\                         Historical screenshots
```

## Privacy & Security

- **Local-only printing**: documents are processed on your machine only — never uploaded, never sent to any cloud, no personal information collected;
- **Read-only update check**: the client only fetches the version manifest; nothing about your machine is included or uploaded;
- **Local logs**: logs stay in the program folder `logs\` (falls back to `%LOCALAPPDATA%\BatchPrintCN\logs` if not writable);
- **No telemetry, no tracking**;
- **Independence**: independently developed — no code or asset reuse from the PrintPool software.

## License

- Source code and official binaries are licensed under the **PolyForm Noncommercial License 1.0.0** (full text in [LICENSE](LICENSE) and `LICENSE-PolyForm.md` in the source tree):
  - ✅ Permitted: use, copy, modify, and distribute for personal, noncommercial purposes;
  - ❌ Restricted: any commercial use requires a separate commercial license (contact the repository owner);
- Third-party components: pdfium (Apache License 2.0, shipped as pdfium.dll), .NET 8 runtime (MIT) — see [NOTICE](NOTICE).

## FAQ

**Q: "Check for updates" fails?**
The official update server may be unreachable from some mainland-China networks; the app times out after 8 seconds with a retry hint — printing is never affected. Users with a proxy are unaffected.

**Q: Antivirus flags the installer/exe?**
Official binaries are not code-signed, so a few scanners may false-positive. You can build from source in this repository to verify.

**Q: Windows 7 supported?**
No. .NET 8 requires Windows 10 or later; Win7 support is on hold.

**Q: Are there limits on templates/history?**
Yes — up to 5 templates and the last 200 history entries. This is the current design.

---

*BatchPrint developers · project started in 2026 · this README corresponds to source version 4.8.0.0*
