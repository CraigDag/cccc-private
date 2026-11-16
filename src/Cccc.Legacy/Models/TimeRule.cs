namespace CCCCScheduler.Models;

/// <summary>What a <see cref="TimeRule"/> adds to a facilitator's week.</summary>
public enum TimeRuleKind
{
    /// <summary>
    /// <b>Preparation</b>: each time one of the rule's classes runs, everybody teaching it earns
    /// <see cref="TimeRule.Percent"/> of its length on top. Prep time since schema 15, a rule since 18.
    /// </summary>
    Preparation,

    /// <summary>
    /// <b>A break that counts as work time</b>, such as lunch: on a day one of the rule's classes runs,
    /// a facilitator who meets <see cref="TimeRule.Condition"/> earns <see cref="TimeRule.Minutes"/>.
    /// Nobody is named on the break itself; the rule works out who was in.
    /// </summary>
    Break,
}

/// <summary>Who a <see cref="TimeRuleKind.Break"/> rule counts for, on a day the break runs.</summary>
public enum TimeRuleCondition
{
    /// <summary>
    /// Somebody who teaches a class that ENDS by the time the break starts and another that STARTS once
    /// it is over — in school on both sides of it. Craig's lunch rule, 2026-09-24.
    /// </summary>
    TeachesBeforeAndAfter,

    /// <summary>Somebody who teaches anything at all that day — the Monday meeting's shape.</summary>
    TeachesThatDay,
}

/// <summary>
/// <b>A rule adding time to a facilitator's week beyond the hours spent teaching.</b> Schema 18, Craig,
/// 2026-09-24: lunch counts half an hour for somebody in school before and after it, an hour on a Monday
/// when it is the facilitators' meeting — and, asked whether the same list should hold prep time too,
/// <i>"Can we use time rules to clean up the prep time too?"</i> So preparation moved off the class
/// form and into this list, which is now the one place anything is added to somebody's hours.
///
/// <para><b>A fixed choice of kinds, not a formula anybody types.</b> Each kind is a sentence a person
/// can read back — "these classes earn 50% prep", "this break counts 30 minutes for anybody teaching
/// before and after it" — and the next odd case the school meets is either one of these with different
/// numbers or a new kind written here on purpose.</para>
///
/// <para><b>Everything a rule adds counts towards the weekly limit</b>, as prep always has, and is
/// reported BESIDE the teaching under the rule's own name rather than folded into it.
/// <see cref="ViewModels.TimeRules"/> does the counting.</para>
/// </summary>
public sealed class TimeRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>What the rule's time is called wherever it is reported: "Prep time", "Lunch".</summary>
    public string Name { get; set; } = "";

    public TimeRuleKind Kind { get; set; } = TimeRuleKind.Preparation;

    /// <summary>The classes the rule is about: the ones that earn prep, or the break's own class.</summary>
    public List<Guid> ClassIds { get; set; } = new();

    /// <summary><see cref="TimeRuleKind.Preparation"/> only: the share of a class's length earned. 50 is Craig's figure.</summary>
    public int Percent { get; set; } = 50;

    /// <summary><see cref="TimeRuleKind.Break"/> only: what the break is worth to each person it counts for.</summary>
    public int Minutes { get; set; } = 30;

    /// <summary><see cref="TimeRuleKind.Break"/> only: the days it applies on. Empty means every day.</summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary><see cref="TimeRuleKind.Break"/> only: who it counts for.</summary>
    public TimeRuleCondition Condition { get; set; } = TimeRuleCondition.TeachesBeforeAndAfter;
}
