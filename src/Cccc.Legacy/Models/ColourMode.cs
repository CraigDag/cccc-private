namespace CCCCScheduler.Models;

/// <summary>What the floor plan and week grid colour their zones/blocks by.</summary>
public enum ColourMode
{
    /// <summary>Each room/block uses its room colour — "where is everything" (default).</summary>
    Room,

    /// <summary>Blocks use their session's group colour; rooms use their dominant group — "who's using the building".</summary>
    Group,
}
