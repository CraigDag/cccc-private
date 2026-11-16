namespace CCCCScheduler.Models;

/// <summary>
/// Who a session is on for. An exact mirror of <see cref="HolidayScope"/>, deliberately: a reader who
/// has understood one understands the other, and the same migration trick works for the same reason.
///
/// <para><see cref="Group"/> is first, and is therefore the value a <see cref="ScheduleEntry"/> takes
/// when the JSON carries no <c>Scope</c> key at all. Every session written before schema 11 belonged
/// to the single group in <see cref="ScheduleEntry.GroupId"/>, so that default is what makes an
/// existing project behave identically after migration — for free, without the loader having to
/// guess. <b>Reordering these would silently reinterpret every session in every existing file.</b></para>
/// </summary>
public enum SessionScope
{
    /// <summary>
    /// One cohort's, named by <see cref="ScheduleEntry.GroupId"/> — which may be null, meaning the
    /// session belongs to no cohort at all: an External Hire booking or a one-off. A null group
    /// matches no cohort filter, and that is not the same state as <see cref="WholeBuilding"/>.
    /// </summary>
    Group,

    /// <summary>
    /// Shared by the cohorts listed in <see cref="ScheduleEntry.GroupIds"/> — a class BA and Prep
    /// both attend. <b>An empty list matches nobody</b>, exactly as an empty
    /// <see cref="Holiday.GroupIds"/> closes nothing; it must never be read as "everybody".
    /// </summary>
    Groups,

    /// <summary>
    /// The school's, not any cohort's — open training, an all-school meeting. It matches EVERY cohort
    /// filter, because it is on for every cohort, and it is never shut by a group-scoped closure.
    /// </summary>
    WholeBuilding,

    /// <summary>
    /// <b>A whole programme's — every group in the <see cref="Strand"/> named by
    /// <see cref="ScheduleEntry.ProgrammeId"/></b>, schema 17: a class for all of BA, which is Year One
    /// now and Year One and Year Two once the second year exists. Craig's shape, 2026-09-24:
    /// <i>"the Degree (BA) is the top level, both years combined, Y1 and Y2 are distinct groups
    /// within that year."</i>
    ///
    /// <para><b>Read exactly as <see cref="Groups"/> is</b>, off <see cref="ScheduleEntry.GroupIds"/>,
    /// which for this scope holds the programme's groups as they currently are.
    /// <c>ScheduleOps.RefreshProgrammeSessions</c> is the one writer of that list, and it runs on
    /// every load and every change to the groups — so a group added to BA next year joins every BA
    /// class without anybody touching one, and every reader of a session's cohorts stays a reader of
    /// a list rather than having to be handed the building to look the programme up.</para>
    ///
    /// <para>Last, and appended rather than inserted, for the reason at the top of this enum.</para>
    /// </summary>
    Programme,
}
