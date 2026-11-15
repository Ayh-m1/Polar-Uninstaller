namespace PolarUninstallerWpf.Services;

public sealed record ThemePalette(string Background,string Glow,string Card,string Border,
    string Accent,string Text,string Dim,string? Sidebar=null,string? Hover=null,
    string? SecondaryBackground=null,string? SecondaryAccent=null);

/// <summary>One source for selectable identities, defaults and palette values.</summary>
public static class ThemeCatalog
{
    public const string DefaultTheme="Midnight Champagne";
    public static bool IsLegacyDefault(string? name)=>string.Equals(name?.Trim(),"Polar Navy",StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string,ThemePalette> All {get;}=
        new System.Collections.ObjectModel.ReadOnlyDictionary<string,ThemePalette>(new Dictionary<string,ThemePalette>
        {
            [DefaultTheme]=new("#0A1626","#203A52","#14263B","#8C795D","#F0D7A7","#F5F2EA","#B8C0CB",
                Sidebar:"#0A1728",Hover:"#1B3048",SecondaryBackground:"#0D1B2D",SecondaryAccent:"#D9B878"),
            ["Blue"]=new("#061125","#214D80","#101F36","#213C5A","#34C5F5","#EAF4FF","#A4B9CE"),
            ["Black"]=new("#08090C","#31353C","#171A20","#3A404B","#B5D7E9","#F4F7FA","#AAB3C1"),
            ["White"]=new("#EDF1F5","#FFFFFF","#FFFFFF","#CED7E1","#126CAC","#172333","#566578"),
            ["Beige"]=new("#E3D0B3","#F2E7D5","#F7EDDE","#B99B78","#79512F","#302219","#65503E"),
            ["Espresso"]=new("#100C0B","#604634","#302018","#765239","#DEA66E","#F9EAD9","#C4A78D"),
            ["Rose Noir"]=new("#0C070D","#3B1429","#1E101C","#71304F","#E865A5","#FFF0F7","#C6A3B5"),
            ["Pure Black"]=new("#030303","#111111","#090909","#909090","#F4F4F4","#F4F4F4","#A3A3A3")
        });
    public static string Resolve(string? name)=>IsLegacyDefault(name)?DefaultTheme:
        All.Keys.FirstOrDefault(key=>string.Equals(key,name?.Trim(),StringComparison.OrdinalIgnoreCase))??DefaultTheme;
}
