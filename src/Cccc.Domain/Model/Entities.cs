using System.Collections.Immutable;

namespace Cccc.Domain.Model;

/// <summary>Anything the timetable holds that has an identity. Every reference in the model is by id.</summary>
public interface IEntity
{
    Guid Id { get; }
}

/// <summary>
/// A class, a space or a facilitator: something the weeks already run still name after the school stops
/// using it. Deleting one that was used before today keeps it as FORMER from that day, so those weeks keep
/// its name, colour, area and hours, while nothing ahead offers it (<see cref="Rules.Former"/>).
/// </summary>
public interface IRetirable : IEntity
{
    /// <summary>The day it stopped being used, or null while it is in use.</summary>
    DateOnly? FormerSince { get; }
}

/// <summary>The project's own settings. A single entity with the fixed id <see cref="TheId"/>.</summary>
public sealed record Settings : IEntity
{
    public static readonly Guid TheId = Guid.Empty;
    public Guid Id { get; init; } = TheId;
    public string Name { get; init; } = "";
    public string SchoolName { get; init; } = "CCCC";
    public TimeSpan? WeeklyScheduledTarget { get; init; }
    /// <summary>The longest stretch of hard training a group should have without a rest; none, no limit.</summary>
    public TimeSpan? MostHardInARow { get; init; }
    /// <summary>The most hard training a group should have in a day; none, no limit.</summary>
    public TimeSpan? MostHardInADay { get; init; }
    /// <summary>The shortest break that ends a stretch of hard training; none, <see cref="Rules.HardTraining.DefaultRest"/>.</summary>
    public TimeSpan? RestBetweenHard { get; init; }
}

/// <summary>A semester. The old code calls this a Term; the screen has said Semester since 1.38.2.</summary>
public sealed record Semester : IEntity
{
    public const int MaxWeekCount = 520;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateOnly StartMonday { get; init; }
    public int WeekCount { get; init; } = 12;

    public DateOnly FirstMonday => Week.MondayOf(StartMonday);

    public DateOnly LastMonday => FirstMonday.AddDays(7 * Math.Max(0, Math.Min(WeekCount, MaxWeekCount) - 1));

    public bool Contains(DateOnly anyDate)
    {
        if (WeekCount <= 0) return false;
        var m = Week.MondayOf(anyDate);
        return m >= FirstMonday && m <= LastMonday;
    }

    public IEnumerable<DateOnly> Mondays()
    {
        for (int i = 0; i < Math.Clamp(WeekCount, 0, MaxWeekCount); i++)
            yield return FirstMonday.AddDays(7 * i);
    }
}

public enum ClosureScope { WholeBuilding, Groups }

/// <summary>A day, a range or a group-scoped closure. The old code calls this a Holiday.</summary>
public sealed record Closure : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public ClosureScope Scope { get; init; }
    public ImmutableList<Guid> GroupIds { get; init; } = [];

    public bool Covers(DateOnly date) => date >= From && date <= To;
}

public sealed record Room : IRetirable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string? Category { get; init; }
    public string Colour { get; init; } = "#3F51B5";
    public DateOnly? FormerSince { get; init; }

    /// <summary>The equipment it has (<see cref="Equipment"/>), by id: what a class needing it may count on here.</summary>
    public ImmutableList<Guid> Has { get; init; } = [];
}

/// <summary>
/// Something a space can have and a class can need: rigging points, crash mats, a sprung floor, a tumble track.
/// The rebuild's own; the current app knows only a space's kind. Nothing needs anything until the school says so.
/// </summary>
public sealed record Equipment : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
}

/// <summary>A programme. Still <c>Strand</c> in the old code and file.</summary>
public sealed record Programme : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#607D8B";
    public bool IsGuest { get; init; }
}

public sealed record Group : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#3F51B5";
    public Guid? ProgrammeId { get; init; }
}

public sealed record Branch : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#607D8B";
}

public sealed record Discipline : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#5E35B1";
    public Guid? BranchId { get; init; }
}

public sealed record Student : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; init; } = "";
    public string Surname { get; init; } = "";
    public string Email { get; init; } = "";
    public Guid? GroupId { get; init; }
    public Guid? DisciplineId { get; init; }

    public string Name =>
        string.IsNullOrWhiteSpace(Surname) ? FirstName.Trim()
        : string.IsNullOrWhiteSpace(FirstName) ? Surname.Trim()
        : FirstName.Trim() + " " + Surname.Trim();
}

public sealed record AvailabilityWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record Facilitator : IRetirable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#00838F";
    public string Email { get; init; } = "";
    public bool IsGuest { get; init; }
    public ImmutableList<AvailabilityWindow> Availability { get; init; } = [];
    public TimeSpan? MaxWeeklyHours { get; init; }
    public TimeSpan? MaxGap { get; init; }
    public DateOnly? FormerSince { get; init; }

    /// <summary>The first letters of the first two words of the name.</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }
}

public sealed record Area : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public TimeSpan? WeeklyTarget { get; init; }
}

/// <summary>How a class counts towards a student's week.</summary>
public enum Workload
{
    /// <summary>Required attendance: counts towards a student's hours.</summary>
    Counted,
    /// <summary>An open session (open training): optional, hideable from the week.</summary>
    DropIn,
    /// <summary>Not counted (lunch): on the timetable, never a student's hours.</summary>
    NotCounted,
}

/// <summary>
/// How hard a class is on the body. The school says, class by class; a class nobody has said of is no
/// part of a group's hard training, so a timetable brought in from a file warns of nothing until it is set.
/// </summary>
public enum Effort
{
    NotSet,
    Light,
    Moderate,
    Hard,
}

public sealed record Class : IRetirable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Colour { get; init; } = "#5E35B1";
    public Guid? AreaId { get; init; }
    public Workload Workload { get; init; }
    public Effort Effort { get; init; }
    public DateOnly? FormerSince { get; init; }

    /// <summary>The equipment it cannot be taught without (<see cref="Equipment"/>), by id: a space without it is the wrong space.</summary>
    public ImmutableList<Guid> Needs { get; init; } = [];
}

public enum TimeRuleKind { Preparation, Break }

public enum TimeRuleCondition { TeachesBeforeAndAfter, TeachesThatDay }

public sealed record TimeRule : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public TimeRuleKind Kind { get; init; }
    public ImmutableList<Guid> ClassIds { get; init; } = [];
    public int Percent { get; init; } = 50;
    public int Minutes { get; init; } = 30;
    public ImmutableList<DayOfWeek> Days { get; init; } = [];
    public TimeRuleCondition Condition { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Kind == TimeRuleKind.Preparation ? "Prep time" : "Break"
        : Name.Trim();
}
