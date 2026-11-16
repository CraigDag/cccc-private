using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Visiting groups (schema 21) and visiting facilitators (schema 20). Hiding is drawing, never counting:
/// a hidden guest class still clashes, needs cover and counts hours.
/// </summary>
public static class Guests
{
    /// <summary>Every group it is on for is a guest programme's. A shared or Everyone class never is.</summary>
    public static bool IsGuestOnly(Timetable t, Session s)
    {
        if (s.Audience.IsEveryone) return false;
        bool any = false;
        foreach (var gid in s.Audience.Cohorts)
        {
            var g = t.Groups[gid];
            if (g is null) continue;
            if (t.Programmes.Find(g.ProgrammeId) is not { IsGuest: true }) return false;
            any = true;
        }
        return any;
    }

    /// <summary>At least one of its groups is a guest programme's.</summary>
    public static bool Involves(Timetable t, Session s) =>
        !s.Audience.IsEveryone && s.Audience.Cohorts.Any(gid => t.Programmes.Find(t.Groups[gid]?.ProgrammeId) is { IsGuest: true });
}

/// <summary>Who is looking at events. Built on the server from who is signed in, never from the browser.</summary>
public sealed record EventViewer(bool SeesEverything, bool IsEditor, bool IsStudent, bool IsFacilitator, bool IsStaff)
{
    public static EventViewer Administrator { get; } = new(true, false, false, false, false);
    public static EventViewer Nobody { get; } = new(false, false, false, false, false);

    public bool MayChooseAdministrators => SeesEverything;

    /// <summary>The one answer to who sees an event; the screen, the paper and every download ask it.</summary>
    public bool Sees(SchoolEvent e) =>
        SeesEverything
        || (IsEditor && e.Audience != EventAudience.Administrators)
        || e.Audience == EventAudience.Everyone
        || (IsStudent && e.Audience.HasFlag(EventAudience.Students))
        || (IsFacilitator && e.Audience.HasFlag(EventAudience.Facilitators))
        || (IsStaff && e.Audience.HasFlag(EventAudience.Staff));

    /// <summary>Paper never carries an Administrators-only event, for anybody.</summary>
    public bool Prints(SchoolEvent e) => Sees(e) && e.Audience != EventAudience.Administrators;
}
