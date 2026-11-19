namespace PolarUninstallerWpf.Services;

/// <summary>
/// Decides when a runtime/component row gets the neutral component icon instead of
/// a decoded picture. Only generic installer/redistributable pictures and missing
/// icons qualify; a genuine vendor logo (NVIDIA, .NET, Rockstar) is never replaced.
/// </summary>
public static class ComponentIconPolicy
{
    public static bool UseNeutral(InstalledProgram program, ProgramIcon? icon) =>
        LocalIconSources.Component(program) &&
        (icon is null || LocalIconSources.GenericInstallerSource(icon.SourcePath, icon.Method));
}
