namespace Paperbunkr.Data.Entities;

/// <summary>What one page does about auto-crop (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #3): follow the setting, never crop this page, or crop it even with the setting off.</summary>
public enum PageCropMode
{
    Auto,
    Never,
    Always,
}
