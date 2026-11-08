[README_Polar_Uninstaller_v1.0.125.md](https://github.com/user-attachments/files/34551134/README_Polar_Uninstaller_v1.0.125.md)
<div align="center">

<img src="Polar_Logo_Beige_512.png" width="112" alt="Polar Uninstaller logo">

# Polar Uninstaller

**A modern, bilingual Windows utility for uninstalling apps and games, cleaning temporary files, analyzing storage, and managing files.**

**Built with C# • .NET • WPF**

[![Version](https://img.shields.io/badge/version-1.0.125-00B8F0)](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
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

**Polar Uninstaller v1.0.125** builds on the native **C# / .NET / WPF** edition introduced in v1.0.120. It improves storage analysis, file browsing, bulk file management, uninstall inventory accuracy, and reliability while retaining the responsive Windows interface and consistent themes.

The interface supports **English and Arabic**, with right-to-left layouts where appropriate.

### What's new in v1.0.125

- **Accurate storage analysis:** the Dashboard separates **Largest Folders** and **Largest Files** into dedicated tabs. Folder results show the immediate children of the scanned location, with cumulative sizes and no repeated nested-folder entries.
- **Direct Dashboard-to-File-Browser navigation:** open a storage-result folder inside Polar's integrated File Browser to inspect its contents.
- **Automatic folder sizes:** calculate cumulative folder sizes in the background, update results as sizes become available, and indicate incomplete or inaccessible scans instead of presenting misleading zero-byte results.
- **File Browser multi-selection:** select multiple files and folders using the selection markers, **Ctrl+Click**, **Shift+Click**, and **Ctrl+A**. Selection click targets are larger without changing the visible marker size.
- **Batch file operations:** move selected items to the Recycle Bin or permanently delete them after explicit confirmation, with protections for sensitive locations.
- **Persistent sorting:** remember the selected sort field and direction when navigating between folders and after restarting the application; keep sorting accurate as background size calculations finish.
- **Improved scan responsiveness:** use asynchronous processing, caching, cancellation, and safeguards against stale results and repeated scanning.
- **More reliable uninstall history:** reduce false external-removal reports caused by incomplete inventory scans or transient Windows entries.
- **Consistent installed-app counts:** Home and Deep Uninstaller use the same merged, deduplicated inventory source, including supported game launchers, and exclude Polar's own entries.
- **Clear Cache improvements:** clear stored scan results and temporary data while resetting locally stored dashboard statistics and removal history.
- **Bilingual release information:** updated What's New content in English and Arabic, with RTL/LTR interface support preserved.

### Core features

- **Deep, Official, and supported game uninstall workflows**, with verification and optional leftover review.
- **Quick Cleaner** for selected temporary-file and cleanup categories.
- **Dashboard** with drive capacity and large-folder/file analysis.
- **Integrated File Browser** with navigation, folder-size calculation, sorting, multi-selection, and confirmed batch operations.
- **Duplicate Finder** with SHA-256 content verification.
- **Five themes**, English/Arabic localization, and an Activity Log.
- **Portable EXE, portable ZIP, and Windows Setup installer** build options.

### Features

#### Deep Uninstaller

Discover installed Windows applications and supported game-library entries from Steam, Epic Games, Ubisoft, and Rockstar when detectable. The page displays application names, versions, publishers, sizes, and icons when that information is available. Home and Deep Uninstaller share the same merged inventory and installed-program count.

- **Deep Uninstall** is available for installations that support a verified direct-removal workflow.
- **Official Uninstall** uses a valid registered uninstall command when direct removal is inappropriate.
- **Games Uninstall** uses supported launcher or game-specific removal methods when available. Some launchers require you to finish removal in their own interface.
- Both Deep and Official workflows can use the **same leftover scanner and review window** after removal is verified.
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

Browse directories with an address/path bar, Back/Forward/Up/Refresh navigation, sortable columns, file metadata, and a details panel. Folder sizes are calculated automatically in the background and displayed as they become available. Sorting by size updates as results arrive, and sort preferences persist across navigation and application restarts.

Select multiple files and folders using the selection markers, **Ctrl+Click**, **Shift+Click**, or **Ctrl+A**. The markers retain their original appearance while offering a larger click target. Move selected items to the Recycle Bin or permanently delete them with explicit confirmation and safeguards for protected paths. The interface supports Arabic RTL and English LTR.

**Important:** Review every selected path before a batch operation. **Permanent deletion cannot be undone.**

#### Duplicate Finder

Find potential duplicate files by comparing sizes and verifying matching candidates with **SHA-256** hashes. Review each group before selecting extra copies for cleanup; retain at least one copy of important files.

#### Themes and languages

Polar Uninstaller supports **Blue, Black, White, Beige, and Dark Beige / Espresso** themes, with English and Arabic interfaces. Theme and language preferences are designed to remain consistent across the application.

#### Activity Log, What's New, and Feedback

- **Activity Log:** shows progress, outcomes, and relevant warnings.
- **Clear Cache:** resets temporary scan data and stored dashboard statistics/history; review the action before confirming.
- **What's New:** displays release information within the application.
- **Feedback & Reports:** lets users submit a category, message, and app version. Do not include passwords or other sensitive information in reports.

### Download and run

1. Visit the [official Releases page](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. Choose the latest **C# / WPF** release (v1.0.125 or newer when available). Older Python releases use a different application build.
3. Choose one of the published assets:
   - **`PolarUninstaller-Setup-v1.0.125.exe`** — Windows Setup installer.
   - **`PolarUninstaller-v1.0.125-portable.zip`** — portable archive; extract it and run `PolarUninstaller.exe`.
   - **`PolarUninstaller.exe`** — standalone single-file executable, when provided as a release asset.
4. Review any Windows security or administrator-permission prompt before approving it.

The WPF portable builds are packaged with their required .NET runtime components, so a separate .NET runtime installation is not normally needed. The Setup installer is a different distribution format from the portable EXE and ZIP.

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
dotnet publish .\PolarUninstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\Build\SingleFile
```

The intended portable output is `Build/SingleFile/PolarUninstaller.exe`. Keep the actual project file name, resource settings, and release-script requirements as the source of truth if they differ from the example command.

**The old Python commands (`app.py`, PyInstaller, `.spec`) apply only to legacy releases and are not used to build the C# edition.**

### Project structure

| Path | Purpose |
|---|---|
| `Polar-Uninstaller-WPF/` | C# / WPF application project |
| `Views/` and WPF `*.xaml` files | Application pages and visual layout, where present |
| `Services/` | Uninstall, shared inventory, storage scanning, folder-size calculation, file management, and other application services |
| `Assets/` | Branding, images, release notes, and other bundled resources |
| `Build/SingleFile/` | Self-contained portable EXE output |
| `Build/` | Release artifacts, portable ZIP, and installer packaging output |

Actual folders may differ as the project evolves. Use the project files in the repository for the definitive layout.

### Safety and limitations

> **Always review selected files, folders, shortcuts, and Registry entries before deletion. Some changes are irreversible.**

- Back up important data before uninstalling software or cleaning files.
- An application's name or publisher alone is not sufficient proof that a folder or Registry entry is safe to delete.
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

**Developer:** Ayhm  
**License:** MIT

---

## العربية

### نظرة عامة

**Polar Uninstaller v1.0.125** هو تحديث للنسخة الأصلية المبنية باستخدام **C# وWPF و.NET** لنظام Windows، والتي بدأت مع الإصدار v1.0.120 بدل واجهة Python وCustomTkinter السابقة.

يركز الإصدار الجديد على **سلاسة تغيير حجم النافذة، وسرعة التنقل، وتناسق المظاهر، وتحسين إدارة الملفات وإزالة البرامج**. يدعم البرنامج **العربية والإنجليزية** مع مراعاة اتجاه الواجهة.

### أبرز تغييرات v1.0.125

- **تحليل تخزين أدق:** فصل **أكبر المجلدات** و**أكبر الملفات** في تبويبين مستقلين. تعرض نتائج المجلدات العناصر الفرعية المباشرة للمسار المفحوص بأحجامها التراكمية، دون تكرار المجلدات المتداخلة.
- **الانتقال من لوحة المعلومات:** فتح المجلدات الظاهرة في نتائج الفحص مباشرة داخل متصفح ملفات Polar.
- **حساب أحجام المجلدات تلقائيًا:** حساب الأحجام في الخلفية وتحديث النتائج عند اكتمالها، مع توضيح النتائج الجزئية والمجلدات التي يتعذر الوصول إليها.
- **التحديد المتعدد:** اختيار عدة ملفات ومجلدات باستخدام علامة التحديد أو **Ctrl+Click** أو **Shift+Click** أو **Ctrl+A**، مع توسيع مساحة الضغط دون تكبير العلامة المرئية.
- **عمليات الملفات الجماعية:** نقل العناصر المحددة إلى سلة المحذوفات أو حذفها نهائيًا بعد التأكيد، مع إجراءات حماية للمسارات الحساسة.
- **حفظ الترتيب:** الاحتفاظ بطريقة الترتيب واتجاهه عند الانتقال بين المجلدات وبعد إعادة تشغيل البرنامج، وتحديث الترتيب عند اكتمال حساب الأحجام.
- **فحص أكثر سلاسة:** معالجة في الخلفية وتخزين مؤقت وإلغاء للعمليات القديمة، مع تقليل الفحص المكرر.
- **تحسين سجل الإزالة:** تقليل الاكتشافات الخاطئة لعمليات الإزالة الخارجية الناتجة عن فحوص غير مكتملة أو إدخالات Windows المؤقتة.
- **توحيد عدد البرامج:** الصفحة الرئيسية والإزالة العميقة تستخدمان مصدرًا واحدًا للبرامج والألعاب المدعومة، بعد دمج النتائج وإزالة التكرار واستبعاد إدخالات Polar.
- **تحسين إزالة الكاش:** مسح بيانات الفحص المؤقتة والنتائج المحفوظة وإعادة تعيين إحصاءات لوحة المعلومات وسجل الإزالة المحلي.
- **تحديث صفحة ما الجديد:** محتوى جديد بالعربية والإنجليزية مع الحفاظ على دعم RTL/LTR.

### المميزات الأساسية

- الإزالة العميقة والرسمية وإزالة الألعاب المدعومة، مع التحقق من النتائج ومراجعة البقايا.
- التنظيف السريع لفئات الملفات المؤقتة المختارة.
- لوحة معلومات تعرض مساحة القرص وأكبر الملفات والمجلدات.
- متصفح ملفات مدمج يدعم أحجام المجلدات التلقائية والتحديد المتعدد والعمليات الجماعية.
- مكتشف ملفات مكررة يتحقق من المحتوى باستخدام SHA-256.
- خمسة مظاهر وواجهتان بالعربية والإنجليزية وسجل نشاط.
- خيارات إصدار تشمل EXE مستقلًا وZIP محمولًا ومثبت Windows.

### مميزات البرنامج

#### الإزالة العميقة والرسمية

يعرض القسم البرامج المثبتة وبعض ألعاب Steam وEpic Games وUbisoft وRockstar عند إمكانية اكتشافها، مع الاسم والإصدار والناشر والحجم والأيقونة عند توفر البيانات. ويستخدم مع الصفحة الرئيسية مصدرًا موحدًا لعدد البرامج المثبتة بعد دمج السجلات وإزالة التكرار.

- **Deep Uninstall:** إزالة مباشرة للتثبيتات التي يتوفر لها مسار حذف موثوق ومتحقق منه.
- **Official Uninstall:** استخدام أمر الإزالة الرسمي المسجّل للبرامج التي لا يناسبها الحذف المباشر.
- **Games Uninstall:** استخدام طرق إزالة الألعاب المتاحة حسب المنصة؛ وقد تحتاج لإكمال الإزالة داخل مشغّل الألعاب.
- يمكن للطريقتين **العميقة والرسمية** تشغيل فاحص البقايا نفسه بعد التحقق من إزالة البرنامج.
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

التنقل بين المجلدات باستخدام شريط المسار وأزرار الرجوع والتقدم والأعلى والتحديث، مع أعمدة قابلة للترتيب وتفاصيل الملفات والأيقونات. تُحسب أحجام المجلدات تلقائيًا في الخلفية، ويُحدّث ترتيب الحجم عند وصول النتائج، مع حفظ تفضيلات الترتيب بين المجلدات وبعد إعادة التشغيل.

يدعم تحديد عدة ملفات ومجلدات باستخدام علامات التحديد أو **Ctrl+Click** أو **Shift+Click** أو **Ctrl+A**، مع مساحة ضغط أكبر للعلامة دون تغيير شكلها. يمكن نقل العناصر المحددة إلى سلة المحذوفات أو حذفها نهائيًا بعد تأكيد واضح وحماية المسارات الحساسة، مع دعم العربية والإنجليزية.

**تنبيه:** راجع العناصر المحددة قبل تنفيذ العمليات الجماعية؛ **الحذف النهائي لا يمكن التراجع عنه**.

#### مكتشف الملفات المكررة

يفحص الملفات المرشحة بحسب الحجم ثم يقارن محتواها باستخدام بصمة **SHA-256**. راجع مجموعات التكرار وحدد النسخ الزائدة فقط مع الاحتفاظ بنسخة مهمة على الأقل.

#### المظاهر واللغات

يدعم البرنامج خمسة مظاهر: **الأزرق، الأسود، الأبيض، البيج، والبيج الغامق / إسبريسو**، مع واجهتين بالعربية والإنجليزية.

#### سجل النشاط وما الجديد والملاحظات

- **سجل النشاط:** لمتابعة تقدم العمليات ونتائجها والتنبيهات.
- **إزالة الكاش:** مسح بيانات الفحص المؤقتة وإعادة تعيين الإحصاءات والسجل المحلي المحفوظ بعد التأكيد.
- **ما الجديد:** لعرض معلومات الإصدارات داخل البرنامج.
- **الملاحظات والبلاغات:** لإرسال نوع البلاغ والرسالة وإصدار البرنامج. تجنب إرسال كلمات المرور أو البيانات الحساسة.

### التحميل والتشغيل

1. افتح [صفحة الإصدارات الرسمية على GitHub](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. اختر أحدث إصدار **C# / WPF** (v1.0.125 أو أحدث عند توفره)؛ فإصدارات Python السابقة مختلفة عن هذا البناء.
3. اختر أحد الملفات المنشورة:
   - **`PolarUninstaller-Setup-v1.0.125.exe`** — مثبت Windows.
   - **`PolarUninstaller-v1.0.125-portable.zip`** — نسخة محمولة؛ فك الضغط ثم شغّل `PolarUninstaller.exe`.
   - **`PolarUninstaller.exe`** — ملف تنفيذي مستقل إذا كان مرفقًا بالإصدار.
4. راجع أي طلب صلاحيات أو تحذير أمني من Windows قبل الموافقة.

تحتوي حزم WPF المحمولة على مكونات تشغيل .NET المطلوبة، لذلك لا يلزم عادةً تثبيت .NET بشكل منفصل. ويختلف ملف Setup عن الحزمة المحمولة.

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
dotnet publish .\PolarUninstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\Build\SingleFile
```

المخرج المستهدف للنسخة المحمولة هو `Build/SingleFile/PolarUninstaller.exe`. إذا اختلف اسم المشروع أو إعدادات الموارد، فاعتمد الملفات الفعلية وسكربت البناء في المستودع.

**أوامر Python وPyInstaller وملفات `.spec` تخص الإصدارات القديمة فقط، ولا تُستخدم لبناء إصدار C#.**

### بنية المشروع

| المسار | الغرض |
|---|---|
| `Polar-Uninstaller-WPF/` | مشروع التطبيق بلغة C# وواجهة WPF |
| `Views/` وملفات `*.xaml` | الصفحات والتصميم، عند وجودها |
| `Services/` | خدمات الإزالة وجرد البرامج وفحص التخزين وحساب أحجام المجلدات وإدارة الملفات وغيرها |
| `Assets/` | الشعارات والصور وبيانات الإصدارات والموارد |
| `Build/SingleFile/` | ملف EXE المحمول والمستقل |
| `Build/` | ملفات البناء والحزمة المحمولة وملفات المثبت |

قد تتغير المجلدات أثناء التطوير؛ ملفات المشروع الفعلية هي المرجع النهائي.

### الأمان والقيود

> **مهم:** راجع الملفات والمجلدات والاختصارات ومفاتيح Registry المحددة قبل الحذف. بعض العمليات لا يمكن التراجع عنها.

- احتفظ بنسخة احتياطية من البيانات المهمة قبل إزالة البرامج أو التنظيف.
- تشابه اسم الملف أو الناشر لا يعني أن العنصر يخص البرنامج المحذوف وحده.
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

**المطور:** Ayhm  
**الترخيص:** MIT
