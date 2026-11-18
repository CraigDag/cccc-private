using System.Text.Json;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Plan;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

/// <summary>Behind a chosen class, when what it needs is elsewhere: drawn from the domain's own answer, and only what the reader may see.</summary>
public class ElsewhereTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);
    private static readonly Facilitator Ada = T.Facilitators.Single(f => f.Name == "Ada Brightwater");

    private static Access As(Role role, Guid? facilitator = null) =>
        new(new Account(Guid.NewGuid(), "x", "X", role, AccountTags.Staff, facilitator, false, false, "s", DateTimeOffset.UnixEpoch, true));

    private static PlanView View(Lens lens, Guid? chosen, DateOnly day, Scale scale = Scale.Day, Focus? focus = null)
    {
        var v = new PlanView { Lens = lens, Scale = scale };
        v.Start(day);
        if (focus is not null) { v.FocusOn(focus); v.Lens = lens; }
        v.Chosen = chosen;
        return v;
    }

    /// <summary>Ada's first class of the sample's Monday that is for a group and in a space.</summary>
    private static Session AdasMonday(Timetable t) =>
        t.Sessions.Where(s => s.Date == Monday && s.FacilitatorIds.Contains(Ada.Id) && !s.Audience.IsEveryone && s.RoomId is not null)
            .OrderBy(s => s.Start).First();

    [Fact]
    public void AChosenClassShadesItsOwnDayByTheDomainsAnswerAndNoOtherDayOrScale()
    {
        var s = AdasMonday(T);
        var admin = As(Role.Administrator);
        var m = PlanModel.Build(T, View(Lens.Spaces, s.Id, Monday), admin, Monday);
        Assert.Equal([Monday], m.Elsewhere.Keys);
        var busy = Busy.For(T, s, Monday, admin.Events);
        Assert.NotEmpty(busy);
        Assert.Equal(busy.Select(Said), m.Elsewhere[Monday].Select(Said));

        // On rows that are spaces, the space's own classes say when it is in use: only people and groups are shaded.
        var bands = m.Bands(Monday, withSpace: false);
        Assert.NotEmpty(bands);
        Assert.All(bands.SelectMany(b => b.Stretches), x => Assert.NotEqual(BusyKind.Space, x.Kind));
        // Each band is its stretches run together and cut to the frame, in order, never touching the next.
        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            Assert.True(band.Start >= m.FrameStart && band.End <= m.FrameEnd && band.End > band.Start, $"Band {band.Start}–{band.End} in a frame of {m.FrameStart}–{m.FrameEnd}.");
            Assert.Equal(band.Start, Math.Max(m.FrameStart, band.Stretches.Min(x => PlanModel.Minutes(x.From))));
            Assert.Equal(band.End, Math.Min(m.FrameEnd, band.Stretches.Max(x => PlanModel.Until(x.Until))));
            if (i > 0) Assert.True(band.Start > bands[i - 1].End, $"Bands {bands[i - 1].End} and {band.Start} touch.");
        }
        Assert.Equal(busy.Count(x => x.Kind != BusyKind.Space && PlanModel.Until(x.Until) > m.FrameStart && PlanModel.Minutes(x.From) < m.FrameEnd),
            bands.Sum(b => b.Stretches.Count));
        // Rows that are not spaces shade the space too.
        Assert.True(m.Bands(Monday, withSpace: true).Sum(b => b.Stretches.Count) > bands.Sum(b => b.Stretches.Count),
            "The sample's Monday has nothing else in Ada's first space.");

        // Another day of the same week with it still chosen, the week, nothing chosen: no shading.
        var tuesday = View(Lens.Spaces, s.Id, Monday);
        tuesday.GoTo(Monday.AddDays(1));
        Assert.Equal(s.Id, tuesday.Chosen);
        Assert.Empty(PlanModel.Build(T, tuesday, admin, Monday).Elsewhere);
        Assert.Empty(PlanModel.Build(T, View(Lens.Spaces, s.Id, Monday, Scale.Week), admin, Monday).Elsewhere);
        Assert.Empty(PlanModel.Build(T, View(Lens.Spaces, null, Monday), admin, Monday).Elsewhere);
    }

    [Fact]
    public void AWeekReadAsDaysShadesEveryDayItCouldBeDraggedOntoAndAdaAwayIsAllOfThursday()
    {
        var s = AdasMonday(T);
        var m = PlanModel.Build(T, View(Lens.People, s.Id, Monday, focus: new Focus("facilitator", Ada.Id, Ada.Name)), As(Role.Administrator), Monday);
        Assert.True(m.RowsAreDays);
        Assert.Equal(m.Days, m.Elsewhere.Keys.Order());
        var thursday = Monday.AddDays(3);
        var whole = Assert.Single(m.Bands(thursday, withSpace: true));
        Assert.True(whole.Away);
        Assert.Equal((m.FrameStart, m.FrameEnd), (whole.Start, whole.End));
        Assert.Contains(whole.Stretches, x => x is { Kind: BusyKind.Away, From.Hour: 0 } && x.Until == TimeOnly.MaxValue && x.PersonId == Ada.Id);
    }

    [Fact]
    public void BandsRunStretchesThatTouchOrOverlapTogetherAndCutThemToTheFrame()
    {
        var ada = new Facilitator { Name = "Ada" };
        var ba = new Group { Name = "BA 1" };
        var monday = new DateOnly(2030, 3, 4);
        Session At(string from, string to, Audience a, params Guid[] who) => new()
        {
            Title = "Class", Date = monday, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to), Audience = a,
            Facilitators = [.. who.Select(id => new Assignment(id))],
        };
        var chosen = At("15:00", "16:00", Audience.ForGroup(ba.Id), ada.Id);
        var t = Timetable.Empty with
        {
            Facilitators = EntitySet<Facilitator>.Of([ada]),
            Groups = EntitySet<Group>.Of([ba]),
            // Ada 09:00–10:00 and 10:00–10:30 touch; a quarter of an hour later is another stretch; Ada 12:00–13:00
            // and BA 1 12:30–13:30 overlap.
            Sessions = EntitySet<Session>.Of([chosen, At("09:00", "10:00", Audience.ForGroups([]), ada.Id), At("10:00", "10:30", Audience.ForGroups([]), ada.Id),
                At("10:45", "11:00", Audience.ForGroups([]), ada.Id), At("12:00", "13:00", Audience.ForGroups([]), ada.Id), At("12:30", "13:30", Audience.ForGroup(ba.Id))]),
            Absences = EntitySet<Absence>.Of([new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = monday.AddDays(1), To = monday.AddDays(1) }]),
        };
        var v = View(Lens.Groups, chosen.Id, monday);
        var m = PlanModel.Build(t, v, As(Role.Administrator), monday);
        Assert.Equal([(540, 630, 2), (645, 660, 1), (720, 810, 2)], m.Bands(monday, withSpace: true).Select(b => (b.Start, b.End, b.Stretches.Count)));

        // Away the whole of a day is the whole of the frame, and no further.
        var week = PlanModel.Build(t, View(Lens.People, chosen.Id, monday, focus: new Focus("facilitator", ada.Id, ada.Name)), As(Role.Administrator), monday);
        var tuesday = Assert.Single(week.Bands(monday.AddDays(1), withSpace: true));
        Assert.Equal((week.FrameStart, week.FrameEnd), (tuesday.Start, tuesday.End));
        Assert.Equal(24 * 60, PlanModel.Until(TimeOnly.MaxValue));
        Assert.Equal(10 * 60, PlanModel.Until(new TimeOnly(10, 0)));
    }

    [Fact]
    public void OnlyWhatTheReaderMaySeeKeepsSomebodyBusy()
    {
        var s = AdasMonday(T);
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(7, 0), End = new(7, 30), Audience = EventAudience.Administrators, FacilitatorIds = [Ada.Id] };
        var t = T with { Events = T.Events.Put(talks) };
        bool Sees(Access a) => PlanModel.Build(t, View(Lens.Spaces, s.Id, Monday), a, Monday).Elsewhere[Monday].Any(x => x.Event?.Id == talks.Id);
        Assert.True(Sees(As(Role.Administrator)));
        Assert.False(Sees(As(Role.Editor)));
        Assert.False(Sees(As(Role.Viewer, Ada.Id)));
    }

    [Fact]
    public void TheWordsSayWhatAndWhoOnOneLineForEachThingAndNeverNameAStudentOrAnEventTheReaderMayNotRead()
    {
        var ada = new Facilitator { Name = "Ada" };
        var bo = new Facilitator { Name = "Bo" };
        var ba = new Group { Name = "BA 1" };
        var prep = new Group { Name = "Prep" };
        var sam = new Student { FirstName = "Sam", Surname = "Sandvik", GroupId = ba.Id };
        var kim = new Student { FirstName = "Kim", Surname = "Krogh", GroupId = ba.Id };
        var studio = new Room { Name = "Studio" };
        var day = new DateOnly(2030, 3, 4);
        Session At(string title, string from, string to, Audience a, Room? r = null, params Guid[] who) => new()
        {
            Title = title, Date = day, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to), Audience = a, RoomId = r?.Id,
            Facilitators = [.. who.Select(id => new Assignment(id))],
        };
        var chosen = At("Acrobatics", "10:00", "11:00", Audience.ForGroup(ba.Id), studio, ada.Id);
        var juggling = At("Juggling", "09:00", "10:00", Audience.ForGroup(ba.Id), null, ada.Id);
        var covered = At("Tumbling", "11:00", "12:00", Audience.ForGroup(prep.Id), null, bo.Id) with { CoverIds = [ada.Id] };
        var extra = At("Hand to hand", "12:00", "13:00", Audience.ForGroup(prep.Id)) with { AddedStudentIds = [sam.Id, kim.Id] };
        var aerial = At("Aerial", "13:00", "14:00", Audience.ForGroup(prep.Id), studio);
        var secret = new SchoolEvent { Title = "Salary talks", Date = day, Start = new(14, 0), End = new(15, 0), Audience = EventAudience.Administrators, RoomId = studio.Id };
        var t = Timetable.Empty with
        {
            Facilitators = EntitySet<Facilitator>.Of([ada, bo]),
            Groups = EntitySet<Group>.Of([ba, prep]),
            Students = EntitySet<Student>.Of([sam, kim]),
            Rooms = EntitySet<Room>.Of([studio]),
            Sessions = EntitySet<Session>.Of([chosen, juggling, covered, extra, aerial]),
            Events = EntitySet<SchoolEvent>.Of([secret]),
            Absences = EntitySet<Absence>.Of([new Absence { Person = AbsentPerson.Facilitator, PersonId = bo.Id, From = day, To = day },
                new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = day, To = day, StartTime = new(15, 0), EndTime = new(16, 0) }]),
        };
        var editor = new EventViewer(false, true, false, false, false);
        var busy = Busy.For(t, chosen, day, editor);

        Assert.Equal(
            [
                "09:00–10:00 Juggling: Ada, BA 1",
                "11:00–12:00 Tumbling: Ada covering",
                "12:00–13:00 Hand to hand: 2 of its students",
                "13:00–14:00 Aerial: Studio in use",
                "14:00–15:00 Booked: Studio booked",
                "15:00–16:00 Away: Ada",
            ],
            ElsewhereWords.Lines(t, busy).Select(l => l.Text));
        Assert.Equal([juggling.Id, covered.Id, extra.Id, aerial.Id, null, null], ElsewhereWords.Lines(t, busy).Select(l => l.ClassId));
        Assert.Equal([false, false, false, false, false, true], ElsewhereWords.Lines(t, busy).Select(l => l.Away));
        Assert.DoesNotContain(ElsewhereWords.Lines(t, busy), l => l.Text.Contains("Sam") || l.Text.Contains("Salary"));
        // An administrator reads what the booking is.
        Assert.Contains("14:00–15:00 Salary talks: Studio booked", ElsewhereWords.Lines(t, Busy.For(t, chosen, day, EventViewer.Administrator)).Select(l => l.Text));
        Assert.Equal("All day", ElsewhereWords.When(TimeOnly.MinValue, TimeOnly.MaxValue));

        Assert.Equal(["Ada busy", "BA 1 busy", "Ada busy", "2 students busy", "Studio in use", "Studio booked", "Ada away"], busy.Select(b => ElsewhereWords.Short(t, b)));

        // What the script is given: minutes, whose stretch it is (a person's, or everybody's), and the words; the space only where asked.
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement[][]>>(ElsewhereWords.DragData(t, new Dictionary<DateOnly, IReadOnlyList<BusyStretch>> { [day] = busy }, withSpace: false))!;
        var rows = data["2030-03-04"];
        Assert.Equal(5, rows.Length);
        Assert.Equal(["540", "600", ada.Id.ToString(), "Ada busy"], rows[0].Select(Text));
        Assert.Equal("", rows[1][2].GetString());
        Assert.DoesNotContain(rows, r => r[3].GetString()!.StartsWith("Studio"));

        // Each person's part, in minutes; -1 where it runs to the class's own edge.
        var parts = JsonSerializer.Deserialize<JsonElement[][]>(ElsewhereWords.Parts(chosen with { Facilitators = [new Assignment(ada.Id), new Assignment(bo.Id, From: new(10, 30))] }))!;
        Assert.Equal([ada.Id.ToString(), "600", "660", "-1", "-1"], parts[0].Select(Text));
        Assert.Equal([bo.Id.ToString(), "630", "660", "630", "-1"], parts[1].Select(Text));
    }

    private static string Said(BusyStretch b) =>
        $"{b.Kind} {b.From}–{b.Until} {b.PersonId} {string.Join("+", b.GroupIds)} {b.Students} {b.RoomId} {b.Class?.Session.Id} {b.Event?.Id}";

    private static string Text(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetInt32().ToString();
}
