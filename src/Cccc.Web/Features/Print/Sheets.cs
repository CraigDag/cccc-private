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
    // The Paper look's ink, quiet text, rules, vermilion and warning, so a sheet and the screen are one design.
    private static readonly string Ink = "#1C1A16";
    private static readonly string Quiet = "#686256";
    private static readonly string Line = "#D6D0C3";
    private static readonly string Accent = "#C63F26";
    private static readonly string Warning = "#A86200";
    private static readonly string InUse = "#E4DFD4";

    private static string Head(DateOnly monday, string shape) => $"CCCC Scheduler · {Week.ForDocuments(monday)} · {shape}";

    private static void Frame(PageDescriptor page, string heading, string narrowed, DateTimeOffset printed, bool landscape = true)
    {
        page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(Margin);
        page.DefaultTextStyle(x => x.FontSize(8).FontColor(Ink));
        page.Header().PaddingBottom(6).Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(heading).SemiBold().FontSize(11);
                r.AutoItem().AlignRight().AlignBottom().Text(narrowed).FontColor(Quiet);
            });
            col.Item().PaddingTop(3).LineHorizontal(0.75f).LineColor(Accent);
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
        if (hex is not { Length: 7 } || hex[0] != '#') return "#EEEAE0";
        int r = Convert.ToInt32(hex[1..3], 16), g = Convert.ToInt32(hex[3..5], 16), b = Convert.ToInt32(hex[5..7], 16);
        int Mix(int c) => (int)Math.Round(c * 0.22 + 255 * 0.78);
        return $"#{Mix(r):X2}{Mix(g):X2}{Mix(b):X2}";
    }

    private static string Spine(string? hex) => hex is { Length: 7 } && hex[0] == '#' ? hex : "#8A8476";

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
                        .Element(b => o.NeedsCover ? b.Border(0.75f).BorderColor(Warning) : m.Ringed.Contains(o.Session.Id) ? b.Border(0.75f).BorderColor(Accent) : b)
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
                    foreach (var name in new[] { "Day", "Time", "Class", "For", "Space", "In the room", "" })
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

    /// <summary>
    /// The building's day, a page a day: the plan's Spaces view on paper. Spaces down the side, grouped as
    /// on the plan; hours across, on the week's own frame so the pages of a week line up; each class where
    /// and when it is, coloured by its class, overlapping classes stacked. Events run in a band along the
    /// top. What is drawn is <see cref="SpacesDay.Rows"/>; this only draws it, and always on one sheet a
    /// day: rows get thinner, and words drop out of a bar too thin to hold them, before anything is left off.
    /// </summary>
    public static IDocument RoomGrid(Timetable t, WeekModel m, string narrowed, DateTimeOffset printed, EventViewer viewer, IReadOnlySet<Guid>? onlyRooms = null)
    {
        return Document.Create(doc =>
        {
            foreach (var d in m.Days)
            {
                var events = SpacesDay.Events(m, d, viewer);
                foreach (var sheet in SpacesDay.Pages(SpacesDay.Rows(t, m, d, onlyRooms), events.Count == 0 ? 0 : events.Max(e => e.Lane) + 1))
                    doc.Page(page =>
                    {
                        Frame(page, Head(m.Monday, "Spaces, " + Week.ShortDate(d)), narrowed, printed);
                        page.Content().Element(c => SpacesPage(c, t, m, sheet, events));
                    });
            }
        });
    }

    private static void SpacesPage(IContainer c, Timetable t, WeekModel m, SheetPage sheet, IReadOnlyList<SheetEvent> events)
    {
        const float width = 842 - 2 * Margin, label = 112;
        const float rulerH = SpacesDay.Ruler, sectionH = SpacesDay.Section, legendH = SpacesDay.Legend, eventH = SpacesDay.EventLane;
        const float height = SpacesDay.PageHeight;
        int f0 = m.FrameStart, f1 = Math.Max(m.FrameStart + 60, m.FrameEnd);
        float hourW = (width - label) / ((f1 - f0) / 60f);
        float X(int minutes) => label + (Math.Clamp(minutes, f0, f1) - f0) / 60f * hourW;
        var rows = sheet.Rows;
        float laneH = sheet.Lane, top = sheet.Top, bottom = sheet.Bottom;
        // A few spaces get tall rows and bigger words, for a sheet read from across a room; many get thin ones.
        float titleSize = Math.Clamp(laneH * 0.27f, 5.5f, 9f), detailSize = Math.Max(5.2f, titleSize - 1.5f);

        c.Height(height).Layers(layers =>
        {
            layers.PrimaryLayer().Height(height).ExtendHorizontal();

            // The hours across the top, and a hairline down from each.
            for (int minute = (f0 + 59) / 60 * 60; minute <= f1; minute += 60)
            {
                float x = X(minute);
                layers.Layer().OffsetX(x).OffsetY(top).Width(0.4f).Height(bottom - top).Background(Line);
                if (minute < f1)
                    layers.Layer().OffsetX(x + 2).OffsetY(0).Width(hourW - 2).Height(rulerH).StopPaging()
                        .Text($"{minute / 60:00}").FontSize(7).FontColor(Quiet);
            }

            foreach (var e in events)
            {
                float x = X(e.Start) + 0.5f, w = Math.Max(2, X(e.End) - X(e.Start) - 1);
                layers.Layer().OffsetX(x).OffsetY(rulerH + e.Lane * eventH).Width(w).Height(eventH - 2)
                    .Border(0.6f).BorderColor(Quiet).Background("#FFFFFF").PaddingHorizontal(3).StopPaging().AlignMiddle()
                    .Text((e.AllDay ? "" : Week.Hm(e.E.Event.Start) + " ") + e.E.Event.Title).Italic().FontSize(6.5f).ClampLines(1);
            }
            if (events.Count > 0)
                layers.Layer().OffsetX(0).OffsetY(rulerH).Width(label - 6).Height(eventH - 2).StopPaging().AlignMiddle()
                    .Text("EVENTS").FontSize(6).SemiBold().FontColor(Quiet).LetterSpacing(0.08f);

            float y = top;
            string? section = null;
            foreach (var r in rows)
            {
                if (r.Section.Length > 0 && r.Section != section)
                {
                    section = r.Section;
                    layers.Layer().OffsetX(0).OffsetY(y).Width(width).Height(sectionH).PaddingTop(5).StopPaging()
                        .Text(r.Section.ToUpperInvariant()).FontSize(6).SemiBold().FontColor(Quiet).LetterSpacing(0.08f);
                    y += sectionH;
                }
                float rowH = r.Lanes * laneH;
                layers.Layer().OffsetX(0).OffsetY(y + 1).Width(label - 6).Height(Math.Max(1, rowH - 2))
                    .BorderLeft(2.5f).BorderColor(r.Key == SpacesDay.NoSpace ? Warning : Spine(r.Colour)).PaddingLeft(4).StopPaging().AlignMiddle()
                    .Text(r.Name).SemiBold().FontSize(Math.Clamp(rowH * 0.3f, 6.5f, 10f)).ClampLines(1);
                layers.Layer().OffsetX(0).OffsetY(y + rowH).Width(width).Height(0.4f).Background(Line);

                foreach (var b in r.Bars)
                {
                    float x = X(b.Start) + 0.5f, w = Math.Max(2, X(b.End) - X(b.Start) - 1.5f);
                    float by = y + b.Lane * laneH + 1, bh = Math.Max(2, laneH - 2);
                    var bar = layers.Layer().OffsetX(x).OffsetY(by).Width(w).Height(bh);
                    if (!b.Shown)
                    {
                        bar.Background(InUse);
                        continue;
                    }
                    var o = b.O;
                    var colour = Looks.ColourOf(t, o.Session);
                    bar.Background(Wash(colour)).BorderLeft(2.5f).BorderColor(Spine(colour))
                        .Element(x2 => o.NeedsCover ? x2.Border(0.75f).BorderColor(Warning) : m.Ringed.Contains(o.Session.Id) ? x2.Border(0.75f).BorderColor(Accent) : x2)
                        .PaddingLeft(3).PaddingTop(laneH >= 20 ? 2.5f : 0.5f).StopPaging().Column(col =>
                        {
                            if (w < 12 || bh < 5) return;
                            var state = o.NeedsCover ? "COVER" : Looks.StateWord(o).ToUpperInvariant();
                            var detail = string.Join(" · ", new[] { Looks.When(o.Session), Looks.Audience(t, o.Session), Looks.Initials(t, o), state }.Where(x3 => x3.Length > 0));
                            if (laneH >= 20)
                            {
                                var title = col.Item().Text(Looks.Title(t, o.Session)).SemiBold().FontSize(titleSize).ClampLines(1);
                                if (!o.Happens) title.Strikethrough();
                                col.Item().Text(detail).FontSize(detailSize).FontColor(Quiet).ClampLines(laneH >= 40 ? 2 : 1);
                            }
                            else
                            {
                                col.Item().Text(text =>
                                {
                                    text.ClampLines(1);
                                    var title = text.Span(Looks.Title(t, o.Session)).SemiBold().FontSize(titleSize);
                                    if (!o.Happens) title.Strikethrough();
                                    if (laneH >= 11) text.Span("  " + detail).FontSize(detailSize).FontColor(Quiet);
                                });
                            }
                        });
                }
                y += rowH;
            }

            if (sheet.Legend)
                layers.Layer().OffsetX(0).OffsetY(bottom + 4).Width(width).Height(legendH - 4).Row(row =>
                {
                    row.ConstantItem(22).PaddingVertical(1.5f).Background(InUse);
                    row.RelativeItem().PaddingLeft(6).StopPaging().Text("In use by a class that is not on this sheet: another group's, or a guest class or open session left out of it.")
                        .FontSize(6.5f).FontColor(Quiet);
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
