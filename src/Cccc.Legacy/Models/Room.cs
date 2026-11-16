using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// A named space in the building. <b>The record is the room; the outline is an optional attribute of
/// it, and so — since schema 10 — is the timetable.</b>
///
/// <para>It used to be the other way round twice over. First this said <i>"a named, clickable space
/// on a floor plan"</i>, and it was accurate: a room could only come into existence by being drawn,
/// so a building nobody had drawn could not be scheduled at all. A room now exists whether or not
/// anybody has put a shape on it; see <see cref="IsPlaced"/>. Second, a room used to <em>own</em> its
/// sessions — see <see cref="LegacySchedule"/> — which made it the session's owner rather than one of
/// its attributes, and which is why deleting a room used to destroy a term's teaching outright. The
/// timetable itself now lives on <c>Building.Sessions</c>, and a session names its room, or none at
/// all, through <see cref="ScheduleEntry.RoomId"/>.</para>
/// </summary>
public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Optional grouping, e.g. "Aerial", "Mat", "Office".</summary>
    public string? Category { get; set; }

    /// <summary>The zone's display colour, e.g. "#3F51B5".</summary>
    public string ColourHex { get; set; } = "#3F51B5";

    /// <summary>Whether the label may wrap onto multiple lines (always at word boundaries),
    /// or is kept to a single line. Defaults to true.</summary>
    public bool WrapLabel { get; set; } = true;

    /// <summary>
    /// Which <see cref="FloorPlan.Index"/> this room lives on, or <c>null</c> for <b>a room that is
    /// on no floor at all</b> — an ordinary room that schedules, appears in every grid, exports and
    /// takes sessions, and simply is not on any plan.
    ///
    /// <para><b>It became optional in schema 9, and the requirement it removed was structural rather
    /// than visual.</b> A room could only ever point <em>into</em> <c>Building.Floors</c>, so that
    /// list had to be non-empty before any room could exist — which made a project made in the web
    /// app, where a floor is a photograph nobody can bring in, a dead end with no rooms and therefore
    /// no sessions. The owner's ruling, 2026-08-29: <i>"A project shouldn't need a floorplan and a
    /// room shouldn't need a floor."</i> A default floor was the alternative and was rejected as
    /// exactly the workaround that accumulates.</para>
    ///
    /// <para><b>Null is the deliberate absence; a NUMBER naming no floor is the accidental one</b>,
    /// and the two behave identically everywhere downstream — both show
    /// <c>RoomList.UnknownFloorText</c>, neither is ever drawn on a plan, and both export a blank
    /// floor column. They are not merged into one representation because the accidental form is
    /// reachable from a hand-edited file at any time; what schema 9's migration does is retire every
    /// historical one, so no file this app has ever written still carries it.</para>
    /// </summary>
    public int? FloorIndex { get; set; }

    /// <summary>
    /// The drawn outline, or an empty one when the room has not been placed on the plan. There is no
    /// <c>GeometryKind.None</c>: unplaced is an <em>empty point list</em> under the default kind, which
    /// is why <see cref="IsPlaced"/> counts points and never reads <see cref="RoomGeometry.Kind"/> as a
    /// signal on its own.
    /// </summary>
    public RoomGeometry Geometry { get; set; } = new();

    /// <summary>
    /// Whether this room has a usable shape on the floor plan. <b>The predicate two of the three
    /// readers ask</b> — the renderer (<c>FloorPlanView.RedrawOverlays</c>) and the jump-to
    /// (<c>BringRoomIntoView</c>) both read it, so those two cannot come to disagree about what a
    /// shape is. <b>The third, the hit-test, does not read it</b>; it only happens to agree.
    ///
    /// <para><b>The hit-test does not ask it.</b> <c>PointInRoom</c> — in both
    /// <c>FloorPlanView</c>s — still tests <c>Kind == Rectangle &amp;&amp; Points.Count &gt;= 2</c>
    /// itself, and otherwise falls through to <c>GeometryMath.PointInPolygon</c>'s own
    /// <c>Count &lt; 3</c>; it is downstream of nothing. The numbers match what is written here,
    /// which is why the effect is right in every case today — but they are a separate copy of the
    /// rule, so <b>widening this predicate does not widen the hit-test</b>, and a shape can go back
    /// to being painted and unclickable the moment the two are edited apart.
    /// <c>RoomsAsDataTests.ATwoPointPolygon_IsNoLongerPainted_SoRenderAndHitTestAgree</c> is what
    /// holds them together. <b>Re-read against both hit-tests on 2026-08-23 and true as written</b>
    /// — this paragraph exists because an earlier version of the sentence above claimed the hit-test
    /// was downstream of the predicate, which would have told a maintainer this was safe to widen
    /// on its own.</para>
    ///
    /// <para>There used to be four separate thresholds and they disagreed two ways: a one-point
    /// rectangle could be panned to but never drawn, and a two-point polygon was drawn but refused by
    /// the hit-test — so it could never be selected, edited or deleted by any control in the app.
    /// Neither is reachable from the drawing tools; both are reachable from a hand-edited file.</para>
    ///
    /// <para><b>Point count only — never area.</b> A thin corridor has a real shape and a near-zero
    /// area, and an area test would make it vanish off the plan. And never
    /// <see cref="GeometryMath.Bounds"/>: an empty outline's bounds are a real zero rect at the origin,
    /// indistinguishable from a room of no size at (0,0) — there is no sentinel and no inversion to
    /// read, which <c>GeometryMathTests</c> pins.</para>
    ///
    /// <para><b>Derived, and deliberately not persisted.</b> Writing it would add a key to every room of
    /// every project file that no build has ever read, for a feature whose whole claim is that the file
    /// format has not changed — an older build opens these projects and saves them back unharmed.</para>
    /// </summary>
    [JsonIgnore]
    public bool IsPlaced =>
        Geometry is { Points: { } pts }
        && (Geometry.Kind == GeometryKind.Rectangle ? pts.Count >= 2 : pts.Count >= 3);

    /// <summary>
    /// <b>Schema 9's sessions, read on load and cleared once read.</b> A room owned its own timetable
    /// until schema 10; this is where such a file's sessions arrive so the migration can move them
    /// onto <c>Building.Sessions</c>.
    ///
    /// <para><b>Now genuinely the same arrangement as <see cref="Student.LegacyName"/>, having twice
    /// failed to be it.</b> A <c>List&lt;ScheduleEntry&gt;?</c>, cleared to <c>null</c>, ungated —
    /// found false two ways by the final whole-branch review. First,
    /// <c>JsonIgnoreCondition.WhenWritingDefault</c> suppresses an explicit <c>null</c> but not an
    /// empty list, and this property's default WAS an empty list (<c>= new()</c>), so every room of
    /// every file this app ever saved still carried <c>"Schedule": []</c>. Second, the clear
    /// (<c>.Clear()</c>, in <see cref="Services.ProjectStore"/>) sat <em>inside</em> the
    /// <c>&lt; 10</c> migration gate, which never runs on a file already claiming schema 10 — so a
    /// schema-10 file hand-edited to carry a <c>"Schedule": [...]</c> array kept that ghost session
    /// for ever and wrote it back out on every save, which is exactly what this paragraph used to
    /// claim could not happen. Being genuinely nullable and cleared UNGATED (see
    /// <see cref="Services.ProjectStore"/>'s own remarks) closes both holes: the global
    /// <c>DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull</c> omits the key outright for
    /// <c>null</c> the way it always has for <c>Student.LegacyName</c>, and an ungated clear runs on
    /// every load regardless of the file's claimed schema, so a stray hand-edited value cannot survive
    /// to be written back out even once.</para>
    /// </summary>
    [JsonPropertyName("Schedule")]
    public List<ScheduleEntry>? LegacySchedule { get; set; }
}
