namespace CCCCScheduler.Models;

/// <summary>
/// One floor of the building: its image (copied into the project's images/ folder)
/// and the native pixel size used to validate normalized room coordinates.
/// </summary>
public sealed class FloorPlan
{
    public int Index { get; set; }

    /// <summary>Display name, e.g. "Ground", "First", "Second".</summary>
    public string Name { get; set; } = "";

    /// <summary>Path relative to the project folder, e.g. "images/ground.jpg".</summary>
    public string ImageFileName { get; set; } = "";

    public int ImagePixelWidth { get; set; }

    public int ImagePixelHeight { get; set; }
}
