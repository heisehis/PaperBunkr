namespace Paperbunkr.Data.Entities;

/// <summary>
/// Avalonia GPU rendering backend selection, backing <see cref="AppSettings.RenderingBackend"/>
/// (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md). No CE
/// equivalent - ComicRack CE was WinForms/GDI+ with no GPU rendering concept at all.
/// </summary>
/// <remarks>
/// <see cref="Auto"/> = GPU-first with a software fallback (the app's default, and Avalonia's own
/// implicit default made explicit). <see cref="Gpu"/> = GPU only, no software fallback - the app
/// may fail to start on a broken GPU, which is the point (it proves whether GPU actually works).
/// <see cref="Software"/> = force the CPU rasterizer, the escape hatch for broken drivers, RDP
/// sessions and VMs.
/// </remarks>
public enum RenderBackend
{
    Auto,
    Gpu,
    Software,
}
