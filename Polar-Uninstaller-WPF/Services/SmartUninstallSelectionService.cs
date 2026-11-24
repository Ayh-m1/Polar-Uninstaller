using System.IO;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public enum UninstallMethod { Unavailable, Games, Deep }
public sealed record GameRemovalRoute(string Target, IReadOnlyList<string> Arguments, bool ManualInteraction, string DetailEn, string DetailAr,bool DirectEpic=false,bool CloseLauncher=false,bool TrackProcess=false);
public sealed record SmartUninstallDecision(UninstallMethod Method, string ReasonEn, string ReasonAr, GameRemovalRoute? GameRoute = null,bool UsesOfficialUninstaller=false)
{
    public bool GamesEnabled => Method == UninstallMethod.Games;
    public bool DeepEnabled => Method == UninstallMethod.Deep;
    public string Explanation(bool arabic) => arabic ? ReasonAr : ReasonEn;
    public string Tooltip(UninstallMethod method, bool arabic) => method == Method ? Explanation(arabic) :
        (arabic ? "هذه الطريقة غير مناسبة. " : "This method is not appropriate. ") + Explanation(arabic);
}

/// <summary>Read-only capability selection. This service never scans disks, deletes
/// resources or decides that uninstall completed. Execution re-evaluates its result.</summary>
public static class SmartUninstallSelectionService
{
    public static SmartUninstallDecision Evaluate(InstalledProgram program, IReadOnlyList<InstalledProgram> inventory)
    {
        try
        {
            if (PolarApplicationIdentity.IsPolarOwned(program))
                return new(UninstallMethod.Unavailable, "Polar cannot remove its own installation here.", "لا يمكن إزالة تثبيت Polar من هنا.");
            var classification=GameClassificationService.Classify(program);
            if (classification==GameClassificationService.Classification.VerifiedGame)
            {
                var route = ResolveGame(program);
                if(route==null&&LauncherRemovalCoordinator.Supports(program))return new(UninstallMethod.Unavailable,
                    "No verified game-specific uninstaller is available for this "+LauncherRemovalCoordinator.Profile(program)!.Name+" installation. Shared launcher databases and unrelated files will not be modified.",
                    "لا يتوفر مزيل متحقق منه لهذه اللعبة في "+LauncherRemovalCoordinator.Profile(program)!.Name+". لن تُعدّل قواعد بيانات اللانشر المشتركة أو الملفات غير المرتبطة.");
                return route == null
                    ? new(UninstallMethod.Unavailable, "No verified game uninstall route is available. Direct deletion is blocked to protect launcher records, shared files and saves.", "لا يتوفر مسار إزالة متحقق منه لهذه اللعبة. الحذف المباشر محظور لحماية سجلات اللانشر والملفات المشتركة والحفظ.")
                    : new(UninstallMethod.Games, route.DetailEn, route.DetailAr, route);
            }
            if (UninstallCommandService.TryParse(program.Uninstall, out _, out _))
                return new(UninstallMethod.Deep, "Deep Uninstall runs the registered official uninstaller, verifies removal, then automatically cleans eligible leftovers. Personal settings and files are preserved.",
                    "تشغّل الإزالة العميقة المزيل الرسمي المسجل وتتحقق من الإزالة ثم تنظّف البقايا المؤهلة تلقائيًا، مع الحفاظ على الملفات والإعدادات الشخصية.",UsesOfficialUninstaller:true);
            if(classification==GameClassificationService.Classification.PossibleGame)
                return new(UninstallMethod.Unavailable,"Possible game without verified launcher ownership or a supported registered uninstaller; refresh or use its launcher. Direct deletion is not authorized.",
                    "لعبة محتملة دون إثبات ملكية اللانشر أو مزيل مسجل مدعوم؛ حدّث القائمة أو استخدم لانشرها. الحذف المباشر غير مسموح.");
            DirectRemovalService.Validate(program, inventory);
            if (!Directory.Exists(program.Location))
                return new(UninstallMethod.Unavailable, "The verified installation directory is unavailable; refresh the inventory.", "مجلد التثبيت المتحقق منه غير متاح؛ حدّث قائمة البرامج.");
            return new(UninstallMethod.Deep, "No supported registered uninstaller was found. Deep Uninstall can use verified direct removal and eligible cleanup while preserving personal settings and files.", "لم يُعثر على مزيل مسجل مدعوم. يمكن للإزالة العميقة استخدام الحذف المباشر المتحقق منه وتنظيف البقايا المؤهلة مع الحفاظ على الملفات والإعدادات الشخصية.");
        }
        catch (Exception e) when (LauncherMetadataDiscovery.ReadFailure(e))
        {
            System.Diagnostics.Debug.WriteLine(e);
            return new(UninstallMethod.Unavailable, "Safe removal could not be established. Refresh the list and check installation access.",
                "تعذر التحقق من الإزالة الآمنة. حدّث القائمة وتحقق من صلاحيات الوصول إلى التثبيت.");
        }
    }

