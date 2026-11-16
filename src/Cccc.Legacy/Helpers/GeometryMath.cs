using CCCCScheduler.Models;

namespace CCCCScheduler.Helpers;

/// <summary>
/// Conversions between the three coordinate spaces and basic polygon math.
/// Spaces: <b>normalized</b> [0..1] (on disk) ↔ <b>world/image</b> pixels
/// (canvas children, before the view transform) ↔ <b>screen</b> pixels
/// (after the matrix transform). Keep ALL of this here so a single bug can't
/// silently misalign every zone.
/// </summary>
public static class GeometryMath
{
    /// <summary>Normalized fraction → world/image pixel point.</summary>
    public static PlanPoint NormToWorld(NormPoint n, double imageWidth, double imageHeight)
        => new(n.X * imageWidth, n.Y * imageHeight);

    /// <summary>World/image pixel point → normalized fraction (clamped to [0..1]).</summary>
    public static NormPoint WorldToNorm(PlanPoint world, double imageWidth, double imageHeight)
    {
        double x = imageWidth <= 0 ? 0 : world.X / imageWidth;
        double y = imageHeight <= 0 ? 0 : world.Y / imageHeight;
        return new NormPoint(Clamp01(x), Clamp01(y));
    }

    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Centroid (average) of a set of points.</summary>
    public static PlanPoint Centroid(IReadOnlyList<PlanPoint> points)
    {
        if (points.Count == 0) return new PlanPoint(0, 0);
        double sx = 0, sy = 0;
        foreach (var p in points) { sx += p.X; sy += p.Y; }
        return new PlanPoint(sx / points.Count, sy / points.Count);
    }

    /// <summary>Axis-aligned bounding rect of a point set.</summary>
    public static PlanRect Bounds(IReadOnlyList<PlanPoint> points)
    {
        if (points.Count == 0) return PlanRect.Empty;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new PlanRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Ray-casting point-in-polygon test (works for convex and concave).</summary>
    public static bool PointInPolygon(PlanPoint test, IReadOnlyList<PlanPoint> polygon)
    {
        if (polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            PlanPoint pi = polygon[i], pj = polygon[j];
            bool crosses = (pi.Y > test.Y) != (pj.Y > test.Y);
            if (crosses)
            {
                double atX = (pj.X - pi.X) * (test.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                if (test.X < atX) inside = !inside;
            }
        }
        return inside;
    }
}
