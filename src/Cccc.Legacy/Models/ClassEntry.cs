using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// A named class/course in the building's catalogue (e.g. "Youth Acro", "Adult Beginners").
/// A managed, editable list used to populate the session title dropdown. Named ClassEntry
/// because "Class" is a C# keyword; the UI labels it simply "Class".
/// </summary>
public sealed class ClassEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour, reserved for future colour-coding by class.</summary>
    public string ColourHex { get; set; } = "#5E35B1";

    /// <summary>
    /// The curriculum <see cref="Area"/> this activity's hours are summed under, or <c>null</c> for
    /// none. Optional on purpose: not everything scheduled belongs to the curriculum, and unassigned
    /// is a state a project can legitimately stay in forever — it is how every class in every existing
    /// project arrives.
    ///
    /// <para>An unassigned class still contributes to the total scheduled hours (when it counts as
    /// workload); it simply contributes to no area's target.</para>
    ///
    /// <para>An id naming an area the project does not contain is corruption, never a state to honour,
    /// and is scrubbed back to <c>null</c> on load — never repaired by inventing an area. See the
    /// orphan scrub in <c>ProjectStore.Validate</c>.</para>
    /// </summary>
    public Guid? AreaId { get; set; }

    /// <summary>
    /// Whether this class's hours are curriculum, drop-in, or not activity at all. Replaced the
    /// <c>CountsAsWorkload</c> bool at schema 11; <see cref="WorkloadKind.Counted"/> is the default
    /// and is what the old <c>true</c> migrates to.
    /// </summary>
    public WorkloadKind Workload { get; set; }

    /// <summary>
    /// <b>Schema 15 to 17's prep-time box, read from an older file and never written.</b> Since schema
    /// 18 which classes earn preparation is a <see cref="TimeRule"/>, and the <c>&lt; 18</c> block in
    /// <c>ProjectStore.Validate</c> turns every class ticked here into that rule's list and clears this,
    /// so a migrated file carries no trace of the old key. Kept under the old JSON name only so the
    /// ticks the school made on 2026-09-18 survive the upgrade.
    /// </summary>
    [JsonPropertyName("HasPrepTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyHasPrepTime { get; set; }

    /// <summary>
    /// Schema-10 and earlier stored this as a bool. Read by the 10 → 11 migration in
    /// <c>ProjectStore.Validate</c>'s <c>&lt; 11</c> block, which re-derives <see cref="Workload"/>
    /// from it on every load with an incoming version under 11 — including a pre-6 file, which is why
    /// the pre-6 scrub in the same method's <c>&lt; 6</c> block WRITES this back to <c>true</c>
    /// alongside clearing <see cref="Workload"/>: leaving it untouched there would let the <c>&lt; 11</c>
    /// block silently re-apply a smuggled value straight back after the scrub ran.
    ///
    /// <para><b>A plain public property, not a write-suppressed one</b> — following the precedent of
    /// <see cref="Holiday.WeekMonday"/> — since System.Text.Json cannot express "deserialise-only"
    /// without a private-setter dance. That means it IS serialised into every project this build
    /// saves: a class the owner sets to <see cref="WorkloadKind.DropIn"/> or
    /// <see cref="WorkloadKind.NotCounted"/> in a schema-11 project newly created in this build still
    /// writes <c>"CountsAsWorkload": true</c> beside its real <c>"Workload"</c> key, from this
    /// property's own default — nothing in this build ever sets it deliberately. <b>Deserialisation
    /// still sets it</b>, though: a schema-11 file that arrived carrying <c>"CountsAsWorkload": false</c>
    /// — a hand-edit, or a value this property held before schema 11 existed — keeps writing
    /// <c>false</c> back out on every save, rather than snapping to the default. <b>That copy is stale
    /// by design and harmless only because the <c>&lt; 11</c> gate never reads it on a schema-11
    /// file</b> — it must never be read from anywhere except under a <c>&lt; 11</c> (or narrower
    /// pre-11) gate, or a schema-11 project's real <see cref="Workload"/> would be silently overridden
    /// by this fossil.</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("CountsAsWorkload")]
    public bool LegacyCountsAsWorkload { get; set; } = true;
}
