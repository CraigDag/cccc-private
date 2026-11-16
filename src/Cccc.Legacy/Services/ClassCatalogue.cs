using System.Text;
using CCCCScheduler.Models;

namespace CCCCScheduler.Services;

/// <summary>
/// Resolving a free-text session title against the class catalogue — the one place the rules for
/// matching a <see cref="ScheduleEntry.Title"/> to a <see cref="ClassEntry"/> live, shared by the
/// load-time migration and by every write path that has to carry a link.
///
/// <para><b>The comparison is normalised and nothing more.</b> Trimmed, inner whitespace collapsed,
/// case-insensitive — enough to land the administrator's own spellings on one entry, and deliberately
/// no further. See <see cref="Find"/> for why there is no fuzzy match and why there never should
/// be.</para>
///
/// <para>UI-free and Core-only, so it is unit-testable and usable from the loader; see
/// <c>SessionClassLinkTests</c>.</para>
/// </summary>
public static class ClassCatalogue
{
    /// <summary>
    /// The normalised comparison key for a class name or a session title: trimmed, with every run of
    /// whitespace inside it collapsed to one space. A blank name gives <c>""</c>, which never matches
    /// anything — an untitled session names no activity.
    ///
    /// <para><b>Case is the comparer's job, not the key's</b>, so this keeps whatever casing it was
    /// given. Lower-casing here would make the key useless as a display string and would hide which
    /// spelling was actually seen, which is the thing the created-entry rule in
    /// <see cref="FindOrAdd"/> depends on.</para>
    ///
    /// <para>Whitespace is <see cref="char.IsWhiteSpace(char)"/> rather than the space character, so a
    /// tab or a non-breaking space pasted out of a spreadsheet collapses the same way a double space
    /// does. That paste is exactly how the administrator's data gets here.</para>
    /// </summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var sb = new StringBuilder(name.Length);
        bool pendingSpace = false;
        foreach (char ch in name)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0) pendingSpace = true;    // never leads; trailing runs are dropped below
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The catalogue entry <paramref name="title"/> names, compared on <see cref="Key"/> and ignoring
    /// case, or <c>null</c> when the title is blank or matches nothing.
    ///
    /// <para><b>First occurrence wins</b>, matching the tiebreak the five duplicate-id repairs in
    /// <c>ProjectStore.Validate</c> already follow, and it is a walk rather than a dictionary <b>on
    /// purpose</b>. <see cref="ClassEntry.Name"/> is not unique and nothing enforces it — not the
    /// model, not the dialogs, not <c>ApplyClassChanges</c>, not <c>Validate</c> — so a
    /// <c>ToDictionary</c> here would throw <see cref="ArgumentException"/> on the first repeated
    /// key, during migration, on load, on a real file the owner could then no longer open. That is
    /// not a hypothetical shape: <i>Conditioning</i> beside <i>Conditioning</i> with a double space
    /// normalises to one key, and both are on the administrator's real sheet.</para>
    ///
    /// <para><b>There is no fuzzy matching and there must never be.</b> <i>Floor Acrobatic</i> and
    /// <i>Floor Acrobatics</i> are two entries here, because the app cannot know they are the same
    /// activity — a human can, and the catalogue merge is how they say so. Any edit-distance, prefix
    /// or stemming rule that joined those two would also join <i>Move</i> and <i>Movement</i>, which
    /// are two different things on this timetable. Pinned by
    /// <c>SessionClassLinkTests.Load_DoesNotFuzzyMatch_SoNearlyIdenticalTitlesBecomeTwoEntries</c>.</para>
    /// </summary>
    public static ClassEntry? Find(IEnumerable<ClassEntry> classes, string? title)
    {
        string key = Key(title);
        if (key.Length == 0) return null;

        foreach (var c in classes)
            if (string.Equals(Key(c.Name), key, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    /// <summary>
    /// <see cref="Find"/>, or a new entry appended to <paramref name="classes"/> carrying
    /// <paramref name="title"/> <b>exactly as given</b>. <c>null</c> for a blank title, and in that
    /// case nothing is added.
    ///
    /// <para>The new entry's name is the <em>first spelling seen</em>, verbatim — double space and
    /// all. Normalising it would be renaming the administrator's data on the way in, and the whole
    /// promise of the migration is that nothing is dropped and nothing is renamed. Later sessions
    /// spelling it differently still land on this entry, because they are matched on
    /// <see cref="Key"/>.</para>
    /// </summary>
    public static ClassEntry? FindOrAdd(IList<ClassEntry> classes, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (Find(classes, title) is { } found) return found;

        var created = new ClassEntry { Name = title };
        classes.Add(created);
        return created;
    }

    /// <summary>
    /// The link a session should carry once its title has been set to <paramref name="title"/>, given
    /// the link it has now. The rule the session editor applies, written down here rather than left
    /// to decay one edit at a time, and the reason <b>Duplicate</b> is safe.
    ///
    /// <list type="bullet">
    /// <item><b>Blank title → no link.</b> An untitled session names no activity.</item>
    /// <item><b>The title still names the entry it is linked to → keep it.</b> This is the ordinary
    /// edit that only moved the time, and it is Duplicate, which prefills from the row it copies.</item>
    /// <item><b>The title names a different entry → re-resolve.</b> Renaming a session onto another
    /// activity is exactly how a user re-files it.</item>
    /// <item><b>The title names nothing the catalogue has → keep the link it had.</b> After a merge a
    /// session's title and its class's name legitimately differ, so a session edited for a typo must
    /// not silently lose its class.</item>
    /// </list>
    ///
    /// <para>A link resolving to no live entry is never handed back as a "kept" one, so this can only
    /// ever return <c>null</c> or the id of an entry in <paramref name="classes"/>.</para>
    ///
    /// <para><b>It never creates</b>, and that is now a statement about <em>this function</em> rather
    /// than about the app. It resolves; <see cref="FindOrAdd"/> creates. A title naming nothing, on a
    /// session holding no link to keep, is filed under a new entry by
    /// <c>MainViewModel.LinkOrCreate</c> — deliberately there and not here, because creating has to
    /// happen <b>after</b> the undo step is captured or the entry outlives the Ctrl+Z that removes the
    /// session which invented it. A dialog's OK runs before that.</para>
    ///
    /// <para>Creating at all was the wrong answer while the catalogue could only grow: before a merge
    /// existed, every typo would have left a permanent entry with no cure. The merge is that cure, and
    /// the balance tipped — an unlinked session is invisible to every dock figure, and silence beats a
    /// wrong number only when the number is visible.</para>
    /// </summary>
    public static Guid? LinkFor(IEnumerable<ClassEntry> classes, string? title, Guid? currentLink)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var current = currentLink is Guid id ? classes.FirstOrDefault(c => c.Id == id) : null;
        if (current is not null && string.Equals(Key(current.Name), Key(title), StringComparison.OrdinalIgnoreCase))
            return current.Id;

        return Find(classes, title)?.Id ?? current?.Id;
    }
}
