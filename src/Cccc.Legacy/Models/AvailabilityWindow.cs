namespace CCCCScheduler.Models;

/// <summary>
/// A weekly window in which a facilitator is available to teach, e.g. Wednesday 18:00–22:00.
/// A facilitator with no windows at all is treated as "available any time" (so existing data
/// raises no false unavailability warnings); once any window is set, sessions outside their
/// windows are flagged.
/// </summary>
public sealed class AvailabilityWindow
{
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
    public TimeOnly Start { get; set; } = new(9, 0);
    public TimeOnly End { get; set; } = new(17, 0);
}
