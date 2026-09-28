<div align="center">

# ❄️ Polar Uninstaller

**Deep uninstaller for Windows — remove apps & games by their roots.**

[![Version](https://img.shields.io/badge/version-1.0.50-blue)](https://github.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/)
[![Python](https://img.shields.io/badge/python-3.14-yellow)](https://github.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Official uninstall + leftover cleanup (files, registry, shortcuts) • Quick cleaner • Disk insights • File scanner

[Features](#-features) • [Download](#-download) • [Usage](#-usage) • [Build](#-build-from-source) • [العربية](#-بالعربية)

</div>

## 📸 Screenshots

> Drop your screenshots into a `screenshots/` folder with these names:

| Home | Deep Uninstaller |
|---|---|
| `screenshots/home.png` | `screenshots/uninstaller.png` |

| Dashboard | File Browser |
|---|---|
| `screenshots/dashboard.png` | `screenshots/files.png` |

## ✨ Features

- 🗑️ **Deep Uninstaller** — full app/game list with icons, versions and sizes; official uninstaller +
  deep leftover cleanup (program files, AppData, registry keys, **desktop / Start Menu / taskbar shortcuts**)
- ✨ **Quick Cleaner** — user temp, Windows temp, Prefetch and Recycle Bin in one click
- 📊 **Dashboard** — usage stats, drive usage bars, **largest C:\ folders & files** (manual rescan, cached)
- 📁 **File Browser** — scan any path by size, safe or permanent delete
- 🌍 **Arabic / English** UI (saved)
- 🎨 **3 themes**, each with its own button: Dark 🌙 / Pure Black ⬛ / Light ☀ (saved)
- 🧩 Responsive title bar — tabs shrink in windowed mode, stretch evenly in fullscreen
- ⚡ Fast by design: paginated lists (50/page), background icon loading with icon cache,
  synchronous list builds (no scroll tearing), debounced search with same-result skip

## 📥 Download

Get `Polar Uninstaller.exe` from the
[**Releases**](https://github.com/) page (Windows 10/11, 64-bit).
Run it — it will request administrator rights (needed for full cleanup).

## 🚀 Usage

1. Open the **Deep Uninstaller** tab (apps load automatically).
2. Search, click a row (or its checkbox) to select.
3. **Uninstall Official** — runs the program's own uninstaller (best for games/launcher titles).
4. **Deep Clean Leftovers** — removes remaining folders, registry keys and shortcuts.
5. Use **Dashboard → Rescan** anytime to refresh disk usage and largest files.

## 🛠️ Build from source

Requirements: Python 3.14, `pip install customtkinter pillow psutil`

```powershell
# run
py app.py

# build single-file EXE (asks for admin via manifest)
py -m PyInstaller --onefile --noconsole --windowed --name "Polar Uninstaller" `
  --icon="icon.ico" --uac-admin "app.py" `
  --distpath "dist" --workpath "$env:TEMP\polar_build" --specpath "$env:TEMP\polar_build" --clean
```

## 🧠 How it works

- **App discovery:** reads `HKLM`/`HKCU` `...Uninstall` keys (+WOW64 mirrors), Steam/Epic libraries and
  MS Store packages; filters out drivers.
- **Leftover hunt:** keyword match on `Program Files`, AppData, ProgramData + deep registry sweep
  (`Software\<Publisher|App>`, `App Paths`, Startup, Uninstall key itself).
- **Shortcut cleanup:** scans Desktop, Start Menu (user + public, 2 levels) and pinned taskbar for
  matching `.lnk` files (name match or target-inside-install-folder check, no extra dependencies).
- **Disk sizing:** single lightweight `robocopy /L /MT:1` per top folder (sequential, time-boxed),
  never at startup — Dashboard scans are manual and cached.
- **Safety:** all heavy work runs in background threads; UI updates via `after()`; logs queued and flushed.

## 📁 Project structure

```
.
├── app.py                  # the whole app (single file, embedded logo/icon)
├── icon.ico / AppLogo.png  # icon sources
├── dist/                   # built EXE (git-ignored, ship via Releases)
└── README.md / LICENSE
```

Runtime cache & settings live in `%APPDATA%\PolarUninstaller`
(icons, storage cache, config, activity logs).

## ⚠️ Disclaimer

This tool deletes files, registry keys and shortcuts **with administrator rights**.
Review the Activity Log before confirming, and use it at your own risk.
Orphaned `robocopy.exe` helpers (if any) are cleaned automatically on scan and on exit.

## 👤 Author

**Ayhm** — Discord: `moon_ayhm` • https://polarwolves.com/supportus/p1682342912

---

## 🇸🇦 بالعربية

برنامج ويندوز لحذف البرامج والألعاب من جذورها: إلغاء التثبيت الرسمي + تنظيف البقايا
(ملفات، ريجستري، اختصارات سطح المكتب وقائمة ابدأ وشريط المهام)، مع منظف سريع
ولوحة مساحة ومستعرض ملفات. الواجهة عربية/إنجليزية و3 ثيمات (داكن/أسود/فاتح).
شغّل `Polar Uninstaller.exe` كمسؤول من صفحة Releases.
