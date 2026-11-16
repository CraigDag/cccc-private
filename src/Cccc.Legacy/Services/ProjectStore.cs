using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CCCCScheduler.Helpers;
using CCCCScheduler.Models;
using CCCCScheduler.Platform;
using CCCCScheduler.ViewModels;

namespace CCCCScheduler.Services;

/// <summary>
/// Loads and saves a project. A "project" is a folder (conventionally *.cccproj)
/// containing <c>project.json</c> and an <c>images/</c> subfolder holding copies of
/// the floor-plan images, so the whole thing is portable on a network share.
/// </summary>
public sealed class ProjectStore
{
    public const string ProjectJsonName = "project.json";
    public const string ImagesDir = "images";
    public const string BackupsDir = "backups";

    /// <summary>
    /// <b>The settings <c>project.json</c> is written and read with — the only ones, for every host.</b>
    ///
    /// <para><see cref="DurationJsonConverter"/> is here rather than on the four properties that need it
    /// because this is the one door project.json comes through, and a duration is a duration wherever
    /// the format grows one next. It widens READING only — the file this build writes is unchanged to
    /// the byte, so nothing on the school's share is churned by its existence. See its own doc for the
    /// thirty-hour target that used to cost the owner the whole project.</para>
    ///
    /// <para>Public because a host that serializes a <see cref="ProjectFile"/> itself — a web server
    /// putting one into a response, say — must use <b>this instance</b> rather than settings that look
    /// like it. Every one of the four below changes the bytes: without
    /// <see cref="JsonSerializerOptions.WriteIndented"/> the file is one line, without
    /// <see cref="JsonStringEnumConverter"/> every <c>Scope</c> and <c>Status</c> becomes a number the
    /// next reader takes for something else, without <see cref="DurationJsonConverter"/> a limit
    /// written as <c>"01:30:00"</c> is unreadable, and without
    /// <see cref="JsonIgnoreCondition.WhenWritingNull"/> the file grows a null for every optional key
    /// in it. A second copy of these settings anywhere is the thing that eventually disagrees; see
    /// <c>JsonOptionsSharingTests</c>, which fails if one appears on this class.</para>
    ///
    /// <para><b>Frozen here, at construction, rather than left to freeze itself.</b> System.Text.Json
    /// makes an options instance read-only on its first use — but "first use" is the first save or
    /// load, and a host's startup runs before either. <c>ProjectStore.JsonOptions.WriteIndented =
    /// false</c> in a <c>Program.cs</c> would have succeeded, and every project on the school's share
    /// would have been rewritten on one line from then on, by a line nobody would think to look at.
    /// <see cref="JsonSerializerOptions.MakeReadOnly()"/> makes the guarantee unconditional instead of
    /// nearly-always-true.</para>
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = ReadOnlyJsonOptions();

    /// <summary>
    /// The one settings object, sealed before the property that exposes it can be read.
    ///
    /// <para><b><c>populateMissingResolver: true</c> is load-bearing, and the parameterless overload
    /// is not the same thing.</b> <see cref="JsonSerializerOptions.MakeReadOnly()"/> <i>requires</i> a
    /// <c>TypeInfoResolver</c> to have been set already and throws
    /// <see cref="InvalidOperationException"/> when there is none — from a static initialiser, which
    /// surfaces as a <see cref="TypeInitializationException"/> on the first save or load and takes the
    /// whole app with it. This overload fills in the reflection-based resolver, which is the one this
    /// build has always serialized with, and then freezes. Written the other way first; 270 tests
    /// said so.</para>
    /// </summary>
    private static JsonSerializerOptions ReadOnlyJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(), new DurationJsonConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private readonly IImageInfo _images;

    public ProjectStore(IImageInfo images) => _images = images;

    /// <summary>
    /// Absolute path to a floor's image inside a given project folder. The stored name comes from
    /// <c>project.json</c>, and <see cref="Path.Combine(string, string)"/> discards the base entirely
    /// when the second argument is rooted (and lets <c>..</c> resolve through), so a crafted or
    /// hand-edited name could otherwise point anywhere on disk — or at a UNC share. Anything that
    /// escapes the project folder is reduced to its bare file name inside <c>images/</c>.
    /// </summary>
    public static string ImageFullPath(string projectFolder, FloorPlan floor)
    {
        string rel = (floor.ImageFileName ?? "").Replace('/', Path.DirectorySeparatorChar);
        string combined = Path.Combine(projectFolder, rel);

        string root = Path.GetFullPath(projectFolder);
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(combined).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return combined;

        return Path.Combine(projectFolder, ImagesDir, Path.GetFileName(rel));
    }

    /// <summary>
    /// Creates a new project folder, copies the chosen image into images/ground.jpg,
    /// records its native pixel size, seeds the default groups, and writes project.json.
    /// </summary>
    public ProjectFile CreateNewProject(string projectFolder, string sourceImagePath, string floorName = "Ground")
    {
        var (rel, w, h) = CopyFloorImage(projectFolder, sourceImagePath, 0);

        var project = NewProject(new FloorPlan
        {
            Index = 0,
            Name = floorName,
            ImageFileName = rel,
            ImagePixelWidth = w,
            ImagePixelHeight = h,
        });

        Save(project, projectFolder);
        return project;
    }

    /// <summary>
    /// <b>What a new project contains, with no disk anywhere near it.</b> The building's name, the
    /// default strands and the default groups those strands carry — and the ground floor, when the
    /// caller has one.
    ///
    /// <para><b>It exists so that there is exactly one answer to "what is in a new project".</b>
    /// <see cref="CreateNewProject"/> is a file copy in front of this call, and a host with no file
    /// system — the web, which has no image picker and no save path to offer — calls it with no
    /// floor. A second copy of these four lines somewhere else is the hand-written field list this
    /// repository has been bitten by: a default added here and not there is silently absent, on a
    /// project that looks perfectly ordinary.</para>
    ///
    /// <para><b>A floor is optional and its absence is a real state</b>, not a placeholder. A room
    /// is created on a floor, so a project with none can hold every catalogue — classes, strands,
    /// groups, students, facilitators, the term and its holidays — and no rooms, and therefore no
    /// sessions. That is what a browser can make, because a floor is a photograph and drawing rooms
    /// on it is the one thing the web deliberately does not do.</para>
    /// </summary>
    public static ProjectFile NewProject(FloorPlan? ground = null)
    {
        var strands = Strand.CreateDefaults();
        var project = new ProjectFile
        {
            Building = new Building
            {
                Name = "CCCC",
                Strands = strands,
                Groups = Group.CreateDefaults(strands),
            },
        };

        if (ground is not null) project.Building.Floors.Add(ground);
        return project;
    }

