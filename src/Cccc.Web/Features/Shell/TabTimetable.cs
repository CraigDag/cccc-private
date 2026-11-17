using Cccc.Domain.Model;
using Cccc.Store;
using Cccc.Web.Features.Plan;
using Cccc.Web.Features.Week;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Shell;

using Week = Cccc.Domain.Week;

/// <summary>
/// The timetable this tab is looking at when it is not the one open for everybody: an administrator's, to
/// prepare next year or check a file just brought in, without moving the school onto it. Held by the tab
/// (its circuit) and, through the layout, in the tab's own sessionStorage, so a reload keeps it and no
/// other tab, and nobody else, is moved. Whether it is shown at all is <see cref="ProjectChoice.ShownId"/>'s
/// answer, asked every time the tab's workspace is built.
/// </summary>
public sealed class TabTimetable(PlanView plan, WeekView week, LiveProjects live, SchoolClock clock)
{
    private DateOnly? _dayBefore;

    public Guid? LookingAt { get; private set; }

    /// <summary>The day the tab was on before it looked away, which Back returns to; kept with the look over a reload.</summary>
    public DateOnly? DayBefore => _dayBefore;

    /// <summary>Raised when the tab starts looking at another timetable, or goes back to the open one.</summary>
    public event Action? Moved;

    /// <summary>
    /// Looks at <paramref name="id"/> in this tab. The plan and the week go where its classes are (see
    /// <see cref="Landing"/>), and what they were narrowed to is let go, being another timetable's.
    /// </summary>
    public void LookAt(Guid id)
    {
        if (LookingAt == id) return;
        plan.Start(clock.Today);
        if (LookingAt is null) _dayBefore = plan.RealDay;
        LookingAt = id;
        LetGoOfTheViews();
        if (Landing(live.Get(id).State, plan.RealDay) is { } day) Move(day);
        Moved?.Invoke();
    }

    /// <summary>
    /// Back to the timetable open for everybody, on the day the tab was on before it looked away (today when
    /// that is not known).
    /// </summary>
    public void Back()
    {
        if (LookingAt is null) return;
        LookingAt = null;
        LetGoOfTheViews();
        Move(_dayBefore ?? clock.Today);
        _dayBefore = null;
        Moved?.Invoke();
    }

    /// <summary>Put back after a reload, from the tab's own storage. The plan puts back its own day.</summary>
    public void Restore(Guid id, DateOnly? dayBefore)
    {
        LookingAt = id;
        _dayBefore = dayBefore;
    }

    /// <summary>Let go without moving anything: the timetable was deleted, or is now the one open for everybody.</summary>
    public void Drop()
    {
        LookingAt = null;
        _dayBefore = null;
    }

    /// <summary>
    /// Where the plan goes on looking at a timetable: nowhere when the week it is on has classes there;
    /// otherwise the first later week that has, or failing that the last earlier one. So next year's
    /// timetable opens on next year's first week rather than on an empty one today.
    /// </summary>
    public static DateOnly? Landing(Timetable t, DateOnly day)
    {
        var monday = Week.MondayOf(day);
        var weeks = t.Sessions.Select(s => s.WeekMonday).Distinct().Order().ToList();
        if (weeks.Count == 0 || weeks.Contains(monday)) return null;
        return weeks.Where(m => m > monday).Cast<DateOnly?>().FirstOrDefault() ?? weeks[^1];
    }

    /// <summary>The plan and the week to a day.</summary>
    private void Move(DateOnly day)
    {
        plan.Start(clock.Today);
        plan.GoTo(day);
        week.Start(clock.Today, null);
        week.GoTo(day);
    }

    private void LetGoOfTheViews()
    {
        plan.FocusOn(null);
        plan.Chosen = null;
        if (week.AnyFilter) week.ClearFilters();
    }
}
