namespace CCCCScheduler.Models;

/// <summary>
/// A room's drawn outline, stored in normalized image coordinates.
/// Rectangle = exactly 2 points (top-left, bottom-right). Polygon = 3+ points.
/// </summary>
public sealed class RoomGeometry
{
    public GeometryKind Kind { get; set; } = GeometryKind.Rectangle;

    public List<NormPoint> Points { get; set; } = new();
}
