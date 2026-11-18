using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cccc.Domain.Model;

namespace Cccc.Domain.Editing;

/// <summary>
/// One change to one thing. <see cref="After"/> null means removed; <see cref="Before"/> null means it
/// did not exist. Values are whole entities serialised by <see cref="ModelJson"/>, so a field added to
/// the model is carried by every operation without anybody listing it.
/// </summary>
public sealed record Op(string Collection, Guid Id, JsonNode? Before, JsonNode? After)
{
    public bool IsRemove => After is null;
    public bool IsAdd => Before is null;

    public Op Inverse() => new(Collection, Id, After?.DeepClone(), Before?.DeepClone());
}

/// <summary>One person's change at one moment: one undo step.</summary>
public sealed record Batch(string Label, ImmutableList<Op> Ops)
{
    public bool IsEmpty => Ops.Count == 0;

    public Batch Inverse() => new("Undo " + Label, Ops.Reverse().Select(o => o.Inverse()).ToImmutableList());
}

/// <summary>A batch as stored: who, when, and its place in the project's sequence.</summary>
public sealed record Change(long Sequence, string By, DateTimeOffset At, Batch Batch);

/// <summary>One kind of thing the timetable holds, by the name operations use for it.</summary>
public abstract class Collection
{
    public abstract string Name { get; }
    public abstract Type ItemType { get; }
    public abstract JsonNode? Read(Timetable t, Guid id);
    public abstract Timetable Write(Timetable t, Guid id, JsonNode? value);
    public abstract IEnumerable<IEntity> All(Timetable t);
}

public sealed class Collection<T>(string name, Func<Timetable, EntitySet<T>> get, Func<Timetable, EntitySet<T>, Timetable> set)
    : Collection where T : class, IEntity
{
    public override string Name => name;
    public override Type ItemType => typeof(T);

    public EntitySet<T> Get(Timetable t) => get(t);

    public override JsonNode? Read(Timetable t, Guid id) =>
        get(t)[id] is { } item ? JsonSerializer.SerializeToNode(item, ModelJson.Options) : null;

    public override Timetable Write(Timetable t, Guid id, JsonNode? value)
    {
        if (value is null) return set(t, get(t).Remove(id));
        var item = value.Deserialize<T>(ModelJson.Options)
                   ?? throw new InvalidDataException($"An operation on {name} carried no value.");
        if (item.Id != id) throw new InvalidDataException($"An operation on {name} carried a value with another id.");
        return set(t, get(t).Put(item));
    }

    public override IEnumerable<IEntity> All(Timetable t) => get(t);

    public Op Put(Timetable t, T item) =>
        new(name, item.Id, Read(t, item.Id), JsonSerializer.SerializeToNode(item, ModelJson.Options));

    public Op Remove(Timetable t, Guid id) => new(name, id, Read(t, id), null);
}

/// <summary>Settings are a collection of one, so every change is the same two kinds of operation.</summary>
public sealed class SettingsCollection : Collection
{
    public override string Name => "settings";
    public override Type ItemType => typeof(Settings);

    public override JsonNode? Read(Timetable t, Guid id) =>
        id == Settings.TheId ? JsonSerializer.SerializeToNode(t.Settings, ModelJson.Options) : null;

    public override Timetable Write(Timetable t, Guid id, JsonNode? value)
    {
        if (id != Settings.TheId) throw new InvalidDataException("Settings have one fixed id.");
        return t with { Settings = value?.Deserialize<Settings>(ModelJson.Options) ?? new Settings() };
    }

    public override IEnumerable<IEntity> All(Timetable t) => [t.Settings];

    public Op Put(Timetable t, Settings s) =>
        new(Name, Settings.TheId, Read(t, Settings.TheId), JsonSerializer.SerializeToNode(s with { Id = Settings.TheId }, ModelJson.Options));
}

