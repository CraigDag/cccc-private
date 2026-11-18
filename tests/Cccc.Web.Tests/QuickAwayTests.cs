using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Find;

namespace Cccc.Web.Tests;

/// <summary>Somebody away, typed into Find in one line (<see cref="QuickAway"/>), read against the made-up school on Wednesday 18 November 2026.</summary>
public class QuickAwayTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Today = Monday.AddDays(2);
    private static readonly Timetable T = SampleSchool.Create(Monday, Today);

    private static Guid Person(string name) => T.Facilitators.Single(f => f.Name == name).Id;
    private static Guid Student(string name) => T.Students.Single(s => s.Name == name).Id;

    private static QuickAway.Reading Read(string line, Guid? me = null, DateOnly? shown = null, Timetable? t = null) =>
        QuickAway.Read(t ?? T, line, Today, shown ?? Today, me) ?? throw new Xunit.Sdk.XunitException($"\"{line}\" was not read as somebody away");

    /// <summary>Who, the days and the times, as one line a failure can show whole.</summary>
    private static string Said(QuickAway.Reading r)
    {
        var a = r.Draft;
        var who = a.Person == AbsentPerson.Student ? T.Students[a.PersonId]!.Name : T.Facilitators[a.PersonId]!.Name;
        var times = a.StartTime is { } s && a.EndTime is { } e ? $" {Week.Hm(s)}–{Week.Hm(e)}" : "";
        return $"{who} {a.From:yyyy-MM-dd}{(a.To != a.From ? $" to {a.To:yyyy-MM-dd}" : "")}{times}";
    }

    private static FindHit Records(string line) => QuickAway.Hit(T, Read(line), records: true, Today);

    [Fact]
    public void AnAdministratorRecordsSomebodyAwayTodayWithWhatWasSaidAboutIt()
    {
        var r = Read("ada sick");
        Assert.Equal("Ada Brightwater 2026-11-18", Said(r));
        Assert.Equal((AbsentPerson.Facilitator, (AbsenceCause?)AbsenceCause.Illness, (AbsenceNotice?)AbsenceNotice.Unplanned), (r.Draft.Person, r.Draft.Cause, r.Draft.Notice));
        Assert.Null(r.Draft.Reason);
        Assert.Empty(r.NotUnderstood);
        var hit = QuickAway.Hit(T, r, records: true, Today);
        Assert.Equal("Record Ada Brightwater away · Wed 18 Nov", hit.Label);
        Assert.Equal("illness, unplanned · none of their classes", hit.Detail);
        Assert.Equal("quick-away", hit.Kind);
        Assert.Same(r.Draft, hit.Away);
        Assert.Equal(T.Facilitators[Person("Ada Brightwater")]!.Colour, hit.Colour);
    }

    [Fact]
    public void TheHitSaysWhatItWouldTakeThemOffFromTheRuleTheFormsAsk()
    {
        // Bo shares Friday's guest workshop with Gwen, so it runs without him.
        Assert.Equal("Record Bo Lindqvist away · Fri 20 Nov", Records("bo off fri").Label);
        Assert.Equal("planned · Guest workshop Fri 09:30: Gwen Stroud still teaching it", Records("bo off fri").Detail);
        // Esme teaches Monday's Strength alone.
        Assert.Equal("planned · Strength and conditioning Mon 09:00: needs cover", Records("esme away mon").Detail);
        // Dag's next week: his part of Monday's circus disciplines is Ada's too, and the other three are his alone.
        var dag = Records("dag away 23/11 to 27/11");
        Assert.Equal("Record Dag Øverlie away · Mon 23 Nov – Fri 27 Nov", dag.Label);
        Assert.Equal("planned · takes them off 4 classes, 3 would need cover", dag.Detail);
        // A student misses their group's classes, and never the whole school's lunch.
        var alba = Records("alba absent mon-wed");
        Assert.Equal("Record Alba Ashcombe away · Mon 23 Nov – Wed 25 Nov", alba.Label);
        Assert.Equal("planned · misses 7 classes", alba.Detail);
        // Dag's week before last: the morning he covered for Bo is said apart, as its cover would then need changing.
        Assert.Equal("unplanned · takes them off 5 classes, 3 would need cover, 1 they cover", Records("dag away 9/11 to 13/11").Detail);
        Assert.Equal("illness, unplanned · misses Aerial Tue 09:00", Records("alba sick yesterday morning").Detail);
    }

    [Theory]
    [InlineData("bo away fri", "Bo Lindqvist 2026-11-20")]
    [InlineData("bo away friday", "Bo Lindqvist 2026-11-20")]
    [InlineData("bo away next fri", "Bo Lindqvist 2026-11-27")]
    [InlineData("bo away tomorrow", "Bo Lindqvist 2026-11-19")]
    [InlineData("bo away yesterday", "Bo Lindqvist 2026-11-17")]
    [InlineData("bo away 4/12", "Bo Lindqvist 2026-12-04")]
    [InlineData("bo away 4.12.", "Bo Lindqvist 2026-12-04")]
    [InlineData("bo away 4 dec", "Bo Lindqvist 2026-12-04")]
    [InlineData("bo away mon to wed", "Bo Lindqvist 2026-11-23 to 2026-11-25")]
    [InlineData("bo away from mon until wed", "Bo Lindqvist 2026-11-23 to 2026-11-25")]
    [InlineData("bo away mon-wed", "Bo Lindqvist 2026-11-23 to 2026-11-25")]
    [InlineData("bo away fri to tue", "Bo Lindqvist 2026-11-20 to 2026-11-24")]
    [InlineData("bo away 1/12-4/12", "Bo Lindqvist 2026-12-01 to 2026-12-04")]
    [InlineData("bo away 28/12 to 3/1", "Bo Lindqvist 2026-12-28 to 2027-01-03")]
    [InlineData("bo away until fri", "Bo Lindqvist 2026-11-18 to 2026-11-20")]
    [InlineData("bo away till monday", "Bo Lindqvist 2026-11-18 to 2026-11-23")]
    [InlineData("bo away this week", "Bo Lindqvist 2026-11-18 to 2026-11-22")]
    [InlineData("bo away all week", "Bo Lindqvist 2026-11-18 to 2026-11-22")]
    [InlineData("bo away next week", "Bo Lindqvist 2026-11-23 to 2026-11-29")]
    public void DaysAndStretchesOfThemAreReadTheWaysPeopleSayThem(string line, string said) => Assert.Equal(said, Said(Read(line)));

    [Theory]
    [InlineData("bo away fri 9-12", "Bo Lindqvist 2026-11-20 09:00–12:00")]
    [InlineData("bo away fri 9 to 12", "Bo Lindqvist 2026-11-20 09:00–12:00")]
    [InlineData("bo away fri 13:00-15:30", "Bo Lindqvist 2026-11-20 13:00–15:30")]
    [InlineData("bo away fri 1-3", "Bo Lindqvist 2026-11-20 13:00–15:00")]
    [InlineData("bo away fri morning", "Bo Lindqvist 2026-11-20 00:00–12:00")]
    [InlineData("bo away fri afternoon", "Bo Lindqvist 2026-11-20 12:00–23:45")]
    [InlineData("bo away fri evening", "Bo Lindqvist 2026-11-20 17:00–23:45")]
    [InlineData("bo away fri from 14", "Bo Lindqvist 2026-11-20 14:00–23:45")]
    [InlineData("bo away fri after 2pm", "Bo Lindqvist 2026-11-20 14:00–23:45")]
    [InlineData("bo away fri until 11", "Bo Lindqvist 2026-11-20 00:00–11:00")]
    [InlineData("bo away fri 9:10-11:50", "Bo Lindqvist 2026-11-20 09:15–11:45")]
    [InlineData("bo away fri all day", "Bo Lindqvist 2026-11-20")]
    public void TimesAreReadToo(string line, string said) => Assert.Equal(said, Said(Read(line)));

    [Fact]
    public void AWindowIsSaidInFindTheWayItWouldBeSaidAloud()
    {
        Assert.Equal("Record Bo Lindqvist away · Fri 20 Nov 09:00–12:00", Records("bo away fri 9-12").Label);
        Assert.Equal("Record Bo Lindqvist away · Fri 20 Nov until 12:00", Records("bo away fri morning").Label);
        Assert.Equal("Record Bo Lindqvist away · Fri 20 Nov from 14:00", Records("bo away fri from 14").Label);
        // Out for a morning Ada shares with Bo, she leaves him the class.
        Assert.Equal("planned · Acrobatics Mon 10:45: Bo Lindqvist still teaching it", Records("ada away mon morning").Detail);
        // Cleo's Thursday afternoon is Esme's too; her morning has nothing.
        Assert.Equal("planned · Creation lab Thu 13:00: Esme Halloran still teaching it", Records("cleo out tomorrow afternoon").Detail);
        Assert.Equal("planned · none of their classes", Records("cleo out tomorrow 9-12").Detail);
    }

    [Theory]
    [InlineData("ada is off sick", AbsenceCause.Illness, AbsenceNotice.Unplanned)]
    [InlineData("ada ill tomorrow", AbsenceCause.Illness, AbsenceNotice.Unplanned)]
    [InlineData("ada unwell fri", AbsenceCause.Illness, AbsenceNotice.Unplanned)]
    [InlineData("ada sick fri planned", AbsenceCause.Illness, AbsenceNotice.Planned)]
    [InlineData("ada on leave fri", AbsenceCause.ApprovedLeave, AbsenceNotice.Planned)]
    [InlineData("ada annual leave next week", AbsenceCause.ApprovedLeave, AbsenceNotice.Planned)]
    [InlineData("ada holiday today", AbsenceCause.ApprovedLeave, AbsenceNotice.Unplanned)]
    [InlineData("ada away fri personal", AbsenceCause.Personal, AbsenceNotice.Planned)]
    [InlineData("ada away fri unplanned", null, AbsenceNotice.Unplanned)]
    [InlineData("ada can't come fri", null, AbsenceNotice.Planned)]
    [InlineData("ada can’t come fri", null, AbsenceNotice.Planned)]
    [InlineData("ada not in today", null, AbsenceNotice.Unplanned)]
    [InlineData("ada will be out fri", null, AbsenceNotice.Planned)]
    [InlineData("ada away fri all day", null, AbsenceNotice.Planned)]
    public void WhatIsSaidAboutItIsItsCauseAndNoticeAndNeverItsReason(string line, AbsenceCause? cause, AbsenceNotice notice)
    {
        var r = Read(line);
        Assert.Equal((cause, (AbsenceNotice?)notice), (r.Draft.Cause, r.Draft.Notice));
        Assert.Null(r.Draft.Reason);
        Assert.Empty(r.NotUnderstood);
    }

    [Fact]
    public void WordsNotUnderstoodAreSaidAndKeptNowhere()
    {
        // Whatever else is typed is said in Find and stored nowhere: a line about being ill can carry what nobody should keep.
        var r = Read("ada sick fri flu");
        Assert.Equal(["flu"], r.NotUnderstood);
        Assert.Null(r.Draft.Reason);
        Assert.Equal("illness, unplanned · none of their classes · not understood: flu", Records("ada sick tomorrow flu").Detail);
        // One person at a time; a second date that does not join the first; a stretch that ends before it starts.
        Assert.Equal(["Bo Lindqvist"], Read("ada and bo away fri").NotUnderstood);
        Assert.Equal(["wed"], Read("ada away mon wed").NotUnderstood);
        Assert.Equal(["4 dec"], Read("ada away mon 4 dec").NotUnderstood);
        // A date said in two words takes both.
        Assert.Empty(Read("ada away 4 dec").NotUnderstood);
        Assert.Empty(Read("ada away dec 4th").NotUnderstood);
        var backwards = Read("ada away 4/12 to 1/12");
        Assert.Equal("Ada Brightwater 2026-12-04", Said(backwards));
        Assert.Equal(["to Tue 1 Dec"], backwards.NotUnderstood);
    }

    [Fact]
    public void NamesAreWholeOrTheStartOfOneAndAStudentAndAFacilitatorWithOneFirstNameAreNeither()
    {
        Assert.Equal("Bo Lindqvist 2026-11-20", Said(Read("bo lindqvist away fri")));
        Assert.Equal("Bo Lindqvist 2026-11-20", Said(Read("lindq away fri")));
        Assert.Equal("Dag Øverlie 2026-11-20", Said(Read("øverlie away fri")));
        Assert.Equal("Finn Aalborg-Reyes 2026-11-20", Said(Read("finn aalborg-reyes away fri")));
        Assert.Equal(Student("Carys Corvino"), Read("carys sick").Draft.PersonId);
        Assert.Equal(AbsentPerson.Student, Read("carys sick").Draft.Person);
        // A student called Ada: "ada" is neither, so it is nobody's absence, and never quietly yours.
        var twoAdas = T with { Students = T.Students.Put(new Student { FirstName = "Ada", Surname = "Quist" }) };
        Assert.Null(QuickAway.Read(twoAdas, "ada sick", Today, Today, null));
        Assert.Null(QuickAway.Read(twoAdas, "ada sick", Today, Today, Person("Cleo Marchetti")));
        Assert.Equal("Ada Brightwater 2026-11-18", Said(Read("ada brightwater sick", t: twoAdas)));
        Assert.Equal(["ada (Ada Brightwater or Ada Quist?)"], Read("bo and ada sick", t: twoAdas).NotUnderstood);
    }

    [Fact]
    public void WithNobodyNamedItIsWhoeverIsSignedInAndOnlyThen()
    {
        var cleo = Person("Cleo Marchetti");
        Assert.Equal("Cleo Marchetti 2026-11-19", Said(Read("sick tomorrow", me: cleo)));
        Assert.Equal("Cleo Marchetti 2026-11-19", Said(Read("I'm sick tomorrow", me: cleo)));
        Assert.Equal("Cleo Marchetti 2026-11-19", Said(Read("i can't come tomorrow", me: cleo)));
        Assert.Null(QuickAway.Read(T, "sick tomorrow", Today, Today, null));
        // A word that may be somebody gone, or nobody, is not you.
        Assert.Null(QuickAway.Read(T, "zed sick tomorrow", Today, Today, cleo));
        Assert.Equal(["me"], Read("bo sick me", me: cleo).NotUnderstood);
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("ada fri")]
    [InlineData("ada fri 9-12")]
    [InlineData("aerial fri 13")]
    [InlineData("staff meeting fri")]
    [InlineData("zed away")]
    [InlineData("")]
    public void NothingIsOfferedWithoutSomebodyAndAWordThatSaysTheyAreAway(string line) =>
        Assert.Null(QuickAway.Read(T, line, Today, Today, null));

    [Fact]
    public void ADayNamedWhileTheScratchWeekIsOnScreenIsAReadDay()
    {
        var scratch = Week.DateOf(Week.ScratchMonday, DayOfWeek.Wednesday);
        Assert.Equal("Bo Lindqvist 2026-11-20", Said(Read("bo away fri", shown: scratch)));
        // And a later week on screen moves "fri" to that week, as for a class, and "this week" and "next week" with it.
        Assert.Equal("Bo Lindqvist 2026-11-27", Said(Read("bo away fri", shown: Monday.AddDays(7))));
        Assert.Equal("Bo Lindqvist 2026-11-23 to 2026-11-29", Said(Read("bo away this week", shown: Monday.AddDays(7))));
        Assert.Equal("Bo Lindqvist 2026-11-30 to 2026-12-06", Said(Read("bo away next week", shown: Monday.AddDays(7))));
    }

    [Fact]
    public void AFacilitatorAsksForThemselvesSayingOnlyTheDaysGo()
    {
        var cleo = Person("Cleo Marchetti");
        var hit = QuickAway.Hit(T, Read("sick tomorrow", me: cleo), records: false, Today);
        Assert.Equal("quick-ask", hit.Kind);
        Assert.Equal("Ask to be away · Thu 19 Nov", hit.Label);
        Assert.Equal("Creation lab Thu 13:00: Esme Halloran still teaching it · only the days are sent, never why", hit.Detail);
        Assert.Equal("none of your classes · only the days are sent, never why", QuickAway.Hit(T, Read("away tomorrow 9-12", me: cleo), records: false, Today).Detail);
        var gone = QuickAway.Hit(T, Read("away yesterday", me: cleo), records: false, Today);
        Assert.Equal("that day has gone: say from today on · Aerial Tue 09:00: needs cover · only the days are sent, never why", gone.Detail);
    }

    [Fact]
    public void FindOffersItFirstToAnAdministratorAndToAFacilitatorOnlyForThemselves()
    {
        var cleo = Person("Cleo Marchetti");
        FindHit? First(string q, bool mayEdit, bool admin, bool records, Guid? me) =>
            FindIndex.Search(T, q, mayEdit, admin, today: Today, shown: Today, recordsAway: records, me: me).FirstOrDefault();
        Assert.Equal("quick-away", First("bo sick fri", true, true, true, null)?.Kind);
        Assert.Equal("quick-ask", First("sick tomorrow", false, false, false, cleo)?.Kind);
        Assert.Equal("quick-ask", First("cleo away tomorrow", false, false, false, cleo)?.Kind);
        // Somebody else, for a facilitator, is nothing they may ask; nor for an editor who is nobody.
        Assert.DoesNotContain(FindIndex.Search(T, "bo sick fri", false, false, today: Today, shown: Today, me: cleo), h => h.Kind is "quick-ask" or "quick-away");
        Assert.DoesNotContain(FindIndex.Search(T, "bo sick fri", true, false, today: Today, shown: Today), h => h.Kind is "quick-ask" or "quick-away");
        // Whoever reads it, a line about somebody away is never a class titled with its words.
        Assert.DoesNotContain(FindIndex.Search(T, "bo off fri 9-12", true, false, today: Today, shown: Today), h => h.Kind == "quick-add");
        Assert.Equal(["quick-away"], FindIndex.Search(T, "bo off fri 9-12", true, true, today: Today, shown: Today, recordsAway: true).Select(h => h.Kind));
        // A class named with it is still a class to add, after the absence.
        Assert.Equal(["quick-away", "quick-add"], FindIndex.Search(T, "aerial off fri 13 bo", true, true, today: Today, shown: Today, recordsAway: true).Take(2).Select(h => h.Kind));
    }
}
