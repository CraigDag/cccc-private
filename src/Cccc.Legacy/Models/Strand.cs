namespace CCCCScheduler.Models;

/// <summary>
/// A programme the school runs — Degree, Prep, Open Training and so on. A strand contains the
/// <see cref="Group"/>s timetabled under it: Degree gains a group per year as year groups
/// accumulate, and each is scheduled separately.
///
/// Sessions reference a <see cref="Group"/>, never a strand directly; a session's strand is derived
/// through its group. The level is intended to let holidays scope to a strand and cascade to every
/// group inside it, so "Prep are off next week" would be one entry rather than one per cohort — that
/// scoping does not exist yet: <see cref="Holiday"/> today blacks out every group without exception.
/// </summary>
public sealed class Strand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour, e.g. "#1E88E5". Groups keep their own colour; this is for strand-level views.</summary>
    public string ColourHex { get; set; } = "#607D8B";

    /// <summary>
    /// <b>A visiting group's programme</b> — Ollerup's week at the school, say: real classes in real
    /// rooms taught by the school's own staff, for people who are not the school's students. Craig,
    /// 2026-11-02, on why he had been entering one as events: a guest group in the timetable adds a
    /// lane to every day it is in, and the school's own week became unreadable. Schema 21.
    ///
    /// <para><b>Everything that CHECKS or COUNTS treats a guest class exactly like any other</b>: it
    /// books its room and clashes with what else is in it, its facilitators are double-booked by it,
    /// their hours count, it needs cover when they are away. <b>Only what is DRAWN differs</b>: the
    /// week opens with guest classes off it, behind <i>Guests: Hidden | Shown</i> in + Filter
    /// (<c>GuestSessions.Hides</c> is the rule, and says when it does not apply).</para>
    /// </summary>
    public bool IsGuest { get; set; }

    /// <summary>The five strands every new project starts with, in the order they are shown.</summary>
    public static List<Strand> CreateDefaults() => new()
    {
        new Strand { Name = "Degree",        ColourHex = "#1E88E5" },
        new Strand { Name = "Prep",          ColourHex = "#43A047" },
        new Strand { Name = "Open Training", ColourHex = "#00897B" },
        new Strand { Name = "Event Hire",    ColourHex = "#8E24AA" },
        new Strand { Name = "Adult Classes", ColourHex = "#FB8C00" },
    };

    /// <summary>
    /// Maps a group's name onto the strand name it belongs under. The four names the app shipped with
    /// before strands existed map onto the programme names the school actually uses; any other name is
    /// its own strand name unchanged.
    ///
    /// This is migration policy, for <c>ProjectStore.Validate</c>'s schema 1→2 migration ONLY — it
    /// exists to translate an OLD FILE's vocabulary into the vocabulary this app uses today.
    /// <c>MainViewModel.ApplyGroupChanges</c> deliberately does NOT call this: a name typed into
    /// Manage ▸ Groups today is not legacy vocabulary to translate, it is exactly what the user means,
    /// so a brand-new group named "External Hire" must get a strand named "External Hire", not silently
    /// join "Event Hire". The two sites were unified onto this one method once before, and that made
    /// exactly that scenario happen — do not re-unify them; this method should have exactly one caller.
    /// </summary>
    public static string StrandNameForGroupName(string groupName) => groupName switch
    {
        "BA Students"   => "Degree",
        "Prep Students" => "Prep",
        "Adult Courses" => "Adult Classes",
        "External Hire" => "Event Hire",
        _               => groupName,
    };
}
