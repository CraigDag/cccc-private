using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCCCScheduler.Helpers;

/// <summary>
/// A stored duration as somebody would <b>write it into the file</b> — the third sibling of
/// <see cref="DurationText"/> (as somebody types it) and <see cref="DurationChoices"/> (as somebody
/// picks it), and the one that decides whether a hand-edited <c>project.json</c> still opens.
///
/// <para><b>The trap it exists for.</b> <see cref="TimeSpan"/>'s JSON round-trip format is
/// <c>"c"</c> — <c>[-][d'.']hh':'mm':'ss['.'fffffff]</c>, with the hours field limited to 0–23 —
/// so a thirty-hour weekly target is written <c>"1.06:00:00"</c> and <c>"30:00:00"</c> is not a
/// duration .NET will read at all. <c>"30:00:00"</c> is, of course, exactly how a person writes down
/// thirty hours. Typing it into the file threw before <c>ProjectStore.Validate</c> ever ran, so the
/// owner was told <i>"This project could not be read. The file is likely corrupt…"</i> and lost the
/// entire project — every room, every session, every register — for setting their real target.</para>
///
/// <para><b>Reading is widened; writing is not moved.</b> <see cref="Write"/> emits the same
/// <c>"c"</c> text the serializer has always emitted, byte for byte, so every project.json already on
/// the school's share saves back identically and no file is churned by this existing. A file that
/// arrives spelled the human way is normalised on its next save and never again. Pinned by
/// <c>DurationJsonTests.Writing_IsByteForByteWhatTheSerializerAlreadyWrote</c>, which compares against
/// the serializer's own output rather than against a copy of this class's.</para>
///
/// <para><b>Leniency about the spelling of a duration is not leniency about whether it is one.</b>
/// Text naming no duration is still refused, as a <see cref="JsonException"/> — the exception
/// <c>ProjectStore.Load</c> catches and rewords. Every failure path here has to be that type: a
/// converter that let an <see cref="OverflowException"/> out of <see cref="Read"/> would sail past
/// that catch and reach the owner as an unhandled crash, which is worse than the bug being fixed.
/// A number too large to be a <see cref="TimeSpan"/> is therefore a parse failure, never an
/// arithmetic one.</para>
///
/// <para><b>Written for <see cref="TimeSpan"/>, not <see cref="Nullable{T}"/>, on purpose.</b>
/// System.Text.Json wraps a value-type converter for the nullable form itself, so <c>null</c> keeps
/// meaning what it has always meant on the four properties that carry one — <b>no target</b> and
/// <b>no limit</b>, which is not <see cref="TimeSpan.Zero"/> (see <see cref="Models.Area.WeeklyTarget"/>).
/// Nothing here can collapse the two, because a JSON <c>null</c> never reaches this class.</para>
/// </summary>
public sealed class DurationJsonConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc />
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A duration must be written as a string, e.g. \"30:00:00\".");

        string? text = reader.GetString();
        if (TryParse(text, out var value)) return value;

        throw new JsonException(
            $"\"{text}\" is not a duration. Write hours, minutes and seconds — \"30:00:00\" for thirty "
            + "hours, \"02:30:00\" for two and a half.");
    }

    /// <summary>
    /// Exactly what the serializer wrote before this class existed. <b>Do not "improve" this</b> — the
    /// human spelling is what we accept, never what we produce, and emitting it would rewrite every
    /// duration in every project the next time anybody pressed Ctrl+S.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));

    /// <summary>
    /// Both spellings of one duration. <c>"1.06:00:00"</c> and <c>"30:00:00"</c> are thirty hours;
    /// so is nothing else.
    ///
    /// <para><b>The one place this is NARROWER than <c>"c"</c>, and it is checked first.</b> A
    /// duration written as two colon-separated fields names no units — <c>"20:30"</c> is
    /// hours-and-minutes to one person and minutes-and-seconds to the next, and on a weekly target or
    /// a gap limit the two readings differ by sixty — so it is refused, <b>whatever its first
    /// number</b>. That gate has to run ahead of <c>"c"</c>, because <c>"c"</c> is precisely what
    /// guesses: it accepts two fields whenever the first is 23 or less. <c>"20:30"</c> written for
    /// twenty and a half minutes therefore used to load as twenty and a half hours, silently, with a
    /// gap warning that could never fire again and nothing on screen looking wrong — while
    /// <c>"30:00"</c>, the identical shape, failed the whole file. Refusing both is the only answer
    /// that is the same answer twice.</para>
    ///
    /// <para><b>Everything else reads exactly as it always has.</b> The canonical form goes to
    /// <c>"c"</c> untouched, so every file the app has ever written is unaffected and this class
    /// cannot alter the meaning of one. Only text <c>"c"</c> rejects reaches the second reading, and
    /// that reading is deliberately narrow: three colon-separated fields, an hours field of any
    /// magnitude, a minutes and a seconds field that must each still be a minutes and a seconds
    /// field. A <c>d.</c> prefix pins every field behind it, so <c>"1.06:00"</c> is <c>d.hh:mm</c>
    /// rather than an ambiguous two — there is nothing to guess there, and it is left alone.</para>
    /// </summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrEmpty(text)) return false;

        string s = text;
        bool negative = s[0] == '-';
        if (negative) s = s[1..];

        var parts = s.Split(':');

        // Ambiguity is settled BEFORE "c" is consulted, because "c" is the thing that guesses — it
        // reads any two fields as hours-and-minutes so long as the first is 23 or less, which is why
        // "20:30" and "30:00" used to have opposite fates. Two fields name no units and are refused.
        // A day prefix is not a guess: "1.06:00" is d.hh:mm, every field pinned by the "1.", so it is
        // not two fields in this sense and drops through to "c" as it always has.
        if (parts.Length == 2 && !parts[0].Contains('.')) return false;

        // The round-trip format, otherwise untouched: whatever this build has ever written.
        if (TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out value)) return true;

        // ...and the way a person writes the same thing down, where the hours run past a day.
        if (parts.Length != 3) return false;

        // Seconds may carry a fraction, as they may in the canonical form.
        string secondsField = parts[2];
        long fractionTicks = 0;
        int dot = secondsField.IndexOf('.');
        if (dot >= 0)
        {
            if (!TryFractionTicks(secondsField[(dot + 1)..], out fractionTicks)) return false;
            secondsField = secondsField[..dot];
        }

        if (!TryField(parts[0], int.MaxValue, out long hours)) return false;
        if (!TryField(parts[1], 59, out long minutes)) return false;
        if (!TryField(secondsField, 59, out long seconds)) return false;

        // Checked, because the whole point of accepting an unbounded hours field is that somebody can
        // write a number too big to be a TimeSpan — and an OverflowException out of here would escape
        // ProjectStore.Load's catch and crash the app instead of reporting an unreadable file.
        try
        {
            long ticks = checked(
                hours * TimeSpan.TicksPerHour
                + minutes * TimeSpan.TicksPerMinute
                + seconds * TimeSpan.TicksPerSecond
                + fractionTicks);
            value = new TimeSpan(negative ? checked(-ticks) : ticks);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// One colon-separated field: digits only, no sign, no spaces, and within <paramref name="max"/>.
    /// Rejecting a sign here is what stops <c>"30:-5:00"</c> from reading as anything.
    /// </summary>
    private static bool TryField(string text, long max, out long value)
    {
        value = 0;
        if (text.Length == 0) return false;
        foreach (char c in text)
            if (c is < '0' or > '9') return false;

        // A digit run long enough to overflow a long is a typo, not a duration.
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
        return value <= max;
    }

    /// <summary>The fractional seconds, to <see cref="TimeSpan"/>'s own seven-digit resolution.</summary>
    private static bool TryFractionTicks(string digits, out long ticks)
    {
        ticks = 0;
        if (digits.Length is 0 or > 7) return false;
        if (!TryField(digits, long.MaxValue, out long raw)) return false;

        for (int i = digits.Length; i < 7; i++) raw *= 10;   // ".5" is five million ticks, not five
        ticks = raw;
        return true;
    }
}