/// <summary>Every collection, by name. A new list on <see cref="Timetable"/> must be added here.</summary>
public static class Collections
{
    public static readonly SettingsCollection Settings = new();
    public static readonly Collection<Semester> Semesters = new("semesters", t => t.Semesters, (t, s) => t with { Semesters = s });
    public static readonly Collection<Closure> Closures = new("closures", t => t.Closures, (t, s) => t with { Closures = s });
    public static readonly Collection<Room> Rooms = new("rooms", t => t.Rooms, (t, s) => t with { Rooms = s });
    public static readonly Collection<Equipment> Equipment = new("equipment", t => t.Equipment, (t, s) => t with { Equipment = s });
    public static readonly Collection<Programme> Programmes = new("programmes", t => t.Programmes, (t, s) => t with { Programmes = s });
    public static readonly Collection<Group> Groups = new("groups", t => t.Groups, (t, s) => t with { Groups = s });
    public static readonly Collection<Branch> Branches = new("branches", t => t.Branches, (t, s) => t with { Branches = s });
    public static readonly Collection<Discipline> Disciplines = new("disciplines", t => t.Disciplines, (t, s) => t with { Disciplines = s });
    public static readonly Collection<Student> Students = new("students", t => t.Students, (t, s) => t with { Students = s });
    public static readonly Collection<Facilitator> Facilitators = new("facilitators", t => t.Facilitators, (t, s) => t with { Facilitators = s });
    public static readonly Collection<Area> Areas = new("areas", t => t.Areas, (t, s) => t with { Areas = s });
    public static readonly Collection<Class> Classes = new("classes", t => t.Classes, (t, s) => t with { Classes = s });
    public static readonly Collection<TimeRule> TimeRules = new("timeRules", t => t.TimeRules, (t, s) => t with { TimeRules = s });
    public static readonly Collection<Session> Sessions = new("sessions", t => t.Sessions, (t, s) => t with { Sessions = s });
    public static readonly Collection<Register> Registers = new("registers", t => t.Registers, (t, s) => t with { Registers = s });
    public static readonly Collection<Absence> Absences = new("absences", t => t.Absences, (t, s) => t with { Absences = s });
    public static readonly Collection<SchoolEvent> Events = new("events", t => t.Events, (t, s) => t with { Events = s });
    /// <summary>The Scratch Week's classes (<see cref="ScratchWeek"/>), never part of <see cref="Sessions"/>.</summary>
    public static readonly Collection<Session> Scratch = new("scratch", t => t.Scratch, (t, s) => t with { Scratch = s });
    /// <summary>Facilitators saying they cannot come (<see cref="Rules.Asking"/>), never absences until a yes records one.</summary>
    public static readonly Collection<TimeOffRequest> Requests = new("requests", t => t.Requests, (t, s) => t with { Requests = s });
    public static readonly Collection<CoverOffer> Offers = new("offers", t => t.Offers, (t, s) => t with { Offers = s });

    public static IReadOnlyList<Collection> All { get; } =
    [
        Settings, Semesters, Closures, Rooms, Equipment, Programmes, Groups, Branches, Disciplines, Students,
        Facilitators, Areas, Classes, TimeRules, Sessions, Registers, Absences, Events, Scratch, Requests, Offers,
    ];

    private static readonly Dictionary<string, Collection> ByName = All.ToDictionary(c => c.Name);

    public static Collection Named(string name) =>
        ByName.TryGetValue(name, out var c) ? c : throw new InvalidDataException($"There is no collection called \"{name}\".");
}

public static class Apply
{
    /// <summary>Applies a batch in order. The input is untouched.</summary>
    public static Timetable Batch(Timetable t, Batch batch)
    {
        foreach (var op in batch.Ops)
            t = Collections.Named(op.Collection).Write(t, op.Id, op.After);
        return t;
    }

    /// <summary>Whether the thing an operation wrote is still exactly as it wrote it.</summary>
    public static bool StillAsWritten(Timetable t, Op op) =>
        JsonNode.DeepEquals(Collections.Named(op.Collection).Read(t, op.Id), op.After);
}

/// <summary>The one JSON configuration for the model: operations, snapshots and exports.</summary>
public static class ModelJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            // A property with no setter is derived (a name, a length, a week): never stored, so it can
            // never drift from what it is derived from.
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    info =>
                    {
                        if (info.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object) return;
                        for (int i = info.Properties.Count - 1; i >= 0; i--)
                            if (info.Properties[i].Set is null) info.Properties.RemoveAt(i);
                    },
                },
            },
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new EntitySetConverterFactory());
        o.MakeReadOnly();
        return o;
    }

    public static string Serialise(Timetable t) => JsonSerializer.Serialize(t, Options);

    public static Timetable Deserialise(string json) =>
        JsonSerializer.Deserialize<Timetable>(json, Options) ?? throw new InvalidDataException("An empty timetable.");
}

internal sealed class EntitySetConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EntitySet<>);

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(EntitySetConverter<>).MakeGenericType(t.GetGenericArguments()[0]))!;

    private sealed class EntitySetConverter<T> : JsonConverter<EntitySet<T>> where T : class, IEntity
    {
        public override EntitySet<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            EntitySet<T>.Of(JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? []);

        public override void Write(Utf8JsonWriter writer, EntitySet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.OrderBy(v => v.Id).ToList(), options);
    }
}
