namespace CCCCScheduler.Models;

/// <summary>What a left-click/tap on the floor plan does right now.</summary>
public enum EditMode
{
    /// <summary>Click selects a room; drag pans; wheel/pinch zooms.</summary>
    Select,

    /// <summary>
    /// Drag a rubber-band rectangle to give a room that shape. It creates a new room, <b>or places an
    /// existing one</b> when a room is selected in the plan's "not yet placed" list — see
    /// <c>MainViewModel.PlaceRoom</c>. It used to only ever create, so drawing over an existing room
    /// made a second room on top of it.
    /// </summary>
    DrawRectangle,

    /// <summary>
    /// Click to place polygon vertices; double-click (or click the first vertex) to close. Creates a
    /// new room or places an existing one, exactly as <see cref="DrawRectangle"/> does.
    /// </summary>
    DrawPolygon
}