    public static GameRemovalRoute? ResolveGame(InstalledProgram game)
    {
        string id = game.Identifier ?? "";
        if (!GameClassificationService.IsGame(game) || InventoryMerger.NormalizePath(game.Location).Length == 0) return null;
        if(LauncherRemovalCoordinator.Supports(game))
        {
            var native=LauncherGameCommands.Resolve(game);if(native!=null)return native;
        }
        // A registered command belongs to this inventory identity, not a generated
        // launcher argument. GOG/Battle.net/Rockstar/EA/Riot commands take priority.
        if (UninstallCommandService.TryParse(game.Uninstall, out var exe, out var args))
            return new(exe, args, false, "Run this game's registered official uninstaller; confirmation may be required.", "تشغيل المزيل الرسمي المسجل لهذه اللعبة؛ قد يُطلب التأكيد.");
        if(EpicGameRemovalService.Enabled&&id.StartsWith("epic:",StringComparison.OrdinalIgnoreCase))
            return new("polar:epic-direct",[],false,
                "Epic direct removal: Polar requests launcher shutdown, verifies the installed build manifest, removes only unchanged game files, and backs up and cleans this game's exact Epic records. Close the game first. Saves, modified and unlisted files are preserved.",
                "الإزالة المباشرة لـEpic: يطلب Polar إغلاق اللانشر ويتحقق من قائمة ملفات التثبيت ويحذف ملفات اللعبة الأصلية فقط، وينسخ سجلاتها المحددة احتياطيًا وينظفها. أغلق اللعبة أولًا. تُحفظ بيانات الحفظ والملفات المعدلة والمضافة.",true);
        if(!Directory.Exists(game.Location))return null;
        if (id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) && NumericId(id[6..]) && ProtocolRegistered("steam"))
            return new("steam://uninstall/" + id[6..], [], false, "Open Steam's uninstall dialog for the selected App ID.", "فتح نافذة إزالة Steam لمعرف اللعبة المحددة.");
        // Ubisoft's supported URI is used by Playnite's UplayUninstallController.
        // No invented executable flags or undocumented database edits.
        if (id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase) && NumericId(id[8..]) && ProtocolRegistered("uplay"))
            return new("uplay://uninstall/" + id[8..], [], false, "Open Ubisoft Connect's uninstall dialog for this exact game ID.", "فتح نافذة إزالة Ubisoft Connect لمعرف هذه اللعبة تحديدًا.",CloseLauncher:LauncherRemovalCoordinator.Enabled);
        if(LauncherRemovalCoordinator.Supports(game))return null;
        string[] supported = ["steam:", "epic:", "battlenet:", "ubisoft:", "rockstar:", "gog:", "riot:"];
        if (!supported.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && id.Length > prefix.Length)) return null;
        var launcher = LauncherRouteService.Find(game);
        if (launcher == null) return null;
        return new(launcher, [], true,
            "No verified direct uninstall command is available. The launcher must be used to select this game and authorize removal; Polar will monitor it. Automatic deletion is not supported.",
            "لا يتوفر أمر إزالة مباشر متحقق منه. يلزم اختيار اللعبة وتأكيد إزالتها داخل اللانشر؛ سيراقب Polar النتيجة. الحذف الآلي غير مدعوم.");
    }

    private static bool NumericId(string id) => id.Length > 0 && id != "0" && id.All(char.IsAsciiDigit);
    private static bool ProtocolRegistered(string protocol)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Classes\" + protocol + @"\shell\open\command");
                if (UninstallCommandService.TryParse(Convert.ToString(key?.GetValue("")) ?? "", out _, out _)) return true;
            }
            catch (Exception e) when (LauncherMetadataDiscovery.ReadFailure(e)) { }
        return false;
    }
}
