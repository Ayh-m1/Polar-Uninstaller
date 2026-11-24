namespace PolarUninstallerWpf.Services;

public sealed record ThemePalette(string Background,string Glow,string Card,string Border,
    string Accent,string Text,string Dim,string? Sidebar=null,string? Hover=null,
    string? SecondaryBackground=null,string? SecondaryAccent=null,string? AccentText=null,string? HoverAccent=null);

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
            ["Pure Black"]=new("#030303","#111111","#090909","#909090","#F4F4F4","#F4F4F4","#A3A3A3"),
            ["Emerald"]=new("#071C18","#102D27","#123D32","#205447","#10B981","#F0FFF8","#A0C9BA",
                Sidebar:"#0A2420",SecondaryBackground:"#102D27",AccentText:"#071C18",HoverAccent:"#16D49A"),
            ["Purple"]=new("#100D1C","#1D142D","#241637","#493069","#8B5CF6","#F5F0FF","#BBA9D5",
                Sidebar:"#191127",SecondaryBackground:"#1D142D",AccentText:"#100D1C",HoverAccent:"#A78BFA"),
            ["Crimson"]=new("#190B10","#2B111A","#38151F","#63303E","#B63B52","#FFF0F3","#D4A6B1",
                Sidebar:"#240D15",SecondaryBackground:"#2B111A",AccentText:"#FFF0F3",HoverAccent:"#D94C68"),
            ["Teal"]=new("#07191D","#0C2B31","#10343B","#20565B","#24B8B0","#EEFFFD","#9BCAC8",
                Sidebar:"#0A2429",SecondaryBackground:"#0C2B31",AccentText:"#07191D",HoverAccent:"#35D5CC")
        });
    public static string Resolve(string? name)=>IsLegacyDefault(name)?DefaultTheme:
        All.Keys.FirstOrDefault(key=>string.Equals(key,name?.Trim(),StringComparison.OrdinalIgnoreCase))??DefaultTheme;
}
