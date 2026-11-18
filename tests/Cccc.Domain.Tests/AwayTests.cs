using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// What an absence takes somebody off (<see cref="Away"/>), said before it is recorded: the one answer the
/// absence form, a facilitator asking and a line typed into Find all give.
/// </summary>
public class AwayTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private static TimeOnly? At(string? hm) => hm is null ? null : TimeOnly.Parse(hm);

    [Fact]
    public void AStudentMissesTheClassesTheyAreOnThatItsTimesReachAndNoOthers()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var alba = b.Student("Alba", ba1);
        b.Student("Benno", ba1);
        var ada = b.Facilitator("Ada");
        var aerial = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba1.Id), null, b.Class("Aerial"), In(ada));
        b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba2.Id), null, b.Class("Juggling"), In(ada));   // not their group
        b.Session(Tuesday, "12:00", "12:45", Audience.Everyone, null, b.Class("Lunch"));                      // nobody is on it
        var added = b.Session(Tuesday, "13:00", "14:00", Audience.ForGroup(ba2.Id), null, b.Class("Creation lab"), In(ada));
        b.Put(added with { AddedStudentIds = [alba.Id] });
        var leftOff = b.Session(Tuesday, "15:00", "16:00", Audience.ForGroup(ba1.Id), null, b.Class("Strength"));
        b.Put(leftOff with { LeftOffStudentIds = [alba.Id] });
        var cancelled = b.Session(Tuesday, "16:00", "17:00", Audience.ForGroup(ba1.Id), null, b.Class("Dance"));
        b.Put(cancelled with { Cancelled = true });
        b.Session(Tuesday.AddDays(1), "09:00", "10:00", Audience.ForGroup(ba1.Id), null, b.Class("Acrobatics"));   // another day

        Absence Away(string? from = null, string? until = null) => new()
        {
            Person = AbsentPerson.Student, PersonId = alba.Id, From = Tuesday, To = Tuesday, StartTime = At(from), EndTime = At(until),
        };
        Assert.Equal([aerial.Id, added.Id], Rules.Away.TakesOff(b.T, Away()).Select(o => o.Session.Id));
        Assert.Equal([added.Id], Rules.Away.TakesOff(b.T, Away("11:00", "13:15")).Select(o => o.Session.Id));
        Assert.Empty(Rules.Away.TakesOff(b.T, Away("11:00", "13:00")));
        // Two days: the next day's class too.
        Assert.Equal(3, Rules.Away.TakesOff(b.T, Away() with { To = Tuesday.AddDays(1) }).Count);
        // A student being away changes nothing about how a class runs.
        Assert.All(Rules.Away.WhatItLeaves(b.T, Away()), x => Assert.Equal(x.Now.State, x.After.State));
    }

    [Fact]
    public void AnAbsenceBeingChangedStillListsTheClassesItTakesThemOff()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        var shared = b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Creation lab"), In(ada), In(bo));
        var recorded = new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = Tuesday, To = Tuesday };
        b.T = b.T with { Absences = b.T.Absences.Put(recorded) };

        // With it in, Ada is in no room that day; read without it, it takes her off both.
        Assert.Equal([acro.Id, shared.Id], Rules.Away.TakesOff(b.T, recorded).Select(o => o.Session.Id));
        var leaves = Rules.Away.WhatItLeaves(b.T, recorded);
        Assert.Equal([(acro.Id, true), (shared.Id, false)], leaves.Select(x => (x.Now.Session.Id, x.After.NeedsCover)));
        // Changed to the afternoon, it leaves Acrobatics alone, and Bo still has all of the lab.
        var afternoon = Rules.Away.WhatItLeaves(b.T, recorded with { StartTime = new(12, 0), EndTime = new(16, 0) });
        Assert.Equal([(shared.Id, false)], afternoon.Select(x => (x.Now.Session.Id, x.After.NeedsCover)));
        Assert.Equal([bo.Id], afternoon[0].After.InTheRoom);
        // A second absence for the same day takes her off nothing: she is off it already.
        Assert.Empty(Rules.Away.TakesOff(b.T, new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = Tuesday, To = Tuesday }));
    }

    [Fact]
    public void SaidBeforeItsFirstDayAnAbsenceIsPlannedOnTheDayUnplannedAndAnIllnessIsNeverPlanned()
    {
        Assert.Equal(AbsenceNotice.Planned, Absence.NoticeWhenSaid(Monday, Tuesday, null));
        Assert.Equal(AbsenceNotice.Planned, Absence.NoticeWhenSaid(Monday, Tuesday, AbsenceCause.ApprovedLeave));
        Assert.Equal(AbsenceNotice.Unplanned, Absence.NoticeWhenSaid(Tuesday, Tuesday, null));
        Assert.Equal(AbsenceNotice.Unplanned, Absence.NoticeWhenSaid(Tuesday.AddDays(1), Tuesday, AbsenceCause.Personal));
        Assert.Equal(AbsenceNotice.Unplanned, Absence.NoticeWhenSaid(Monday, Tuesday, AbsenceCause.Illness));
    }
}
