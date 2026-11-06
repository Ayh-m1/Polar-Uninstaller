[Uploading README (1).md…]()
<div align="center">

<img src="Polar_Logo_Beige_512.png" width="112" alt="Polar Uninstaller logo">

# Polar Uninstaller

**A modern, bilingual Windows utility for uninstalling apps and games, cleaning temporary files, analyzing storage, and managing files.**

**Built with C# • .NET • WPF**

[![Version](https://img.shields.io/badge/version-1.0.120-00B8F0)](https://github.com/Ayh-m1/Polar-Uninstaller/releases)
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

**Polar Uninstaller v1.0.120** introduces the native **C# / WPF** edition as the main Windows application, replacing the earlier Python-based interface. The new implementation focuses on smooth window resizing, responsive navigation, a consistent theme system, and integrated Windows functionality.

The interface supports **English and Arabic**, with right-to-left layouts where appropriate.

### Highlights in v1.0.120

- **C# + WPF migration:** native Windows desktop interface, replacing the former Python / CustomTkinter implementation.
- **Refined UI:** responsive navigation, smoother resizing, improved file-management layout, and consistent styling across themes.
- **Deep and Official Uninstall:** distinct removal methods with a shared leftover scanner and review window.
- **Leftover review:** inspect candidate files, folders, shortcuts, and Registry entries before choosing what to remove.
- **Verification and Rescan:** review an uninstall result again when completion cannot be confirmed automatically.
- **Application and game icons:** improved icon lookup, including supported launcher metadata and local shortcuts; coverage varies by title.
- **Modern File Browser:** navigation, sorting, file details, and file operations with confirmation.
- **Duplicate Finder:** compares file contents using SHA-256 before reporting duplicates.
- **Portable release:** a self-contained, single-file Windows executable is available in the `Build/SingleFile` output of the WPF project.

### Features

#### Deep Uninstaller

Discover installed Windows applications and supported game-library entries. The page displays application names, versions, publishers, sizes, and icons when that information is available.

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

View drive capacity, used and available space, and storage information. Start a storage scan when you need a more detailed breakdown of folders and large files. Heavy scans are not required just to open the page.

#### File Browser

Browse directories with an address/path bar, navigation controls, sorting, file metadata, and a details panel. The updated WPF layout supports Arabic paths and file icons.

File and folder deletion requires confirmation. **Permanent deletion cannot be undone.**

#### Duplicate Finder

Find potential duplicate files by comparing sizes and verifying matching candidates with **SHA-256** hashes. Review each group before selecting extra copies for cleanup; retain at least one copy of important files.

#### Themes and languages

Polar Uninstaller supports **Blue, Black, White, Beige, and Dark Beige / Espresso** themes, with English and Arabic interfaces. Theme and language preferences are designed to remain consistent across the application.

#### Activity Log, What's New, and Feedback

- **Activity Log:** shows progress, outcomes, and relevant warnings.
- **What's New:** displays release information within the application.
- **Feedback & Reports:** lets users submit a category, message, and app version. Do not include passwords or other sensitive information in reports.

### Download and run

1. Visit the [official Releases page](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. Choose a release explicitly labeled for **C# / WPF v1.0.120 or newer**. Older Python releases are a different application build.
3. Download the provided portable executable or release package, if published.
4. For the portable package, extract the archive if necessary, then launch `PolarUninstaller.exe`.
5. Review any Windows security or administrator-permission prompt before approving it.

The current WPF portable build is **self-contained**: users do not need to install the .NET runtime separately. A **Setup installer is planned separately**; do not confuse the portable EXE or ZIP with an installer.

> **Release status:** A successful local build does not mean an installer or GitHub release has already been published. Check the Releases page for what is actually available.

### Build from source

The current application is written in **C# using WPF and .NET**, targeting **64-bit Windows 10/11**.

Requirements:

- Windows 10 or 11 (64-bit)
- The .NET SDK version required by the WPF project
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
| `Services/` | Uninstall, leftover scanning, file-management, and other application services |
| `Assets/` | Branding, images, release notes, and other bundled resources |
| `Build/SingleFile/` | Self-contained portable EXE output |
| `Build/` | Release artifacts and packaging output |

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

**Polar Uninstaller v1.0.120** هو الإصدار الرئيسي الجديد المبني باستخدام **C# وWPF و.NET** لنظام Windows، بدل الواجهة السابقة المبنية بلغة Python ومكتبة CustomTkinter.

يركز الإصدار الجديد على **سلاسة تغيير حجم النافذة، وسرعة التنقل، وتناسق المظاهر، وتحسين إدارة الملفات وإزالة البرامج**. يدعم البرنامج **العربية والإنجليزية** مع مراعاة اتجاه الواجهة.

### أبرز تغييرات v1.0.120

- **الانتقال إلى C# وWPF:** واجهة Windows أصلية بدل تنفيذ Python السابق.
- **واجهة محسّنة:** تغيير حجم أكثر سلاسة، وتنقل أسرع، وتصميم موحّد للمظاهر والصفحات.
- **الإزالة العميقة والرسمية:** طريقتان للإزالة مع فاحص بقايا ونافذة مراجعة مشتركة.
- **مراجعة البقايا:** عرض الملفات والمجلدات والاختصارات ومفاتيح Registry المرشحة، مع اختيار العناصر يدويًا.
- **التحقق وإعادة الفحص:** إعادة التحقق عندما لا يمكن تأكيد اكتمال الإزالة تلقائيًا.
- **أيقونات البرامج والألعاب:** تحسين اكتشاف الأيقونات من بيانات التثبيت والمنصات والاختصارات المحلية، مع اختلاف الدعم بين الألعاب.
- **مستعرض ملفات أحدث:** تنقل وترتيب وتفاصيل وعمليات ملفات مع التأكيد قبل الحذف.
- **كشف الملفات المكررة:** التحقق من تطابق المحتوى باستخدام SHA-256.
- **نسخة محمولة:** ملف EXE مستقل ضمن مخرجات مشروع WPF في `Build/SingleFile`.

### مميزات البرنامج

#### الإزالة العميقة والرسمية

يعرض القسم البرامج المثبتة وبعض ألعاب المنصات المدعومة، مع الاسم والإصدار والناشر والحجم والأيقونة عند توفر البيانات.

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

عرض معلومات الأقراص والمساحة المستخدمة والمتاحة، مع إمكانية بدء فحص تفصيلي للتخزين والمجلدات والملفات الكبيرة عند الحاجة، دون فرض فحص شامل بمجرد فتح الصفحة.

#### مستعرض الملفات

التنقل بين المجلدات باستخدام شريط المسار وأزرار التنقل، مع الترتيب وتفاصيل الملفات والأيقونات ودعم المسارات العربية. تتطلب عمليات الحذف التأكيد، و**الحذف النهائي لا يمكن التراجع عنه**.

#### مكتشف الملفات المكررة

يفحص الملفات المرشحة بحسب الحجم ثم يقارن محتواها باستخدام بصمة **SHA-256**. راجع مجموعات التكرار وحدد النسخ الزائدة فقط مع الاحتفاظ بنسخة مهمة على الأقل.

#### المظاهر واللغات

يدعم البرنامج خمسة مظاهر: **الأزرق، الأسود، الأبيض، البيج، والبيج الغامق / إسبريسو**، مع واجهتين بالعربية والإنجليزية.

#### سجل النشاط وما الجديد والملاحظات

- **سجل النشاط:** لمتابعة تقدم العمليات ونتائجها والتنبيهات.
- **ما الجديد:** لعرض معلومات الإصدارات داخل البرنامج.
- **الملاحظات والبلاغات:** لإرسال نوع البلاغ والرسالة وإصدار البرنامج. تجنب إرسال كلمات المرور أو البيانات الحساسة.

### التحميل والتشغيل

1. افتح [صفحة الإصدارات الرسمية على GitHub](https://github.com/Ayh-m1/Polar-Uninstaller/releases).
2. اختر إصدار **C# / WPF v1.0.120 أو أحدث**؛ فإصدارات Python السابقة مختلفة عن هذا البناء.
3. حمّل النسخة المحمولة أو الحزمة المنشورة، إن كانت متاحة.
4. إذا كانت الحزمة مضغوطة، فك الضغط ثم شغّل `PolarUninstaller.exe`.
5. راجع أي طلب صلاحيات أو تحذير أمني من Windows قبل الموافقة.

النسخة المحمولة الحالية **مستقلة وتحتوي على متطلبات تشغيل .NET**، فلا يحتاج المستخدم لتثبيت بيئة .NET بشكل منفصل. أما **Setup فهو مشروع منفصل مخطط له**، ولا تُعد الحزمة المحمولة مثبّتًا.

> **حالة النشر:** نجاح بناء النسخة محليًا لا يعني نشرها تلقائيًا على GitHub. تحقق من صفحة Releases لمعرفة الملفات المنشورة بالفعل.

### البناء من المصدر

النسخة الحالية مبنية باستخدام **C# وWPF و.NET** لنظام **Windows 10/11 بنواة 64 بت**.

المتطلبات:

- Windows 10 أو 11 (64 بت)
- إصدار .NET SDK المتوافق مع مشروع WPF
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
| `Services/` | خدمات الإزالة وفحص البقايا وإدارة الملفات وغيرها |
| `Assets/` | الشعارات والصور وبيانات الإصدارات والموارد |
| `Build/SingleFile/` | ملف EXE المحمول والمستقل |
| `Build/` | ملفات البناء وحزم الإصدار |

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
