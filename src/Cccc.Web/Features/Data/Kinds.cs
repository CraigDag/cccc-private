namespace Cccc.Web.Features.Data;

/// <summary>The kinds Data has a page for, in the order the tabs show them, with the words for each.</summary>
public sealed record Kind(string Slug, string Title, string One);

public static class Kinds
{
    public static readonly Kind[] All =
    [
        new("rooms", "Spaces", "space"),
        new("programmes", "Programmes", "programme"),
        new("groups", "Groups", "group"),
        new("students", "Students", "student"),
        new("facilitators", "Facilitators", "facilitator"),
        new("classes", "Classes", "class"),
        new("equipment", "Equipment", "piece of equipment"),
        new("areas", "Areas", "area"),
        new("disciplines", "Disciplines", "discipline"),
        new("time-rules", "Time rules", "time rule"),
        new("semesters", "Semesters & closures", "semester"),
        new("absences", "Absences", "absence"),
        new("settings", "Settings", "setting"),
    ];

    public static Kind Find(string? slug) => All.FirstOrDefault(k => k.Slug == slug) ?? All[0];
}
