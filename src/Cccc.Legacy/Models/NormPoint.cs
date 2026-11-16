namespace CCCCScheduler.Models;

/// <summary>
/// A point expressed as a fraction of the floor image: (0,0) = top-left,
/// (1,1) = bottom-right. Storing geometry normalized makes zones survive any
/// display size, zoom level, or even re-scanning the plan at a higher resolution.
/// </summary>
public struct NormPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public NormPoint() { }

    public NormPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
