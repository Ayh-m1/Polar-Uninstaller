[README.md](https://github.com/user-attachments/files/34175661/README.md)
<div align="center">

<img src="logo.png" width="112" alt="Polar Uninstaller logo">

# Polar Uninstaller

**A bilingual Windows utility for uninstalling applications, cleaning temporary data, inspecting storage, and managing files.**

[![Version](https://img.shields.io/badge/version-1.0.100-blue)](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest)
[![Python](https://img.shields.io/badge/python-3.14-yellow)](https://www.python.org/downloads/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

[English](#english) | [العربية](#العربية)

</div>

---

## English

### Overview

Polar Uninstaller is a desktop application for Windows 10 and 11 (64-bit). It brings five main destinations together in one bilingual interface:

- **Deep Uninstaller** for installed applications and games
- **Quick Cleaner** for selected temporary-file categories and the Recycle Bin
- **Dashboard** for usage and storage information
- **File Browser** for browsing and managing files
- **What's New** for the in-app release history

Version **1.0.99** improves official game removal. The red game action can send Ubisoft Connect's uninstall request by the registered game ID; Steam keeps its uninstall protocol, and registered per-game uninstall commands are used where available. Epic has no documented external uninstall command, so Polar explains the Library steps. Rockstar uses a registered game uninstaller when available, or opens its launcher. After removal, Polar checks the game's registration before allowing leftover cleanup. The regular-app action continues to launch its registered uninstaller and keeps cleanup as a separate reviewed step.

Version **1.0.98** improved navigation and manual resizing. Page switching no longer waits for the previous tab's delayed hide, heavier sections are built when first opened, and the ambient theme stays synchronized during edge resizing while expensive layout work is deferred until release. Main navigation buttons blend with the title bar across themes.

### What's new in v1.0.100

- Reorganized Home with compact cards and a side navigation bar; removed duplicate navigation icons.
- Added a subtle glass-style sidebar shine and adjusted navigation and social-link text/icon sizes.
- Replaced the top search bar with a centered logo and soft glow; placed the application name beside the sidebar logo.
- Refined Home card spacing, sizes and rounded corners.
- Matched title-bar controls and page surfaces to the shared ambient background across Blue, Black and White themes, including Arabic layout.
- Improved File Browser background consistency and slightly enlarged file names.
- Matched Deep Uninstaller surfaces and Quick Cleaner labels to the ambient background, including first-open rendering.
- Reduced redundant navigation updates and reused existing pages; optimized rounded-image rendering and caching.
- Drew Home cards and text on one canvas and coalesced resize layout events; added buffered window rendering to reduce old or duplicated interface fragments.
- Maximize uses the current monitor's native work area directly, preserving taskbar space and repainting after child layout settles. Manual resizing and minimize behavior are retained.
- What's New opens without a nested window-creation event loop and renders the complete bilingual history in one scrollable text surface.
- Introduced a new typography system using Inter for English and Tajawal for Arabic, improving readability and visual consistency across pages, dialogs and themes.
- Refined the White theme with softer Home cards, clearer icons and text, consistent light scrollable surfaces, and refreshed page backgrounds when switching from a dark theme.
- Preserved uninstall commands, cleanup rules, shortcut review, file operations and background scans.

See [the bilingual release notes](RELEASE_NOTES_v1.0.100.md) for GitHub release text and validation details.

### Interface and themes

English text uses bundled **Inter** and Arabic text uses bundled **Tajawal**.
Mixed text and input fields use a renamed combined font with Inter Latin and
Tajawal Arabic glyphs. No system font installation is required. Font licensing,
Windows-native dialog exceptions and verification are documented in
[FONTS.md](FONTS.md). Build using `Polar Uninstaller.spec` to include the fonts.

| Theme | Design |
|---|---|
| **Black** | Deep black surfaces with subtle silver-gray ambient lighting. |
| **Blue** | Deep navy surfaces with soft blue and indigo ambient gradients. |
| **White** | A light theme with improved text contrast and consistent controls. |

The ambient backgrounds are shared across Home, Deep Uninstaller, Quick Cleaner, Dashboard, and File Browser. They are cached and redrawn only when the layout or theme changes, which helps keep resizing, tab switching, and theme switching visually consistent.

The interface is available in **Arabic** and **English**. Language and theme selections are saved between sessions.

### Main sections

#### Deep Uninstaller

Deep Uninstaller lists applications and games discovered from Windows uninstall registry entries and supported launcher libraries. Icons, versions, and installation sizes are shown when available.

- The red **Uninstall Official (for games)** action is enabled for recognized games and uses the existing game/launcher removal flow.
- For regular applications, the second action launches the registered official uninstaller. Finish the removal, then select the app again and press the same action to review and clean leftovers.
- Leftover cleanup checks common installation and user-data locations, related registry entries, and shortcuts. Shortcuts are shown for review before deletion.
- Cleanup is stopped while the app is still registered as installed, to avoid deleting an active installation. The official uninstaller must finish before leftover cleanup.
- Shared launcher or game-container folders are protected from cleanup.
- Leftover results depend on the application, its vendor uninstaller, file permissions, and the Windows installation. Review the Activity Log and the selected items before deleting anything.

#### Quick Cleaner

Quick Cleaner provides selectable categories for:

- User temporary files (`%TEMP%`)
- Windows temporary files (`C:\Windows\Temp`)
- Windows Prefetch
- Recycle Bin

Each category shows an estimated size when available. Select only the categories you want, start the cleanup, and review the result and Activity Log. Files that are in use or protected by permissions may not be removed.

#### Dashboard

The Dashboard presents live usage counters, recent activity and storage information, including:

- Online Now sessions, application opens and total click counters
- Recent Clicks activity with a clear-cache control
- Drive capacity, used space, free space, and usage bars
- Sizes of top-level folders on drive C:
- Largest files found during the storage scan
- Cached results with a manual **Rescan** action

Storage scanning is intentionally manual so it does not run during normal startup. Clicking a folder or file entry opens its location for review.

#### File Browser

File Browser lets you navigate folders, enter a path directly, move back or up, and sort entries by name, size, or type. Folder sizes are calculated in the background.

- **Delete Selected Safely** moves selected files and folders to the Recycle Bin.
- **Permanent Delete** requires two confirmations and cannot be undone.
- Deleting a folder also deletes its contents.
- Review the selection, item count, and total size before confirming.

#### Duplicate Finder

Duplicate Finder is available from File Browser. It scans one or more added folders, or the current folder, in two stages:

1. Files are grouped by size.
2. Candidate files are read and verified with a streaming **SHA-256** content hash.

Only files with matching content hashes are presented as duplicates. You can select extra copies while keeping at least one copy in each group. Duplicate cleanup moves the selected extras to the Recycle Bin and does not fall back to permanent deletion.

#### What's New

What's New is a dedicated bilingual window that shows the current version and the release history. Open it from the sidebar or the fifth card on Home.

#### Feedback & Reports

Feedback & Reports is available from Home. Choose a category, write a message of up to 2,000 characters, and send it directly to the Polar team. The window validates the report, prevents duplicate submissions while a request is in progress, and preserves the message if sending fails. The message field automatically aligns Arabic text to the right and English text to the left. Only the category, message, and app version are sent; device identifiers, file paths, and installed-application data are not collected.

### Download and run

1. Open the [latest GitHub Release](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest).
2. In the release assets, download `Polar Uninstaller.exe`.
3. Run the EXE. Windows will request administrator rights because cleanup and storage operations may access protected locations.
4. Accept the UAC prompt only if you trust the file and its source.

The application is distributed as a single EXE and does not require a separate installer. Use only the official repository Releases page rather than third-party download mirrors.

If Windows displays a security prompt, verify the file source and review the prompt before continuing.

### Quick start

1. Open Home and choose a main section.
2. In **Deep Uninstaller**, select a game and use the red game action. For a regular application, use the second action to launch its official uninstaller, finish it, then press the second action again to clean leftovers.
3. In **Quick Cleaner**, select the temporary-data categories and review the result.
4. In **Dashboard**, select **Rescan** when you want fresh storage results.
5. In **File Browser**, review files before moving them to the Recycle Bin or deleting them permanently.
6. Open **What's New** to read the release history.

### Safety and limitations

> **Important:** Review selected files, duplicate groups, leftover candidates, and cleanup results before confirming deletion. File and registry cleanup can be irreversible, especially **Permanent Delete**.

- Back up important files before cleanup.
- Registry entries and application-data folders may belong to other software or shared components.
- Cleanup results depend on permissions, the vendor uninstaller, and the Windows installation.
- Polar Uninstaller does not guarantee complete removal of every file or registry entry, and detection results should not be treated as guaranteed safe to delete.
- Use the application at your own risk.

### Build from source

Requirements:

- Windows 10 or 11, 64-bit
- Python 3.14, 64-bit
- `customtkinter`, `Pillow`, and `psutil`
- PyInstaller for building the EXE

Install the dependencies and run the application:

```powershell
py -m pip install --upgrade customtkinter pillow psutil pyinstaller
py app.py
```

Build the single-file Windows executable:

```powershell
py -m PyInstaller --clean "Polar Uninstaller.spec"
```

The output is `dist\Polar Uninstaller.exe`. The embedded manifest requests administrator rights and the executable includes file and product version metadata.

Runtime settings, caches, and activity data are stored under:

```text
%APPDATA%\PolarUninstaller
```

### How it works

- **Application discovery:** reads Windows uninstall registry entries, including WOW64 entries, and merges supported game libraries.
- **Leftover search:** matches application and publisher names in common installation and user-data locations, then checks related shortcuts and registry entries.
- **Shortcut cleanup:** checks desktop, Start Menu, and pinned taskbar shortcuts using name and target-path matching.
- **Storage analysis:** calculates drive usage and folder sizes in the background. Heavy storage scans are manual and cached.
- **Duplicate detection:** groups candidates by size, then verifies their contents with SHA-256.
- **Responsiveness:** long-running work is handled in background threads, while lists use reusable canvas rows for large collections.

### Project structure

| Path | Purpose |
|---|---|
| `app.py` | Main Polar Uninstaller desktop application. |
| `dashboard.py` | Separate local dashboard viewer. |
| `icon.ico`, `logo.png`, `AppLogo.png` | Application icon and logo assets. |
| `screenshots/` | Expected location for the documentation screenshots listed above. |
| `dist/` | Local build output; ignored by Git and distributed through Releases. |

### Links

- [Latest release](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest)
- [Issues](https://github.com/Ayh-m1/Polar-Uninstaller/issues)
- [Support](https://polarwolves.com/supportus/p1682342912)
- [Discord](https://discord.gg/plr)

**Author:** Ayhm

---

## العربية

### نظرة عامة

Polar Uninstaller تطبيق سطح مكتب لنظام Windows 10 و11 (64 بت)، يجمع خمس وجهات رئيسية في واجهة واحدة باللغتين العربية والإنجليزية:

- **إزالة التطبيقات والألعاب** لإلغاء تثبيت البرامج والألعاب
- **التنظيف السريع** لتنظيف فئات الملفات المؤقتة المحددة وسلة المحذوفات
- **لوحة التحكم** لعرض معلومات الاستخدام ومساحة التخزين
- **مستعرض الملفات** لاستعراض الملفات وإدارتها
- **ما الجديد** لسجل التحديثات داخل التطبيق

يضيف الإصدار **1.0.99** تحسينات للإزالة الرسمية للألعاب. يرسل زر الألعاب الأحمر طلب الإزالة إلى Ubisoft Connect باستخدام معرّف اللعبة، وتستمر ألعاب Steam باستخدام رابطها الرسمي. تستخدم أوامر الإزالة المسجّلة للعبة عند توفرها. لا توثّق Epic أمر إزالة خارجيًا، لذلك يعرض Polar خطوات الإزالة من المكتبة؛ ويستخدم مزيل Rockstar المسجّل إن توفر، وإلا يفتح مشغّل Rockstar. بعد الإزالة، يتحقق Polar من اختفاء تسجيل اللعبة قبل السماح بتنظيف البقايا. ويظل زر البرامج يشغّل مزيل البرنامج المسجّل ثم يتيح مراجعة منفصلة للبقايا.

حسّن الإصدار **1.0.98** التنقل وتغيير حجم النافذة يدويًا؛ تجد تفاصيله في سجل «ما الجديد» داخل البرنامج.

### ما الجديد في v1.0.100

- إعادة ترتيب الرئيسية ببطاقات أصغر وشريط تنقل جانبي وإزالة الأيقونات المكررة.
- إضافة لمعة زجاجية خفيفة للشريط الجانبي وضبط أحجام خطوط وأيقونات التنقل والدسكورد والدعم والاقتراحات.
- استبدال البحث العلوي بشعار في الوسط بلمعة طفيفة ووضع اسم البرنامج بجانب الشعار الجانبي.
- ضبط مسافات البطاقات وأحجامها وحوافها المستديرة.
- توحيد خلفيات شريط العنوان والصفحات مع التدرج العام في الثيم الأزرق والأسود والأبيض، بما فيها الواجهة العربية.
- تحسين خلفية متصفح الملفات وتكبير أسماء الملفات قليلاً.
- توحيد خلفيات إزالة البرامج ونصوص التنظيف السريع، بما يشمل العرض الأول للصفحة.
- تقليل تحديثات التنقل المكررة وإعادة استخدام الصفحات وتحسين رسم الصور المستديرة وتخزينها المؤقت.
- جمع بطاقات الرئيسية ونصوصها في لوحة رسم واحدة ودمج أحداث تغيير الحجم وتفعيل تجميع الرسم لتقليل بقايا الرسم القديم وتكرار أجزاء الواجهة.
- استخدام مساحة عمل الشاشة الحالية مباشرةً عند التكبير، مع مراعاة شريط المهام وإعادة الرسم بعد اكتمال التخطيط وإبقاء التحكم اليدوي والتصغير كما هما.
- تسريع فتح «ما الجديد» بإلغاء حلقة التحديث المتداخلة وعرض سجل الإصدارات كاملاً في مربع نص واحد قابل للتمرير.
- إضافة نظام خطوط جديد باستخدام Inter للإنجليزية وTajawal للعربية، لتحسين وضوح النصوص وتناسقها في الصفحات والنوافذ والثيمات.
- تحسين الثيم الأبيض ببطاقات أكثر نعومة في الرئيسية وأيقونات ونصوص أوضح وخلفيات فاتحة متناسقة للمساحات القابلة للتمرير، مع تحديث خلفيات الصفحات عند الانتقال من ثيم داكن.
- الحفاظ على أوامر الإزالة وقواعد التنظيف ومراجعة الاختصارات وعمليات الملفات والفحص في الخلفية.

نص الإصدار وتفاصيل التحقق متاحة في [ملاحظات الإصدار](RELEASE_NOTES_v1.0.100.md).

### الواجهة والثيمات

تستخدم النصوص الإنجليزية خط **Inter** والعربية خط **Tajawal** المضمّنين.
تستخدم النصوص المختلطة وحقول الإدخال خطًا مشتقًا يجمع حروف Inter اللاتينية
وحروف Tajawal العربية، دون تثبيت عام في Windows. راجع [توثيق الخطوط](FONTS.md)
للتراخيص واستثناءات حوارات النظام والاختبارات، وابنِ EXE باستخدام ملف spec.

| الثيم | التصميم |
|---|---|
| **Black** | خلفية سوداء عميقة مع إضاءة محيطة رمادية وفضية خافتة. |
| **Blue** | خلفية كحلية داكنة مع تدرجات زرقاء ونيلية ناعمة. |
| **White** | ثيم فاتح مع تحسين تباين النصوص وتناسق عناصر التحكم. |

تُستخدم الخلفيات المحيطة نفسها في الرئيسية وأقسام إزالة التطبيقات والألعاب، والتنظيف السريع، ولوحة التحكم، ومستعرض الملفات. تُخزن هذه الخلفيات مؤقتًا ولا يُعاد رسمها إلا عند تغير التخطيط أو الثيم، مما يحافظ على تناسق الواجهة عند تغيير الحجم أو التبويب أو الثيم.

الواجهة متاحة باللغتين **العربية** و**الإنجليزية**، ويتم حفظ اختيار اللغة والثيم بين جلسات التشغيل.

### الأقسام الرئيسية

#### إزالة التطبيقات والألعاب

يعرض هذا القسم التطبيقات والألعاب المكتشفة من مفاتيح إلغاء التثبيت في سجل Windows ومن مكتبات المنصات المدعومة. تظهر الأيقونات والإصدارات وأحجام التثبيت عند توفرها.

- زر **Uninstall Official (for games)** الأحمر مخصص للألعاب المعروفة ويستخدم مسار الإزالة الخاص باللعبة أو منصتها.
- للبرامج العادية، يشغّل الزر الثاني مزيل البرنامج الرسمي. أكمل الإزالة، ثم حدد البرنامج مجدداً واضغط الزر نفسه لمراجعة البقايا وتنظيفها.
- يفحص التنظيف مواقع التثبيت وبيانات المستخدم ومدخلات السجل والاختصارات المرتبطة. تظهر الاختصارات للمراجعة قبل حذفها.
- يتوقف التنظيف ما دام البرنامج مسجلاً كتثبيت قائم، لحماية ملفات برنامج ما زال مثبتاً. أكمل الإزالة الرسمية قبل تنظيف البقايا.
- تُحمى مجلدات منصات الألعاب أو الحاويات المشتركة من التنظيف.
- تعتمد نتائج البقايا على التطبيق وبرنامج إلغاء التثبيت الخاص به وصلاحيات الملفات وإعدادات Windows. راجع سجل النشاط والعناصر المحددة قبل حذف أي شيء.

#### التنظيف السريع

يوفر التنظيف السريع خيارات قابلة للتحديد لكل من:

- الملفات المؤقتة للمستخدم (`%TEMP%`)
- الملفات المؤقتة في Windows (`C:\Windows\Temp`)
- ملفات Windows Prefetch
- سلة المحذوفات

يظهر حجم تقديري لكل فئة عند توفره. حدد الفئات المطلوبة فقط، ثم ابدأ التنظيف وراجع النتيجة وسجل النشاط. قد لا تُحذف الملفات قيد الاستخدام أو المحمية بصلاحيات النظام.

#### لوحة التحكم

تعرض لوحة التحكم عدادات الاستخدام المباشرة والنشاط الأخير ومعلومات التخزين، ومنها:

- عدد الجلسات المتصلة حاليًا، ومرات فتح التطبيق، وإجمالي النقرات
- سجل النقرات الأخيرة مع خيار مسح البيانات المؤقتة
- سعة الأقراص والمساحة المستخدمة والمتاحة وأشرطة الاستخدام
- أحجام المجلدات الرئيسية على القرص C:
- أكبر الملفات المكتشفة أثناء فحص التخزين
- نتائج مخزنة مؤقتًا مع زر **Rescan** لإعادة الفحص يدويًا

يتم تشغيل فحص التخزين يدويًا حتى لا يستهلك موارد النظام عند بدء التشغيل العادي. يؤدي الضغط على مجلد أو ملف إلى فتح موقعه لمراجعته.

#### مستعرض الملفات

يتيح مستعرض الملفات التنقل بين المجلدات، وإدخال المسار مباشرة، والرجوع أو الصعود، وترتيب العناصر حسب الاسم أو الحجم أو النوع. تُحسب أحجام المجلدات في الخلفية.

- **Delete Selected Safely** ينقل الملفات والمجلدات المحددة إلى سلة المحذوفات.
- **Permanent Delete** يتطلب تأكيدين ولا يمكن التراجع عنه.
- حذف مجلد يحذف محتوياته أيضًا.
- راجع التحديد وعدد العناصر والحجم الإجمالي قبل التأكيد.

#### مكتشف الملفات المكررة

يتوفر مكتشف الملفات المكررة من داخل مستعرض الملفات. يفحص مجلدًا واحدًا أو أكثر بعد إضافته، أو المجلد الحالي، على مرحلتين:

1. تجميع الملفات حسب الحجم.
2. قراءة الملفات المرشحة والتحقق من محتواها باستخدام بصمة **SHA-256** متدفقة.

لا تظهر الملفات كمكررة إلا عندما تتطابق بصمات المحتوى. يمكنك تحديد النسخ الزائدة مع الإبقاء على نسخة واحدة على الأقل في كل مجموعة. ينقل تنظيف الملفات المكررة النسخ المحددة إلى سلة المحذوفات، ولا يلجأ إلى الحذف النهائي.

#### ما الجديد

نافذة مستقلة ثنائية اللغة تعرض الإصدار الحالي وسجل الإصدارات السابقة، ويتم فتحها من البطاقة الخامسة في الرئيسية.

#### الملاحظات والبلاغات

نافذة الملاحظات والبلاغات متاحة من الرئيسية. اختر الفئة واكتب رسالة لا تتجاوز 2000 حرف وأرسلها مباشرة إلى فريق Polar. تتحقق النافذة من البلاغ وتمنع الإرسال المتكرر أثناء معالجة الطلب وتحتفظ بالرسالة إذا تعذر الإرسال. يحاذي حقل الرسالة النص العربي تلقائياً إلى اليمين والنص الإنجليزي إلى اليسار. يتم إرسال الفئة والرسالة وإصدار التطبيق فقط؛ لا نجمع معرّفات الجهاز أو مسارات الملفات أو بيانات التطبيقات المثبتة.

### التحميل والتشغيل

1. افتح [أحدث إصدار على GitHub](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest).
2. من ملفات الإصدار، نزّل `Polar Uninstaller.exe`.
3. شغّل الملف. سيطلب Windows صلاحيات المسؤول لأن التنظيف وعمليات التخزين قد تصل إلى مواقع محمية.
4. اقبل طلب UAC فقط إذا كنت تثق بالملف ومصدره.

يُوزع التطبيق كملف EXE واحد ولا يحتاج إلى مثبّت منفصل. استخدم صفحة Releases الرسمية في المستودع فقط، وتجنب مواقع التحميل الخارجية.

إذا ظهرت رسالة أمان من Windows، فتحقق من مصدر الملف وراجع الرسالة قبل المتابعة.

### الاستخدام السريع

1. افتح الرئيسية واختر القسم المطلوب.
2. في **إزالة التطبيقات والألعاب**، استخدم الزر الأحمر للعبة. وللبرنامج العادي، استخدم الزر الثاني لتشغيل مزيله الرسمي، ثم اضغطه مجدداً بعد انتهاء الإزالة لتنظيف البقايا.
3. في **التنظيف السريع**، حدد فئات الملفات المؤقتة وراجع النتيجة.
4. في **لوحة التحكم**، اضغط **Rescan** عندما تحتاج إلى نتائج تخزين حديثة.
5. في **مستعرض الملفات**، راجع الملفات قبل نقلها إلى سلة المحذوفات أو حذفها نهائيًا.
6. افتح **ما الجديد** لقراءة سجل التحديثات.

### الأمان والحدود

> **مهم:** راجع الملفات المحددة ومجموعات الملفات المكررة وعناصر البقايا ونتائج التنظيف قبل تأكيد الحذف. قد يكون حذف الملفات أو تعديل السجل غير قابل للتراجع، خاصة عند استخدام **Permanent Delete**.

- احتفظ بنسخة احتياطية من الملفات المهمة قبل التنظيف.
- قد تنتمي مدخلات السجل ومجلدات بيانات التطبيقات إلى برامج أخرى أو مكونات مشتركة.
- تعتمد نتائج التنظيف على الصلاحيات وبرنامج إلغاء التثبيت وإعدادات Windows.
- لا يضمن Polar Uninstaller حذف كل ملف أو مدخل سجل، ولا ينبغي اعتبار العناصر المكتشفة آمنة للحذف بشكل مؤكد.
- استخدم التطبيق على مسؤوليتك.

### البناء من المصدر

المتطلبات:

- Windows 10 أو 11، إصدار 64 بت
- Python 3.14، إصدار 64 بت
- الحزم `customtkinter` و`Pillow` و`psutil`
- PyInstaller لبناء ملف EXE

ثبّت المتطلبات ثم شغّل التطبيق:

```powershell
py -m pip install --upgrade customtkinter pillow psutil pyinstaller
py app.py
```

لبناء ملف Windows التنفيذي:

```powershell
py -m PyInstaller --clean "Polar Uninstaller.spec"
```

تكون النتيجة في `dist\Polar Uninstaller.exe`، ويتضمن الملف المضمّن طلب صلاحيات المسؤول وبيانات إصدار الملف والمنتج.

تُحفظ إعدادات التشغيل والملفات المؤقتة وبيانات النشاط في:

```text
%APPDATA%\PolarUninstaller
```

### آلية العمل

- **اكتشاف التطبيقات:** قراءة مفاتيح إلغاء التثبيت في سجل Windows، بما فيها مفاتيح WOW64، ودمج مكتبات الألعاب المدعومة.
- **البحث عن البقايا:** مطابقة أسماء التطبيقات والناشرين في مواقع التثبيت وبيانات المستخدم الشائعة، ثم فحص الاختصارات ومدخلات السجل المرتبطة.
- **تنظيف الاختصارات:** فحص اختصارات سطح المكتب وقائمة ابدأ وشريط المهام المثبتة باستخدام الاسم ومسار الهدف.
- **تحليل التخزين:** حساب استخدام الأقراص وأحجام المجلدات في الخلفية. عمليات الفحص الثقيلة يدوية وتُخزن نتائجها مؤقتًا.
- **كشف التكرار:** تجميع المرشحين حسب الحجم ثم التحقق من المحتوى باستخدام SHA-256.
- **الاستجابة:** تنفيذ العمليات الطويلة في خيوط خلفية، واستخدام صفوف Canvas قابلة لإعادة الاستخدام لعرض القوائم الكبيرة.

### بنية المشروع

| المسار | الغرض |
|---|---|
| `app.py` | تطبيق Polar Uninstaller الرئيسي. |
| `dashboard.py` | عارض محلي مستقل للوحة التحكم. |
| `icon.ico`, `logo.png`, `AppLogo.png` | أيقونة التطبيق وشعاراته. |
| `dist/` | مخرجات البناء المحلية؛ مستبعدة من Git وتُوزع عبر Releases. |

### روابط

- [أحدث إصدار](https://github.com/Ayh-m1/Polar-Uninstaller/releases/latest)
- [المشكلات](https://github.com/Ayh-m1/Polar-Uninstaller/issues)
- [الدعم](https://polarwolves.com/supportus/p1682342912)
- [Discord](https://discord.gg/plr)

**المطور:** Ayhm