    /// <summary>Copies a floor image into the project's images/ folder and reads its native pixel size.</summary>
    public (string relativePath, int width, int height) CopyFloorImage(string projectFolder, string sourceImagePath, int floorIndex)
    {
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, ImagesDir));

        string ext = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        string name = floorIndex == 0 ? "ground" : $"floor{floorIndex}";
        string rel = $"{ImagesDir}/{name}{ext.ToLowerInvariant()}";
        string dest = Path.Combine(projectFolder, rel.Replace('/', Path.DirectorySeparatorChar));
        // Validate the source before copying: a file WPF has no codec for (.webp, .heic, or anything
        // renamed to .png) must not land in images/, where it would be packaged into the .cccc and
        // never cleaned up. Throws for the caller to report.
        var (w, h) = _images.PixelSize(sourceImagePath);
        File.Copy(sourceImagePath, dest, overwrite: true);
        return (rel, w, h);
    }

    /// <summary>
    /// Reads project.json from a project folder and brings it to a state the rest of the app can rely
    /// on. Throws <see cref="InvalidDataException"/> for a file this build cannot safely open.
    /// </summary>
    public ProjectFile Load(string projectFolder)
        => LoadFrom(File.ReadAllText(Path.Combine(projectFolder, ProjectJsonName)));

    /// <summary>
    /// <b>The whole of loading a project, minus the disk.</b> Takes the text of a <c>project.json</c>
    /// and returns a <see cref="ProjectFile"/> the rest of the app can rely on — deserialised,
    /// repaired, and <b>migrated through every schema version</b>. Throws
    /// <see cref="InvalidDataException"/> for text this build cannot safely open.
    ///
    /// <para>This is the real implementation; <see cref="Load(string)"/> is a file read in front of
    /// it. A host with no file paths — a web server holding an upload, or a blob out of object
    /// storage — comes in here, and it matters that it is <em>here</em> and not in the wrapper: a
    /// migration chain that only runs on the path version is a host that silently reads a schema-1
    /// file as if it were schema 8. See <c>ByteRoundTripTests</c> and
    /// <c>MigrationOnTheBytePathTests</c>.</para>
    /// </summary>
    public static ProjectFile LoadFrom(string json)
    {
        ProjectFile? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Everything Validate refuses, it refuses in the app's own words. A file System.Text.Json
            // cannot read at all never reached Validate, so it escaped as a raw JsonException and the
            // owner was shown the serializer's wording — "Expected end of string ... Path: $ |
            // LineNumber: 0 | BytePositionInLine: 26" — for a file the app simply cannot open.
            //
            // Holiday.Scope made that reachable without any corruption at all: it is the first enum in
            // this format, and a scope NAME this build does not have (a newer build adding Strand, an
            // un-updated machine opening that file) throws here rather than falling back to anything.
            // The original exception is kept as the inner one — the dialog shows Message only, so it
            // stays available for diagnosis without being put in front of the owner.
            throw new InvalidDataException(UnreadableMessage(json), ex);
        }

        return Validate(project ?? throw new InvalidDataException("project.json was empty or invalid."));
    }

    /// <summary>
    /// What to tell the owner about a <c>project.json</c> that could not be deserialised at all.
    ///
    /// <para>The schema-version check that produces the precise "saved by a newer version" message
    /// lives in <see cref="Validate"/>, i.e. after deserialization — so a newer file that ALSO carries
    /// something this build cannot read (which is what "newer" tends to mean) would fail first and be
    /// described as corrupt, when the true answer is "update the app". Reading just the version number
    /// back off the raw text recovers that message for exactly the case it was written for; anything
    /// else falls through to the same wording <see cref="Validate"/> uses for a file whose schema
    /// version is not one this app has ever written.</para>
    /// </summary>
    private static string UnreadableMessage(string json)
        => TryReadSchemaVersion(json) is int version && version > ProjectFile.CurrentSchemaVersion
            ? NewerVersionMessage(version)
            : "This project could not be read. The file is likely corrupt, truncated, or not a "
              + "CCCC Scheduler project.";

    /// <summary>
    /// The <c>SchemaVersion</c> from raw JSON, or null when it cannot be read — which is the normal
    /// answer for a genuinely truncated file, and why every failure here is swallowed rather than
    /// replacing one unreadable-file error with another.
    /// </summary>
    private static int? TryReadSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var property in doc.RootElement.EnumerateObject())
                // Ordinal, like the deserializer's own property matching: a key that differs in case
                // would not have populated SchemaVersion either, so agreeing with it is the point.
                if (property.NameEquals("SchemaVersion") && property.Value.TryGetInt32(out int version))
                    return version;

            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Shared by both paths that can meet a file from a newer build — the version check in
    /// <see cref="Validate"/> and the unreadable-file path in <see cref="Load"/> — so the two cannot
    /// drift into telling the owner different things about the same file.
    /// </summary>
    private static string NewerVersionMessage(int incomingSchemaVersion)
        => $"This project was saved by a newer version of CCCC Scheduler (format {incomingSchemaVersion}; " +
           $"this version understands {ProjectFile.CurrentSchemaVersion}). Update the app to open it. " +
           "Opening it here would silently discard the newer version's data.";

    /// <summary>
    /// Repairs what can be repaired and refuses what can't.
    ///
    /// Deserialization alone is not enough: <c>System.Text.Json</c> overwrites <b>any</b> property
    /// initialiser when the JSON holds an explicit <c>null</c> — <c>= new()</c> and <c>= ""</c>
    /// alike, so a non-nullable <see langword="string"/> is no safer here than a collection — and it
    /// silently discards properties it doesn't recognise. A hand-edited, truncated or foreign file
    /// could therefore put the app into states the rest of the code treats as impossible — a null
    /// <c>Building</c>, a null collection, a null <c>Room.Name</c> (which is
    /// <see cref="ArgumentNullException"/> the moment <c>ReportBuilder</c> keys a room by it), or a
    /// session whose end precedes its start (which defeats both clash detection and the week grid's
    /// collision-free packing, neither of which the UI can produce).
    /// </summary>
    /// <summary>
    /// Puts one closure's or absence's date range into a shape that means what the file was trying to
    /// say. Two different defects, and telling them apart is the whole point.
    ///
    /// <para><b>Transposed</b> — both ends are real dates, in the wrong order. A range with To before
    /// From covers no date at all (<c>CoversDate</c> is <c>date &gt;= From &amp;&amp; date &lt;= To</c>,
    /// unsatisfiable), so such a file loads perfectly intact and then silently never applies: no
    /// error, no warning, just a holiday that stops closing anything and an absence that flags
    /// nothing. Swapped rather than rejected — both dates are almost certainly right and only their
    /// order is wrong. <c>MainViewModel.AddAbsence</c> already swaps on the way in and the holiday
    /// dialog only ever builds whole weeks, so the live UI cannot produce one; nothing normalised what
    /// arrives off disk.</para>
    ///
    /// <para><b>Half-written</b> — one end is missing. An absent key leaves the property at
    /// <c>default(DateOnly)</c>, i.e. 0001-01-01, and that is NOT a date: it is the hole where a date
    /// should have been. Swapping it produced a plausible-looking range closing two millennia (or,
    /// with From missing, no swap was even needed to get one), and nothing on screen looked wrong.
    /// Repaired to the day the file DOES name — one end missing means one day — which is the smallest
    /// reading consistent with what was actually written, and the only one that cannot invent a
    /// closure nobody meant. A record naming no date at all is left as it arrived: there is nothing to
    /// infer a day from, and it already covers nothing real.</para>
    ///
    /// <para><b>Why this cannot reject a legitimate long closure:</b> the discriminator is a MISSING
    /// end, never a duration. No length is measured and no bound is imposed, so a six-week summer
    /// shutdown or a year of building works, written correctly, passes through untouched however long
    /// it runs. 0001-01-01 is the one value treated as absent, and it is a value nothing in this app
    /// has ever written — the 2→3 holiday migration below already reads it as "no range here" for
    /// exactly the same reason.</para>
    /// </summary>
    private static (DateOnly From, DateOnly To) RepairRange(DateOnly from, DateOnly to)
    {
        if (from == default && to == default) return (from, to);   // nothing named — nothing to infer
        if (from == default) return (to, to);                      // half-written: the one day it names
        if (to == default) return (from, from);
        return to < from ? (to, from) : (from, to);                // transposed: both real, wrong order
    }

    /// <summary>
    /// Puts one absence's optional HOURS into a shape that means what the file was trying to say.
    /// <c>RepairRange</c>'s counterpart, one field across, and there is a real difference between them
    /// that decides the whole of this method.
    ///
    /// <para><b>Every defect here widens to the WHOLE DAY — both times cleared — rather than being
    /// narrowed or swapped.</b> A date range is repaired towards what it names because a range that
    /// covers nothing is inert and invisible; an hour window that covers nothing is worse than inert,
    /// because the absence around it is still real. <c>"StartTime": "16:00", "EndTime": "16:00"</c>
    /// loads perfectly, appears in <c>Manage ▸ Absences…</c> as three days off for surgery, and
    /// overlaps not one session — so nobody is asked to arrange cover for a week the person is in
    /// hospital. No error, no warning, and the failure lands on the day. Clearing both times is the
    /// widest reading of a broken window and the only one that cannot hide a gap, which is the
    /// direction this app takes every time: an over-wide absence is visible and correctable, a
    /// vanished one is neither.</para>
    ///
    /// <para><b>Transposed is not swapped here, and deliberately not.</b> <c>RepairRange</c> swaps a
    /// backwards date range because both dates are almost certainly right and only their order is
    /// wrong — and the live UI can produce one, which is why <c>AddAbsence</c> swaps on the way in.
    /// Neither absence editor can produce an inverted time window — both refuse one rather than
    /// storing it — so an inverted one arriving here carries no such evidence: 17:00–16:00 is as
    /// likely a truncated edit as a transposition, and inventing 16:00–17:00 from it would narrow a
    /// whole day's absence to an hour somebody never chose. The whole day is what the record already
    /// says everywhere else.</para>
    ///
    /// <para>The three defects, all of them arriving hand-edited, foreign or truncated: one end
    /// missing (half a window is not a window), zero-length, and inverted. A well-formed window is
    /// returned exactly as written, however long or short it runs — no duration is measured and no
    /// bound is imposed, so a fifteen-minute absence and an all-but-midnight one both pass through
    /// untouched.</para>
    ///
    /// <para><b>The rule itself lives on the model</b>, in <see cref="Absence.NormaliseWindow"/>, and
    /// is deferred to here rather than written out a second time. There are now four places that owe
    /// an answer to it — this loader, both entry dialogs' write doors, and <see cref="Absence"/>'s own
    /// reading — and a hand-copied version would be one edit away from disagreeing with the reading it
    /// exists to satisfy.</para>
    /// </summary>
    private static (TimeOnly? Start, TimeOnly? End) RepairWindow(TimeOnly? start, TimeOnly? end)
        => Absence.NormaliseWindow(start, end);

    /// <summary>
    /// What <see cref="Validate"/> names a thing whose stored name was an explicit <c>null</c>. The
    /// owner's own word for one, asked and answered: <i>"(unnamed) is fine"</i>.
    ///
    /// <para>It is a repair, not a label: the value is written into the model, so every reader —
    /// dialog, grid, export, report — sees a real name rather than each having to invent one. Only
    /// <see cref="Room"/> can need <see cref="NumberedUnnamed"/> beside it; see the block in
    /// <see cref="Validate"/> for why rooms are the one collection that cannot all share it.</para>
    /// </summary>
    private const string UnnamedPlaceholder = "(unnamed)";

    /// <summary><see cref="UnnamedPlaceholder"/> with a number, for the second and later of a set.</summary>
    private static string NumberedUnnamed(int n) => $"(unnamed {n})";

    // The colour an entity whose stored ColourHex was an explicit null is repaired to. Read off a
    // fresh instance of each model rather than written out again as literals, so each is BY
    // CONSTRUCTION the value an ABSENT key would have given and cannot drift from the model the day
    // somebody changes one. NullNameRepairTests pins the actual hex strings, so such a change turns
    // red there and becomes a decision rather than a silent shift in the owner's palette.
    private static readonly string DefaultRoomColour = new Room().ColourHex;
    private static readonly string DefaultGroupColour = new Group().ColourHex;
    private static readonly string DefaultFacilitatorColour = new Facilitator().ColourHex;
    private static readonly string DefaultClassColour = new ClassEntry().ColourHex;
    private static readonly string DefaultStrandColour = new Strand().ColourHex;
    private static readonly string DefaultBranchColour = new Branch().ColourHex;
    private static readonly string DefaultDisciplineColour = new Discipline().ColourHex;

    /// <summary>
    /// Puts one stored duration into a shape the app can both mean and show. Two defects, neither of
    /// which any box in the app can produce — <c>DurationText</c> refuses a leading sign, refuses a
    /// negative total, and rounds what it does accept to whole minutes — so both arrive hand-edited,
    /// foreign, or truncated, exactly like the shapes the loops above repair.
    ///
    /// <para>Written for the two facilitator limits, and used for the weekly TARGETS on
    /// <see cref="Area.WeeklyTarget"/> and <see cref="Building.WeeklyScheduledTarget"/> as well: a
    /// target is aimed at rather than kept under, but both defects read the same way for one and the
    /// same reasoning below applies unchanged. Kept as one implementation rather than two so the two
    /// cannot drift into disagreeing about what a stored duration may be — the paragraphs below say
    /// "limit" throughout for the case they were written about.</para>
    ///
    /// <para><b>Negative</b> — not a limit anybody can mean, and one that warns forever: nothing
    /// teaches less than nothing, so every week is over it, week after week, in a sentence whose
    /// bracketed "(maximum -12h)" reads like a rule rather than a defect. <b>Clamped to zero, not
    /// cleared</b>: zero is the strictest thing the box can say and the nearest reading of what was
    /// written, while clearing it to <c>null</c> would silently DISARM a limit the file was asking for
    /// — the exact inversion <c>DurationText</c>'s own doc exists to prevent, and one nothing on screen
    /// would look wrong for.</para>
    ///
    /// <para><b>Sub-minute</b> — a value the dialog can neither show nor type. The limit boxes speak
    /// hours and minutes, so <c>01:30:30</c> displays as "1h 30m" and reads back as <c>01:30:00</c>:
    /// a profile somebody opened only to LOOK at rewrote the stored value the moment they pressed OK.
    /// Normalised here instead, at the door, so what is stored is what the app can show and re-read.
    /// Truncated rather than rounded up, because truncating is what the dialog was already going to do
    /// to it — the app has always meant "1h 30m" by that value, and this makes it say so once, at load,
    /// rather than as an edit somebody did not make.</para>
    ///
    /// <para><b>Why neither can reject a legitimate value:</b> the discriminators are the SIGN and the
    /// sub-minute remainder. No length is measured and no ceiling is imposed, so every limit anybody
    /// could type survives exactly however large or small — a deliberate <see cref="TimeSpan.Zero"/>
    /// ("may teach nothing") included, and <c>null</c> ("no limit") untouched, since those two must
    /// never be confused with each other or with a repair.</para>
    /// </summary>
    private static TimeSpan? RepairLimit(TimeSpan? limit)
    {
        if (limit is not { } span) return null;                    // unset: not a value to repair
        if (span < TimeSpan.Zero) return TimeSpan.Zero;
        return new TimeSpan(span.Ticks - span.Ticks % TimeSpan.TicksPerMinute);
    }

    /// <remarks>
    /// <b>Private, and it stays private.</b> It was briefly widened to <c>internal</c> so tests could
    /// reach the migration chain without a folder to read it out of — and then the other half of the
    /// same task made <see cref="LoadFrom(string)"/> public, which reaches the whole chain from a
    /// string and is the better door anyway: it is the one that also decides what an unreadable file
    /// is told to the owner. <c>MigrationOnTheBytePathTests</c> goes through it. Nothing needed the
    /// widening, so it was reverted; the public surface should only grow where something uses it.
    /// </remarks>
    /// <summary>
    /// <b>The events' share of the repairs above, in one place</b> (schema 16). Every rule here is one
    /// the rest of this method already follows, applied to the new list:
    /// <list type="bullet">
    /// <item>an explicit null is repaired to what an absent key gives — an empty string or list;</item>
    /// <item>an id naming a room, facilitator or student this file does not contain is REMOVED, never
    /// repaired by inventing one; an event naming nobody is a legitimate event;</item>
    /// <item>two events sharing an id are given distinct ones, or editing one would change the other;</item>
    /// <item><b>a repeat with no end is given one</b>, a year after it starts, because the spec's rule is
    /// that nothing repeats for ever and <c>EventSchedule</c> would otherwise run to its horizon;</item>
    /// <item>a range that ends before it starts is made one day, or one hour, rather than refused.</item>
    /// </list>
    /// UNGATED on the incoming version, like the scrubs above: every route to these shapes is a file
    /// that already says it is current.
    /// </summary>
    private static void RepairEvents(ProjectFile p, HashSet<Guid> facilitatorIds, HashSet<Guid> studentIds)
    {
        var roomIds = p.Building.Rooms.Select(r => r.Id).ToHashSet();
        var seenEventIds = new HashSet<Guid>();
        p.Events.RemoveAll(e => e is null);

        foreach (var e in p.Events)
        {
            e.Title ??= "";
            e.Place ??= "";
            e.Note ??= "";
            e.Skipped ??= new List<DateOnly>();
            e.FacilitatorIds ??= new List<Guid>();
            e.StudentIds ??= new List<Guid>();

            if (e.Id == Guid.Empty || !seenEventIds.Add(e.Id))
            {
                e.Id = Guid.NewGuid();
                seenEventIds.Add(e.Id);
            }

            // A category this build has no name for is Other. An audience bit it has no name for is
            // dropped — and if that leaves NONE, the event becomes Administrators rather than Everyone,
            // because an empty audience means Everyone and an unknown audience must never widen to
            // that. Administrators is the narrowest audience there is (schema 19; it was Staff before).
            if (!Enum.IsDefined(e.Category)) e.Category = EventCategory.Other;
            const EventAudience known = EventAudience.Students | EventAudience.Facilitators
                                        | EventAudience.Staff | EventAudience.Administrators;
            if ((e.Audience & ~known) != 0)
                e.Audience = (e.Audience & known) is var kept && kept != 0 ? kept : EventAudience.Administrators;

            if (e.RoomId is { } room && !roomIds.Contains(room)) e.RoomId = null;
            e.FacilitatorIds = e.FacilitatorIds.Where(facilitatorIds.Contains).Distinct().ToList();
            e.StudentIds = e.StudentIds.Where(studentIds.Contains).Distinct().ToList();

            if (e.EndDate is { } end && end <= e.Date) e.EndDate = null;
            if (!e.AllDay && e.End <= e.Start)
                e.End = e.Start < new TimeOnly(23, 0) ? e.Start.AddHours(1) : new TimeOnly(23, 59);

            if (e.Repeat is { } rule)
            {
                rule.Days ??= new List<DayOfWeek>();
                if (rule.Every < 1) rule.Every = 1;
                if (rule.Count is < 1) rule.Count = null;
                if (!Enum.IsDefined(rule.Kind)) e.Repeat = null;
                else if (rule.Until is null && rule.Count is null) rule.Until = e.Date.AddYears(1).AddDays(-1);
            }
        }
    }

    private static ProjectFile Validate(ProjectFile p)
    {
        // Captured before the stamp at the end of this method overwrites it: the strand migration must
        // run once, on the 1→2 upgrade, not on every load of an already-current file. StrandId == null
        // is a legitimate, persistable "unassigned" state from schema 2 onward — it is how a deleted
        // strand's groups are represented — and re-deriving it from the group's name every load would
        // silently undo that deletion (and, for a strand with several groups under it, shatter it into
        // one strand per group instead of restoring the original).
        //
        // A file with no SchemaVersion key at all is read as the OLDEST schema, never the newest. The
        // property has to default to "current" for a ProjectFile built in memory (CreateNewProject's
        // is current by definition and must not be written out claiming to be a legacy file), so the
        // distinction lives in IsVersioned rather than in the value. Getting this backwards was worth
        // a real bug: an unversioned file deserialised as the newest format, every migration below was
        // skipped, and its holidays loaded with From = 0001-01-01 — matching no date, so they simply
        // stopped applying. Every migration below reads THIS local, never p.SchemaVersion, so each one
        // inherits that guard; a new block that read the property directly would silently reopen it.
        int incomingSchemaVersion = p.IsVersioned ? p.SchemaVersion : ProjectFile.OldestSchemaVersion;

        if (incomingSchemaVersion > ProjectFile.CurrentSchemaVersion)
            throw new InvalidDataException(NewerVersionMessage(incomingSchemaVersion));

        // Zero or negative is not "an older format to migrate" — it isn't a schema version this app has
        // ever written, so it is a sign of a truncated or foreign file. Unlike the structural gaps
        // repaired below, there is nothing sensible to repair this to — refuse rather than silently
        // adopting the file and stamping it as current on the next save. An ABSENT key is different and
        // is not caught here: it is the pre-versioning format, which is a real thing this app once
        // wrote, so it migrates.
        if (incomingSchemaVersion < ProjectFile.OldestSchemaVersion)
            throw new InvalidDataException(
                $"This project's format number ({incomingSchemaVersion}) is not a valid CCCC Scheduler schema " +
                "version. The file is likely corrupt, truncated, or not a CCCC Scheduler project.");

        p.Building ??= new Building();
        p.Holidays ??= new List<Holiday>();
        p.Absences ??= new List<Absence>();
        p.Events ??= new List<SchoolEvent>();

        // Same hole as the three lines above, one level down: an explicit "GroupIds": null in the JSON
        // overwrites the property's = new() initialiser, and a scoped closure with a null list would
        // then throw the first time anything asked which groups it applies to, rather than simply
        // applying to none of them. Unconditional, not part of the 3→4 migration below: a schema-4 file
        // can be hand-edited to this shape too, and it must recover on every load, not just the upgrade.
        foreach (var h in p.Holidays)
            h.GroupIds ??= new List<Guid>();

        foreach (var h in p.Holidays)
            (h.From, h.To) = RepairRange(h.From, h.To);
        foreach (var a in p.Absences)
            (a.From, a.To) = RepairRange(a.From, a.To);

        var b = p.Building;
        b.Floors ??= new ObservableCollection<FloorPlan>();
        b.Rooms ??= new ObservableCollection<Room>();
        b.Groups ??= new List<Group>();
        b.Facilitators ??= new List<Facilitator>();
        b.Students ??= new List<Student>();
        b.Branches ??= new List<Branch>();
        b.Disciplines ??= new List<Discipline>();
        b.Classes ??= new List<ClassEntry>();
        b.Areas ??= new List<Area>();

        b.Strands ??= new List<Strand>();

        // The same hole as every other collection guarded above, one level up: an explicit
        // "Sessions": null in the JSON overwrites the property's = new() initialiser. Room.Schedule
        // needed this per-room, walked one room at a time; Sessions is a single flat list on Building
        // itself since Task 3, so it is one line rather than a loop.
        b.Sessions ??= new List<ScheduleEntry>();

        // ============================================================================================
        //  The OTHER half of the hole this method's doc comment describes, and the half it had never
        //  plugged. System.Text.Json overwrites a `= ""` property initialiser when the JSON holds an
        //  explicit null exactly as it overwrites a `= new()` one — the twenty-two collection and
        //  object properties were repaired in the hardening wave, and every non-nullable string beside
        //  them was left. The type said non-nullable and the value was null.
        //
        //  ONE OF THEM WAS NOT MERELY UNTIDY. ReportBuilder keys room hours in a
        //  Dictionary<string, double> on Room.Name — the facilitator and group dictionaries beside it
        //  key by Guid — and its "ensure every room appears even with zero bookings" line is
        //  `roomHours[r.Name] = 0`, which is ArgumentNullException("key") on the first room, before a
        //  single session is counted. Term report… and the week report both go through it.
        //
        //  Repaired HERE, at the door, rather than guarded at each reader, for the reason all
        //  twenty-two above are: the value must be real by the time anything reads it, and a project
        //  loaded through this method is what every other path in the app is handed.
        //
        //  WHAT A NULL NAME BECOMES is the owner's decision and he made it — "(unnamed) is fine" —
        //  applied with the judgement the two exceptions below record:
        //
        //   • A COLOUR is repaired to its own entity's default, never to a placeholder. "(unnamed)" is
        //     not a colour; storing it would only move the failure to whatever parses it. The defaults
        //     are read off a fresh model instance rather than written out again here, so the repair is
        //     BY CONSTRUCTION the value an ABSENT key would have given, and cannot drift from the
        //     models the day somebody changes one. They are also the same literals
        //     MainViewModel's Apply*Changes already write for a null choice.
        //
        //   • THREE NAMES THE APP ALREADY HAS ITS OWN WORD FOR are repaired to "" instead:
        //     ScheduleEntry.Title (every reader says "(untitled)"), TermInfo.Name (the report says
        //     "Term", MainViewModel says "the term") and Holiday.Name (the grid says "no sessions this
        //     week"). "" is what an absent key gives, string.IsNullOrWhiteSpace cannot tell it from
        //     null, so the repair closes the null while being indistinguishable from today at every
        //     reader — and the word the owner sees stays the app's own rather than a second one
        //     invented for the same condition. This is the same judgement the colour rule is: use what
        //     the app already uses.
        //
        //  DELIBERATELY LEFT, and reported rather than guessed at:
        //   • FloorPlan.ImageFileName — a path, not a name. ImageFullPath already reads it as
        //     `(floor.ImageFileName ?? "")`, so a null one resolves to the project folder and the
        //     floor simply shows no image, which is the honest answer for a floor whose picture the
        //     file does not name. There is nothing to invent.
        //   • ProjectFile.LastSavedBy — audit metadata. Save overwrites it before every write, so a
        //     null is gone by the next save. Its one reader (the web's list of earlier saves,
        //     VersionHistory, since 2026-10-02) treats a null or blank one as "not recorded".
        // ============================================================================================

        b.Name ??= UnnamedPlaceholder;

        foreach (var f in b.Floors) f.Name ??= UnnamedPlaceholder;
        foreach (var s in b.Strands) { s.Name ??= UnnamedPlaceholder; s.ColourHex ??= DefaultStrandColour; }
        foreach (var g in b.Groups) { g.Name ??= UnnamedPlaceholder; g.ColourHex ??= DefaultGroupColour; }
        foreach (var f in b.Facilitators) { f.Name ??= UnnamedPlaceholder; f.ColourHex ??= DefaultFacilitatorColour; f.ContactEmail ??= ""; }

        // A student's name is now TWO strings and neither may be null, so the one line this used to be
        // is three. The rule is unchanged and is the one every entry above follows: an explicit null is
        // repaired, and "" — what an ABSENT key gives — is left exactly as it is, because "" is a
        // legitimate value that string.IsNullOrWhiteSpace cannot tell from null at any reader anyway.
        //
        // FIRSTNAME takes the placeholder and SURNAME does not, which is the same judgement the block
        // above makes between a name and a colour: "(unnamed)" is the app's own word for a thing whose
        // name is missing, and a student with neither half set is exactly that — Student.Name composes
        // to "(unnamed)" and every reader shows it. A surname repaired to "(unnamed)" would instead put
        // the placeholder into the MIDDLE of a real name ("Robin (unnamed)"), which is worse than the
        // blank it replaced. Blank is also the state every migrated student is in on purpose, so it
        // cannot be treated as a defect here.
        //
        // ContactEmail is repaired to "" for the same reason Holiday.Name is: "" is what an absent key
        // gives and there is nothing to invent an address from.
        //
        // Runs BEFORE the 7→8 migration below, which is right rather than incidental: on a pre-8 file
        // all three properties are absent and therefore already "" — there is nothing here to repair —
        // and the migration then overwrites all three from the old Name. On a schema-8 file the
        // migration does not run and this is the only repair there is.
        foreach (var s in b.Students)
        {
            s.FirstName ??= UnnamedPlaceholder;
            s.Surname ??= "";
            s.ContactEmail ??= "";
        }

        foreach (var br in b.Branches) { br.Name ??= UnnamedPlaceholder; br.ColourHex ??= DefaultBranchColour; }
        foreach (var d in b.Disciplines) { d.Name ??= UnnamedPlaceholder; d.ColourHex ??= DefaultDisciplineColour; }
        foreach (var c in b.Classes) { c.Name ??= UnnamedPlaceholder; c.ColourHex ??= DefaultClassColour; }
        foreach (var a in b.Areas) a.Name ??= UnnamedPlaceholder;

        foreach (var h in p.Holidays) h.Name ??= "";
        foreach (var t in p.Terms) t.Name ??= "";
        if (p.LegacyTerm is not null) p.LegacyTerm.Name ??= "";

        // Rooms are the ONE collection anything in this app keys BY NAME, so they are the one that
        // cannot all be given the same string. Repairing every null room name to one literal would
        // MANUFACTURE the duplicate-name defect the rooms wave reported and left as the owner's
        // decision: ReportBuilder collapses same-named rooms into one bucket and then emits one row per
        // Room object, each carrying the COMBINED total, while the headline counts each session once.
        // Measured, not assumed — two rooms holding 2h and 1h print two rows of 3h, six hours in a
        // table under a headline that says three, and nothing throws.
        //
        // A duplicate the owner TYPED is his data and an open decision; two buildings really do have
        // two rooms of one name on different floors, so nothing here enforces uniqueness. One the
        // LOADER invented out of two nulls is this method's own defect, and repairing corruption must
        // not create a second silent wrong number on the way past. So the numbering applies ONLY to
        // names this block invents, and can never touch one anybody typed.
        //
        // STABLE ACROSS LOADS, for two reasons and the second is the one that matters: the order is
        // the order the rooms appear in project.json (System.Text.Json preserves array order, this
        // method never reorders b.Rooms, and Save writes the collection as it stands) — and after one
        // load the names are REAL, so a re-load repairs nothing and cannot renumber anybody.
        //
        // Seeded with the names the file already holds so the invented one never collides with one of
        // them, including the literal "(unnamed)" itself. Ordinal, matching the comparer of the
        // dictionary this protects: ReportBuilder's Dictionary<string, double> uses the default one.
        //
        // Everything else in the building is resolved by Guid, so two nameless facilitators are two
        // people and stay two people; numbering them would assert an ordering the file never had.
        var takenRoomNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in b.Rooms)
            if (room.Name is not null)
                takenRoomNames.Add(room.Name);

        int unnamedRoomSuffix = 1;
        foreach (var room in b.Rooms)
        {
            room.ColourHex ??= DefaultRoomColour;
            if (room.Name is not null) continue;

            string candidate = UnnamedPlaceholder;
            while (!takenRoomNames.Add(candidate))
                candidate = NumberedUnnamed(++unnamedRoomSuffix);
            room.Name = candidate;
        }

        // Rooms are the EIGHTH and last collection in the building to need the duplicate-id renumber,
        // and the one where the cost of not having it is highest. Every other managed list is
        // referenced BY ID from a session, so losing one to a reconcile blanks a link. Before schema
        // 10, Room.Schedule hung off the Room with the sessions INSIDE it, so ApplyRoomChanges's
        // reconcile, which drops any room that is not the object its own dictionary kept for that id,
        // destroyed the second room's WHOLE TIMETABLE: rosters, registers and one-off week exceptions
        // with it. Unlisted, unnamed and unprompted, on an OK that changed nothing. Worse still, the
        // two rows then both applied to the one survivor, so it came out wearing the second room's name.
        //
        // It was survivable while a room could only exist by being drawn: nothing could reach a room
        // except by clicking its shape, and two shapes are two clicks. Manage ▸ Rooms… is what makes
        // it reachable, so the guard belongs at the door rather than in that method — which keeps its
        // TryAdd and its shadow clause as belt and braces, the same standing they have in the seven
        // siblings, instead of being the only thing between a foreign file and a lost term.
        //
        // Only the LATER duplicate is renumbered and the first occurrence keeps its id, matching all
        // seven. THIS SET IS ALSO WHAT THE SCRUB BELOW READS: since schema 10, ScheduleEntry.RoomId
        // does reference a room by id — the one link the paragraph above once claimed did not exist —
        // and the id it renumbers here is exactly the id a session must eventually agree with, or be
        // orphaned by the scrub after the 9->10 migration reads seenRoomIds below.
        //
        // Guid.Empty is NOT treated as invalid, following Groups, Facilitators, Classes, Areas and
        // Students rather than Strands and Absences: it is a sentinel for those two ("(all strands)";
        // "not a real absence"), whereas nothing reads a zero room id as meaning anything.
        var seenRoomIds = new HashSet<Guid>();
        foreach (var room in b.Rooms)
        {
            if (!seenRoomIds.Add(room.Id))
            {
                room.Id = Guid.NewGuid();
                seenRoomIds.Add(room.Id);
            }
        }

        // Every group must be reachable by a unique id: ApplyGroupChanges builds a Dictionary keyed on
        // Group.Id, which throws on a hand-edited or foreign file with two groups sharing one. Renumber
        // whichever group repeats an id already seen, so the load itself can never hand the rest of the
        // app a state that crashes the first time someone opens Manage ▸ Groups.
        var seenGroupIds = new HashSet<Guid>();
        foreach (var g in b.Groups)
        {
            if (!seenGroupIds.Add(g.Id))
            {
                g.Id = Guid.NewGuid();
                seenGroupIds.Add(g.Id);
            }
        }

        // Every strand must be reachable by a unique, non-zero id too. Guid.Empty is the "(all strands)"
        // sentinel used throughout the app (filter dropdown, PDF export title), so a strand carrying it
        // is indistinguishable from "no filter" — selecting it would show every strand's sessions. Two
        // strands sharing a non-zero id both appear in the dropdown, but BuildRow resolves a group's
        // StrandId with FirstOrDefault, so picking the second one filters and labels by the first
        // strand's identity instead. Renumber whichever strand(s) are wrong; the first occurrence of a
        // repeated id keeps it.
        var seenStrandIds = new HashSet<Guid>();
        foreach (var s in b.Strands)
        {
            if (s.Id == Guid.Empty || !seenStrandIds.Add(s.Id))
            {
                Guid oldId = s.Id;
                s.Id = Guid.NewGuid();
                seenStrandIds.Add(s.Id);

                // A zero id never survives this repair: every strand carrying Guid.Empty is renumbered
                // here, so any group that pointed at it (StrandId == Guid.Empty) must move to the new id
                // too, or the "repaired" strand comes out orphaned — it exists under a fresh Guid, but
                // every group that belonged to it still carries the old Guid.Empty and matches no strand
                // filter. A *duplicate* non-zero id is different: the first occurrence above always keeps
                // it (that's what "first occurrence unchanged" means), so a group referencing the shared
                // id still resolves correctly via BuildRow's FirstOrDefault — re-pointing it here would
                // wrongly drag it off the strand it already resolves to. Verified: only Guid.Empty is ever
                // fully vacated by this loop, so only Guid.Empty needs this.
                if (oldId == Guid.Empty)
                    foreach (var g in b.Groups)
                        if (g.StrandId == oldId)
                            g.StrandId = s.Id;
            }
        }

        // Facilitators need both repairs the two loops above do — one for nulls, one for duplicate ids —
        // for the same reasons, one object across. Neither was ever written, so both defects are live.
        //
        // A null Availability: System.Text.Json overwrites a = new() initialiser when the JSON holds an
        // explicit null, exactly as this method's doc comment describes and as the other repairs
        // here already assume. A null one reaches FacilitatorAvailability.HasAny — called for every
        // facilitator on every session from ScheduleConflicts.FindIn ← RebuildConflicts ←
        // RebuildSessions ← project OPEN — and throws a NullReferenceException there. A hard crash
        // opening a file, not a bad render. The app cannot produce that shape (the dialogs always build
        // a real list, and JsonOptions declines to WRITE nulls at all while reading them happily), so it
        // arrives only hand-edited, foreign or truncated. Schema 5 makes it likelier rather than less:
        // once two of a facilitator's properties are optional, "a facilitator object with keys missing"
        // becomes the ordinary shape people see and edit.
        //
        // A duplicate id: ApplyFacilitatorChanges builds a Dictionary keyed on Facilitator.Id, which
        // throws on the OK button of Manage ▸ Facilitators, on the UI thread, with no try — the identical
        // failure the group block's comment above names, in the third of the three collections that
        // needed the lesson and the one that never got it.
        //
        // Only the LATER duplicate is renumbered, and the first occurrence keeps its id — the same rule
        // the strand block follows, and here it is what makes the repair cheap: every
        // ScheduleEntry.FacilitatorIds, SessionException.CoverFacilitatorIds and Absence.FacilitatorId
        // that named the shared id still resolves to the same person it resolved to before the load, so
        // nothing has to be re-pointed. Re-pointing them would drag sessions, covers and absences onto
        // somebody who never taught them.
        var seenFacilitatorIds = new HashSet<Guid>();
        foreach (var f in b.Facilitators)
        {
            f.Availability ??= new List<AvailabilityWindow>();

            if (!seenFacilitatorIds.Add(f.Id))
            {
                f.Id = Guid.NewGuid();
                seenFacilitatorIds.Add(f.Id);
            }
        }

        // Absences are the fourth collection needing the same repair, and the one the three loops above
        // never grew for. It is project-level rather than building-level, which is the only reason it
        // sits apart from them; the reasoning is identical, and BOTH defects bite here.
        //
        // Something keys on the id: ApplyFacilitatorProfile keeps this person's absences by matching
        // them against the whitelist the profile dialog hands back, and the dialog drops Guid.Empty
        // from that whitelist because a zero id is not an id — one absence saying "keep me" would
        // otherwise keep a different one. So an absence carrying zero is DELETED by opening somebody's
        // profile and pressing OK without touching anything, and two absences sharing an id cannot be
        // told apart: removing one on the profile removes neither, because the survivor's id is in the
        // kept list and matches the deleted one too.
        //
        // Nothing anywhere references an absence by id — a session points at facilitators, never at an
        // absence — so unlike the strand block there is nothing to re-point, and renumbering is free.
        // The later duplicate is the one renumbered, first occurrence unchanged, purely to match the
        // rule the other three follow.
        var seenAbsenceIds = new HashSet<Guid>();
        foreach (var a in p.Absences)
        {
            if (a.Id == Guid.Empty || !seenAbsenceIds.Add(a.Id))
            {
                a.Id = Guid.NewGuid();
                seenAbsenceIds.Add(a.Id);
            }
        }

        // …and the OTHER id an absence carries: the facilitator it names. The last of the half-swept
        // shapes — live path covered, load path missed — that the AreaId, ClassId, StudentIds and
        // FacilitatorIds scrubs each closed one collection at a time.
        // MainViewModel.ApplyFacilitatorChanges has always run exactly this cascade when somebody is
        // deleted through Manage ▸ Facilitators… (Absences.RemoveAll(a => removed.Contains(
        // a.FacilitatorId))); an absence naming nobody arrives instead through a restored backup, a
        // second machine or a hand-edit, and loaded unrepaired.
        //
        // It does not fail loudly, which is why it survived: the absence is INERT — no facilitator is
        // ever matched by it, so it flags no session and changes no answer anywhere — but it shows in
        // Manage ▸ Absences… as a row with a blank name (AllAbsences resolves it to "(removed)") and
        // no way to tell what it was for or whose it was.
        //
        // REMOVED, never repaired by inventing somebody: there is no name to invent one from, and
        // unlike a session — which is a legitimate, persistable thing with nobody assigned to it — an
        // absence belongs to a person by definition and one belonging to nobody is not a record of
        // anything. Removal is also what the live path does, and the two paths agreeing about what a
        // project contains is the line every one of these scrubs is drawn on.
        //
        // Resolved against seenFacilitatorIds, the post-repair set built by the duplicate-id renumber
        // above, so an id that block reissued cannot be read here as dangling. Nothing points AT an
        // absence, so dropping one re-points nothing.
        // MOVED DOWN THE METHOD BY SCHEMA 14, and the move is the point: an absence can now name a
        // STUDENT, so this scrub needs seenStudentIds as well as seenFacilitatorIds, and that set is
        // not built until the student renumber further down. Splitting the rule in two — facilitators
        // here, students there — is precisely the half-swept shape this comment block is about, so
        // the whole of it went to where both sets are known. See ScrubDanglingAbsences.

        // Classes are the FIFTH collection needing the duplicate-id renumber, and the only one that
        // had no repair whatsoever — the whole of the class handling in this method was the
        // `b.Classes ??=` line above. Same reasoning as the four loops before it, one collection
        // across, and it stopped being cosmetic the moment anything keyed on a class:
        // MainViewModel.ApplyClassChanges now indexes the catalogue by id to reconcile it in place
        // (with TryAdd, because ToDictionary is what threw ArgumentException on the facilitator list,
        // on the UI thread, with no try), and a session's link to the catalogue resolves by id — so
        // two entries sharing one would be resolved by FirstOrDefault to whichever happened to load
        // first, which is exactly the ambiguity the strand block above exists to prevent.
        //
        // Only the LATER duplicate is renumbered and the first occurrence keeps its id, matching the
        // other four, and here that is what makes the repair free: anything already naming the shared
        // id still resolves to what it resolved to before the load, so nothing has to be re-pointed.
        //
        // Guid.Empty is NOT treated as invalid here, unlike the strand and absence loops. It is a
        // sentinel for those two ("(all strands)" in the filter; "not a real absence" in the profile
        // dialog's keep-list); nothing in the app reads a zero class id as meaning anything, so a
        // class carrying one is merely an odd id, and reissuing it would re-point every link naming
        // it for no gain. This follows the Groups and Facilitators loops, which dedupe only.
        //
        // seenClassIds is the post-repair set of live class ids, so anything scrubbing orphaned class
        // references later in this method can use it and cannot orphan an id this block just moved —
        // exactly as the Holiday.GroupIds scrub uses seenGroupIds.
        var seenClassIds = new HashSet<Guid>();
        foreach (var c in b.Classes)
        {
            if (!seenClassIds.Add(c.Id))
            {
                c.Id = Guid.NewGuid();
                seenClassIds.Add(c.Id);
            }
        }

        // Areas are the SIXTH managed collection, and the first to arrive with its repairs already
        // written rather than added after a review found them missing — which is how Classes reached
        // their fifth year with none at all. Same reasoning as the five loops above, one collection
        // across, and it is load-bearing from the day the collection exists: a ClassEntry.AreaId
        // resolves by id, so two areas sharing one are told apart by FirstOrDefault picking whichever
        // loaded first, and an activity's hours land under a heading nobody chose. Anything that
        // GROUPS by area — which is every figure the admin dock shows — silently reports the same area
        // twice, each with part of its own hours, and both lines read plausibly.
        //
        // Only the LATER duplicate is renumbered and the first occurrence keeps its id, matching all
        // five. Here that is what makes the repair free: every AreaId already naming the shared id
        // still resolves to what it resolved to before the load, so nothing has to be re-pointed — and
        // the orphan scrub further down cannot mistake a moved id for a dangling one.
        //
        // Guid.Empty is NOT treated as invalid, following Groups, Facilitators and Classes rather than
        // Strands and Absences: it is a sentinel for those two ("(all strands)" in the filter; "not a
        // real absence" in the profile dialog's keep-list), whereas nothing reads a zero area id as
        // meaning anything. Unassigned is encoded as a NULL AreaId, never as a zero one, so a zero here
        // is merely an odd id and reissuing it would re-point every class naming it for no gain.
        //
        // seenAreaIds is the post-repair set of live area ids, for the AreaId scrub further down — the
        // same contract seenGroupIds has with the Holiday.GroupIds scrub. The 5→6 migration empties it
        // alongside b.Areas so it stays true after that block, whichever way the file arrived.
        var seenAreaIds = new HashSet<Guid>();
        foreach (var a in b.Areas)
        {
            if (!seenAreaIds.Add(a.Id))
            {
                a.Id = Guid.NewGuid();
                seenAreaIds.Add(a.Id);
            }
        }

        // Students are the SEVENTH managed collection to need the duplicate-id renumber, and the last
        // one that had none — every other one got theirs after a real crash or a real silent loss.
        // ApplyStudentChanges used TryAdd rather than ToDictionary precisely BECAUSE this collection
        // was not renumbered here, calling itself "a live path rather than belt and braces". This is
        // the belt; that comment has been updated to say so.
        //
        // Two students under one id are one person to everything that resolves by id. A roster naming
        // the shared id enrols the FIRST of them, and the second is on no roster in the building
        // however many sessions name them — invisible on the grid's counts, in Register… and in every
        // attendance rate.
        //
        // Then Manage ▸ Students… makes it permanent, and this is the part worth the block. Its OK
        // hands back every row it is showing, and the reconcile does two things with the pair: it
        // drops any student that is not the object its own dictionary kept for that id, and it applies
        // each listed name to whatever that dictionary returns. So pressing OK without touching
        // anything DELETES the second person and RENAMES the survivor to them — two people become one,
        // wearing the second one's name, with the dialog having shown two rows throughout. Verified:
        // "Sam, Robin, Kit" comes back "Robin, Kit".
        //
        // Only the LATER duplicate is renumbered and the first occurrence keeps its id, matching all
        // six above, and here that is what makes the repair free: every ScheduleEntry.StudentIds and
        // AttendanceRecord.PresentStudentIds entry naming the shared id still resolves to the person
        // it resolved to before the load, so nothing has to be re-pointed. The renumbered student is
        // enrolled in nothing, which is honest — they already were.
        //
        // Guid.Empty is NOT treated as invalid, following Groups, Facilitators, Classes and Areas
        // rather than Strands and Absences: it is a sentinel for those two ("(all strands)" in the
        // filter; "not a real absence" in the profile dialog's keep-list), whereas nothing anywhere
        // reads a zero student id as meaning anything. A zero one is merely an odd id, and reissuing
        // it would re-point every roster naming it for no gain.
        //
        // seenStudentIds is the post-repair set of live student ids, for the roster and register
        // scrub at the end of this method — the same contract seenGroupIds has with the
        // Holiday.GroupIds scrub, and seenAreaIds with the AreaId one. THE ORDER IS LOAD-BEARING: that
        // scrub must see the set as it is AFTER this block, or the id this block just left with the
        // first student would be read as dangling and every roster naming it would be emptied.
        var seenStudentIds = new HashSet<Guid>();
        foreach (var s in b.Students)
        {
            if (!seenStudentIds.Add(s.Id))
            {
                s.Id = Guid.NewGuid();
                seenStudentIds.Add(s.Id);
            }
        }

        // Branches and Disciplines are the NINTH and TENTH managed collections, and the first pair to
        // arrive with every repair already written rather than added after a review found them missing.
        // Same reasoning as the eight loops above, one collection across each, and both are
        // load-bearing from the day the collections exist:
        //
        //   • two disciplines sharing an id are one specialism to everything that resolves by id, so a
        //     Student.DisciplineId naming the shared one is told apart by FirstOrDefault picking
        //     whichever loaded first — a student filed under a specialism nobody chose;
        //   • two branches sharing an id do the same to Discipline.BranchId one level up, and quietly
        //     split or merge whatever a branch-level view groups by;
        //   • both Apply*Changes methods index their collection by id, which is the ArgumentException
        //     the facilitator list threw on the UI thread with no try — avoided there by TryAdd, and
        //     avoided here by the ids being unique before either dialog ever sees them.
        //
        // Only the LATER duplicate is renumbered and the first occurrence keeps its id, matching all
        // eight. Here that is what makes the repair free: every DisciplineId and BranchId already
        // naming the shared id still resolves to what it resolved to before the load, so nothing has to
        // be re-pointed — and the two orphan scrubs further down cannot mistake a moved id for a
        // dangling one.
        //
        // Guid.Empty is NOT treated as invalid, following Groups, Facilitators, Classes, Areas,
        // Students and Rooms rather than Strands and Absences: it is a sentinel for those two ("(all
        // strands)"; "not a real absence"), whereas nothing reads a zero branch or discipline id as
        // meaning anything. Unassigned is encoded as a NULL BranchId/DisciplineId, never a zero one.
        //
        // seenBranchIds and seenDisciplineIds are the post-repair sets of live ids, for the scrubs
        // below — the same contract seenGroupIds has with the Holiday.GroupIds scrub. THE ORDER IS
        // LOAD-BEARING, as it is for students: a scrub reading the set as it stands BEFORE this block
        // would read an id this block just moved as dangling and null every link naming it.
        var seenBranchIds = new HashSet<Guid>();
        foreach (var br in b.Branches)
        {
            if (!seenBranchIds.Add(br.Id))
            {
                br.Id = Guid.NewGuid();
                seenBranchIds.Add(br.Id);
            }
        }

        var seenDisciplineIds = new HashSet<Guid>();
        foreach (var d in b.Disciplines)
        {
            if (!seenDisciplineIds.Add(d.Id))
            {
                d.Id = Guid.NewGuid();
                seenDisciplineIds.Add(d.Id);
            }
        }

        // Schema 1 had no strands: groups WERE the programmes. Give each group a strand of its own so
        // nothing is lost and no session is re-pointed — a session keeps referencing its group, and the
        // strand is derived through it. Runs only for a file that arrived below schema 2 — see the
        // capture above.
        //
        // The name mapping below is migration policy, deliberately, and must stay confined to this
        // migration: it translates an OLD FILE's vocabulary ("External Hire") into the vocabulary this
        // app uses today ("Event Hire"). MainViewModel.ApplyGroupChanges's new-group path does NOT call
        // it — a name typed into Manage ▸ Groups today is not legacy vocabulary to translate, it is
        // exactly what the user means, so a brand-new group named "External Hire" gets a strand named
        // "External Hire". The two sites were unified onto one shared mapping once before, and that made
        // creating a group named "External Hire" silently create a strand called "Event Hire" instead —
        // do not do that again; see the comment at ApplyGroupChanges's new-group branch for its half.
        if (incomingSchemaVersion < 2)
        {
            foreach (var g in b.Groups)
            {
                if (g.StrandId is Guid existing && b.Strands.Any(s => s.Id == existing)) continue;

                string strandName = Strand.StrandNameForGroupName(g.Name);

                var strand = b.Strands.FirstOrDefault(s => s.Name == strandName);
                if (strand is null)
                {
                    strand = new Strand { Name = strandName, ColourHex = g.ColourHex };
                    b.Strands.Add(strand);
                }
                g.StrandId = strand.Id;
            }
        }

        // Schema 2 and earlier stored a holiday as a whole week keyed by its Monday. A Monday-to-Sunday
        // range means exactly the same thing, so no project changes behaviour.
        if (incomingSchemaVersion < 3)
        {
            foreach (var h in p.Holidays)
            {
                if (h.From != default) continue;               // already a range
                h.From = h.WeekMonday;
                h.To = h.WeekMonday.AddDays(6);
            }

            // Cleared once migrated, on every holiday in the file — including ones that were already
            // ranges. WeekMonday is legacy input only, and a migrated holiday that keeps it writes a
            // field contradicting its own From/To into every later save. That exact staleness has
            // already caused one regression: the Term & holidays dialog read WeekMonday, showed a
            // bogus date, and fed it back through Holiday.ForWeek on OK.
            foreach (var h in p.Holidays)
                h.WeekMonday = default;
        }

        // Schema 3 and earlier had no notion of scope: a holiday shut the entire building, ungrouped
        // sessions and External Hire included. WholeBuilding is HolidayScope's first value and so the
        // one a Scope-less holiday deserialises as, which means the assignments below are no-ops for
        // every project this app has ever written — that is exactly the point, and it is what makes
        // the upgrade behaviour-neutral without the loader having to guess anything.
        //
        // They run anyway, unconditionally, so that a hand-edited or foreign pre-4 file cannot smuggle
        // in a scope no build could have produced. A file claiming schema 3 while carrying
        // "Scope": "Groups" is not a closure the owner narrowed on purpose — nothing could set that
        // when schema 3 was current — and honouring it would quietly reopen the school to everyone
        // outside the list, during a closure the owner believes is school-wide. Any GroupIds that
        // arrived with it go too, so nothing is left contradicting the scope it ends up with.
        //
        // Gated on the incoming version like the two blocks above, and for the same reason in reverse:
        // on a file that genuinely says 4, a Groups scope IS the owner's deliberate choice. Ungated,
        // every Prep-only closure would widen back to the whole building on the very next load and
        // cancel Degree's term with it.
        if (incomingSchemaVersion < 4)
        {
            foreach (var h in p.Holidays)
            {
                h.Scope = HolidayScope.WholeBuilding;
                h.GroupIds.Clear();
            }
        }

        // Schema 4 and earlier had no notion of a personal limit: nobody had a maximum teaching week or
        // a maximum gap between their classes, because neither property existed. Both are nullable and
        // null means NO LIMIT, which is exactly what a facilitator object without the keys deserialises
        // to — so the assignments below are no-ops for every project this app has ever written. That is
        // the point, and it is what makes the upgrade behaviour-neutral without the loader guessing
        // anything: nothing changes for anybody until somebody sets a limit.
        //
        // They run anyway, unconditionally for a pre-5 file, so that a hand-edited or foreign one cannot
        // smuggle in a limit no build could have produced. A file claiming schema 4 while carrying
        // "MaxWeeklyHours" is not a maximum its owner chose — nothing could set one when schema 4 was
        // current, and a 1.7.x build round-tripping a schema-5 file DROPS both keys (System.Text.Json
        // discards members it doesn't know) rather than preserving them, so that route cannot produce
        // this shape either. Honouring it would start warning somebody about a ceiling they never set
        // and cannot find the source of.
        //
        // Gated on the incoming version like the three blocks above, and for the same reason in reverse:
        // on a file that genuinely says 5, both limits ARE the owner's deliberate choice. Ungated, every
        // limit in the project would clear on the very next load — and because absent means "no limit",
        // both warnings would simply stop firing, project-wide, with nothing on screen looking wrong.
        if (incomingSchemaVersion < 5)
        {
            foreach (var f in b.Facilitators)
            {
                f.MaxWeeklyHours = null;
                f.MaxGapBetweenSessions = null;
            }
        }

        // …and whatever survives that gate is put into a shape the app can both mean and show. UNGATED,
        // unlike the block above and for the same reason the null-Availability repair is: a file that
        // genuinely says 5 can be hand-edited to either defect too, and must recover on every load
        // rather than only on the upgrade. Runs after the clear, so on a pre-5 file there is nothing
        // left to repair — which is the right order, not a coincidence: the gate decides whether a
        // limit exists at all, and this only shapes one that does.
        foreach (var f in b.Facilitators)
        {
            f.MaxWeeklyHours = RepairLimit(f.MaxWeeklyHours);
            f.MaxGapBetweenSessions = RepairLimit(f.MaxGapBetweenSessions);
        }

        // Schema 5 and earlier had no curriculum structure at all: there were no areas, a class could
        // not belong to one, nothing carried a weekly target, and every scheduled hour was taught time
        // because there was no other kind. An empty list, a null AreaId, and Workload = Counted are
        // exactly what a schema-5 file's missing keys deserialise to — so the assignments below are
        // no-ops for every project this app has ever written. That is the point, and it is what makes
        // the upgrade behaviour-neutral without the loader guessing anything: an existing project opens
        // with an empty area list, every class unassigned, and every hour counting, which is precisely
        // how it behaves today.
        //
        // Workload defaults to Counted rather than NotCounted for that reason, and the direction is not
        // arbitrary: everything on every timetable that exists is taught time until somebody says
        // otherwise. Lunch is the case somebody unticks — the owner's words, "included in the daily
        // time in the school but not in the working load for hours" — and cleaning and open training
        // fall out the same way with no extra rule.
        //
        // They run anyway, unconditionally for a pre-6 file, so that a hand-edited or foreign one
        // cannot smuggle in structure no build could have produced. A file claiming schema 5 while
        // carrying an area, an assignment or a workload flag did not get them from a build of this app:
        // nothing could set any of them when schema 5 was current, and a 1.8.x build round-tripping a
        // schema-6 file DROPS all of them (System.Text.Json discards members it doesn't know) rather
        // than preserving them, so that route cannot produce this shape either.
        //
        // A Workload of NotCounted is the one worth the block on its own. Honouring it removes an
        // activity's hours from the taught total SILENTLY: the timetable is identical, the session
        // still shows, still exports, still clashes — the week just comes up short against a target the
        // administrator is reading as a fact, with nothing on screen to explain it. A smuggled AreaId
        // is the same failure with the sign reversed, crediting hours to an area nobody put them in.
        //
        // Gated on the incoming version like the four blocks above, and for the same reason in reverse:
        // on a file that genuinely says 6, the areas, targets and assignments are the administrator's
        // deliberate work. Ungated, the whole curriculum structure would clear on the very next load —
        // and because empty/null/Counted is exactly what a fresh project looks like, it would do so with
        // nothing on screen looking wrong.
        if (incomingSchemaVersion < 6)
        {
            b.Areas.Clear();
            seenAreaIds.Clear();          // kept true for the AreaId scrub below; see its comment
            b.WeeklyScheduledTarget = null;

            foreach (var c in b.Classes)
            {
                c.AreaId = null;
                // The < 11 block below currently recomputes this to the same value from
                // LegacyCountsAsWorkload (cleared just below), which makes this line unobservable by
                // any fixture today — falsifying it alone cannot turn any test red. It stays anyway, as
                // defence in depth: if that gate is ever narrowed to `>= 6 && < 11` (so it stops running
                // for pre-6 files, matching the schema range its own name suggests), this line becomes
                // load-bearing again and a pre-6 file's Workload would otherwise go unscrubbed.
                c.Workload = WorkloadKind.Counted;
                // Two spellings of this flag exist now: this bool is the pre-11 one, Workload above is
                // the post-11 one. Clearing only Workload here is not enough — the < 11 block below
                // ALSO runs on this same load (every incoming version < 11 includes every pre-6 one),
                // and re-derives Workload from THIS bool. Leaving a smuggled "CountsAsWorkload": false
                // untouched here would have the < 11 block quietly UNDO this scrub and re-apply the
                // smuggled value straight back, on the very load that is meant to clear it. true is
                // what a legitimately-absent key deserialises to, so this restores "as if never
                // written" rather than inventing a state — the same answer Workload gets, spelled the
                // other way.
                c.LegacyCountsAsWorkload = true;
            }
        }

        // …and whatever survives that gate is put into a shape the app can both mean and show, exactly
        // as the facilitator limits are one block above: gated clear first, ungated shaping second, so
        // on a pre-6 file there is nothing left to repair. UNGATED for the same reason — a file that
        // genuinely says 6 can be hand-edited to a negative or sub-minute target too, and must recover
        // on every load rather than only on the upgrade.
        //
        // A target is not a limit, but both repairs read the same way for one, and a negative target is
        // the worse of the two defects because THE HEADLINE IS THE GAP: a week that taught four hours
        // against a target of minus twenty reports twenty-four hours of surplus, so a corrupt area
        // reads as the healthiest one in the dock. Clamped to zero rather than cleared, because zero is
        // the nearest reading of what was written while clearing would silently remove a line the file
        // was asking for.
        foreach (var a in b.Areas)
            a.WeeklyTarget = RepairLimit(a.WeeklyTarget);
        b.WeeklyScheduledTarget = RepairLimit(b.WeeklyScheduledTarget);

        // Schema 6 and earlier had no absence times at all: an Absence was a range of WHOLE DAYS, and
        // somebody at a dentist appointment for an hour had to be marked away for the day — which
        // flagged every one of their sessions as needing cover, including the ones they were there
        // for. Two nulls are exactly what a schema-6 file's missing keys deserialise to, and null
        // times mean whole days, so the assignments below are no-ops for every project this app has
        // ever written. That is the point, and it is what makes the upgrade behaviour-neutral without
        // the loader guessing anything: every existing absence opens covering exactly the days it
        // covered before, which is precisely how it behaves today.
        //
        // They run anyway, unconditionally for a pre-7 file, so that a hand-edited or foreign one
        // cannot smuggle in hours no build could have produced. A file claiming schema 6 while
        // carrying "StartTime" did not get it from a build of this app: nothing could set one when
        // schema 6 was current, and a 1.9.x build round-tripping a schema-7 file DROPS both keys
        // (System.Text.Json discards members it doesn't know) rather than preserving them, so that
        // route cannot produce this shape either.
        //
        // And honouring it is the SILENT direction, which is what earns the block. An absence its
        // owner recorded as three whole days off for surgery would quietly narrow to one hour: the
        // absence still sits in Manage ▸ Absences… saying "16 – 18 Nov", the timetable is unchanged,
        // and every session outside that hour simply stops being flagged, with nothing on screen
        // explaining why nobody is arranging cover for a week somebody is in hospital.
        //
        // Gated on the incoming version like the five blocks above, and for the same reason in
        // reverse: on a file that genuinely says 7, the hours are the owner's deliberate choice.
        // Ungated, every timed absence in the project would widen back to a whole day on the very
        // next load — and because whole days is what they came from, it would do so with nothing on
        // screen looking wrong.
        if (incomingSchemaVersion < 7)
        {
            foreach (var a in p.Absences)
            {
                a.StartTime = null;
                a.EndTime = null;
            }
        }

        // …and whatever survives that gate is put into a shape the app can both mean and show, the
        // same gated-clear-then-ungated-shaping pair as the two blocks above. UNGATED for the same
        // reason: a file that genuinely says 7 can be hand-edited to half a window or an inverted one
        // too, and must recover on every load rather than only on the upgrade.
        foreach (var a in p.Absences)
            (a.StartTime, a.EndTime) = RepairWindow(a.StartTime, a.EndTime);

        // Schema 7 and earlier barely knew what a student was: an id and one name, with no surname, no
        // contact, no group, no specialism — and no notion of a Branch or a Discipline at all. This is
        // the block that gives every existing student the shape the rest of the feature reads, and it
        // is the SECOND migration on this project that cannot be a no-op: unlike areas, limits, holiday
        // scopes and absence times, "what a missing key deserialises to" (a blank FirstName) is not the
        // right answer for a file that already holds the school's whole roll.
        //
        // THE NAME MOVES WHOLE. Student.LegacyName is what schema 7's "Name" key deserialises into —
        // see that property — and it goes into FirstName verbatim, with Surname left blank. NOTHING IS
        // EVER GUESSED: splitting on the last space would break on "van der Berg" and on anyone entered
        // surname-first, and it would fail SILENTLY ON A HANDFUL of real students rather than loudly on
        // all of them, which is precisely the failure nobody would find. The owner chose this; the
        // surname is typed in once, by a human, in the profile.
        //
        // Because the split is additive, that leaves Student.Name — computed over the two — reading
        // back BYTE-IDENTICAL to what schema 7 stored, for every student in every project. That is not
        // a happy accident: it is the whole reason this upgrade touches no reader anywhere.
        //
        // A null "Name" becomes "(unnamed)" exactly as it did before the split, via the ungated repair
        // near the top of this method — this block hands it null and that block names it. Pinned by
        // NullNameRepairTests.
        //
        // The other five assignments are no-ops for every project this app has ever written: an absent
        // Surname, ContactEmail, GroupId, DisciplineId, Branches and Disciplines deserialise to blank,
        // blank, null, null, empty and empty. That is the point, and it is what makes the upgrade
        // behaviour-neutral without the loader guessing anything.
        //
        // They run anyway, unconditionally for a pre-8 file, so that a hand-edited or foreign one
        // cannot smuggle in structure no build could have produced. A file claiming schema 7 while
        // carrying a "DisciplineId" did not get it from a build of this app: nothing could set one when
        // schema 7 was current, and a 1.12.x build round-tripping a schema-8 file DROPS every one of
        // these keys (System.Text.Json discards members it does not know) rather than preserving them,
        // so that route cannot produce this shape either.
        //
        // And honouring them is the SILENT direction, twice over. A smuggled DisciplineId files a
        // student under a specialism nobody chose, and shows up in every per-discipline count as though
        // somebody had; a smuggled ContactEmail puts an address the school never collected on a person
        // — on the very file that is about to start carrying contact details for minors — with nothing
        // on screen saying where it came from.
        //
        // Gated on the incoming version like the six blocks above, and for the same reason in reverse:
        // on a file that genuinely says 8, every one of these IS the administrator's deliberate work.
        // Ungated, the surname, contact, group and specialism of every student in the project would
        // clear on the very next load, and the branch and discipline lists with them — and because
        // blank/null/empty is exactly what a migrated project looks like, it would do so with nothing
        // on screen looking wrong.
        if (incomingSchemaVersion < 8)
        {
            foreach (var s in b.Students)
            {
                s.FirstName = s.LegacyName ?? UnnamedPlaceholder;   // whole, never split; see above
                s.Surname = "";
                s.ContactEmail = "";
                s.GroupId = null;
                s.DisciplineId = null;
            }

            b.Branches.Clear();
            b.Disciplines.Clear();
            seenBranchIds.Clear();          // kept true for the two scrubs below; see their comments
            seenDisciplineIds.Clear();
        }

        // 8 → 9: A ROOM'S FLOOR BECAME OPTIONAL, so <c>Room.FloorIndex</c> is an int? and null means
        // "on no floor" — an ordinary room that schedules, exports and takes sessions and is simply
        // not on any plan. Before 9 a room could only point INTO Building.Floors, which is why that
        // list had to be non-empty before any room could exist.
        //
        // TWO STEPS, AND THE FIRST IS THE ONE THAT MATTERS.
        //
        // 1. An ABSENT "FloorIndex" key meant FLOOR 0 under every schema before this one — that is
        //    what an int property with no key deserialises to — and it means null now. Restoring the
        //    zero is what keeps a pre-9 file's ground-floor rooms on the ground floor. It is
        //    defensive rather than observed: every build that has written a room wrote the key,
        //    because JsonIgnoreCondition.WhenWritingNull does not omit a value type. But the cost of
        //    being wrong is every room in a project silently coming off its floor, and the cost of
        //    the line is one assignment.
        //
        // 2. A NUMBER NAMING NO FLOOR becomes null. Before 9 that state was unsayable and therefore
        //    accidental — a hand-edit, or a floor removed some other way — and it already behaved as
        //    "no floor" everywhere: RoomList.UnknownFloorText on both room lists, an empty floor
        //    column in the room grid and in every export row, and never drawn on any plan. Schema 9
        //    gives that behaviour a representation, so this writes it down instead of leaving two
        //    spellings of one fact in the file. NOT a repair by invention: nothing here guesses a
        //    floor for a room, which is exactly what a default floor would have been.
        //
        // Gated on the incoming version, like the seven blocks above. Ungated, a schema-9 file that
        // an operator hand-edited to name a floor they are about to add back would have the number
        // taken off it on the very next load. Reachable-by-hand corruption keeps behaving as it
        // always did — see Room.FloorIndex's own remarks for why the two forms are not merged.
        if (incomingSchemaVersion < 9)
        {
            foreach (var r in b.Rooms)
            {
                if (r is null) continue;
                r.FloorIndex ??= 0;
                if (r.FloorIndex is int idx && !b.Floors.Any(f => f is not null && f.Index == idx))
                    r.FloorIndex = null;
            }
        }

        // …and the legacy key is cleared once read, on every student in the file — including ones that
        // arrived with no name to migrate. UNGATED, unlike the 2→3 holiday migration's identical clear
        // of Holiday.WeekMonday, and deliberately so: a stray "Name" hand-edited onto a schema-8 file
        // would otherwise be re-serialised on every save from then on, sitting beside FirstName and
        // Surname and free to contradict them forever. That exact staleness has already cost this
        // project one regression, when the Term & holidays dialog read a migrated holiday's surviving
        // WeekMonday, showed a bogus date, and fed it back through on OK.
        //
        // Clearing it is not data loss: on a pre-8 file the value is already in FirstName, and on a
        // schema-8 file no build wrote it.
        foreach (var s in b.Students)
            s.LegacyName = null;

        // A StrandId that resolves to no strand in b.Strands is corruption — hand-edited, or a strand
        // removed some other way outside Manage ▸ Strands — and must never be repaired by inventing a
        // replacement. That is unlike the schema-1 migration just above, which must always give a group
        // SOME strand because a schema-1 group never had one to lose: from schema 2 onward, a NULL
        // StrandId is the legitimate result of the user deleting a strand through Manage ▸ Strands
        // (ApplyStrandChanges nulls it deliberately), and inventing a replacement on the next load would
        // silently resurrect a programme the user chose to remove. A non-null StrandId that matches no
        // strand, on the other hand, is never legitimate — null it. Unlike the block above, this is not
        // gated on incomingSchemaVersion: it runs on every load, so a hand-edited or otherwise-corrupted
        // schema-2-or-later file recovers too, not just the file going through the 1→2 upgrade.
        foreach (var g in b.Groups)
        {
            if (g.StrandId is Guid sid && !b.Strands.Any(s => s.Id == sid))
                g.StrandId = null;
        }

        // Discipline.BranchId is Group.StrandId one hierarchy across, so its scrub is that loop
        // verbatim and for the identical reasons: UNGATED, because every route that produces the shape
        // produces it on a file that says it is current; a dangling id set to NULL and never repaired
        // by inventing a branch, because null is the legitimate, persistable "unassigned" state
        // ApplyBranchChanges leaves behind on a delete, and inventing one would silently resurrect a
        // family the user chose to remove; and resolved against seenBranchIds, the post-repair set, so
        // an id the renumber above reissued cannot be read here as dangling.
        foreach (var d in b.Disciplines)
        {
            if (d.BranchId is Guid bid && !seenBranchIds.Contains(bid))
                d.BranchId = null;
        }

        // The two links a STUDENT carries — the group they are timetabled with and the specialism they
        // chose. Same orphan class as the three scrubs around them, and both arrive the same way: a
        // restored backup, a second machine, a hand-edit, or a group deleted some other way.
        //
        // Neither fails loudly, and that is what earns the block. A student whose GroupId names nothing
        // is still on the roll, still on every roster, still in every register — they simply belong to
        // no cohort, so they drop out of anything filtered or counted by group while the head count
        // still includes them. Two figures on one report disagreeing, with no visible cause, which is
        // the sentence the AreaId, ClassId, StudentIds and FacilitatorIds scrubs were each written
        // under. A dangling DisciplineId is the same with the sign reversed: the student reads as
        // specialising in something, and every per-discipline count silently omits them.
        //
        // Follows Group.StrandId exactly:
        //   • UNGATED on incomingSchemaVersion, like every scrub in this method;
        //   • a non-null id matching nothing live is set to NULL, NEVER repaired by inventing a record.
        //     There is no name to invent one from, and null is a legitimate PERMANENT state for both:
        //     it is how every migrated student arrives, it is what every Prep student's discipline is
        //     by design, and it is what the two live-path cascades leave behind on a delete;
        //   • resolved against seenGroupIds and seenDisciplineIds, the post-repair sets built by the
        //     duplicate-id renumbers above (and emptied by the 7→8 migration alongside b.Disciplines),
        //     so an id either block reissued cannot be mistaken here for one that no longer exists.
        //
        // Their live-path counterparts are the delete cascades in MainViewModel.ApplyGroupChanges and
        // ApplyDisciplineChanges, so the load path and the live path cannot disagree about what the
        // project contains — the half-swept shape ("the live path scrubs this, the load path does not")
        // that every scrub in this method was written to close.
        foreach (var s in b.Students)
        {
            if (s.GroupId is Guid gid && !seenGroupIds.Contains(gid))
                s.GroupId = null;
            if (s.DisciplineId is Guid did && !seenDisciplineIds.Contains(did))
                s.DisciplineId = null;
        }

        // The same orphan class as the StrandId loop directly above, one object across: a
        // Holiday.GroupIds entry naming a group this file does not contain — a restored backup, a
        // second machine, a hand-edit. An id pointing at nothing does not fail loudly; it silently
        // stops meaning anything, and here that means a Groups-scoped closure that shuts NOBODY while
        // still loading, listing in Term & holidays and re-saving exactly as though it did.
        //
        // MainViewModel.ApplyGroupChanges already does this in memory when a group is deleted through
        // Manage ▸ Groups. Nothing did it for what arrives off disk, which is the same half-swept
        // shape the strand migration had: the live path covered, the load path missed.
        //
        // seenGroupIds is the post-repair set of live group ids — built by the duplicate-id renumber
        // above, so a group whose id was reissued is present under its NEW id and nothing here can
        // orphan an id that block just moved.
        //
        // What happens to a closure left scoped to nobody is decided the same way ApplyGroupChanges
        // decides it, so the two paths cannot disagree about what a project contains:
        //   • a closure THIS scrub emptied is removed — it can no longer close anything, and repairing
        //     it would mean inventing a group it never named;
        //   • a closure that already named no groups is left alone — nothing about it was reconciled,
        //     and it may be one the owner is midway through scoping;
        //   • a WholeBuilding closure is never removed whatever stray ids it carries, because it never
        //     consults them. It keeps shutting the doors; only the strays go, so nothing is left on
        //     the file contradicting the scope it has.
        var emptiedByScrub = new List<Holiday>();
        foreach (var h in p.Holidays)
        {
            if (h.GroupIds.Count == 0) continue;                       // nothing to reconcile
            h.GroupIds.RemoveAll(id => !seenGroupIds.Contains(id));
            if (h.GroupIds.Count == 0 && h.Scope == HolidayScope.Groups) emptiedByScrub.Add(h);
        }
        if (emptiedByScrub.Count > 0)
            p.Holidays.RemoveAll(emptiedByScrub.Contains);             // by identity, not by value

        // The third instance of the two scrubs directly above, one object across, and the first of the
        // two the admin dock needs. A ClassEntry.AreaId naming an area this file does not contain — a
        // restored backup, a second machine, a hand-edit, or an area deleted some other way — does not
        // fail loudly. It silently stops meaning anything: the class still lists, still colours, still
        // schedules, and its hours simply drop out of every area total while the scheduled total still
        // counts them. A discrepancy between two figures on the same panel, with no visible cause.
        //
        // Follows Group.StrandId exactly:
        //   • UNGATED on incomingSchemaVersion, like both scrubs above — every route that produces the
        //     shape produces it on a file that says it is current, so it must recover on EVERY load
        //     rather than only on an upgrade;
        //   • a non-null AreaId matching no live area is set to NULL, never repaired by inventing an
        //     area. Null is a legitimate, persistable "unassigned" state — it is how every class in
        //     every project today arrives, and the class is unharmed by being in no area;
        //   • resolved against seenAreaIds, the post-repair set built by the duplicate-id renumber (and
        //     emptied by the 5→6 migration alongside b.Areas), so an area whose id that block reissued
        //     cannot be mistaken here for one that no longer exists.
        //
        // Its live-path counterpart has since been built and is MainViewModel.ApplyAreaChanges, which
        // nulls exactly these links behind Manage ▸ Areas' delete. Both halves are swept, so the load
        // path and the live path cannot disagree about what the project contains — the half-swept shape
        // the Holiday.GroupIds comment above was written about, where the live path was covered and the
        // load path missed. That dialog now states its deletions rather than inferring them from an
        // absent row; this scrub is unaffected either way, because it resolves against what the file
        // actually holds rather than against anything the dialog said.
        foreach (var c in b.Classes)
        {
            if (c.AreaId is Guid aid && !seenAreaIds.Contains(aid))
                c.AreaId = null;
        }

        // LegacySchedule is normalised HERE rather than relied on from the migration immediately
        // below: an explicit "Schedule": null in the JSON overwrites the property's = new()
        // initialiser, exactly as this method's doc comment describes. Room.LegacySchedule is what a
        // schema-9-or-earlier file's sessions deserialise into now — see that property — and walking
        // it unguarded throws on exactly the hand-edited shape
        // ProjectStoreTests.Load_RepairsMissingOrNullStructure carries a case for.
        foreach (var room in b.Rooms)
        {
            if (room is null) continue;
            room.LegacySchedule ??= new List<ScheduleEntry>();
        }

        // 9 -> 10: a room stops OWNING its sessions and Building.Sessions starts holding them all. The
        // room a session is read out of IS its room, so this is a MOVE and not a guess — and a move
        // rather than a copy, because a room's own list has to end up gone, or a save straight after
        // loading would write every session out twice: once under the room that used to hold it and
        // once on the building.
        //
        // The MOVE is gated on incomingSchemaVersion, deliberately: an explicit null RoomId on a
        // schema-10 entry is a session whose space has not been decided, and an ungated move would
        // walk whichever room's LegacySchedule it happened to be read from and stamp that id onto
        // whatever it found there — which for a genuine schema-10 file is nothing, but for one
        // hand-edited to carry a "Schedule" array would silently promote a stray value into a real,
        // stamped session. Genuine migration data only ever arrives on a file claiming a schema before
        // 10, which is exactly what this gate tests.
        //
        // Placed HERE, after the LegacySchedule normalisation just above, rather than beside the < 9
        // block this bump otherwise sits next to: this is the earliest point every room's
        // LegacySchedule is guaranteed non-null, exactly the reason the < 6 catalogue-linking block
        // below is also down here rather than beside its own numeric neighbours.
        if (incomingSchemaVersion < 10)
        {
            foreach (var r in b.Rooms)
            {
                if (r is null) continue;
                // `?? Enumerable.Empty<ScheduleEntry>()`, not `!`: the normalisation loop above already
                // guarantees this is never null by the time execution reaches here, but it does so
                // across a SEPARATE loop over the same b.Rooms, which nullable-reference analysis
                // cannot see through — the compiler's warning is a real gap in what it can prove, not a
                // false positive to silence. Failing safe to "nothing to migrate" costs nothing on the
                // path that is actually taken and matches this file's own aversion to null-forgiving
                // operators elsewhere (see RoomGridLayout.Plan's identical reasoning).
                foreach (var s in r.LegacySchedule ?? Enumerable.Empty<ScheduleEntry>())
                {
                    if (s is null) continue;
                    s.RoomId = r.Id;
                    b.Sessions.Add(s);
                }
            }
        }

        // ...and the legacy key is cleared once read, on every room in the file — including ones that
        // arrived with nothing to migrate. UNGATED, the same shape Student.LegacyName's own clear a
        // little further down uses and — until the final whole-branch review — this clear did NOT:
        // it used to be `r.LegacySchedule.Clear()` inside the `< 10` gate just above, which never runs
        // on a file already claiming schema 10. A schema-10 file hand-edited to carry a "Schedule"
        // array was therefore never migrated (correctly — the gate above is what tells genuine
        // migration data apart from a stray hand-edit) but ALSO never cleared, so it round-tripped
        // forever: read on load, left standing, and written straight back out on every save from then
        // on — precisely what this property's own doc comment claims cannot happen. Clearing to
        // `null` rather than emptying the list matters too: `JsonIgnoreCondition.WhenWritingDefault`
        // does not suppress an empty list, only a null one, which is the other half of the same defect
        // — every room of every file this app saved carried a `"Schedule": []` it did not need to.
        foreach (var r in b.Rooms)
        {
            if (r is null) continue;
            r.LegacySchedule = null;
        }

        // A NEW kind of dangling reference, and the same class as the six orphan scrubs elsewhere in
        // this method (Group.StrandId, Discipline.BranchId, Student.GroupId/DisciplineId,
        // Holiday.GroupIds, ClassEntry.AreaId): a ScheduleEntry.RoomId naming a room this file does
        // not contain — a restored backup, a second machine, a hand-edit, or (before this task closed
        // the gap) a room deleted through a door that forgot to carry the session with it. It could
        // not arise before schema 10, because a session had no room reference of its own to dangle —
        // it simply sat inside whichever room's own list, and that link could not go stale.
        //
        // b.Rooms elements are guarded against a hand-edited null TWICE above (the LegacySchedule
        // normalisation and the < 10 migration, both `if (r is null) continue;`) — b.Sessions
        // elements, until the final whole-branch review, were guarded nowhere, and this is the first
        // per-session loop in this method, so a hand-edited "Sessions": [null] threw
        // NullReferenceException right here on `s.RoomId`. Dropped outright, on every load, matching
        // the zero-or-negative-length sweep a few lines below: there is strictly less to repair a null
        // session INTO than a zero-length one, so the same "genuine deletion only" answer applies more
        // strongly. Reachable only on a file already claiming schema 10 — an earlier schema has no
        // top-level "Sessions" array on Building for a hand-edit to plant a null inside.
        //
        // Placed here, immediately before the first per-element read of b.Sessions, rather than beside
        // `b.Sessions ??= new List<ScheduleEntry>();` above: that line guarantees the LIST is real, not
        // that its ELEMENTS are — the conflation this fix exists to undo — and every session in this
        // list arrived either straight off JSON or (already null-guarded) out of a pre-10 file's
        // LegacySchedule, so nothing between here and there needed the elements to be non-null yet.
        b.Sessions.RemoveAll(s => s is null);

        // Resolved against seenRoomIds, the post-renumber set built above, so a room whose id that
        // block reissued cannot be mistaken here for one that no longer exists. UNGATED, matching all
        // six precedents: every route that produces the shape produces it on a file that already says
        // it is current, so recovery has to run on every load rather than only on the 9->10 upgrade.
        // Set to null, never repaired by inventing a room — null is the legitimate, persistable
        // "space not decided" state every one of these sessions is already allowed to be in.
        foreach (var s in b.Sessions)
            if (s.RoomId is Guid rid && !seenRoomIds.Contains(rid))
                s.RoomId = null;

        // Schema 5 and earlier had no link between a session and the catalogue at all: a session named
        // its activity in free text and a ClassEntry only populated a dropdown, so "hours per activity"
        // was only ever as reliable as consistent typing. The administrator's own sheet is the proof it
        // is not reliable — Floor Acrobatic beside Floor Acrobatics, two spellings of one activity
        // differing only in case, and Conditioning with a double space. This is where every existing
        // session gets the link the dock counts by, and it is the ONE migration on this project that
        // cannot be a no-op: unlike areas, limits and holiday scopes, "what a missing key deserialises
        // to" (null) is not the right answer for a file that already has a whole timetable in it.
        //
        // The policy, and each half of it matters:
        //   • matched on a NORMALISED comparison — trimmed, inner whitespace collapsed,
        //     case-insensitive (ClassCatalogue.Key) — so the three spellings above land on one entry
        //     and one tally, which is the entire point of the exercise;
        //   • a title matching nothing CREATES an entry carrying that exact title. Nothing is dropped
        //     and nothing is renamed: the created entry's name is the first spelling seen, verbatim,
        //     and every session's Title is untouched throughout;
        //   • an EMPTY title creates nothing and links nothing — there is no activity to name;
        //   • NO FUZZY MATCHING. Floor Acrobatic and Floor Acrobatics become two entries, because the
        //     app cannot know they are the same activity. A human can, and the catalogue merge is how
        //     they say so. Any rule that guessed those two together would also merge Move and
        //     Movement, which are two different things on this timetable;
        //   • FIRST OCCURRENCE WINS when two catalogue entries normalise to one key, matching the
        //     tiebreak the five duplicate-id repairs above follow — and resolved by a walk, never a
        //     ToDictionary, which would throw ArgumentException here, on load, on the administrator's
        //     real file. ClassEntry.Name is not unique and nothing at any layer enforces it.
        //
        // GATED on the incoming version, like the five blocks above, and this one is not merely
        // conventional: an unlinked session with a title is a legitimate, persistable state from
        // schema 6 onward — it is exactly what deleting a class through Manage ▸ Classes leaves behind
        // — so re-running this on every load would silently resurrect the catalogue entry the user
        // just deleted, and re-link the sessions they detached from it. The same trap the strand
        // migration's comment describes, one collection across.
        //
        // Running it inside the gate also overwrites any ClassId a pre-6 file arrived carrying, which
        // is the same reasoning the four gated blocks above carry: nothing could set one when schema 5
        // was current, and a 1.8.x build round-tripping a schema-6 file DROPS the key (System.Text.Json
        // discards members it does not know) rather than preserving it, so a smuggled link is not the
        // owner's work and honouring it would credit an activity's hours to a class nobody chose.
        //
        // Ids of entries created here are added to seenClassIds so the scrub below cannot null the very
        // links this block just wrote — the same contract seenGroupIds has with the Holiday.GroupIds
        // scrub, and seenAreaIds with the AreaId one.
        // Sessions that are not representable through the UI go FIRST, ahead of the migration below,
        // because a session that is about to be discarded must not mint a catalogue entry on its way
        // out. A hand-edited or foreign file carrying a 12:00–12:00 session used to be migrated — its
        // title matched nothing, so FindOrAdd created a class for it — and only then dropped, leaving
        // the project one activity richer than any session in it referenced: a phantom row in
        // Manage ▸ Classes and in the dock's breakdown, carrying no hours and impossible to account
        // for or delete without wondering what it was. The removal itself was already here; only its
        // position was wrong, and nothing between the two orders depends on the old one.
        //
        // A zero-or-negative-length session is dropped outright, on every load, from wherever it
        // arrived — hand-edited, foreign, or a from a build with a bug of its own. This used to be a
        // per-room RemoveAll because a room's own list was the only thing with a RemoveAll to call;
        // Building.Sessions is a single flat list, so it is one call through the seam's bulk-remove
        // rather than a loop. Sessions was already guaranteed real near the top of this method — both
        // as a LIST (`b.Sessions ??= new List<ScheduleEntry>();`) and, since the null-element sweep
        // above, ELEMENT BY ELEMENT too — so there is nothing left to normalise here. The two are not
        // the same guarantee: this comment used to claim the first proved the second, which is exactly
        // the conflation that let a hand-edited "Sessions": [null] reach `s.RoomId` unguarded a few
        // lines up and throw.
        b.RemoveSessions(s => s.End <= s.Start);

        if (incomingSchemaVersion < 6)
        {
            // Every session, not every session a room happens to hold: b.Rooms -> b.SessionsIn(room)
            // never visits one whose RoomId is null. Unreachable from an unmodified pre-schema-10 file
            // (RoomId did not exist yet), but not from a hand-edited or foreign one merely claiming an
            // old SchemaVersion — precisely this method's stated audience — so there is no reason a
            // room-less session's title should be exempt from class-linking. Fixed alongside the other
            // sites of the same shape found in the same review.
            foreach (var s in b.AllSessions())
            {
                var linked = ClassCatalogue.FindOrAdd(b.Classes, s.Title);
                s.ClassId = linked?.Id;
                if (linked is not null) seenClassIds.Add(linked.Id);
            }
        }

        // The third instance of the two scrubs directly above, one object across, and the second of the
        // two the admin dock needs. A ScheduleEntry.ClassId naming a class this file does not contain —
        // a restored backup, a second machine, a hand-edit, or a class deleted some other way — does
        // not fail loudly. It silently stops meaning anything: the session still displays, still
        // exports, still clashes and still occupies its room, and its hours simply drop out of every
        // per-activity and per-area tally while the scheduled total still counts them. A discrepancy
        // between two figures on the same panel, with no visible cause.
        //
        // Follows Group.StrandId exactly:
        //   • UNGATED on incomingSchemaVersion, unlike the migration above and like both scrubs before
        //     it — a restored backup, a second machine, or a 1.8.x build round-tripping a schema-6 file
        //     all produce the shape on a file that says it is current, so it must recover on EVERY
        //     load rather than only on an upgrade;
        //   • a non-null ClassId matching no live class is set to NULL, never repaired by inventing an
        //     entry and never by touching the Title. Removing a class from the catalogue is not
        //     permission to rename the timetable, and null is a legitimate, persistable "unlinked"
        //     state — it is how every session in every project arrives before the block above runs;
        //   • resolved against seenClassIds, the post-repair set built by the duplicate-id renumber and
        //     extended by the migration above, so neither an id that block reissued nor a class that
        //     block created can be mistaken here for one that no longer exists.
        //
        // Its live-path counterpart is the delete cascade in MainViewModel.ApplyClassChanges, so the
        // load path and the live path cannot disagree about what the project contains — the half-swept
        // shape the Holiday.GroupIds comment above was written about. That parity silently broke the
        // moment a session could carry no RoomId: walked as b.Rooms -> b.SessionsIn(room), a room-less
        // session was invisible here, so a dangling ClassId on one survived a load that would have
        // scrubbed the same session's live-path equivalent on the spot. AllSessions() is what "every
        // session" has to mean now that a session is not guaranteed to belong to a room.
        foreach (var s in b.AllSessions())
            if (s.ClassId is Guid cid && !seenClassIds.Contains(cid))
                s.ClassId = null;

        // The term's own field, repaired like every other one above — and the only one here whose
        // absence was fatal rather than merely wrong.
        //
        // ScheduleResolver.EndMonday adds 7 * (WeekCount - 1) days to the start Monday, and
        // WeekTally.For asks TermContains on EVERY dock rebuild. Past roughly 416,000 weeks that
        // AddDays leaves the range of DateOnly and throws ArgumentOutOfRangeException — not on a
        // report or an export, but on the render path, so the dock threw the moment the project
        // opened and went on throwing. Nothing in the app could produce such a count (Term & holidays
        // offers 1–30), so it arrives hand-edited or foreign, exactly like the duplicate ids and the
        // smuggled scopes repaired above; and like them it is clamped rather than refused, because a
        // number nobody could have typed is not a term to honour.
        //
        // Negative goes to zero, matching RepairLimit and preserving the meaning TermContains already
        // gives it: a term of no weeks contains nothing.
        // The clamp itself is below, after 13 → 14 has moved a legacy Term into the list — a repair
        // that ran before the migration would clamp an empty list and leave the fossil untouched.

        // ---- 10 → 11: the old CountsAsWorkload bool becomes WorkloadKind ---------------------------
        //
        // GATED on the incoming version, like every block above, and for the same reason in reverse: an
        // ungated read would re-apply the legacy bool to a schema-11 file whose Workload the owner had
        // since set to DropIn, quietly demoting it back to Counted on every load.
        //
        // NOTHING becomes DropIn here. No schema-10 file knew the difference, and inferring it from a
        // class's name is the kind of cleverness that files a term's sessions under the wrong heading.
        //
        // ScheduleEntry.Scope needs no block: SessionScope.Group is ordinal 0, so a session with no
        // Scope key already deserialises to exactly what it meant. ScheduleEntry.GroupIds is
        // different, and this comment used to claim otherwise — true only of the field's ABSENCE (an
        // absent GroupIds deserialises to an empty list, harmlessly, since Scope.Group never reads it)
        // and silent about a GroupIds that is PRESENT but stale, which is exactly the shape a
        // Scope.Groups session takes when a cohort it once shared a class with has since been deleted.
        // That case gets its own scrub below, following Holiday.GroupIds' shape.
        if (incomingSchemaVersion < 11)
        {
            foreach (var c in b.Classes)
                c.Workload = c.LegacyCountsAsWorkload ? WorkloadKind.Counted : WorkloadKind.NotCounted;
        }

        // SCHEMA 13 HAS NO MIGRATION BLOCK, AND THAT IS THE DECISION RATHER THAN AN OMISSION. A
        // schema-12 file arrives with Building.WeeksAreSeparate false and every ScheduleEntry
        // .WeekMonday null, which is exactly what it already was: one shared pattern, drawn for every
        // week. Nothing honest could be written here — the pattern carries no date at all, so any week
        // this code chose to stamp it with would be invented, and a term's timetable silently
        // appearing on weeks nobody put it on is the failure the whole format change exists to
        // prevent. The conversion is MainViewModel.SeparateTheWeeks, where a person says which weeks.
        //
        // What DOES belong here is the repair below, and it is ungated: every comparison WeekMonday
        // takes part in is an equality against a Monday, so one that is not a Monday puts a session in
        // no week at all — invisible on every grid, uncopied by every copy, deleted by nothing, and
        // carried through every save. A hand-edit or a restored backup is all it takes.
        // ---- 13 → 14: the one Term becomes a list, and a week can be frozen -----------------------
        //
        // GATED, like every block that reads a legacy key: an ungated read would put the fossil back
        // over a Terms list somebody has since edited, on every load. ClassEntry.LegacyCountsAsWorkload
        // is where that lesson was learned.
        //
        // Nothing is invented. One term becomes one term; no term becomes no terms, which means exactly
        // what a null Term meant. The second term a school probably wants is typed in Term & holidays,
        // because only the school knows when its spring starts.
        if (incomingSchemaVersion < 14 && p.LegacyTerm is { } legacy && p.Terms.Count == 0)
            p.Terms.Add(legacy);

        // Cleared on every load whatever the version, so a file written by this build carries the key
        // once — the migration's own input — and never again. JsonOptions omits a null.
        p.LegacyTerm = null;

        // The term counts, repaired here rather than with the other field repairs above because they
        // only exist once the line above has run. ScheduleResolver.EndMonday adds 7 * (WeekCount - 1)
        // days to the start Monday, and WeekTally.For asks TermContains on EVERY dock rebuild. Past
        // roughly 416,000 weeks that AddDays leaves the range of DateOnly and throws — not on a report
        // or an export, but on the render path, so the dock threw the moment the project opened and
        // went on throwing. Nothing in the app could produce such a count (Term & holidays offers
        // 1–30), so it arrives hand-edited or foreign, and like every other such value it is clamped
        // rather than refused: a number nobody could have typed is not a term to honour.
        //
        // Negative goes to zero, matching RepairLimit and preserving the meaning TermContains already
        // gives it: a term of no weeks contains nothing.
        foreach (var term in p.Terms)
            term.WeekCount = Math.Clamp(term.WeekCount, 0, TermInfo.MaxWeekCount);

        // Building.FrozenWeeks has no migration block, for schema 13's own reason: no build before
        // this one could freeze a week, so an absent key is an empty list and that is what it was.
        // What it DOES need is the same repair every date on this file gets. Every comparison a frozen
        // week takes part in is an equality against a Monday, so one that is not a Monday freezes a
        // week nothing can ever ask about — the sessions stamped for it invisible, and the week itself
        // quietly back on the pattern.
        if (b.FrozenWeeks.Count > 0)
        {
            var mondays = b.FrozenWeeks.Select(WeekLabel.MondayOf).Distinct().OrderBy(d => d).ToList();
            b.FrozenWeeks.Clear();
            b.FrozenWeeks.AddRange(mondays);
        }

        // Separate weeks and frozen weeks are alternatives, never both: SeparateTheWeeks clears the
        // record because every week is its own after it. A file carrying both is hand-edited or from a
        // build that does not exist, and the honest repair is to drop the record — the sessions are all
        // stamped either way, so nothing is lost and every week keeps answering with its own.
        if (b.WeeksAreSeparate) b.FrozenWeeks.Clear();

        var frozen = b.FrozenWeeks.ToHashSet();
        foreach (var s in b.AllSessions())
        {
            if (s.WeekMonday is not DateOnly w)
                continue;

            var monday = WeekLabel.MondayOf(w);

            // One state, one meaning. A session carrying a week that is neither a separate-weeks
            // project's own week nor a frozen one is a half-converted project, which is the shape
            // every reader here would disagree about: SessionsInWeek ignores the stamp while a
            // future reader of the stamp would not.
            s.WeekMonday = b.WeeksAreSeparate || frozen.Contains(monday) ? monday : null;
        }

        // SCHEMA 14 MOVES AN ABSENCE OFF ITS FACILITATOR AND ONTO A PERSON OF A NAMED KIND, so that
        // a student can be recorded as away at all — the thing this app could not do, and which left
        // a student off injured for a month looking, on every register, exactly like one who stopped
        // turning up.
        //
        // Absence.Person needs no migration: AbsentPerson.Facilitator is ordinal 0, so an absence
        // arriving without the key already says what every absence in every schema-13 file was. The
        // ID does, because the key it was written under has changed — Absence.LegacyFacilitatorId is
        // what a schema-13 "FacilitatorId" deserialises into, for the reason Student.LegacyName is a
        // real property rather than a raw-JSON read: by the time this method runs, a key with no
        // property behind it has already been discarded by System.Text.Json.
        //
        // GATED, like every block above it and for the same reason in reverse. A file claiming schema
        // 14 while carrying a "FacilitatorId" did not get it from a build of this app, and honouring
        // one would be the silent direction: it would overwrite a PersonId the file states — which,
        // on a STUDENT absence, silently reassigns somebody's medical absence to a member of staff
        // and, through the cover machinery, invents a cover gap for a class nobody is away from.
        if (incomingSchemaVersion < 14)
            foreach (var a in p.Absences)
                a.PersonId = a.LegacyFacilitatorId;

        // ...and the legacy key is cleared once read, on every absence in the file — including ones
        // that arrived with nothing to migrate. UNGATED, exactly the shape Student.LegacyName's and
        // Room.LegacySchedule's own clears use, and for the reason written out beside the second of
        // them: gated, a schema-14 file hand-edited to carry a "FacilitatorId" would be correctly
        // left unmigrated and then INCORRECTLY left uncleared, so the stray key would round-trip
        // through every save from then on.
        foreach (var a in p.Absences)
            a.LegacyFacilitatorId = Guid.Empty;

        // THE ABSENCE ORPHAN SCRUB, whose reasoning sits further up beside the other orphan sweeps,
        // where this line used to be. It is HERE, and this position is load-bearing twice over:
        //
        //   • AFTER the 13 → 14 move above, or every absence in every older file is read with an
        //     empty PersonId, matches nobody, and is deleted on load — a term's staff absences
        //     silently gone, which is what this scrub existing at all is meant to prevent;
        //   • after BOTH post-repair id sets are built (the facilitator and student duplicate-id
        //     renumbers), so an id either block reissued cannot be read here as dangling.
        //
        // ASKED THROUGH THE KIND rather than against the union of the two sets. The union would keep
        // a student absence alive because some facilitator happens to hold that id, which is the one
        // reading that turns a dangling record into a record about the wrong person.
        p.Absences.RemoveAll(a => !(a.Person switch
        {
            AbsentPerson.Student => seenStudentIds.Contains(a.PersonId),
            _ => seenFacilitatorIds.Contains(a.PersonId),
        }));

        // AND THE REGISTERS' SECOND LIST, which schema 14 adds and which needs nothing moved: a
        // schema-13 file has no ExcusedStudentIds key, that deserialises to an empty list, and an
        // empty excused list is precisely what every register ever filed before this meant — nobody
        // could be marked away with the school's knowledge, because there was no such mark. The
        // normalisation and the orphan sweep for it live with the present list's, further down, so a
        // future reader finds both halves of one rule in one place.

        RepairEvents(p, seenFacilitatorIds, seenStudentIds);

        // 16 → 17: A CLASS'S LIST COMES FROM ITS GROUPS NOW, and ScheduleEntry.StudentIds changes
        // meaning from "everybody on it" to "put on it by hand". GATED, because it rewrites a list
        // the file states: run on a schema-17 file it would read the hand-added few as the whole
        // roster and exclude the rest of the group.
        //
        // Two things are carried across so that NOTHING anybody already recorded changes:
        //   • A REGISTER ALREADY FILED is stamped with the roster it was taken against, the old list
        //     exactly, so its absent count reads the same tomorrow as it did yesterday.
        //   • A CLASS WITH A HAND-KEPT LIST keeps exactly that list — whoever of its groups was not on
        //     it becomes left off, whoever outside them was on it becomes added. The same people are
        //     on it after as before.
        //
        // A class with NOBODY ticked is deliberately NOT preserved as empty: it gains its groups'
        // students, which is the feature. Its missing-register count rising is that list arriving,
        // and Craig was told so before he said go. A whole-building session keeps its old list on
        // disk and is read as nobody (SessionRoster), his call on the same day.
        if (incomingSchemaVersion < 17)
        {
            foreach (var s in b.AllSessions())
            {
                s.StudentIds ??= new List<Guid>();
                s.ExcludedStudentIds ??= new List<Guid>();
                s.Attendance ??= new List<AttendanceRecord>();

                foreach (var record in s.Attendance)
                    if (record is not null)
                        record.RosterStudentIds ??= new List<Guid>(s.StudentIds);

                if (s.StudentIds.Count == 0 || s.Scope == SessionScope.WholeBuilding) continue;

                var (added, excluded) = SessionRoster.Differences(b, s, s.StudentIds);
                s.StudentIds = added;
                s.ExcludedStudentIds = excluded;
            }
        }

        // Schema 18: PREP TIME MOVES OFF THE CLASS AND INTO THE TIME RULES. Every class the school
        // ticked becomes the list of one "Prep time" rule at the 50% every schema-15 build used, so
        // nobody's hours move by a minute; the old box is then cleared and is never written again.
        // The rule is added even when nothing was ticked, so the list shows the school where prep
        // time went. Gated, because on a schema-18 file the box is gone and the rules are the truth.
        if (incomingSchemaVersion < 18)
        {
            b.TimeRules ??= new List<TimeRule>();
            if (!b.TimeRules.Any(r => r?.Kind == TimeRuleKind.Preparation))
                b.TimeRules.Insert(0, new TimeRule
                {
                    Name = "Prep time",
                    Kind = TimeRuleKind.Preparation,
                    Percent = 50,
                    ClassIds = b.Classes.Where(c => c is not null && c.LegacyHasPrepTime).Select(c => c.Id).ToList(),
                });
        }
        foreach (var c in b.Classes)
            if (c is not null) c.LegacyHasPrepTime = false;

        // Time rules, UNGATED like every scrub: a null list or entry from a hand-edited file, a class
        // id naming nothing, a percentage or a number of minutes that makes no sense, a repeated day.
        b.TimeRules ??= new List<TimeRule>();
        b.TimeRules.RemoveAll(r => r is null);
        {
            var classIds = b.Classes.Where(c => c is not null).Select(c => c.Id).ToHashSet();
            var seen = new HashSet<Guid>();
            foreach (var r in b.TimeRules)
            {
                if (r.Id == Guid.Empty || !seen.Add(r.Id)) r.Id = Guid.NewGuid();
                r.Name ??= "";
                r.ClassIds = (r.ClassIds ?? new List<Guid>()).Where(classIds.Contains).Distinct().ToList();
                r.Days = (r.Days ?? new List<DayOfWeek>()).Where(d => Enum.IsDefined(d)).Distinct().ToList();
                r.Percent = Math.Clamp(r.Percent, 0, 200);
                r.Minutes = Math.Clamp(r.Minutes, 0, 24 * 60);
                if (!Enum.IsDefined(r.Kind)) r.Kind = TimeRuleKind.Preparation;
                if (!Enum.IsDefined(r.Condition)) r.Condition = TimeRuleCondition.TeachesBeforeAndAfter;
            }
        }

        // A project that has been brought up to date should say so: an older build decides whether a
        // file is safe to open by reading this number, and would otherwise accept a migrated file,
        // silently discard the strands it doesn't recognise, and write the reduced file back.
        p.SchemaVersion = ProjectFile.CurrentSchemaVersion;

        // The fifth instance of the four scrubs above, and the last managed collection to have none.
        // A ScheduleEntry.StudentIds or AttendanceRecord.PresentStudentIds entry naming a student this
        // file does not contain — a restored backup, a second machine, a hand-edit — does not fail
        // loudly. It silently stops meaning anything: the session still lists, still schedules, and
        // its roster simply counts one more person than the project has, keeping Register… enabled for
        // somebody who is not in the building; the register is then a head count with a body missing,
        // and AttendanceReport resolves every rate against a roster that is partly fictional. Two
        // figures on the same report disagreeing, with no visible cause.
        //
        // Follows the four above:
        //   • UNGATED on incomingSchemaVersion, like all of them — every route that produces the shape
        //     produces it on a file that says it is current, so it must recover on EVERY load;
        //   • an id matching no live student is REMOVED, never repaired by inventing a student. There
        //     is no name to invent one from, and an empty roster is a legitimate, persistable state —
        //     it is how every session in every project arrives;
        //   • resolved against seenStudentIds, the post-repair set built by the duplicate-id renumber
        //     above, so a student whose id that block reissued cannot be mistaken here for one that no
        //     longer exists. This read against b.Students directly when it was written, correctly at
        //     the time, on the stated grounds that students were the one collection with no renumber
        //     to move an id out from under it. They have one now — it was the seventh and last — and
        //     the two blocks are ordered so that this one runs after it.
        //
        // A roster or register this scrub EMPTIES is left standing, not removed — the same line
        // MainViewModel.ApplyStudentChanges draws on the live path, so the two cannot disagree about
        // what the project contains. That live-path cascade is this scrub's counterpart and it already
        // covers deletion through Manage ▸ Students…, which is why this is the half-swept shape the
        // Holiday.GroupIds comment above was written about rather than a new hole: live path covered,
        // load path missed.
        foreach (var room in b.Rooms)
        {
            // A null Geometry, or a null Points inside a real one: System.Text.Json overwrites a
            // = new() initialiser when the JSON holds an explicit null, exactly as this method's doc
            // comment describes and as the other repairs above already assume. Both reach
            // FloorPlanView.WorldPoints — g.Kind, then g.Points.Count, neither null-checked — from
            // RedrawOverlays, which runs from a dependency-property callback and from the rooms
            // collection changing: on the UI thread, with no try, on project OPEN. A hard crash
            // opening a file, not a bad render.
            //
            // The app cannot produce either shape (drawing always emits a real geometry with real
            // points, and JsonOptions declines to WRITE nulls at all while reading them happily), so
            // they arrive only hand-edited, foreign or truncated. This is the identical defect
            // Facilitator.Availability was given a repair for one object across — the load path had
            // been hardened around Room twice and missed it both times, which is CLAUDE.md's
            // "half-swept repairs" in its purest form.
            //
            // Repaired to an EMPTY outline rather than an invented one. Empty is already a shape
            // every consumer handles: WorldPoints returns no points, RedrawOverlays' pts.Count < 2
            // filter skips the room, and the three GeometryMath guards return zeros rather than
            // throwing. Inventing a rectangle would put a shape on the plan the file never claimed.
            room.Geometry ??= new RoomGeometry();
            room.Geometry.Points ??= new List<NormPoint>();
        }

        // Every session, not every session found by walking a room: b.Rooms -> b.SessionsIn(room)
        // is silent about one whose RoomId is null, which the repairs and orphan scrubs below must
        // not be -- every one of them exists for a hand-edited or foreign file, and a room-less
        // session is not a rarer case of that, it is the same case.
        //
        // Schedule was null-guarded and swept of zero-length sessions before the class-link
        // migration above, so that a session about to be discarded could not mint a class first.
        foreach (var s in b.AllSessions())
        {
            // The last of the explicit-null string overwrites, and the one repaired to "" rather
            // than to a placeholder — see the block near the top of this method for the rule and
            // the other two it covers. Every reader that SHOWS a session with no title already
            // says "(untitled)" (ScheduleConflicts, WeekGridView, CoverDialog, the facilitator
            // profile), all of them through string.IsNullOrWhiteSpace, which cannot tell "" from
            // null — so this closes the null without changing one word anybody sees, and without
            // putting a second placeholder on screen for the condition the app already names.
            //
            // Deliberately AFTER the 5→6 class-link migration above, though it makes no
            // difference and is not load-bearing: ClassCatalogue treats a null title and a blank
            // one identically ("an untitled session names no activity"), so neither order can mint
            // a phantom class called "(unnamed)" out of a session that named nothing. Pinned by
            // NullNameRepairTests.ANullTitleOnAPreSixFile_MintsNoClass.
            s.Title ??= "";

            s.FacilitatorIds ??= new List<Guid>();
            s.FacilitatorParts ??= new List<FacilitatorPart>();
            s.StudentIds ??= new List<Guid>();
            s.Attendance ??= new List<AttendanceRecord>();
            s.Exceptions ??= new List<SessionException>();
            s.GroupIds ??= new List<Guid>();

            // A hand-edited or foreign file can hold a null ELEMENT inside Exceptions — the ??=
            // above only guards the list itself, not what is inside it — and every scrub below this
            // point (the NewDay repair and the cover-facilitator sweep among them) dereferences each
            // entry without checking. Swept first and unconditionally, exactly like the ??= repairs
            // it sits beside, so a null here is gone before anything downstream can throw out of
            // Validate rather than the app's own refusal.
            s.Exceptions.RemoveAll(x => x is null);

            s.StudentIds.RemoveAll(id => !seenStudentIds.Contains(id));

            // AND SCHEMA 17's SECOND ROSTER LIST, the students left off a class by hand. A schema-16
            // file has no such key and reads as an empty list, which is what it meant; the null
            // repair and the orphan sweep are the same two lines the first list has, for the same
            // reasons, so the two halves of one roster cannot be repaired differently.
            s.ExcludedStudentIds ??= new List<Guid>();
            s.ExcludedStudentIds.RemoveAll(id => !seenStudentIds.Contains(id));

            foreach (var record in s.Attendance)
            {
                // AND THE LIST A FILED REGISTER WAS TAKEN AGAINST, schema 17. Null is a legitimate
                // state (nothing written down) and is left null; a list naming somebody the file
                // does not contain is swept like every other roster here.
                record.RosterStudentIds?.RemoveAll(id => !seenStudentIds.Contains(id));

                // PresentStudentIds was not among the ??= repairs above and needed to be: an
                // explicit null in the JSON overwrites the = new() initialiser, exactly as this
                // method's doc comment describes, and Attendance itself was normalised while the
                // list inside each record was not. A null one reaches AttendanceReport and throws
                // there. The scrub on the next line has to walk it either way.
                record.PresentStudentIds ??= new List<Guid>();
                record.PresentStudentIds.RemoveAll(id => !seenStudentIds.Contains(id));

                // AND THE EXCUSED LIST, schema 14's second half, for all of the same reasons plus
                // one of its own: a schema-13 file has no such key, so the null repair here is the
                // ordinary path for every file written before the bump rather than the hand-edited
                // case. Normalise() then makes the two lists disjoint — asked on the way in as well
                // as at every write door, so neither a foreign file nor an older bug can leave a
                // register whose marks and whose counts answer differently.
                record.ExcusedStudentIds ??= new List<Guid>();
                record.ExcusedStudentIds.RemoveAll(id => !seenStudentIds.Contains(id));
                record.Normalise();

                // AND THE WEEK ITSELF, which is named for what it holds and is not guaranteed to
                // hold it. Every writer here puts a Monday there, so a file this app made is always
                // sound; a hand-edit, another tool or a restored fragment can carry any weekday —
                // which is exactly who this method exists for.
                //
                // BOTH PATHS, deliberately. The four readers normalise through MondayOf now, which
                // makes today's code agree; this repairs the DATA, so a reader written next month is
                // right without having to know. Leaving it to the readers alone is the half-swept
                // shape this file has been caught by before.
                record.WeekMonday = WeekLabel.MondayOf(record.WeekMonday);
            }

            // The SIXTH orphan scrub, and the two lists that name a FACILITATOR — the last pair
            // with none. Same class as the five above, and the same half-swept shape: an id
            // naming somebody this file does not contain arrives through a restored backup, a
            // second machine or a hand-edit, and loads unrepaired, while
            // MainViewModel.ApplyFacilitatorChanges has always scrubbed both lists when somebody
            // is deleted through Manage ▸ Facilitators…. Live path covered, load path missed.
            //
            // It does not fail loudly. The session still displays, still exports, still clashes
            // and still occupies its room; the facilitator column just comes up blank with
            // nothing saying why. Since 1.9.0 it is more than cosmetic: the administrator dock
            // totals each person's week by walking exactly these lists, so an id naming nobody is
            // teaching time belonging to no one — missing from every per-facilitator figure while
            // the scheduled total still counts the session. Two numbers on one panel disagreeing,
            // with no visible cause, which is the sentence the AreaId, ClassId and StudentIds
            // scrubs were each written under.
            //
            // Follows those five: UNGATED on incomingSchemaVersion, because every route that
            // produces the shape produces it on a file that says it is current; an id matching no
            // live facilitator is REMOVED, never repaired by inventing somebody, because there is
            // no name to invent one from and a session with no facilitator is a legitimate,
            // persistable state — it is how every session arrives before anyone is assigned;
            // resolved against seenFacilitatorIds, the post-repair set built by the duplicate-id
            // renumber above, so an id that block reissued cannot be read here as dangling.
            //
            // WHAT SURVIVES IS DECIDED THE WAY ApplyFacilitatorChanges DECIDES IT, so the two
            // paths cannot disagree about what a project contains:
            //   • a Covered exception THIS SCRUB emptied is REMOVED. A cover naming nobody is not
            //     a cover, and nothing has to be invented to drop it: the absence that prompted
            //     it is still recorded, so the resolver re-derives the gap and flags the session
            //     again — the honest answer, since the person who was going to teach it does not
            //     exist in this file;
            //   • an exception that ALREADY named nobody is left alone. Only a hand-edited file
            //     can hold an empty cover list and the resolver copes with one on purpose
            //     (CoverTests.Resolver_CoveredExceptionNamingNobody_LeavesTheRosterExactlyAsItIs),
            //     so nothing here is entitled to bin it. That gate is narrower than "did this
            //     load scrub anybody", and deliberately: the live path was fixed for exactly that
            //     over-wide gate once already;
            //   • a Cancelled or Rescheduled exception this scrub emptied is NEVER removed, or a
            //     load would put a cancelled session back on the timetable — a class the school
            //     believes is off, running.
            //
            // A null CoverFacilitatorIds is left null rather than normalised to an empty list, and
            // that is the difference from the ??= repairs above: the property is nullable on
            // purpose and null is how every Cancelled and Rescheduled exception in every project
            // arrives. The pattern below reads "not a non-empty list" so it skips both.
            s.FacilitatorIds.RemoveAll(id => !seenFacilitatorIds.Contains(id));

            // A ONE-OFF CHANGE'S DAY IS ONLY MEANINGFUL UNDER Rescheduled, AND ONLY IF IT IS A DAY.
            // SessionException.NewDay arrived at schema 12 and ScheduleResolver.EffectiveDay honours
            // it under that kind alone, so both shapes below are already inert where they are read —
            // this repairs the DATA so that a reader written next month is right without having to
            // know, which is the same both-paths rule AttendanceRecord.WeekMonday above is repaired
            // under, and the half-swept shape this file has been caught by before.
            //
            //   • a NewDay on a Cancelled or Covered exception is set to null. Neither kind moves a
            //     class; a day beside one is a hand-edit or a foreign file saying something this app
            //     has no meaning for, and the danger is not today's readers but tomorrow's;
            //   • a NewDay outside DayOfWeek's range is set to null. JsonStringEnumConverter accepts
            //     an out-of-range INTEGER — the identical hazard ScheduleResolver.AppliesTo documents
            //     for HolidayScope, and the identical limit: a NAME this build does not have makes
            //     the deserializer throw and the file is refused before it ever reaches here.
            //
            // NEVER INVENT A DAY. Null is not a missing value to be filled in, it is the answer — the
            // class runs on its usual day, which is what the file said before somebody edited it.
            // UNGATED on incomingSchemaVersion, like every scrub in this method: every route that
            // produces either shape produces it on a file that says it is current.
            foreach (var x in s.Exceptions)
            {
                if (x.NewDay is not { } newDay) continue;
                if (x.Kind != ExceptionKind.Rescheduled || !Enum.IsDefined(newDay)) x.NewDay = null;
            }

            var coversEmptiedByScrub = new List<SessionException>();
            foreach (var x in s.Exceptions)
            {
                if (x.CoverFacilitatorIds is not { Count: > 0 } cover) continue;   // nothing to scrub
                cover.RemoveAll(id => !seenFacilitatorIds.Contains(id));
                if (cover.Count == 0 && x.Kind == ExceptionKind.Covered) coversEmptiedByScrub.Add(x);
            }
            if (coversEmptiedByScrub.Count > 0)
                s.Exceptions.RemoveAll(coversEmptiedByScrub.Contains);   // by identity, not by value

            // One person named twice on one session is one person in one room. Left alone, every
            // total that WALKS this list counts them twice — the conflicts panel's weekly load and
            // the term report's facilitator hours both do — while everything that asks "is this
            // person on it" counts them once, so the same week answers two ways depending on which
            // question was asked. The Edit session dialog offers a checkbox per facilitator and
            // cannot tick anybody twice, so this arrives hand-edited or foreign, exactly like the
            // duplicate facilitator ids repaired above. De-duplicated in place, first occurrence
            // keeping its position, so nothing about the session's order changes.
            if (s.FacilitatorIds.Count > 1)
            {
                var unique = s.FacilitatorIds.Distinct().ToList();
                if (unique.Count != s.FacilitatorIds.Count)
                {
                    s.FacilitatorIds.Clear();
                    s.FacilitatorIds.AddRange(unique);
                }
            }

            // Schema 23's FacilitatorParts, AFTER both facilitator repairs above so a part for
            // somebody scrubbed off the session goes with them: one part per person on the session,
            // inside its hours and saying something the session's own times do not. A hand-edited or
            // foreign file is the only way to arrive with anything else.
            FacilitatorParts.Normalise(s);

            // ScheduleEntry.GroupIds, one object across from the Holiday.GroupIds scrub above and the
            // same orphan class: an id naming a group this file does not contain — a restored backup,
            // a second machine, a hand-edit, or a group deleted some other way. It does not fail
            // loudly, and Task 5 is what gave it teeth: ScheduleResolver.AppliesTo shuts a
            // Scope.Groups session only when EVERY cohort it names is in the closure's own GroupIds,
            // so a dangling id in this list is not inert the way a dangling id usually is here — it is
            // a cohort the closure can never match, which means a session naming [BA, deadId] is never
            // shut by a BA-only closure, and BA's class quietly runs through BA's own half term. That
            // is exactly the "class taught to an empty room" failure AppliesTo's own remarks say the
            // safety direction exists to prevent, reached from the opposite side: not an unrecognised
            // holiday scope, but a session whose audience the file can no longer fully name.
            //
            // Follows Holiday.GroupIds exactly:
            //   • UNGATED on incomingSchemaVersion, like every scrub in this method — every route that
            //     produces the shape produces it on a file that says it is current;
            //   • an id matching no live group is REMOVED, never repaired by inventing one. There is
            //     no name to invent it from, and an empty GroupIds is a legitimate, persistable state
            //     under Scope.Groups — "on for nobody", which SessionScope's own remarks name;
            //   • resolved against seenGroupIds, the same post-repair set the Holiday.GroupIds and
            //     Student.GroupId scrubs use, so a group whose id was reissued by the duplicate-id
            //     renumber is present under its NEW id and nothing here can orphan an id that block
            //     just moved.
            //
            // Unlike Holiday.GroupIds, emptying this list never removes the session — a session with
            // no cohort at all is an ordinary, everyday state (an External Hire booking, a one-off),
            // not a special case earning deletion the way an emptied Groups-scoped CLOSURE does.
            // Its live-path counterpart is MainViewModel.ApplyGroupChanges, so both halves are swept —
            // the load path and the live path cannot disagree about what a session's audience is, the
            // half-swept shape the Holiday.GroupIds comment above was written about.
            s.GroupIds.RemoveAll(id => !seenGroupIds.Contains(id));
        }

        // SCHEMA 17's WHOLE-PROGRAMME SESSIONS, brought up to the programme's groups as they are.
        // UNGATED and LAST: it reads the groups' programmes and the sessions' group lists after
        // every scrub above has had its say, so a programme or group this file does not contain can
        // neither be written into a session here nor survive in one. See
        // ScheduleOps.RefreshProgrammeSessions.
        ScheduleOps.RefreshProgrammeSessions(b);

        return p;
    }

    public const int MaxBackups = 12;

    /// <summary>
    /// Serializes the project to project.json through <see cref="AtomicFile"/>, so a crash mid-write
    /// can never corrupt the only copy and a reader never sees the file half-written or missing. The
    /// previous project.json is first copied into backups/.
    ///
    /// <para><b>This used to hand-roll the idiom</b> — its own <c>project.json.tmp</c>, then
    /// <c>File.Replace</c> — and so carried the defect <see cref="AtomicFile.Swap"/> documents in
    /// full: <c>File.Replace</c> is not an atomic swap, and every save left a window in which
    /// <c>project.json</c> could not be opened by name. Measured here before the change, one writer
    /// with no second thread involved: <b>213 of 16,107 probes could not open the file, across 113
    /// saves</b>. It is routed through <see cref="AtomicFile"/> rather than repaired in place because
    /// this is not merely a similar operation to <see cref="AtomicFile.WriteAllText"/>, it is that
    /// operation — write this text to this path, atomically — and keeping a second copy of it is how
    /// this one came to be four commits behind the original.</para>
    ///
    /// <para>The fixed <c>.tmp</c> name goes with it: <see cref="AtomicFile"/> uses a per-write GUID
    /// so two savers of one project cannot collide on one scratch path, and deletes it in a
    /// <c>finally</c> so a throw strands nothing.</para>
    /// </summary>
    public void Save(ProjectFile project, string projectFolder)
    {
        Directory.CreateDirectory(projectFolder);

        string dest = Path.Combine(projectFolder, ProjectJsonName);
        string? backup = File.Exists(dest) ? TryBackupExisting(projectFolder, dest) : null;

        try
        {
            AtomicFile.WriteAllText(dest, SaveTo(project));
        }
        catch
        {
            // A save that fails must not consume a rolling-backup slot. The copy above is already
            // made and the write did not happen, so the file on disk is still the one just backed up
            // and the slot would hold nothing but a duplicate of it. ProjectPackage.Save was given
            // exactly this guard; this half of the pair never got it, which is the whole of the bug —
            // the asymmetry, not the stakes, which are low because this folder is a temp working dir.
            // SetAttributes first because File.Copy carries the source's attributes across, so a
            // read-only original leaves a copy that cannot be deleted until that is cleared.
            if (backup is not null)
                try { File.SetAttributes(backup, FileAttributes.Normal); File.Delete(backup); }
                catch { /* best-effort; the save's own failure is what matters */ }
            throw;
        }

        // Pruned only after the write succeeded, for the same reason. Pruning as part of taking the
        // backup meant a failed save still cost the oldest slot, to make room for a copy that was
        // then thrown away. Moving it out here put it past the last `throw`, though, so it also has
        // to be unable to throw itself: the project is on disk by this point, and a save that
        // reports failure over data it wrote correctly is worse than an unpruned backups folder.
        if (backup is not null)
            PruneBackups(Path.GetDirectoryName(backup)!, MaxBackups);
    }

    /// <summary>
    /// <b>The whole of saving a project, minus the disk.</b> Stamps who saved it and when, and returns
    /// the exact text <see cref="Save(ProjectFile, string)"/> writes to <c>project.json</c> — same
    /// serializer settings, same stamp, same bytes. A host with nowhere to write a file takes the
    /// string; <see cref="Save(ProjectFile, string)"/> is <see cref="AtomicFile"/> in front of it.
    ///
    /// <para><b>The stamp is here rather than in the wrapper because that is what makes the two doors
    /// produce the same bytes.</b> This method is the whole description of what a save writes; a stamp
    /// applied by <see cref="Save(ProjectFile, string)"/> alone would mean the path saver and the byte
    /// saver wrote different <c>project.json</c> for the same project, which is exactly the drift the
    /// byte API exists to avoid. <c>ByteRoundTripTests.NothingButTheTimestamp_DiffersBetweenTheTwoSavers</c>
    /// is that claim.</para>
    ///
    /// <para><b>An earlier version of this comment claimed the conflict notice and the recovery list
    /// read these fields. Neither does</b> — the notice reads <see cref="LockInfo"/> out of the
    /// <c>.cccc.lock</c> sidecar and the recovery list reads <c>RecoveryService.Meta</c>. They are
    /// audit metadata, exactly as the note beside the undo snapshot says — read since 2026-10-02 only
    /// by the web's list of earlier saves, to say who made each one and when. That
    /// does not change where the stamp belongs; it changes the reason, and a false reason in a comment
    /// is worse than none.</para>
    ///
    /// <para><paramref name="savedBy"/> defaults to <see cref="Environment.UserName"/>, which is the
    /// person at the keyboard on a desktop and the <b>service account</b> on a server. A web host must
    /// pass the signed-in user, or every save from the browser stamps the app-pool identity over what
    /// a real person's desktop save recorded. It is also the way out for a caller using this for a
    /// preview, a diff or an export: this method mutates <paramref name="project"/>, so a host that
    /// serializes without meaning to save should pass the name back that was already there.</para>
    /// </summary>
    public static string SaveTo(ProjectFile project, string? savedBy = null)
    {
        project.LastSavedUtc = DateTimeOffset.UtcNow;
        project.LastSavedBy = savedBy ?? Environment.UserName;
        return JsonSerializer.Serialize(project, JsonOptions);
    }

    /// <summary>Last-write time of a project's json (UTC), or DateTime.MinValue if it doesn't exist.</summary>
    public static DateTime GetWriteTimeUtc(string projectFolder)
    {
        string p = Path.Combine(projectFolder, ProjectJsonName);
        return File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
    }

    /// <summary>
    /// Copies the current project.json into backups/ and returns where it went, or <c>null</c> if no
    /// copy was made. Returns null rather than throwing, and does not prune: backups are best-effort
    /// insurance that must never block a save, and the caller drops the copy again if the save it was
    /// taken for goes on to fail.
    /// </summary>
    private static string? TryBackupExisting(string folder, string dest)
    {
        try
        {
            string dir = Path.Combine(folder, BackupsDir);
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = Path.Combine(dir, $"project-{stamp}.json");
            if (File.Exists(backup))
                backup = Path.Combine(dir, $"project-{stamp}-{Guid.NewGuid():N}.json");
            File.Copy(dest, backup, overwrite: false);
            return backup;
        }
        catch { return null; }
    }

    /// <summary>
    /// Drops all but the newest <paramref name="keep"/> backups. Best-effort throughout, and that
    /// now includes the enumeration: <c>Directory.GetFiles</c> throws if the folder cannot be read —
    /// a scanner holding <c>backups/</c> open, or it being removed between the copy and this call.
    ///
    /// <para>It used to sit inside <see cref="TryBackupExisting"/>'s catch-all, where that could not
    /// matter. It runs after the write now, which is right — a failed save should not cost the oldest
    /// slot — but it left the enumeration as the one unguarded call downstream of a successful write,
    /// so <see cref="Save"/> could report failure on a project that is safely on disk. Only the
    /// per-file deletes were guarded, which is the likelier failure but not the one a user sees.</para>
    /// </summary>
    internal static void PruneBackups(string dir, int keep)
    {
        List<string> files;
        try
        {
            files = Directory.GetFiles(dir, "project-*.json")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
        catch { return; }

        foreach (var old in files.Skip(keep))
            try { File.Delete(old); } catch { /* ignore */ }
    }

    /// <summary>
    /// Saves the project to a new folder (Save As): copies the floor images across and writes
    /// project.json there. The caller then switches its working folder to <paramref name="destFolder"/>.
    /// </summary>
    public void SaveAs(ProjectFile project, string sourceFolder, string destFolder)
    {
        Directory.CreateDirectory(destFolder);
        Directory.CreateDirectory(Path.Combine(destFolder, ImagesDir));

        foreach (var floor in project.Building.Floors)
        {
            string src = ImageFullPath(sourceFolder, floor);
            string dst = ImageFullPath(destFolder, floor);
            if (File.Exists(src) && !string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        }

        Save(project, destFolder);
    }
}
