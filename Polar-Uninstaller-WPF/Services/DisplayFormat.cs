using System.Globalization;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Culture-stable formatting for numbers and sizes. Latin digits with en-US grouping are used
/// in both languages so values stay readable and are not reordered by RTL text.
/// Numeric tokens are wrapped in Unicode LTR isolates (U+2066 … U+2069) when embedded in Arabic.
/// </summary>
public static class DisplayFormat
{
    public static readonly CultureInfo NumericCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Set numeric/calendar defaults before creating windows. UI language,
    /// RTL direction and the original Unicode contents of paths/user text stay intact.</summary>
    public static void ConfigureApplication()
    {
        CultureInfo.DefaultThreadCurrentCulture = NumericCulture;
        CultureInfo.CurrentCulture = NumericCulture;
        System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.FrameworkElement),
            new System.Windows.FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(NumericCulture.IetfLanguageTag)));
    }

    public static string Date(DateTime value, string format = "g") => value.ToString(format, NumericCulture);

    public static string Number(long value) => value.ToString("N0", NumericCulture);

    public static string Number(double value, int decimals = 1) =>
        value.ToString("N" + Math.Clamp(decimals, 0, 4).ToString(CultureInfo.InvariantCulture), NumericCulture);

    /// <summary>Wraps a value in a left-to-right isolate so it cannot be reordered by surrounding RTL text.</summary>
    public static string Isolate(string value) => "\u2066" + value + "\u2069";
    public static string Directional(string value, bool arabic) => arabic ? Isolate(value) : value;

    public static string Quantity(long value, string englishUnit, string arabicUnit, bool arabic) =>
        Isolate(Number(value)) + " " + (arabic ? arabicUnit : englishUnit);

    public static string Size(long bytes, bool arabic)
    {
        string number;
        string unit;
        if (bytes >= 1L << 30)
        {
            number = (bytes / (double)(1L << 30)).ToString("#,##0.##", NumericCulture);
            unit = arabic ? "جيجابايت" : "GB";
        }
        else if (bytes >= 1L << 20)
        {
            number = (bytes / (double)(1L << 20)).ToString("#,##0.##", NumericCulture);
            unit = arabic ? "ميجابايت" : "MB";
        }
        else if (bytes >= 1L << 10)
        {
            number = (bytes / (double)(1L << 10)).ToString("#,##0.##", NumericCulture);
            unit = arabic ? "كيلوبايت" : "KB";
        }
        else
        {
            number = Number(bytes);
            unit = arabic ? "بايت" : "B";
        }
        return (arabic ? Isolate(number) : number) + " " + unit;
    }

    public static string CleanSummary(long bytes, int files, int skipped, bool arabic) =>
        CleanSummary(bytes, files, 0, skipped, arabic);

    public static string CleanOutcomeSummary(long bytes,int files,int directories,int skipped,int failedCategories,bool arabic)
    {
        string result=CleanSummary(bytes,files,directories,skipped,arabic);
        if(failedCategories>0||skipped>0)
            result=result.Replace("اكتمل التنظيف بنجاح","اكتمل التنظيف جزئيًا")
                .Replace("اكتمل التنظيف —","اكتمل التنظيف جزئيًا —")
                .Replace("Cleanup completed —","Cleanup partially completed —");
        if(failedCategories>0)result+=arabic
            ? $" · تعذر تنظيف {Isolate(Number(failedCategories))} فئة"
            : $" · {Number(failedCategories)} categories failed";
        return result;
    }

    public static string CleanSummary(long bytes, int files, int directories, int skipped, bool arabic)
    {
        string note = skipped > 0
            ? arabic
                ? $" · تُجاوز {Isolate(Number(skipped))} عنصرًا"
                : $" · {Number(skipped)} items skipped"
            : "";
        string directoryNote = directories > 0
            ? arabic
                ? $" و{Isolate(Number(directories))} مجلدًا"
                : $" and {Number(directories)} folders"
            : "";
        if (files <= 0 && directories <= 0 && bytes <= 0)
            return arabic
                ? $"لم يتم حذف أي ملفات{note}."
                : $"No files were removed{note}.";
        if (bytes <= 0)
            return arabic
                ? $"اكتمل التنظيف — أُزيل {Isolate(Number(files))} ملف فارغ{directoryNote} (0 بايت){note}."
                : $"Cleanup completed — {Number(files)} empty files{directoryNote} removed (0 B){note}.";
        return arabic
            ? $"اكتمل التنظيف بنجاح — تم تنظيف {Size(bytes, true)}{directoryNote}{note}."
            : $"Cleanup completed — {Size(bytes, false)} cleaned{directoryNote}{note}.";
    }

    public static string CategorySummary(int files, int directories, long bytes, int skipped, bool delete, bool arabic)
    {
        string filesText = delete
            ? arabic ? $"أُزيل {Isolate(Number(files))}" : $"{Number(files)} removed"
            : arabic ? $"{Isolate(Number(files))} عنصر" : $"{Number(files)} items";
        string directoryText = directories > 0
            ? arabic ? $" · {Isolate(Number(directories))} مجلدًا" : $" · {Number(directories)} folders"
            : "";
        string skippedText = arabic
            ? $"تُجاوز {Isolate(Number(skipped))}"
            : $"{Number(skipped)} skipped";
        return filesText + directoryText + " · " + Size(bytes, arabic) + " · " + skippedText;
    }
}
