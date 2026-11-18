using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Ask;

using Week = Cccc.Domain.Week;

/// <summary>
/// The words for what facilitators ask (<see cref="Cccc.Domain.Rules.Asking"/>), the same on My week, on Now
/// and in the answer form. Never a reason: a request has none, so nothing here can say one.
/// </summary>
public static class AskingWords
{
    /// <summary>"Tue 24 Nov, all day", "Tue 24 – Thu 26 Nov, 09:00–12:00".</summary>
    public static string Days(DateOnly from, DateOnly to, TimeOnly? start, TimeOnly? end)
    {
        var days = from == to ? Week.ShortDate(from)
            : from.Month == to.Month && from.Year == to.Year ? $"{Week.ShortDay(from.DayOfWeek)} {from.Day} – {Week.ShortDate(to)}"
            : $"{Week.ShortDate(from)} – {Week.ShortDate(to)}";
        return days + (start is { } a && end is { } b && b > a ? $", {Looks.When(a, b)}" : ", all day");
    }

    public static string Days(TimeOffRequest r) => Days(r.From, r.To, r.StartTime, r.EndTime);

    /// <summary>Where a request stands, as the person who asked reads it.</summary>
    public static string State(RequestState s) => s switch
    {
        RequestState.Waiting => "Waiting for an answer",
        RequestState.Recorded => "Yes: recorded as away",
        RequestState.NotThisTime => "Not this time",
        _ => "Passed without an answer",
    };

    /// <summary>Where an offer stands, as the person who offered reads it.</summary>
    public static string State(OfferState s) => s switch
    {
        OfferState.Waiting => "Offered, waiting for an answer",
        OfferState.Covering => "Yes: you are covering it",
        OfferState.NotThisTime => "No thanks, this time",
        OfferState.NotNeeded => "Not needed now: it has cover",
        _ => "Passed",
    };

    /// <summary>
    /// What a yes would leave a class it takes somebody off: "needs cover", "Bo Lindqvist still teaching it", or,
    /// for a class they are covering, that its cover would then need changing.
    /// </summary>
    public static string After(Timetable t, Guid who, Occurrence now, Occurrence after) =>
        now.Session.CoverIds.Contains(who) ? "covering it: change its cover"
        : after.NeedsCover ? "needs cover"
        : Looks.Names(t, after.InTheRoom) is { Length: > 0 } names ? $"{names} still teaching it"
        : "runs";
}
