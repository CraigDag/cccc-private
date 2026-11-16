namespace CCCCScheduler.Models;

/// <summary>A point in plan coordinates. Replaces WPF's Point type so geometry stays portable.</summary>
public readonly record struct PlanPoint(double X, double Y);

/// <summary>An axis-aligned rectangle in plan coordinates. Replaces WPF's Rect type.</summary>
public readonly record struct PlanRect(double X, double Y, double Width, double Height)
{
    public static PlanRect Empty => new(0, 0, 0, 0);
}
