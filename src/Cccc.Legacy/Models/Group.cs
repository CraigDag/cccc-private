namespace CCCCScheduler.Models;

/// <summary>
/// A managed "user of the space" — e.g. BA Students, Prep Students, Adult Courses,
/// External Hire. Sessions reference a group by <see cref="Id"/> so renaming or
/// recolouring a group updates everywhere automatically.
/// </summary>
public sealed class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour, e.g. "#3F51B5".</summary>
    public string ColourHex { get; set; } = "#3F51B5";

    /// <summary>
    /// The <see cref="Strand"/> this group belongs to. Null means unassigned, which a migrated or
    /// hand-edited project can produce; such a group is simply not covered by strand-scoped rules.
    /// </summary>
    public Guid? StrandId { get; set; }

    /// <summary>The four groups every new project starts with, each under one of <see cref="Strand.CreateDefaults"/>.
    /// Open Training starts with no group — it is drop-in, and gains cohorts only if the school wants them.</summary>
    public static List<Group> CreateDefaults(IReadOnlyList<Strand> strands)
    {
        Guid Id(string name) => strands.Single(s => s.Name == name).Id;
        return new()
        {
            new Group { Name = "BA Students",   ColourHex = "#1E88E5", StrandId = Id("Degree") },
            new Group { Name = "Prep Students", ColourHex = "#43A047", StrandId = Id("Prep") },
            new Group { Name = "Adult Courses", ColourHex = "#FB8C00", StrandId = Id("Adult Classes") },
            new Group { Name = "External Hire", ColourHex = "#8E24AA", StrandId = Id("Event Hire") },
        };
    }
}
