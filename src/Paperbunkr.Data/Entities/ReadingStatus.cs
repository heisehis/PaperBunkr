namespace Paperbunkr.Data.Entities;

/// <summary>
/// The user's reading-progress relationship with a <see cref="Series"/> - distinct from
/// <see cref="SeriesStatus"/> (whether the *publisher* is still releasing it). Has no CE precedent
/// (same "deliberate new feature, not parity" footing as <see cref="SeriesStatus"/> - CE tracked
/// read/unread only at the per-issue level, never a series-wide intent like Planned/Dropped).
/// <see cref="Unknown"/> is the unset default, matching every other Series-level enum in this
/// codebase (<see cref="ContentType"/>, <see cref="SeriesStatus"/>, <see cref="ColorMode"/>).
/// </summary>
public enum ReadingStatus
{
    Unknown,
    Planned,
    Reading,
    Completed,
    Paused,
    Dropped,
    ReReading,
}
