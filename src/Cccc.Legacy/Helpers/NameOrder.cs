using System.Globalization;

namespace CCCCScheduler.Helpers;

/// <summary>
/// <b>How a list of NAMES is ordered: Danish collation, because the school is in Copenhagen.</b>
///
/// <para>Danish puts <b>Æ Ø Å after Z</b>, and <b><c>Aa</c> sorts as <c>Å</c></b>. Neither ordinal
/// nor invariant collation does either, so a room called <em>Ørsted</em> landed between <em>O</em>
/// and <em>P</em> and <em>Aalborg</em> came first in a list where a Dane looks for it last. The
/// symptom is a name a person cannot find in a list they are scanning alphabetically, which on this
/// app's lists is a room, a facilitator, a class or a student.</para>
///
/// <para><b>This is the NARROW fix and that is the point of it.</b> The blunt one is
/// <c>RequestLocalization</c> in the web host, which changes <see cref="CultureInfo.CurrentCulture"/>
/// and with it EVERY culture-sensitive string the server touches — dates, numbers, month names,
/// every comparison — as a side effect of fixing a sort. The owner was offered that and chose this:
/// sort names in Danish, leave everything else exactly as it was. So nothing here is ambient. It is
/// a comparer, passed explicitly, at each site that orders a name somebody reads.</para>
///
/// <para><b>MEASURED, on both platforms, because collation is not a thing to assume.</b> .NET 8 uses
/// ICU on Windows as well as Linux, but not the same ICU: Windows 11 answered sort version 2783641
/// and the deploy image (<c>mcr.microsoft.com/dotnet/aspnet:8.0</c>, Debian 12, libicu72) answered
/// 2783385. (Since the .NET 10 move on 2026-10-29 the deploy image is <c>aspnet:10.0</c>, Ubuntu
/// 24.04, libicu74 — the same ICU as the Linux box <c>NameOrderTests</c> was run on that day, green.)
/// Both produced the identical order for
/// <c>Zebra Ørsted Aalborg Æbletræ Ada Åse Yvonne Bo aarhus Ægir Øst Åben Zulu Anna</c>:</para>
///
/// <list type="bullet">
/// <item><b>da-DK</b> — Ada, Anna, Bo, Yvonne, Zebra, Zulu, Æbletræ, Ægir, Ørsted, Øst, Åben,
/// Aalborg, aarhus, Åse. The three letters follow Z in Æ Ø Å order, and the two <c>Aa</c> names sit
/// inside the Å group sorted on their SECOND letter — <c>Aa</c> compares exactly equal to <c>Å</c>,
/// which is what makes Åben come before Aalborg.</item>
/// <item><b>Ordinal</b> (what the name lists used) — Aalborg, Ada, Anna, Bo, Yvonne, Zebra, Zulu,
/// aarhus, Åben, Åse, Æbletræ, Ægir, Ørsted, Øst. Å before Æ, lower case after upper, and Aalborg
/// first.</item>
/// <item><b>InvariantCulture</b> (what the timetable grid got, since the container's
/// <c>CurrentCulture</c> IS the invariant one) — Aalborg, aarhus, Åben, Ada, Æbletræ, Ægir, Anna,
/// Åse, Bo, Ørsted, Øst, Yvonne, Zebra, Zulu. All three letters folded in among the A's and O's.</item>
/// </list>
///
/// <para><b>No <c>ToLowerInvariant</c>, and no ordinal comparison, anywhere near this.</b> Case
/// folding by hand is how <c>AccessRule.Normalise</c> came to fold U+212A KELVIN SIGN to <c>k</c>;
/// case-insensitivity here is <see cref="CompareOptions.IgnoreCase"/> under the Danish culture,
/// which is the runtime's own and is a question about ORDER rather than about identity.</para>
///
/// <para><b>The sharp edges are real and are the point.</b> Culture-aware comparison weights
/// punctuation and spacing differently from ordinal — <c>"Aerial Basics"</c> and
/// <c>"Aerial-Basics"</c> no longer sit an ASCII code point apart — and that is what a person
/// alphabetising a list by hand does too. What must not happen is a name landing where nobody looks
/// for it.</para>
///
/// <para><b>Where this is NOT used, deliberately.</b> Anything that is not a name a person reads in
/// a list: backup file names and menu ids (<c>StringComparer.Ordinal</c>, for determinism rather
/// than for reading), the <c>"HH:mm"</c> absence windows in <c>AdminDockView</c> (ordinal IS
/// chronological for those), duplicate-name detection in <c>RoomGridLayout</c> and
/// <c>ProjectStore</c> (an equality question about two rooms really called the same thing), and the
/// filter dropdowns, which are in the building's own catalogue order and are not alphabetised at all
/// on any host.</para>
/// </summary>
public static class NameOrder
{
    /// <summary>
    /// The culture whose collation this is. Named rather than inferred from the machine: the server
    /// runs in Frankfurt under whatever culture the container was built with — the invariant one, in
    /// fact — and the school is in Copenhagen either way.
    /// </summary>
    public const string CultureName = "da-DK";

    /// <summary>
    /// <see cref="CultureName"/> resolved. <b>It throws if the runtime cannot supply it</b>, which is
    /// what should happen: a host built with <c>InvariantGlobalization</c> cannot sort Danish, and a
    /// quiet fall back to invariant is the silent wrong answer this file exists to prevent.
    /// <c>NameOrderTests</c> asserts the resulting order rather than the culture's name, so a runtime
    /// that resolved the culture and then collated like the invariant one is caught too.
    /// </summary>
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(CultureName);

    /// <summary>
    /// <b>The one to use.</b> Danish collation, case-insensitive — which is what every name list in
    /// this app was already (as <c>StringComparer.OrdinalIgnoreCase</c>), so only the collation
    /// changed and nothing about case did.
    /// </summary>
    public static StringComparer Comparer { get; } = StringComparer.Create(Culture, ignoreCase: true);

    /// <summary>
    /// Danish collation, <b>case-sensitive</b>. One caller: the web timetable's sortable columns,
    /// which were <c>StringComparer.CurrentCulture</c> because a WPF <c>DataGrid</c> with no comparer
    /// of its own falls through to <c>String.CompareTo</c>. Keeping that half unchanged keeps this a
    /// collation fix rather than a second, unasked-for change to how the grid sorts.
    /// </summary>
    public static StringComparer CaseSensitive { get; } = StringComparer.Create(Culture, ignoreCase: false);
}
