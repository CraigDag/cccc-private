using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Cccc.Web.Features.Print;

using Week = Cccc.Domain.Week;

/// <summary>
/// The printed documents. One heading rule for all of them — "CCCC Scheduler · Week 37, 7–13
/// September 2026 · shape" — and never the project's name, which can be a person's. Paper never
/// carries an Administrators-only event. Every one is generated through <see cref="PdfGate"/>.
/// </summary>
public static class Sheets
{
    private const float Margin = 24;
    private static readonly string Ink = "#1B1F1E";
    private static readonly string Quiet = "#5B6461";
    private static readonly string Line = "#C9D0CD";

    private static string Head(DateOnly monday, string shape) => $"CCCC Scheduler · {Week.ForDocuments(monday)} · {shape}";

    private static void Frame(PageDescriptor page, string heading, string narrowed, DateTimeOffset printed, bool landscape = true)
    {
        page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(Margin);
        page.DefaultTextStyle(x => x.FontSize(8).FontColor(Ink));
        page.Header().PaddingBottom(6).Row(r =>
        {
            r.RelativeItem().Text(heading).SemiBold().FontSize(11);
            r.AutoItem().AlignRight().Text(narrowed).FontColor(Quiet);
        });
        page.Footer().Row(r =>
        {
            r.RelativeItem().Text($"Printed {printed:d MMM yyyy HH:mm}").FontColor(Quiet).FontSize(7);
            r.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontColor(Quiet).FontSize(7));
                t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
            });
        });
    }

    private static string Wash(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return "#ECEFEE";
        int r = Convert.ToInt32(hex[1..3], 16), g = Convert.ToInt32(hex[3..5], 16), b = Convert.ToInt32(hex[5..7], 16);
        int Mix(int c) => (int)Math.Round(c * 0.22 + 255 * 0.78);
        return $"#{Mix(r):X2}{Mix(g):X2}{Mix(b):X2}";
    }

    private static string Spine(string? hex) => hex is { Length: 7 } && hex[0] == '#' ? hex : "#8C9792";

    /// <summary>The week as shown: days across, hours down, the same lanes as the screen.</summary>
    public static IDocument WeekCalendar(Timetable t, WeekModel m, string narrowed, DateTimeOffset printed, EventViewer viewer, string shape = "Calendar")
    {
        return Document.Create(doc => doc.Page(page =>
        {
            Frame(page, Head(m.Monday, shape), narrowed, printed);
            page.Content().Element(c => Calendar(c, t, m, viewer));
        }));
    }

    private static void Calendar(IContainer c, Timetable t, WeekModel m, EventViewer viewer)
    {
        const float width = 842 - 2 * Margin, gutter = 26, dayHead = 26;
        float height = 595 - 2 * Margin - 50;
        int hours = Math.Max(1, (m.FrameEnd - m.FrameStart) / 60);
        float hourH = (height - dayHead) / hours;
        float dayW = (width - gutter) / Math.Max(1, m.Days.Count);
        var events = m.Events.Where(e => viewer.Prints(e.Event)).ToList();

        c.Height(height).Layers(layers =>
        {
            layers.PrimaryLayer().Row(row =>
            {
                row.ConstantItem(gutter).Column(col =>
                {
                    col.Item().Height(dayHead);
                    for (int h = 0; h < hours; h++)
                        col.Item().Height(hourH).Text($"{m.FrameStart / 60 + h:00}:00").FontSize(6).FontColor(Quiet);
                });
                foreach (var d in m.Days)
                    row.ConstantItem(dayW).Column(col =>
                    {
                        col.Item().Height(dayHead).BorderBottom(0.5f).BorderColor(Line).Column(h =>
                        {
                            h.Item().Text(Week.ShortDate(d)).SemiBold();
                            var line = string.Join(" · ", events.Where(e => e.Covers(d)).Select(e => (e.Event.AllDay ? "" : Week.Hm(e.Event.Start) + " ") + e.Event.Title));
                            if (Occurrences.ClosureFor(t, d, null) is { } closed) line = "Closed · " + closed.Name + (line.Length > 0 ? " · " + line : "");
                            if (line.Length > 0) h.Item().Text(line).FontSize(6).FontColor(Quiet).ClampLines(1);
                        });
                        for (int h = 0; h < hours; h++)
                            col.Item().Height(hourH).BorderBottom(0.25f).BorderLeft(0.25f).BorderColor(Line);
                    });
            });

            for (int i = 0; i < m.Days.Count; i++)
            {
                var day = m.Days[i];
                var seen = new HashSet<Guid>();
                foreach (var p in m.Placed.Where(p => p.Date == day))
                {
                    var o = m.Find(p.Id);
                    if (o is null) continue;
                    bool first = seen.Add(p.Id);
                    float x = gutter + dayW * i + (float)(p.Left * dayW) + 0.5f;
                    float w = Math.Max(4, (float)(p.Width * dayW) - 1.5f);
                    float y = dayHead + (p.StartMinutes - m.FrameStart) / 60f * hourH + 0.5f;
                    float h = Math.Max(6, (p.EndMinutes - p.StartMinutes) / 60f * hourH - 1f);
                    var colour = Looks.ColourOf(t, o.Session);
                    layers.Layer().OffsetX(x).OffsetY(y).Width(w).Height(h)
                        .Background(Wash(colour)).BorderLeft(2.5f).BorderColor(Spine(colour))
                        .Element(b => o.NeedsCover ? b.Border(0.75f).BorderColor("#B26B00") : m.Ringed.Contains(o.Session.Id) ? b.Border(0.75f).BorderColor("#B00020") : b)
                        .PaddingLeft(2).PaddingTop(1).Column(col =>
                        {
                            if (!first) return;
                            var title = col.Item().Text(Looks.Title(t, o.Session)).SemiBold().FontSize(6.5f).ClampLines(2);
                            if (!o.Happens) title.Strikethrough();
                            col.Item().Text(Looks.When(o.Session) + (Looks.Room(t, o.Session) is { Length: > 0 } r ? " · " + r : "")).FontSize(5.5f).FontColor(Quiet).ClampLines(1);
                            var who = Looks.Initials(t, o);
                            var state = o.NeedsCover ? "COVER" : Looks.StateWord(o).ToUpperInvariant();
                            if (who.Length + state.Length > 0)
                                col.Item().Text((who + " " + state).Trim()).FontSize(5.5f).FontColor(Quiet).ClampLines(1);
                        });
                }
            }
        });
    }

    /// <summary>The week as a list, one row per session.</summary>
    public static IDocument WeekList(Timetable t, WeekModel m, string narrowed, DateTimeOffset printed, EventViewer viewer)
    {
        var events = m.Events.Where(e => viewer.Prints(e.Event)).ToList();
        return Document.Create(doc => doc.Page(page =>
        {
            Frame(page, Head(m.Monday, "List"), narrowed, printed);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(62); c.ConstantColumn(58); c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(3); c.RelativeColumn(1.4f);
                });
                table.Header(h =>
                {
                    foreach (var name in new[] { "Day", "Time", "Session", "For", "Room", "In the room", "" })
                        h.Cell().BorderBottom(0.75f).BorderColor(Ink).PaddingVertical(2).Text(name).SemiBold();
                });
                foreach (var d in m.Days)
                {
                    foreach (var e in events.Where(e => e.Covers(d)))
                    {
                        table.Cell().Element(Row).Text(Week.ShortDate(d));
                        table.Cell().Element(Row).Text(e.Event.AllDay ? "All day" : $"{Week.Hm(e.Event.Start)}–{Week.Hm(e.Event.End)}");
                        table.Cell().ColumnSpan(5).Element(Row).Text("Event: " + e.Event.Title).Italic();
                    }
                    foreach (var o in m.Visible.Where(o => o.Date == d).OrderBy(o => o.Start).ThenBy(o => Looks.Title(t, o.Session), NameOrder.Comparer))
                    {
                        table.Cell().Element(Row).Text(Week.ShortDate(d));
                        table.Cell().Element(Row).Text(Looks.When(o.Session));
                        var title = table.Cell().Element(Row).Text(Looks.Title(t, o.Session));
                        if (!o.Happens) title.Strikethrough();
                        table.Cell().Element(Row).Text(Looks.Audience(t, o.Session));
                        table.Cell().Element(Row).Text(Looks.Room(t, o.Session));
                        table.Cell().Element(Row).Text(Looks.Names(t, o.InTheRoom));
                        table.Cell().Element(Row).Text(Looks.StateWord(o));
                    }
                }
            });
        }));
    }

    private static IContainer Row(IContainer c) => c.BorderBottom(0.25f).BorderColor(Line).PaddingVertical(2).PaddingRight(4);

    /// <summary>The room grid: a page a day, a row per room.</summary>
    public static IDocument RoomGrid(Timetable t, WeekModel m, string narrowed, DateTimeOffset printed)
    {
        var rooms = t.Rooms.OrderBy(r => r.Name, NameOrder.Comparer).ToList();
        return Document.Create(doc =>
        {
            foreach (var d in m.Days)
                doc.Page(page =>
                {
                    Frame(page, Head(m.Monday, "Rooms, " + Week.ShortDate(d)), narrowed, printed, landscape: false);
                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); });
                        foreach (var r in rooms.Cast<Room?>().Append(null))
                        {
                            var here = m.Visible.Where(o => o.Date == d && o.Session.RoomId == r?.Id).OrderBy(o => o.Start).ToList();
                            if (r is null && here.Count == 0) continue;
                            table.Cell().Element(Row).Text(r?.Name ?? "No room").SemiBold();
                            table.Cell().Element(Row).Column(col =>
                            {
                                if (here.Count == 0) col.Item().Text("Free").FontColor(Quiet);
                                foreach (var o in here)
                                {
                                    var line = col.Item().Text($"{Looks.When(o.Session)}  {Looks.Title(t, o.Session)} · {Looks.Audience(t, o.Session)}" +
                                                    (Looks.Initials(t, o) is { Length: > 0 } i ? " · " + i : "") + (Looks.StateWord(o) is { Length: > 0 } s ? " · " + s : ""));
                                    if (!o.Happens) line.Strikethrough();
                                }
                            });
                        }
                    });
                });
        });
    }

    /// <summary>A calendar per student, a page each, from what each one is on.</summary>
    public static IDocument StudentCalendars(Timetable t, WeekModel m, IEnumerable<Student> students, DateTimeOffset printed, EventViewer viewer)
    {
        return Document.Create(doc =>
        {
            foreach (var st in students.OrderBy(s => s.Name, NameOrder.Comparer))
            {
                var mine = m.All.Where(o => Roster.Includes(t, o.Session, st.Id) || o.Session.Audience.IsEveryone).ToList();
                doc.Page(page =>
                {
                    Frame(page, Head(m.Monday, st.Name), t.Groups.Find(st.GroupId)?.Name ?? "", printed);
                    page.Content().Row(row =>
                    {
                        foreach (var d in m.Days)
                            row.RelativeItem().PaddingRight(4).Column(col =>
                            {
                                col.Item().BorderBottom(0.75f).BorderColor(Ink).Text(Week.ShortDate(d)).SemiBold();
                                foreach (var e in m.Events.Where(e => e.Covers(d) && viewer.Prints(e.Event) && (e.Event.Audience == EventAudience.Everyone || e.Event.Audience.HasFlag(EventAudience.Students))))
                                    col.Item().PaddingTop(3).Text((e.Event.AllDay ? "" : Week.Hm(e.Event.Start) + " ") + e.Event.Title).Italic();
                                foreach (var o in mine.Where(o => o.Date == d).OrderBy(o => o.Start))
                                {
                                    var colour = Looks.ColourOf(t, o.Session);
                                    col.Item().PaddingTop(3).Background(Wash(colour)).BorderLeft(2.5f).BorderColor(Spine(colour)).Padding(3).Column(b =>
                                    {
                                        var title = b.Item().Text(Looks.Title(t, o.Session)).SemiBold();
                                        if (!o.Happens) title.Strikethrough();
                                        b.Item().Text(Looks.When(o.Session) + (Looks.Room(t, o.Session) is { Length: > 0 } r ? " · " + r : "")).FontColor(Quiet);
                                        if (!o.Happens) b.Item().Text(Looks.StateWord(o)).FontColor(Quiet);
                                    });
                                }
                            });
                    });
                });
            }
        });
    }
}
