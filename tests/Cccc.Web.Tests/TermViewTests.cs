using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Plan;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

/// <summary>The term at a glance on the plan: each row's figure is the week's own, and only what the reader may see.</summary>
public class TermViewTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);

    private static Access As(Role role, Guid? facilitator = null) =>
        new(new Account(Guid.NewGuid(), "x", "X", role, AccountTags.Staff, facilitator, false, false, "s", DateTimeOffset.UnixEpoch, true));

    private static PlanView View(Lens lens, Focus? focus = null, bool guests = false)
    {
        var v = new PlanView { Scale = Scale.Term, ShowGuests = guests };
        v.Start(Monday);
        if (focus is not null) v.FocusOn(focus);
        v.Lens = lens;
        return v;
    }

    [Fact]
    public void ACoachSeesTheirOwnHoursAndEverybodyElsesClassesAndNoShadeSaysAnythingOfAnybodyElsesHours()
    {
        var ada = T.Facilitators.Single(f => f.Name == "Ada Brightwater");
        var coach = TermModel.Build(T, View(Lens.People), As(Role.Viewer, ada.Id), Monday);
        var admin = TermModel.Build(T, View(Lens.People), As(Role.Administrator), Monday);
        Assert.Equal(admin.Rows.Select(r => r.Id), coach.Rows.Select(r => r.Id));
        Assert.True(coach.Rows.Count >= 4, $"The sample drew {coach.Rows.Count} people.");

        var week = coach.Weeks.Single(w => w.Monday == Monday);
        foreach (var row in coach.Rows)
        {
            var c = coach.Cell(row, week);
            var a = admin.Cell(row, week);
            Assert.Equal(a is null, c is null);
            if (c is null || a is null) continue;
            var hours = Week.Duration(week.PersonHours[row.Id].Total);
            Assert.Equal(hours, a.Text);
            Assert.Equal(row.Id == ada.Id ? hours : week.PersonClasses[row.Id].ToString(), c.Text);
        }

        // The term's total beside each row is its weeks added up: hours where the reader sees hours, else classes.
        foreach (var row in coach.Rows)
        {
            var theirs = coach.Weeks.Where(w => w.PersonClasses.ContainsKey(row.Id) || w.PersonHours.ContainsKey(row.Id)).ToList();
            var hoursSum = Week.Duration(theirs.Aggregate(TimeSpan.Zero, (x, w) => x + (w.PersonHours.GetValueOrDefault(row.Id)?.Total ?? TimeSpan.Zero)));
            Assert.Equal(hoursSum, admin.Total(row)!.Text);
            Assert.Equal(row.Id == ada.Id ? hoursSum : theirs.Sum(w => w.PersonClasses.GetValueOrDefault(row.Id)).ToString(), coach.Total(row)!.Text);
        }

        // Shaded against what the reader may see: a coach's own busiest week is full, which beside everybody's it is not.
        double Fullest(TermModel m) => m.Weeks.Select(w => m.Cell(m.Rows.Single(r => r.Id == ada.Id), w)?.Fill ?? 0).Max();
        Assert.Equal(1.0, Fullest(coach));
        Assert.True(Fullest(admin) < 1.0, $"Ada's fullest week is {Fullest(admin)} of everybody's.");
        Assert.NotEqual(admin.Measure, coach.Measure);
    }

    [Fact]
    public void APersonWithNoClassInTheTermIsNoRowUnlessAskedFor()
    {
        var idle = new Facilitator { Name = "Idun Ingersen" };
        var t = T with { Facilitators = T.Facilitators.Put(idle) };
        var m = TermModel.Build(t, View(Lens.People), As(Role.Administrator), Monday);
        Assert.DoesNotContain(m.Rows, r => r.Id == idle.Id);
        Assert.Contains(m.Rows, r => r.Name == "Ada Brightwater");
        var asked = TermModel.Build(t, View(Lens.People, new Focus("facilitator", idle.Id, idle.Name)), As(Role.Administrator), Monday);
        Assert.Equal([idle.Id], asked.Rows.Select(r => r.Id));
        Assert.Empty(asked.Cells);
    }

    [Fact]
    public void AGuestGroupIsARowOnlyWhenGuestsAreShownOrItIsAskedForAndTheBarSaysHowManyAreHidden()
    {
        var troupe = T.Groups.Single(g => g.Name == "Nordlys Troupe");
        var visiting = T.Programmes.Find(troupe.ProgrammeId)!;
        var admin = As(Role.Administrator);

        var plain = TermModel.Build(T, View(Lens.Groups), admin, Monday);
        Assert.DoesNotContain(plain.Rows, r => r.Id == troupe.Id);
        Assert.Equal(1, plain.HiddenGuestGroups);

        var shown = TermModel.Build(T, View(Lens.Groups, guests: true), admin, Monday);
        Assert.Contains(shown.Rows, r => r.Id == troupe.Id);
        Assert.Equal(0, shown.HiddenGuestGroups);

        var asked = TermModel.Build(T, View(Lens.Groups, new Focus("programme", visiting.Id, visiting.Name)), admin, Monday);
        Assert.Equal([troupe.Id], asked.Rows.Select(r => r.Id));
        Assert.Equal(0, asked.HiddenGuestGroups);

        // The other lenses hide no group.
        Assert.Equal(0, TermModel.Build(T, View(Lens.Spaces), admin, Monday).HiddenGuestGroups);
    }

    [Fact]
    public void AGroupShortOfItsTargetSaysHowManyStudentsAreShortAndAClosedWeekSaysClosed()
    {
        var prep = T.Groups.Single(g => g.Name == "Prep");
        var ba1 = T.Groups.Single(g => g.Name == "BA 1");
        var m = TermModel.Build(T, View(Lens.Groups), As(Role.Administrator), Monday);
        var prepRow = m.Rows.Single(r => r.Id == prep.Id);
        var baRow = m.Rows.Single(r => r.Id == ba1.Id);
        var built = m.Weeks.Where(w => !w.Blank && w.ClosedAllWeek is null).ToList();
        Assert.Equal(4, built.Count);
        foreach (var w in built)
        {
            var c = m.Cell(prepRow, w)!;
            Assert.True(c.Warn, $"Prep in the week of {w.Monday} reads {c.Text}.");
            Assert.Equal("6 short", c.Note);
            Assert.False(m.Cell(baRow, w)!.Warn);
            Assert.Null(m.Cell(baRow, w)!.Note);
        }
        Assert.Equal("8h 30m", m.Cell(prepRow, m.Weeks.Single(w => w.Monday == Monday))!.Text);
        // The term's total: every built week added up, and how many of them were short.
        Assert.Equal("37h", m.Total(prepRow)!.Text);
        Assert.Equal("4 short", m.Total(prepRow)!.Note);
        Assert.True(m.Total(prepRow)!.Warn);
        Assert.Equal("64h", m.Total(baRow)!.Text);
        Assert.Null(m.Total(baRow)!.Note);

        // A group closed all week reads Closed, not nought hours short of its target.
        var closed = T with
        {
            Closures = T.Closures.Put(new Closure { Name = "Study week", From = Monday, To = Monday.AddDays(4), Scope = ClosureScope.Groups, GroupIds = [prep.Id] }),
        };
        var shut = TermModel.Build(closed, View(Lens.Groups), As(Role.Administrator), Monday);
        var shutRow = shut.Rows.Single(r => r.Id == prep.Id);
        var cell = shut.Cell(shutRow, shut.Weeks.Single(w => w.Monday == Monday))!;
        Assert.Equal("Closed", cell.Text);
        Assert.False(cell.Warn);
        // A week closed to the group is none of its term: three weeks counted, three short.
        Assert.Equal("28h 30m", shut.Total(shutRow)!.Text);
        Assert.Equal("3 short", shut.Total(shutRow)!.Note);
        Assert.EndsWith("counted over 3 weeks, 3 weeks short of the target", shut.Total(shutRow)!.Title);
    }

    [Fact]
    public void AStudentsTermIsTheirHoursAgainstTheTargetForAnAdministratorAndHowManyClassesForAnybodyElse()
    {
        var student = T.Students.First(s => T.Groups.Find(s.GroupId)?.Name == "Prep");
        var focus = new Focus("student", student.Id, student.Name);
        var admin = TermModel.Build(T, View(Lens.People, focus), As(Role.Administrator), Monday);
        var editor = TermModel.Build(T, View(Lens.People, focus), As(Role.Editor), Monday);
        var row = Assert.Single(admin.Rows);
        Assert.Equal(student.Id, row.Id);
        var week = admin.Weeks.Single(w => w.Monday == Monday);
        var a = admin.Cell(row, week)!;
        var e = editor.Cell(editor.Rows.Single(), week)!;
        Assert.Equal("8h 30m", a.Text);
        Assert.True(a.Warn);
        Assert.Matches("^[0-9]+$", e.Text);
        Assert.False(e.Warn);
        Assert.EndsWith(e.Text == "1" ? " 1 class" : $" {e.Text} classes", e.Title);
    }
}
