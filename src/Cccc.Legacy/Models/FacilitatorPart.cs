namespace CCCCScheduler.Models;

/// <summary>
/// <b>One facilitator's own hours inside a session</b> — schema 23. "In Circus Disciplines from 14:00"
/// is <c>From = 14:00</c> and <c>Until = null</c>; null on either side is the session's own start or
/// end, so a part says only what is different about this person.
///
/// <para>Times on the clock rather than minutes from the start, because that is what a person types
/// and reads. When a session is moved by a drag the parts move with it
/// (<c>FacilitatorParts.Shift</c>), and a part reaching outside the session's hours is read clamped to
/// them, so a stretched or shortened session never puts anybody outside it.</para>
/// </summary>
public sealed class FacilitatorPart
{
    public Guid FacilitatorId { get; set; }

    /// <summary>When they join, or null for the session's own start.</summary>
    public TimeOnly? From { get; set; }

    /// <summary>When they leave, or null for the session's own end.</summary>
    public TimeOnly? Until { get; set; }
}
