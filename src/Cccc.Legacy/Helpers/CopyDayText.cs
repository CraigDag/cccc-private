namespace CCCCScheduler.Helpers;

/// <summary>
/// What <em>Copy a day</em> tells the user it will do — the sentence, in one place, because it is a
/// claim about <c>ScheduleOps.Clone</c> rather than a piece of chrome.
///
/// <para><b>It drifted once already.</b> The dialog said it carried "room, time, class, group,
/// facilitators" — the list from before 1.8.2, when the student roster was deliberately put back into
/// the copy. For four versions the app did one thing and the dialog described another, and nothing
/// caught it, because the sentence lived in markup and the behaviour lived in Core.</para>
///
/// <para><b>Why here and not in the two windows.</b> It was written out verbatim in
/// <c>CopyDayDialog.xaml</c> and again in <c>CopyDayDialog.axaml</c>. Two copies of a claim that has
/// already gone stale once is two things to remember, and the port would have made it two things
/// that could disagree with each other as well as with the code. Stated in Core, it is held against
/// <c>ScheduleOps.Clone</c>'s own copied-property list by a test that needs no window at all —
/// <c>ScheduleEntryCloneCoverageTests.TheCopyDayDescription_NamesEverythingTheCopyCarries</c> — and
/// each app's markup binds to it, so a window that stopped showing it is the only failure left for a
/// UI test to catch.</para>
/// </summary>
public static class CopyDayText
{
    /// <summary>The sentence at the top of <em>Copy a day</em>.</summary>
    public const string Description =
        "Copy every session from one weekday onto another — same room, time, class, group, "
        + "facilitators and enrolled students. Registers and one-off changes are not copied.";
}
