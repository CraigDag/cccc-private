namespace CCCCScheduler.Models;

/// <summary>
/// Whether a class's hours are curriculum, and whether anybody is required to be there. Replaces the
/// <c>CountsAsWorkload</c> bool, which was one flag doing the work of two different reasons for not
/// counting — and so filed a staffed three-hour training session under the same heading as lunch.
///
/// <para><see cref="Counted"/> is first, and is therefore what a class with no <c>Workload</c> key
/// deserialises to. <c>CountsAsWorkload</c> defaulted to <c>true</c>, so that ordinal is what makes
/// an existing project behave identically. <b>Do not reorder.</b></para>
///
/// <para><b>No kind removes hours from a FACILITATOR.</b> <c>WeeklyLoad</c> does not read this and
/// never has: the hours are whoever is in the room, and somebody supervises open training.</para>
/// </summary>
public enum WorkloadKind
{
    /// <summary>Curriculum. Counts toward the scheduled total, its area's target, and a student's load.</summary>
    Counted,

    /// <summary>
    /// Real activity, open to anyone, required of nobody — open training. Counts for whoever staffs
    /// it and for nobody else: not toward a curriculum target, and not toward a student's weekly
    /// load, because that number is defensible only while the student is timetabled to be there.
    /// </summary>
    DropIn,

    /// <summary>
    /// In the daily time in the school, but not activity — lunch, cleaning. The owner's own words,
    /// and the reason this kind is shown and marked rather than hidden.
    /// </summary>
    NotCounted,
}
