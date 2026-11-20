[README_Polar_Uninstaller_v1.0.147.md](https://github.com/user-attachments/files/35139278/README_Polar_Uninstaller_v1.0.147.md)
<div align="center">

<img src="Polar_Logo_Beige_512.png" width="112" alt="Polar Uninstaller logo">

# Polar Uninstaller

**A modern, bilingual Windows utility for uninstalling apps and games, cleaning temporary files, analyzing storage, and managing files.**

**Built with C# • .NET • WPF**

[![Version](https://img.shields.io/badge/version-1.0.147-00B8F0)](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
[![Language](https://img.shields.io/badge/language-C%23-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![UI](https://img.shields.io/badge/UI-WPF-0078D4)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

[English](#english) | [العربية](#العربية)

[Releases](https://github.com/Ayh-m1/Polar-Uninstaller/releases) · [Report an issue](https://github.com/Ayh-m1/Polar-Uninstaller/issues) · [Discord](https://discord.gg/plr)

</div>

---

## English

### Overview

**Polar Uninstaller v1.0.147** builds on the native **C# / .NET / WPF** edition introduced in v1.0.120. This release focuses on stability and polish: switching language or theme no longer interrupts running operations, the Dashboard scan shows progress and can be cancelled, Arabic numbers, sizes, and paths appear in the correct order, program icons and duplicates are fixed, and settings and crash handling are more resilient.

The interface supports **English and Arabic**, with right-to-left layouts where appropriate.

### What's new in v1.0.147

#### Improvements

- **Switch language during operations:** you can change the language while uninstalling, scanning for leftovers, running Quick Cleaner, calculating folder sizes, or scanning for duplicates, without restarting the page or the current operation.
- **Dashboard scan progress:** the scan shows progress and can be cancelled, in both languages. Scan operations are recorded in the Activity Log when they start, finish, or are cancelled.
- **Consistent numbers:** version numbers, paths, and sizes now match the surrounding design on Home, Deep Uninstaller, File Browser, and the update center.
- **Responsive loading and saving:** the app stays responsive while loading lists and saving settings, ignores stale scan results, and saves pending settings before closing.

#### Fixes

- **Dashboard keeps scanning:** the scan and update continue when you switch language or theme. Returning to the Dashboard shows the running scan with its progress and results.
- **Correct order in Arabic:** sizes, percentages, drive letters, dates, and paths appear in the correct order. Dashboard values stay under their titles and beside their icons in both languages.
- **Icons and duplicates:** program and game icons are restored correctly, Steam icons are detected more reliably, and duplicate items in the program list are merged.
- **Corrupted settings:** if the saved settings file is damaged, settings are restored from a healthy backup, and the error page offers a retry option instead of a technical message.
- **Crash reports:** crash reports are saved on your device with options to open the logs folder or copy the details. When you reopen the app, the current window is shown, and movement is reduced in Remote Desktop sessions.


<details>
<summary>Also in v1.0.145–v1.0.146</summary>

#### Deep Uninstaller

- **Faster start:** the extra confirmation before starting an uninstall was removed, since you already chose the program. Leftover candidates are still never preselected, and permanent deletions still require confirmation.
- **Workflow review:** the Deep and Games uninstall paths and their buttons were audited in both Arabic and English, with fixes for several Deep Uninstaller problems.
- **Logos for every program and game:** icons are resolved and cached from the first launch, including on a first run, with safeguards so one app never shows another app's icon (for example, Steam no longer shows the wrong logo).
- **No duplicate rows:** the same program or game reported by a launcher and the Windows registry is merged into one entry, and launcher product IDs are no longer shown as versions.
- **Larger program rows** for easier reading.

#### Dashboard and File Browser

- **Working Refresh button** on the Dashboard.
- **Deletion sync:** when you delete a file or folder, the Dashboard's scan results update to match.
- **Scan keeps running:** switching the language or theme no longer interrupts a Dashboard scan.
- **Back history and breadcrumb path** in the File Browser, so you can step back through the folders you visited and jump to any parent folder.

#### Interface

- **Hover animation** on navigation buttons: colors fade smoothly, the button shifts slightly, and the effect follows the active theme across all eight themes. It is disabled when Windows animations are turned off.
- **Better Arabic:** improved interface wording, and numbers with units (such as `41.6 GB`) and dates appear in the correct order in the Arabic interface.

#### Stability

- Global error handling so unexpected errors are logged instead of closing the application.
- Settings are saved atomically with a backup copy, so a crash during saving cannot corrupt them.
- Background scans are cancelled cleanly when you leave a page, and stale results are discarded.

</details>

<details>
<summary>Previous release highlights (v1.0.144)</summary>

#### File Browser

- **Selection-aware action bar:** the actions under the file list appear when items are selected and show the selected count. **Move to Recycle Bin** is the primary action, while **Delete permanently** is red, visually separated from the other actions, and always requires explicit confirmation.
- **Organized "…" menu:** options are grouped (View, Selection, Tools) with consistent icons and keyboard shortcuts shown next to each item, such as **Ctrl+A** to select all and **Ctrl+D / Esc** to deselect.
- **Easier sorting:** column headers look and behave like buttons, with hover feedback, tooltips, and a clear sort-direction indicator. A separate **Sort** menu stays in sync with the headers.
- **Cleaner layout:** columns (Name, Size, Type, Modified) are aligned with consistent spacing, and headers line up with their rows in both English and Arabic.
- **Search in the toolbar:** the current-folder search box now sits in the toolbar, with a clear button and **Ctrl+F** to focus it.
- **Clearer navigation:** navigation icons are easier to tell apart, and the Back/Forward/Up buttons follow the correct direction in Arabic (RTL).
- **Better selection and right-click menu:** select rows more easily, and use the right-click menu for common actions.
- **More useful Details panel:** copy the item's path, and calculate a folder's size with visible progress and the ability to cancel.
- **Correct sizes and dates in Arabic:** values such as `41.6 GB` and dates are displayed in the correct order in the Arabic interface.
- **Accessibility:** improved text contrast, a clearly visible keyboard focus indicator, and better screen-reader labels.

#### Duplicate Finder

- **Reorganized page:** scan controls, progress, summary statistics, filters, and results search are laid out in a clear top-to-bottom flow.
- **Grouped results:** each duplicate group is a collapsible card showing the file name, number of copies, size per copy, and the space you can recover. Large result sets remain usable.
- **Always keep one copy:** each group keeps a protected copy that cannot be selected for removal. You can choose which copy to keep.
- **Smart selection:** select extra copies across all groups while keeping the newest, the oldest, or the copy with the shortest path. Nothing is selected until you choose.
- **Safer cleanup:** a fixed bottom bar shows how many files are selected and how much space will be freed. Moving to the Recycle Bin is the primary action, and permanent deletion is separated and confirmed.
- **Filters and sorting:** filter by file type or minimum size, search by name or path, and sort groups with the same sort pattern used in the File Browser. Your choices are remembered without cluttering the Activity Log.
- **Smoother scanning:** partial results are shown while scanning continues, scans can be cancelled, and the page has clear empty and ready states.

#### Other improvements

- **More supported launchers:** game inventory detection now also covers **GOG** and **Riot Games** when detectable.
- **Themes:** eight themes are available, including Blue, Black, White, Beige, Dark Beige / Espresso, and Midnight Champagne.

</details>

### Core features

- **Deep and supported game uninstall workflows**, with verification and optional leftover review.
- **Quick Cleaner** for selected temporary-file and cleanup categories.
- **Dashboard** with drive capacity and large-folder/file analysis.
- **Integrated File Browser** with navigation, folder-size calculation, clickable sorting, multi-selection, a right-click menu, and confirmed batch operations.
- **Duplicate Finder** with SHA-256 content verification, grouped results, and smart selection that always keeps one copy.
- **Eight themes**, English/Arabic localization, and an Activity Log.
- **Built-in update check.**
- **Portable EXE and Windows Setup installer**, with SHA-256 checksums.

### Features

#### Deep Uninstaller

Discover installed Windows applications and supported game-library entries from Steam, Epic Games, Ubisoft, Rockstar, GOG, and Riot Games when detectable. The page displays application names, versions, publishers, sizes, and icons when that information is available. Home and Deep Uninstaller share the same merged, deduplicated inventory and installed-program count, and logos are resolved from the first launch.

- **Deep Uninstall** is available for installations that support a verified direct-removal workflow.
- **Games Uninstall** uses supported launcher or game-specific removal methods when available. Some launchers require you to finish removal in their own interface.
- Both workflows can use the **same leftover scanner and review window** after removal is verified.
- Review candidate files, folders, shortcuts, and Registry entries. Candidates are **not preselected**.
- **Verify / Rescan** helps recheck a removal that could not be confirmed automatically.
- The application list is refreshed after a removal is verified; the app must not be reported as removed merely because an uninstaller window closed.

**Important:** Detection does not guarantee that every leftover belongs exclusively to the removed program. Shared directories, system components, and unrelated shortcuts must remain protected. Registry changes require careful review and backup.

#### Quick Cleaner

Select the cleanup categories you want to process, such as:

- User temporary files (`%TEMP%`)
- Windows temporary files (`C:\Windows\Temp`)
- Windows Prefetch
- Recycle Bin

Results depend on Windows permissions and whether files are in use. Review the selection and Activity Log before and after cleaning.

#### Dashboard

View drive capacity, used and available space, and storage information. Scan an entire drive or a chosen folder, then switch between **Largest Folders** and **Largest Files**. Folder results list only the scanned location's immediate child folders, ranked by cumulative size; nested folders are not repeated as separate results in the same list. Set a minimum file-size filter for large-file results and open a listed folder directly in Polar's File Browser.

Scanning runs in the background with progress, cancellation, caching, and partial-result indicators for inaccessible locations. Heavy scans are not required just to open the page.

#### File Browser

Browse directories with a clickable path bar, Back/Forward/Up/Refresh navigation, sortable columns, file metadata, and a details panel. Folder sizes are calculated automatically in the background and displayed as they become available.

- **Sorting:** click any column header to sort, or use the **Sort** menu. Both stay in sync, and your choice persists across navigation and application restarts.
- **Selection:** click a row's marker, or use **Ctrl+Click**, **Shift+Click**, or **Ctrl+A**. Use **Ctrl+D** or **Esc** to deselect, and **Ctrl+F** to search the current folder.
- **Actions:** when items are selected, an action bar offers **Open**, **Move to Recycle Bin**, and **Delete permanently**. Permanent deletion is separated from the other actions and requires explicit confirmation, with safeguards for protected paths.
- **Details panel:** shows item information, lets you copy the path or open its location, and calculates folder size with progress and cancellation.

The interface supports Arabic RTL and English LTR.

**Important:** Review every selected path before a batch operation. **Permanent deletion cannot be undone.** Prefer **Move to Recycle Bin** whenever possible.

#### Duplicate Finder

Find potential duplicate files by comparing sizes and verifying matching candidates with **SHA-256** hashes. Results are grouped, so you can review each group before selecting extra copies for cleanup.

- Each group always keeps at least one protected copy, and you can change which copy is kept.
- Use **Smart select** to keep the newest copy, the oldest copy, or the copy with the shortest path across all groups. Nothing is selected automatically.
- Filter by type or minimum size, search by name or path, and sort by recoverable space, file size, number of copies, or name.
- A bottom bar shows how many files are selected and how much space will be freed. **Move to Recycle Bin** is the primary action; **Delete permanently** is separate and requires confirmation.

**Important:** Duplicates may include backups, build outputs, or files that other programs depend on. Review each group and retain at least one copy of important files.

#### Themes and languages

Polar Uninstaller supports eight themes, including **Blue, Black, White, Beige, Dark Beige / Espresso, and Midnight Champagne**, with English and Arabic interfaces. Theme and language preferences are designed to remain consistent across the application.

#### Activity Log, What's New, and Feedback

- **Activity Log:** shows progress, outcomes, and relevant warnings.
- **Clear Cache:** resets temporary scan data and stored dashboard statistics/history; review the action before confirming.
- **What's New:** displays release information within the application.
- **Feedback & Reports:** lets users submit a category, message, and app version. Do not include passwords or other sensitive information in reports.

### Download and run

1. Visit the [official Releases page](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. Choose the latest **C# / WPF** release (v1.0.147 or newer when available). Older Python releases use a different application build.
3. Choose one of the published assets:
   - **`PolarUninstaller-Setup-v1.0.147.exe`** — Windows Setup installer.
      - **`PolarUninstaller.exe`** — standalone portable single-file executable.
   - **`SHA256SUMS.txt`** — SHA-256 checksums to verify your download.
4. Review any Windows security or administrator-permission prompt before approving it.

The WPF portable build is packaged with their required .NET runtime components, so a separate .NET runtime installation is not normally needed. The Setup installer is a different distribution format from the portable EXE. The Setup installer and the app request administrator permission, so Windows will show a User Account Control prompt.

> **Release status:** A successful local build does not mean an installer or GitHub release has already been published. Check the Releases page for what is actually available.

### Build from source

The current application is written in **C# using WPF and .NET**, targeting **64-bit Windows 10/11**.

Requirements:

- Windows 10 or 11 (64-bit)
- The .NET 9 SDK (or the SDK version required by the WPF project)
- Visual Studio with .NET desktop development tools, or a suitable .NET CLI environment

From the **`Polar-Uninstaller-WPF`** project directory, use the existing release script if it is present:

```powershell
.\Build-Release.ps1
```

Alternatively, publish the WPF `.csproj` using the .NET CLI, adjusting the project path and publish options to match the repository:

```powershell
dotnet publish .\PolarUninstallerWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\Build\SingleFile
```

The intended portable output is `Build/SingleFile/PolarUninstaller.exe`. Keep the actual project file name, resource settings, and release-script requirements as the source of truth if they differ from the example command.

**The old Python commands (`app.py`, PyInstaller, `.spec`) apply only to legacy releases and are not used to build the C# edition.**

### Project structure

| Path | Purpose |
|---|---|
| `Polar-Uninstaller-WPF/` | C# / WPF application project |
| `Views/` and WPF `*.xaml` files | Application pages and visual layout, where present |
| `Services/` | Uninstall, shared inventory, storage scanning, folder-size calculation, file management, and other application services |
| `Localization/` | English and Arabic interface text |
| `Assets/` | Branding, images, release notes, and other bundled resources |
| `Build/SingleFile/` | Self-contained portable EXE output |
| `Build/` | Release artifacts and installer packaging output |

Actual folders may differ as the project evolves. Use the project files in the repository for the definitive layout.

### Safety and limitations

> **Always review selected files, folders, shortcuts, and Registry entries before deletion. Some changes are irreversible.**

- Back up important data before uninstalling software or cleaning files.
- Prefer moving files to the Recycle Bin over permanent deletion.
- An application's name or publisher alone is not sufficient proof that a folder or Registry entry is safe to delete.
- Identical file contents do not mean a duplicate is unneeded. Backups, build outputs, and program files may be duplicates by design.
- Shared game-library folders, Windows components, drivers, and security software need additional protection.
- Some games and applications can only be removed through their official launcher or uninstaller.
- Uninstall verification, artwork discovery, and leftover detection vary by application and system configuration.
- Do not treat an unsuccessful scan or a closed installer window as proof of successful removal.

### Credits & Acknowledgments

**[MobV2](https://github.com/MobV2) — Lead Beta Tester**

Special thanks to MobV2 for extensive testing, bug reports, suggestions, and help improving the user experience.

### Links

- [GitHub Releases](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
- [Issues & Bug Reports](https://github.com/Ayh-m1/Polar-Uninstaller/issues)
- [Support](https://polarwolves.com/supportus/p1682342912)
- [Discord](https://discord.gg/plr)

**Developer:** Polar  
**License:** MIT

### Code signing policy

Polar Uninstaller is applying for free code signing through SignPath Foundation. If approved, future official releases will be digitally signed using SignPath.io, with a certificate issued to SignPath Foundation.

**Code Signing Team**
- Author and Maintainer: [Ayh-m1](https://github.com/Ayh-m1)
- Reviewer: Ayh-m1
- Signing Approver: Ayh-m1

**Privacy Policy**

Polar Uninstaller does not intentionally collect personal information, usage analytics, or telemetry.

The application connects to GitHub solely to check for and download software updates. These connections may expose standard network information, such as the user's IP address, to GitHub.

Polar Uninstaller does not otherwise transfer user information to external services unless explicitly requested by the user.

**Build and Signing Process**

Official releases are built from the project's publicly available source code using GitHub Actions. If approved, signing requests will require manual authorization before release.

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

---

## العربية

### نظرة عامة

**Polar Uninstaller v1.0.147** هو تحديث للنسخة المبنية باستخدام **C# وWPF و.NET** لنظام Windows، والتي بدأت مع الإصدار v1.0.120 بدل واجهة Python وCustomTkinter السابقة.

يركز هذا الإصدار على الاستقرار والتحسينات: تبديل اللغة أو المظهر لم يعد يقطع العمليات الجارية، وفحص لوحة المعلومات يعرض التقدّم ويمكن إلغاؤه، والأرقام والأحجام والمسارات العربية بالترتيب الصحيح، وإصلاح الأيقونات والتكرار، وتعزيز التعامل مع الإعدادات والأعطال. يدعم البرنامج **العربية والإنجليزية** مع مراعاة اتجاه الواجهة.

### أبرز تغييرات v1.0.147

#### تحسينات

- **تبديل اللغة أثناء العمليات:** يمكن تبديل اللغة أثناء الإزالة وفحص البقايا والتنظيف السريع وحساب أحجام المجلدات وفحص الملفات المكررة دون إعادة تشغيل الصفحة أو العملية الحالية.
- **تقدّم فحص لوحة المعلومات:** يظهر تقدّم الفحص ويمكن إلغاؤه باللغتين، وتُسجَّل عمليات الفحص في سجل النشاط عند البدء أو الاكتمال أو الإلغاء.
- **أرقام متناسقة:** أصبحت أرقام الإصدارات والمسارات والأحجام متناسقة مع التصميم المحيط في الرئيسية والإزالة العميقة ومتصفح الملفات ومركز التحديث.
- **تحميل وحفظ سريعان:** يبقى التطبيق سريع الاستجابة أثناء تحميل القوائم وحفظ الإعدادات، ويتجاهل نتائج الفحص القديمة، ويحفظ الإعدادات المعلّقة قبل الإغلاق.

#### إصلاحات

- **استمرار فحص لوحة المعلومات:** يستمر الفحص والتحديث عند تبديل اللغة أو المظهر، وعند العودة إلى اللوحة يظهر الفحص الجاري مع تقدّمه ونتائجه.
- **ترتيب صحيح في العربية:** تظهر الأحجام والنسب وحروف الأقراص والتواريخ والمسارات بالترتيب الصحيح، وتبقى قيم لوحة المعلومات تحت عناوينها وبجانب أيقوناتها في اللغتين.
- **الأيقونات والتكرار:** تُستعاد أيقونات البرامج والألعاب بشكل صحيح، وتُكتشف أيقونات Steam بموثوقية أكبر، وتُدمج العناصر المكررة في قائمة البرامج.
- **ملف إعدادات تالف:** إذا تلف ملف الإعدادات المحفوظ، تُستعاد الإعدادات من نسخة احتياطية سليمة، وتظهر صفحة الخطأ خيار إعادة المحاولة بدلًا من رسالة تقنية.
- **تقارير الأعطال:** تُحفظ تقارير الأعطال على جهازك مع خيار فتح مجلد السجلات أو نسخ التفاصيل. وعند فتح التطبيق مرة أخرى تُعرض النافذة الحالية، وتقل الحركة في جلسات سطح المكتب البعيد.


<details>
<summary>أيضًا في v1.0.145–v1.0.146</summary>

#### الإزالة العميقة

- **بدء أسرع:** أُزيلت رسالة التأكيد الإضافية قبل بدء الإزالة لأنك اخترت البرنامج بنفسك. ولا تزال بقايا الملفات غير محددة مسبقًا، ويبقى التأكيد مطلوبًا للحذف النهائي.
- **مراجعة شاملة للوظائف:** فُحصت مسارات الإزالة العميقة وإزالة الألعاب وأزرارها بالعربية والإنجليزية، مع إصلاح عدة مشاكل في الإزالة العميقة.
- **شعارات لكل برنامج ولعبة:** تُحدد الأيقونات وتُحفظ من أول تشغيل حتى في المرة الأولى، مع حمايات تمنع ظهور أيقونة برنامج آخر (مثلًا لم تعد Steam تظهر بشعار خاطئ).
- **بدون صفوف مكررة:** يُدمج البرنامج أو اللعبة نفسها إذا ظهرت من المشغّل ومن سجل Windows في عنصر واحد، ولم تعد معرّفات المنتجات في المشغّلات تظهر كأنها إصدار.
- **صفوف برامج أكبر** لقراءة أسهل.

#### لوحة المعلومات ومتصفح الملفات

- **زر التحديث (Refresh) يعمل** في لوحة المعلومات.
- **مزامنة الحذف:** عند حذف ملف أو مجلد تتحدث نتائج لوحة المعلومات تلقائيًا.
- **استمرار الفحص:** تغيير اللغة أو المظهر لم يعد يقطع فحص لوحة المعلومات.
- **سجل الرجوع ومسار قابل للنقر** في متصفح الملفات، للرجوع بين المجلدات التي زرتها والقفز إلى أي مجلد أب.

#### الواجهة

- **حركة عند تمرير المؤشر** على أزرار التنقل: تتغير الألوان بسلاسة ويتحرك الزر قليلًا، وتتوافق مع المظهر الحالي في جميع المظاهر الثمانية. وتتعطل عند إيقاف حركات Windows.
- **عربية أفضل:** تحسين صياغة النصوص، وتظهر الأرقام مع الوحدات (مثل `41.6 GB`) والتواريخ بالترتيب الصحيح في الواجهة العربية.

#### الاستقرار

- معالجة عامة للأخطاء بحيث تُسجَّل الأخطاء غير المتوقعة بدل إغلاق البرنامج.
- تُحفظ الإعدادات بطريقة ذرّية مع نسخة احتياطية، فلا يفسدها إغلاق مفاجئ أثناء الحفظ.
- تُلغى عمليات الفحص الخلفية بشكل سليم عند مغادرة الصفحة، وتُتجاهل النتائج القديمة.

</details>

<details>
<summary>أبرز تغييرات الإصدار السابق (v1.0.144)</summary>

#### متصفح الملفات

- **شريط إجراءات يتفاعل مع التحديد:** تظهر الإجراءات أسفل قائمة الملفات عند تحديد عناصر، مع عرض عدد العناصر المحددة. **نقل إلى سلة المحذوفات** هو الإجراء الرئيسي، أما **الحذف النهائي** فبلون أحمر ومنفصل عن بقية الإجراءات، ويتطلب تأكيدًا صريحًا دائمًا.
- **قائمة «…» منظمة:** الخيارات مقسمة إلى مجموعات (العرض، التحديد، الأدوات) مع أيقونات متناسقة واختصارات لوحة المفاتيح بجانب كل خيار، مثل **Ctrl+A** لتحديد الكل و**Ctrl+D / Esc** لإلغاء التحديد.
- **ترتيب أسهل:** عناوين الأعمدة تبدو وتعمل كأزرار، مع تأثير عند التمرير وتلميحات ومؤشر واضح لاتجاه الترتيب. وتوجد قائمة **فرز** مستقلة تبقى متزامنة مع العناوين.
- **تخطيط أنظف:** أعمدة (الاسم، الحجم، النوع، التعديل) متراصة بمسافات متناسقة، وتتطابق العناوين مع الصفوف في العربية والإنجليزية.
- **البحث في الشريط العلوي:** صندوق البحث في المجلد الحالي أصبح في شريط الأدوات مع زر مسح، ويمكن التركيز عليه بالاختصار **Ctrl+F**.
- **تنقل أوضح:** أيقونات التنقل أسهل تمييزًا، وأزرار الرجوع والتقدم والأعلى تتبع الاتجاه الصحيح في العربية (RTL).
- **تحديد أفضل وقائمة زر أيمن:** تحديد الصفوف أسهل، وقائمة الزر الأيمن تتيح الإجراءات الشائعة.
- **لوحة تفاصيل أكثر فائدة:** نسخ مسار العنصر، وحساب حجم المجلد مع مؤشر تقدم وإمكانية الإلغاء.
- **الأحجام والتواريخ بترتيب صحيح في العربية:** تظهر القيم مثل `41.6 GB` والتواريخ بالترتيب الصحيح في الواجهة العربية.
- **إمكانية الوصول:** تباين أفضل للنصوص، ومؤشر تركيز واضح للوحة المفاتيح، وتسميات أفضل لقارئات الشاشة.

#### مكتشف الملفات المكررة

- **صفحة منظمة من جديد:** أدوات الفحص والتقدم وإحصاءات الملخص والفلاتر والبحث في النتائج مرتبة بتسلسل واضح من الأعلى إلى الأسفل.
- **نتائج مجمّعة:** كل مجموعة مكررة بطاقة قابلة للطي تعرض اسم الملف وعدد النسخ وحجم النسخة والمساحة التي يمكن استرجاعها، وتبقى النتائج الكبيرة قابلة للاستخدام.
- **الاحتفاظ بنسخة دائمًا:** كل مجموعة تحتفظ بنسخة محمية لا يمكن تحديدها للحذف، ويمكنك اختيار النسخة التي تريد الاحتفاظ بها.
- **تحديد ذكي:** تحديد النسخ الزائدة في كل المجموعات مع الاحتفاظ بالنسخة الأحدث أو الأقدم أو صاحبة أقصر مسار. ولا يُحدد أي شيء تلقائيًا.
- **تنظيف أكثر أمانًا:** شريط ثابت في الأسفل يعرض عدد الملفات المحددة والمساحة التي ستتحرر. نقل الملفات إلى سلة المحذوفات هو الإجراء الرئيسي، والحذف النهائي منفصل ويتطلب تأكيدًا.
- **فلاتر وترتيب:** التصفية حسب نوع الملف أو الحد الأدنى للحجم، والبحث بالاسم أو المسار، وفرز المجموعات بنفس نمط الفرز في متصفح الملفات. وتُحفظ اختياراتك دون إزعاج سجل النشاط.
- **فحص أكثر سلاسة:** عرض النتائج الجزئية أثناء استمرار الفحص، مع إمكانية الإلغاء وحالات واضحة للصفحة الفارغة وللاستعداد.

#### تحسينات أخرى

- **منصات ألعاب إضافية:** يشمل اكتشاف الألعاب الآن **GOG** و**Riot Games** عند إمكانية اكتشافها.
- **المظاهر:** ثمانية مظاهر متاحة، منها الأزرق والأسود والأبيض والبيج والبيج الغامق / إسبريسو وMidnight Champagne.

</details>

### المميزات الأساسية

- الإزالة العميقة وإزالة الألعاب المدعومة، مع التحقق من النتائج ومراجعة البقايا.
- التنظيف السريع لفئات الملفات المؤقتة المختارة.
- لوحة معلومات تعرض مساحة القرص وأكبر الملفات والمجلدات.
- متصفح ملفات مدمج يدعم أحجام المجلدات التلقائية والترتيب بالنقر على العناوين والتحديد المتعدد وقائمة الزر الأيمن والعمليات الجماعية المؤكدة.
- مكتشف ملفات مكررة يتحقق من المحتوى باستخدام SHA-256، مع نتائج مجمّعة وتحديد ذكي يحتفظ بنسخة واحدة دائمًا.
- ثمانية مظاهر وواجهتان بالعربية والإنجليزية وسجل نشاط.
- فحص مدمج للتحديثات.
- ملف EXE محمول ومثبت Windows مع بصمات SHA-256.

### مميزات البرنامج

#### الإزالة العميقة

يعرض القسم البرامج المثبتة وبعض ألعاب Steam وEpic Games وUbisoft وRockstar وGOG وRiot Games عند إمكانية اكتشافها، مع الاسم والإصدار والناشر والحجم والأيقونة عند توفر البيانات. ويستخدم مع الصفحة الرئيسية مصدرًا موحدًا لعدد البرامج المثبتة بعد دمج السجلات وإزالة التكرار.

- **Deep Uninstall:** إزالة مباشرة للتثبيتات التي يتوفر لها مسار حذف موثوق ومتحقق منه.
- **Games Uninstall:** استخدام طرق إزالة الألعاب المتاحة حسب المنصة؛ وقد تحتاج لإكمال الإزالة داخل مشغّل الألعاب.
- يمكن للطريقتين تشغيل فاحص البقايا نفسه بعد التحقق من إزالة البرنامج.
- تُعرض الملفات والمجلدات والاختصارات ومدخلات Registry المرشحة في نافذة مراجعة، **ولا تُحدد تلقائيًا**.
- يتيح زر **Verify / Rescan** إعادة التحقق والفحص إذا لم تتأكد نتيجة الإزالة.
- تُحدّث قائمة البرامج بعد التحقق من الإزالة، وليس بمجرد إغلاق نافذة المزيل.

**تنبيه:** ليست كل نتيجة في فحص البقايا آمنة للحذف؛ فقد تكون بعض الملفات أو مفاتيح السجل مشتركة مع برامج أخرى. راجع النتائج جيدًا، مع الاحتفاظ بنسخة احتياطية قبل تعديل Registry.

#### التنظيف السريع

يمكن اختيار فئات التنظيف المطلوبة، مثل:

- ملفات المستخدم المؤقتة (`%TEMP%`)
- ملفات Windows المؤقتة (`C:\Windows\Temp`)
- ملفات Prefetch
- سلة المحذوفات

قد تمنع الصلاحيات أو الملفات قيد الاستخدام حذف بعض العناصر. راجع النتائج وسجل النشاط.

#### لوحة التحكم

عرض معلومات الأقراص والمساحة المستخدمة والمتاحة، مع فحص القرص أو مجلد محدد وعرض النتائج في تبويبي **أكبر المجلدات** و**أكبر الملفات**. تظهر المجلدات الفرعية المباشرة للمسار المفحوص فقط، مع أحجامها التراكمية، ويمكن تحديد حد أدنى لحجم الملفات وفتح المجلدات داخل متصفح Polar مباشرة.

يعمل الفحص في الخلفية مع دعم الإلغاء والتخزين المؤقت وتوضيح النتائج الجزئية عند تعذر الوصول لبعض المسارات، ولا يبدأ فحص شامل بمجرد فتح الصفحة.

#### مستعرض الملفات

التنقل بين المجلدات باستخدام شريط مسار قابل للنقر وأزرار الرجوع والتقدم والأعلى والتحديث، مع أعمدة قابلة للترتيب وتفاصيل الملفات ولوحة تفاصيل. تُحسب أحجام المجلدات تلقائيًا في الخلفية وتظهر عند توفرها.

- **الترتيب:** انقر على أي عنوان عمود للترتيب، أو استخدم قائمة **الفرز**. الاثنان متزامنان، ويُحفظ اختيارك بين المجلدات وبعد إعادة التشغيل.
- **التحديد:** انقر على علامة الصف، أو استخدم **Ctrl+Click** أو **Shift+Click** أو **Ctrl+A**. استخدم **Ctrl+D** أو **Esc** لإلغاء التحديد، و**Ctrl+F** للبحث في المجلد الحالي.
- **الإجراءات:** عند تحديد عناصر يظهر شريط إجراءات يتيح **فتح** و**نقل إلى سلة المحذوفات** و**حذف نهائي**. الحذف النهائي منفصل عن بقية الإجراءات ويتطلب تأكيدًا صريحًا، مع حماية للمسارات الحساسة.
- **لوحة التفاصيل:** تعرض معلومات العنصر، وتتيح نسخ المسار أو فتح الموقع، وحساب حجم المجلد مع مؤشر تقدم وإمكانية الإلغاء.

تدعم الواجهة العربية (RTL) والإنجليزية (LTR).

**تنبيه:** راجع العناصر المحددة قبل تنفيذ العمليات الجماعية؛ **الحذف النهائي لا يمكن التراجع عنه**. فضّل **النقل إلى سلة المحذوفات** كلما أمكن.

#### مكتشف الملفات المكررة

يفحص الملفات المرشحة بحسب الحجم ثم يقارن محتواها باستخدام بصمة **SHA-256**. تُعرض النتائج في مجموعات، فتراجع كل مجموعة قبل تحديد النسخ الزائدة.

- تحتفظ كل مجموعة بنسخة واحدة محمية على الأقل، ويمكنك تغيير النسخة المحتفظ بها.
- استخدم **التحديد الذكي** للاحتفاظ بالنسخة الأحدث أو الأقدم أو صاحبة أقصر مسار في كل المجموعات. ولا يُحدد أي شيء تلقائيًا.
- التصفية حسب النوع أو الحد الأدنى للحجم، والبحث بالاسم أو المسار، والفرز حسب المساحة القابلة للاسترداد أو حجم الملف أو عدد النسخ أو الاسم.
- يعرض شريط سفلي عدد الملفات المحددة والمساحة التي ستتحرر. **نقل إلى سلة المحذوفات** هو الإجراء الرئيسي، و**الحذف النهائي** منفصل ويتطلب تأكيدًا.

**تنبيه:** قد تتضمن الملفات المكررة نسخًا احتياطية أو مخرجات بناء أو ملفات تعتمد عليها برامج أخرى. راجع كل مجموعة، مع الاحتفاظ بنسخة مهمة على الأقل.

#### المظاهر واللغات

يدعم البرنامج ثمانية مظاهر، منها **الأزرق، الأسود، الأبيض، البيج، البيج الغامق / إسبريسو، وMidnight Champagne**، مع واجهتين بالعربية والإنجليزية.

#### سجل النشاط وما الجديد والملاحظات

- **سجل النشاط:** لمتابعة تقدم العمليات ونتائجها والتنبيهات.
- **إزالة الكاش:** مسح بيانات الفحص المؤقتة وإعادة تعيين الإحصاءات والسجل المحلي المحفوظ بعد التأكيد.
- **ما الجديد:** لعرض معلومات الإصدارات داخل البرنامج.
- **الملاحظات والبلاغات:** لإرسال نوع البلاغ والرسالة وإصدار البرنامج. تجنب إرسال كلمات المرور أو البيانات الحساسة.

### التحميل والتشغيل

1. افتح [صفحة الإصدارات الرسمية على GitHub](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. اختر أحدث إصدار **C# / WPF** (v1.0.147 أو أحدث عند توفره)؛ فإصدارات Python السابقة مختلفة عن هذا البناء.
3. اختر أحد الملفات المنشورة:
   - **`PolarUninstaller-Setup-v1.0.147.exe`** — مثبت Windows.
   - **`PolarUninstaller.exe`** — ملف تنفيذي محمول ومستقل.
   - **`SHA256SUMS.txt`** — بصمات SHA-256 للتحقق من الملف الذي نزّلته.
4. راجع أي طلب صلاحيات أو تحذير أمني من Windows قبل الموافقة.

تحتوي نسخة WPF المحمولة على مكونات تشغيل .NET المطلوبة، لذلك لا يلزم عادةً تثبيت .NET بشكل منفصل. ويختلف ملف Setup عن النسخة المحمولة. يطلب المثبت والبرنامج صلاحيات المسؤول، لذلك ستظهر نافذة التحكم بحساب المستخدم (UAC) في Windows.

> **حالة النشر:** نجاح بناء النسخة محليًا لا يعني نشرها تلقائيًا على GitHub. تحقق من صفحة Releases لمعرفة الملفات المنشورة بالفعل.

### البناء من المصدر

النسخة الحالية مبنية باستخدام **C# وWPF و.NET** لنظام **Windows 10/11 بنواة 64 بت**.

المتطلبات:

- Windows 10 أو 11 (64 بت)
- .NET 9 SDK (أو الإصدار الذي يتطلبه مشروع WPF)
- Visual Studio مع أدوات تطوير سطح المكتب لـ .NET، أو بيئة مناسبة لأوامر .NET CLI

من داخل مجلد المشروع **`Polar-Uninstaller-WPF`**، شغّل سكربت الإصدار الموجود إذا كان متوفرًا:

```powershell
.\Build-Release.ps1
```

أو استخدم أمر النشر التالي كمثال، بعد مطابقة اسم ملف المشروع وخيارات البناء مع ملفات المستودع:

```powershell
dotnet publish .\PolarUninstallerWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\Build\SingleFile
```

المخرج المستهدف للنسخة المحمولة هو `Build/SingleFile/PolarUninstaller.exe`. إذا اختلف اسم المشروع أو إعدادات الموارد، فاعتمد الملفات الفعلية وسكربت البناء في المستودع.

**أوامر Python وPyInstaller وملفات `.spec` تخص الإصدارات القديمة فقط، ولا تُستخدم لبناء إصدار C#.**

### بنية المشروع

| المسار | الغرض |
|---|---|
| `Polar-Uninstaller-WPF/` | مشروع التطبيق بلغة C# وواجهة WPF |
| `Views/` وملفات `*.xaml` | الصفحات والتصميم، عند وجودها |
| `Services/` | خدمات الإزالة وجرد البرامج وفحص التخزين وحساب أحجام المجلدات وإدارة الملفات وغيرها |
| `Localization/` | نصوص الواجهة بالعربية والإنجليزية |
| `Assets/` | الشعارات والصور وبيانات الإصدارات والموارد |
| `Build/SingleFile/` | ملف EXE المحمول والمستقل |
| `Build/` | ملفات البناء وملفات المثبت |

قد تتغير المجلدات أثناء التطوير؛ ملفات المشروع الفعلية هي المرجع النهائي.

### الأمان والقيود

> **مهم:** راجع الملفات والمجلدات والاختصارات ومفاتيح Registry المحددة قبل الحذف. بعض العمليات لا يمكن التراجع عنها.

- احتفظ بنسخة احتياطية من البيانات المهمة قبل إزالة البرامج أو التنظيف.
- فضّل نقل الملفات إلى سلة المحذوفات بدل الحذف النهائي.
- تشابه اسم الملف أو الناشر لا يعني أن العنصر يخص البرنامج المحذوف وحده.
- تطابق محتوى الملفات لا يعني أن النسخة المكررة غير مطلوبة؛ فقد تكون النسخ الاحتياطية ومخرجات البناء وملفات البرامج مكررة بطبيعتها.
- يجب حماية ملفات النظام والتعريفات وبرامج الحماية ومجلدات الألعاب المشتركة.
- بعض البرامج والألعاب تتطلب إكمال الحذف من أداة الإزالة أو المنصة الرسمية.
- يختلف اكتشاف البقايا والأيقونات والتحقق من الإزالة باختلاف التطبيق والنظام.
- إغلاق نافذة إزالة البرنامج أو فشل الفحص لا يُعد دليلًا على نجاح الإزالة.

### الشكر والتقدير

**[MobV2](https://github.com/MobV2) — المختبر الرئيسي للبرنامج (Lead Beta Tester)**

شكر خاص لـ MobV2 على اختبار البرنامج، والإبلاغ عن المشاكل، وتقديم الاقتراحات التي ساعدت في تحسين تجربة الاستخدام.

### روابط

- [إصدارات GitHub](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
- [المشكلات والبلاغات](https://github.com/Ayh-m1/Polar-Uninstaller/issues)
- [الدعم](https://polarwolves.com/supportus/p1682342912)
- [Discord](https://discord.gg/plr)

**المطور:** Polar  
**الترخيص:** MIT

### سياسة التوقيع الرقمي

يتقدم برنامج Polar Uninstaller بطلب للحصول على خدمة التوقيع الرقمي المجانية من مؤسسة SignPath Foundation. وفي حال الموافقة، سيتم توقيع الإصدارات الرسمية المستقبلية رقميًا باستخدام SignPath.io، بشهادة صادرة باسم SignPath Foundation.

**فريق التوقيع الرقمي**
- المطور والمسؤول عن الصيانة: [Ayh-m1](https://github.com/Ayh-m1)
- مراجع الكود: Ayh-m1
- المسؤول عن الموافقة على التوقيع: Ayh-m1

**سياسة الخصوصية**

لا يجمع Polar Uninstaller بيانات شخصية أو إحصائيات استخدام أو بيانات تتبع عن قصد.

يتصل البرنامج بخوادم GitHub فقط للتحقق من تحديثات البرنامج وتنزيلها. وقد تكشف هذه الاتصالات معلومات الشبكة المعتادة، مثل عنوان IP، لخدمة GitHub.

ولا ينقل البرنامج معلومات المستخدم إلى خدمات خارجية أخرى إلا بناءً على طلب صريح من المستخدم.

**عملية البناء والتوقيع**

تُبنى الإصدارات الرسمية من الكود المصدري المنشور على GitHub باستخدام GitHub Actions. وفي حال الموافقة على الطلب، ستتطلب عمليات التوقيع موافقة يدوية قبل نشر الإصدارات.

التوقيع الرقمي المجاني مقدم من SignPath.io، والشهادة باسم SignPath Foundation.
