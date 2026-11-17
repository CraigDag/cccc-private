using Cccc.Domain.Model;

namespace Cccc.Web.Features.Shell;

/// <summary>
/// The headings spaces and groups are listed under, said once: a space's kind, a group's programme.
/// A heading separates one part of a list from another, so it is drawn only when there are two or more:
/// a school that names no kinds, or one kind, gets no heading rather than one over everything.
/// </summary>
public static class Sections
{
    public const string OtherSpaces = "Other spaces";

    public static string Of(Room r) => r.Category ?? OtherSpaces;

    public static string Of(Timetable t, Group g) => t.Programmes.Find(g.ProgrammeId)?.Name ?? "No programme";

    /// <summary>Whether a list in these parts is drawn with headings: only when there is more than one part.</summary>
    public static bool Headed(IEnumerable<string> parts) => parts.Distinct().Skip(1).Any();
}
